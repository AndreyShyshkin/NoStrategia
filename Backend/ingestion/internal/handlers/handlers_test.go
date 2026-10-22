package handlers

import (
	"bytes"
	"context"
	"encoding/json"
	"net/http"
	"net/http/httptest"
	"testing"
	"time"

	"github.com/gofiber/fiber/v2"
	"github.com/nostrategia/backend/ingestion/internal/models"
	"github.com/nostrategia/backend/ingestion/internal/redis"
)

type mockProducer struct {
	telemetryPublished int
	batchPublished     int
	setupsPublished    int
	shouldFail         bool
}

func (m *mockProducer) PublishTelemetry(ctx context.Context, payload *models.TelemetryPayload) (string, error) {
	if m.shouldFail {
		return "", redis.ErrCircuitOpen
	}
	m.telemetryPublished++
	return "1718963241000-0", nil
}

func (m *mockProducer) PublishTelemetryBatch(ctx context.Context, payloads []*models.TelemetryPayload) ([]string, error) {
	if m.shouldFail {
		return nil, redis.ErrCircuitOpen
	}
	m.batchPublished += len(payloads)
	res := make([]string, len(payloads))
	for i := range payloads {
		res[i] = "1718963241000-" + string(rune('0'+i))
	}
	return res, nil
}

func (m *mockProducer) PublishSetup(ctx context.Context, setup *models.SetupPayload) (string, error) {
	if m.shouldFail {
		return "", redis.ErrCircuitOpen
	}
	m.setupsPublished++
	return "1718963242000-0", nil
}

func (m *mockProducer) Ping(ctx context.Context) error {
	return nil
}

func (m *mockProducer) GetStats() redis.ProducerStats {
	return redis.ProducerStats{
		TotalPublishedTelemetry: int64(m.telemetryPublished),
		TotalPublishedSetups:    int64(m.setupsPublished),
		CircuitState:            "CLOSED",
	}
}

func (m *mockProducer) Close() error {
	return nil
}

func setupTestApp(mock *mockProducer) *fiber.App {
	app := fiber.New()
	tHandler := NewTelemetryHandler(mock)
	sHandler := NewSetupHandler(mock)
	hHandler := NewHealthHandler(mock, "")

	app.Get("/health", hHandler.Health)
	app.Get("/ready", hHandler.Ready)
	app.Get("/metrics", hHandler.Metrics)

	v1 := app.Group("/api/v1")
	v1.Post("/telemetry", tHandler.IngestTelemetry)
	v1.Post("/telemetry/stream", tHandler.IngestTelemetry)
	v1.Post("/telemetry/batch", tHandler.IngestBatchTelemetry)
	v1.Post("/setups/sync", sHandler.SyncSetup)
	return app
}

func TestTelemetryIngestion(t *testing.T) {
	mock := &mockProducer{}
	app := setupTestApp(mock)

	payload := models.TelemetryPayload{
		PacketID:              100,
		CarModel:              "amg_gt3_evo",
		Track:                 "spa",
		SpeedKmh:              215.5,
		Gear:                  5,
		Rpm:                   7100,
		NormalizedCarPosition: 0.42,
		Gas:                   1.0,
		Brake:                 0.0,
		SteerAngle:            -0.05,
		Timestamp:             time.Now(),
	}

	body, _ := json.Marshal(payload)
	req := httptest.NewRequest(http.MethodPost, "/api/v1/telemetry", bytes.NewReader(body))
	req.Header.Set("Content-Type", "application/json")

	resp, err := app.Test(req)
	if err != nil {
		t.Fatalf("Failed request: %v", err)
	}

	if resp.StatusCode != http.StatusAccepted {
		t.Errorf("Expected 202 Accepted, got %d", resp.StatusCode)
	}

	if mock.telemetryPublished != 1 {
		t.Errorf("Expected 1 published item, got %d", mock.telemetryPublished)
	}
}

func TestTelemetryBatchIngestion(t *testing.T) {
	mock := &mockProducer{}
	app := setupTestApp(mock)

	batch := []models.TelemetryPayload{
		{PacketID: 1, SpeedKmh: 100},
		{PacketID: 2, SpeedKmh: 105},
		{PacketID: 3, SpeedKmh: 110},
	}

	body, _ := json.Marshal(batch)
	req := httptest.NewRequest(http.MethodPost, "/api/v1/telemetry/batch", bytes.NewReader(body))
	req.Header.Set("Content-Type", "application/json")

	resp, err := app.Test(req)
	if err != nil {
		t.Fatalf("Failed request: %v", err)
	}

	if resp.StatusCode != http.StatusAccepted {
		t.Errorf("Expected 202 Accepted, got %d", resp.StatusCode)
	}

	if mock.batchPublished != 3 {
		t.Errorf("Expected 3 items in batch, got %d", mock.batchPublished)
	}
}

func TestSetupSyncIngestion(t *testing.T) {
	mock := &mockProducer{}
	app := setupTestApp(mock)

	setup := models.SetupPayload{
		FileName:  "spa_q.json",
		CarModel:  "amg_gt3_evo",
		Track:     "spa",
		Hash:      "abc123hash",
		SetupData: json.RawMessage(`{"camber": -3.5}`),
	}

	body, _ := json.Marshal(setup)
	req := httptest.NewRequest(http.MethodPost, "/api/v1/setups/sync", bytes.NewReader(body))
	req.Header.Set("Content-Type", "application/json")

	resp, err := app.Test(req)
	if err != nil {
		t.Fatalf("Failed request: %v", err)
	}

	if resp.StatusCode != http.StatusOK {
		t.Errorf("Expected 200 OK, got %d", resp.StatusCode)
	}

	if mock.setupsPublished != 1 {
		t.Errorf("Expected 1 setup published, got %d", mock.setupsPublished)
	}
}

func TestHealthCheck(t *testing.T) {
	mock := &mockProducer{}
	app := setupTestApp(mock)

	req := httptest.NewRequest(http.MethodGet, "/health", nil)
	resp, err := app.Test(req)
	if err != nil {
		t.Fatalf("Failed request: %v", err)
	}

	if resp.StatusCode != http.StatusOK {
		t.Errorf("Expected 200 OK, got %d", resp.StatusCode)
	}
}
