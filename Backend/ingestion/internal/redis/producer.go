package redis

import (
	"context"
	"encoding/json"
	"errors"
	"fmt"
	"log"
	"strings"
	"sync"
	"sync/atomic"
	"time"

	"github.com/nostrategia/backend/ingestion/internal/config"
	"github.com/nostrategia/backend/ingestion/internal/models"
	goredis "github.com/redis/go-redis/v9"
)

var (
	ErrCircuitOpen = errors.New("circuit breaker open: redis unavailable")
)

type StreamProducer interface {
	PublishTelemetry(ctx context.Context, payload *models.TelemetryPayload) (string, error)
	PublishTelemetryBatch(ctx context.Context, payloads []*models.TelemetryPayload) ([]string, error)
	PublishSetup(ctx context.Context, setup *models.SetupPayload) (string, error)
	Ping(ctx context.Context) error
	GetStats() ProducerStats
	Close() error
}

type ProducerStats struct {
	TotalPublishedTelemetry int64   `json:"totalPublishedTelemetry"`
	TotalPublishedSetups    int64   `json:"totalPublishedSetups"`
	FailedPublishCount      int64   `json:"failedPublishCount"`
	CircuitState            string  `json:"circuitState"`
	AverageLatencyMs        float64 `json:"averageLatencyMs"`
}

type redisProducer struct {
	rdb             *goredis.Client
	cfg             *config.Config
	telemetryStream string
	setupStream     string
	maxLen          int64

	// Metrics & Circuit Breaker
	totalTelemetry atomic.Int64
	totalSetups    atomic.Int64
	failCount      atomic.Int64
	lastLatencyNs  atomic.Int64

	cbMutex         sync.RWMutex
	circuitState    string // "CLOSED", "OPEN", "HALF_OPEN"
	lastFailureTime time.Time
	consecFailures  int
}

func NewStreamProducer(ctx context.Context, cfg *config.Config) (StreamProducer, error) {
	rdb := goredis.NewClient(&goredis.Options{
		Addr:         cfg.RedisAddr,
		Password:     cfg.RedisPassword,
		DB:           cfg.RedisDB,
		DialTimeout:  3 * time.Second,
		ReadTimeout:  500 * time.Millisecond,
		WriteTimeout: 500 * time.Millisecond,
		PoolSize:     50,
		MinIdleConns: 10,
	})

	if err := rdb.Ping(ctx).Err(); err != nil {
		log.Printf("[WARN] Redis ping failed on startup (%s): %v. Producer will operate with circuit breaker resilience.", cfg.RedisAddr, err)
	}

	p := &redisProducer{
		rdb:             rdb,
		cfg:             cfg,
		telemetryStream: cfg.TelemetryStream,
		setupStream:     cfg.SetupStream,
		maxLen:          cfg.StreamMaxLen,
		circuitState:    "CLOSED",
	}

	// Try initializing Consumer Groups if Redis is reachable
	p.initConsumerGroups(ctx)

	return p, nil
}

func (p *redisProducer) initConsumerGroups(ctx context.Context) {
	groups := []struct {
		stream string
		group  string
	}{
		{p.telemetryStream, "cg:analytics"},
		{p.telemetryStream, "cg:gateway"},
		{p.setupStream, "cg:setup_worker"},
	}

	for _, g := range groups {
		err := p.rdb.XGroupCreateMkStream(ctx, g.stream, g.group, "$").Err()
		if err != nil && !strings.Contains(err.Error(), "BUSYGROUP") {
			log.Printf("[INFO] Note on XGroupCreate (%s / %s): %v", g.stream, g.group, err)
		}
	}
}

func (p *redisProducer) checkCircuit() error {
	p.cbMutex.RLock()
	state := p.circuitState
	lastFail := p.lastFailureTime
	p.cbMutex.RUnlock()

	if state == "OPEN" {
		if time.Since(lastFail) > 5*time.Second {
			p.cbMutex.Lock()
			p.circuitState = "HALF_OPEN"
			p.cbMutex.Unlock()
			return nil
		}
		return ErrCircuitOpen
	}
	return nil
}

func (p *redisProducer) recordSuccess(duration time.Duration) {
	p.lastLatencyNs.Store(duration.Nanoseconds())
	p.cbMutex.Lock()
	p.circuitState = "CLOSED"
	p.consecFailures = 0
	p.cbMutex.Unlock()
}

func (p *redisProducer) recordFailure() {
	p.failCount.Add(1)
	p.cbMutex.Lock()
	defer p.cbMutex.Unlock()

	p.consecFailures++
	p.lastFailureTime = time.Now()
	if p.consecFailures >= p.cfg.CircuitBreakerMax {
		p.circuitState = "OPEN"
		log.Printf("[ALERT] Redis circuit breaker tripped to OPEN after %d consecutive failures", p.consecFailures)
	}
}

func (p *redisProducer) PublishTelemetry(ctx context.Context, payload *models.TelemetryPayload) (string, error) {
	if err := p.checkCircuit(); err != nil {
		return "", err
	}

	payload.Normalize()
	dataBytes, err := json.Marshal(payload)
	if err != nil {
		return "", fmt.Errorf("marshal telemetry payload: %w", err)
	}

	start := time.Now()
	id, err := p.rdb.XAdd(ctx, &goredis.XAddArgs{
		Stream: p.telemetryStream,
		MaxLen: p.maxLen,
		Approx: true,
		Values: map[string]interface{}{
			"sessionId":  payload.SessionID,
			"packetId":   payload.PacketID,
			"lap":        payload.CompletedLaps,
			"distNorm":   payload.NormalizedCarPosition,
			"speed":      payload.SpeedKmh,
			"gas":        payload.Gas,
			"brake":      payload.Brake,
			"steerAngle": payload.SteerAngle,
			"gear":       payload.Gear,
			"rpm":        payload.Rpm,
			"yawRate":    payload.YawRate,
			"latG":       payload.LatG,
			"longG":      payload.LongG,
			"fuel":       payload.Fuel,
			"data":       string(dataBytes),
		},
	}).Result()

	if err != nil {
		p.recordFailure()
		return "", err
	}

	p.recordSuccess(time.Since(start))
	p.totalTelemetry.Add(1)
	return id, nil
}

func (p *redisProducer) PublishTelemetryBatch(ctx context.Context, payloads []*models.TelemetryPayload) ([]string, error) {
	if err := p.checkCircuit(); err != nil {
		return nil, err
	}

	start := time.Now()
	pipe := p.rdb.Pipeline()
	cmds := make([]*goredis.StringCmd, len(payloads))

	for i, payload := range payloads {
		payload.Normalize()
		dataBytes, _ := json.Marshal(payload)
		cmds[i] = pipe.XAdd(ctx, &goredis.XAddArgs{
			Stream: p.telemetryStream,
			MaxLen: p.maxLen,
			Approx: true,
			Values: map[string]interface{}{
				"sessionId":  payload.SessionID,
				"packetId":   payload.PacketID,
				"lap":        payload.CompletedLaps,
				"distNorm":   payload.NormalizedCarPosition,
				"speed":      payload.SpeedKmh,
				"gas":        payload.Gas,
				"brake":      payload.Brake,
				"steerAngle": payload.SteerAngle,
				"gear":       payload.Gear,
				"rpm":        payload.Rpm,
				"yawRate":    payload.YawRate,
				"latG":       payload.LatG,
				"longG":      payload.LongG,
				"fuel":       payload.Fuel,
				"data":       string(dataBytes),
			},
		})
	}

	_, err := pipe.Exec(ctx)
	if err != nil {
		p.recordFailure()
		return nil, err
	}

	p.recordSuccess(time.Since(start))
	p.totalTelemetry.Add(int64(len(payloads)))

	results := make([]string, len(cmds))
	for i, cmd := range cmds {
		results[i] = cmd.Val()
	}
	return results, nil
}

func (p *redisProducer) PublishSetup(ctx context.Context, setup *models.SetupPayload) (string, error) {
	if err := p.checkCircuit(); err != nil {
		return "", err
	}

	setup.Normalize()
	start := time.Now()

	id, err := p.rdb.XAdd(ctx, &goredis.XAddArgs{
		Stream: p.setupStream,
		MaxLen: p.maxLen,
		Approx: true,
		Values: map[string]interface{}{
			"sessionId": setup.SessionID,
			"fileName":  setup.FileName,
			"carModel":  setup.CarModel,
			"track":     setup.Track,
			"hash":      setup.Hash,
			"timestamp": setup.Timestamp.Format(time.RFC3339Nano),
			"payload":   string(setup.SetupData),
		},
	}).Result()

	if err != nil {
		p.recordFailure()
		return "", err
	}

	p.recordSuccess(time.Since(start))
	p.totalSetups.Add(1)
	return id, nil
}

func (p *redisProducer) Ping(ctx context.Context) error {
	return p.rdb.Ping(ctx).Err()
}

func (p *redisProducer) GetStats() ProducerStats {
	p.cbMutex.RLock()
	state := p.circuitState
	p.cbMutex.RUnlock()

	avgLatencyMs := float64(p.lastLatencyNs.Load()) / 1e6

	return ProducerStats{
		TotalPublishedTelemetry: p.totalTelemetry.Load(),
		TotalPublishedSetups:    p.totalSetups.Load(),
		FailedPublishCount:      p.failCount.Load(),
		CircuitState:            state,
		AverageLatencyMs:        avgLatencyMs,
	}
}

func (p *redisProducer) Close() error {
	return p.rdb.Close()
}
