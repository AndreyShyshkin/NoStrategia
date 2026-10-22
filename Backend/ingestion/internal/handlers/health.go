package handlers

import (
	"context"
	"database/sql"
	"runtime"
	"time"

	"github.com/gofiber/fiber/v2"
	_ "github.com/jackc/pgx/v5/stdlib"
	"github.com/nostrategia/backend/ingestion/internal/redis"
)

type HealthHandler struct {
	producer  redis.StreamProducer
	db        *sql.DB
	startTime time.Time
}

func NewHealthHandler(producer redis.StreamProducer, dbURL string) *HealthHandler {
	var db *sql.DB
	if dbURL != "" {
		if d, err := sql.Open("pgx", dbURL); err == nil {
			d.SetMaxOpenConns(5)
			d.SetConnMaxLifetime(5 * time.Minute)
			db = d
		}
	}

	return &HealthHandler{
		producer:  producer,
		db:        db,
		startTime: time.Now(),
	}
}

func (h *HealthHandler) Health(c *fiber.Ctx) error {
	ctx, cancel := context.WithTimeout(context.Background(), 1*time.Second)
	defer cancel()

	redisOk := false
	if h.producer != nil {
		redisOk = h.producer.Ping(ctx) == nil
	}

	dbOk := false
	if h.db != nil {
		dbOk = h.db.PingContext(ctx) == nil
	}

	status := "healthy"
	httpCode := fiber.StatusOK
	if !redisOk {
		status = "degraded"
		// If Redis is down, ingestion can't queue data
		httpCode = fiber.StatusServiceUnavailable
	}

	var m runtime.MemStats
	runtime.ReadMemStats(&m)

	return c.Status(httpCode).JSON(fiber.Map{
		"status":      status,
		"uptime":      time.Since(h.startTime).String(),
		"redis":       redisOk,
		"timescaledb": dbOk,
		"memory": fiber.Map{
			"allocMb":      float64(m.Alloc) / 1024 / 1024,
			"totalAllocMb": float64(m.TotalAlloc) / 1024 / 1024,
			"goroutines":   runtime.NumGoroutine(),
		},
	})
}

func (h *HealthHandler) Ready(c *fiber.Ctx) error {
	ctx, cancel := context.WithTimeout(context.Background(), 500*time.Millisecond)
	defer cancel()

	if h.producer == nil || h.producer.Ping(ctx) != nil {
		return c.Status(fiber.StatusServiceUnavailable).SendString("Redis not ready")
	}
	return c.SendString("READY")
}

func (h *HealthHandler) Metrics(c *fiber.Ctx) error {
	stats := h.producer.GetStats()
	return c.JSON(fiber.Map{
		"producer": stats,
		"uptime":   time.Since(h.startTime).Seconds(),
	})
}

func (h *HealthHandler) Close() {
	if h.db != nil {
		_ = h.db.Close()
	}
}
