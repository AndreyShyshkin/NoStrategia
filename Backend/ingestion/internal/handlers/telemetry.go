package handlers

import (
	"context"
	"time"

	"github.com/gofiber/fiber/v2"
	"github.com/nostrategia/backend/ingestion/internal/models"
	"github.com/nostrategia/backend/ingestion/internal/redis"
)

type TelemetryHandler struct {
	producer redis.StreamProducer
}

func NewTelemetryHandler(producer redis.StreamProducer) *TelemetryHandler {
	return &TelemetryHandler{producer: producer}
}

// IngestTelemetry handles single frame push: POST /api/v1/telemetry & POST /api/v1/telemetry/stream
func (h *TelemetryHandler) IngestTelemetry(c *fiber.Ctx) error {
	var payload models.TelemetryPayload
	if err := c.BodyParser(&payload); err != nil {
		return c.Status(fiber.StatusBadRequest).JSON(fiber.Map{
			"error":   "Malformed JSON payload",
			"details": err.Error(),
		})
	}

	// Session header fallback if not present in body
	if payload.SessionID == "" {
		if headerSession := c.Get("X-Session-ID"); headerSession != "" {
			payload.SessionID = headerSession
		}
	}

	ctx, cancel := context.WithTimeout(context.Background(), 300*time.Millisecond)
	defer cancel()

	streamID, err := h.producer.PublishTelemetry(ctx, &payload)
	if err != nil {
		if err == redis.ErrCircuitOpen {
			return c.Status(fiber.StatusServiceUnavailable).JSON(fiber.Map{
				"error":   "Ingestion circuit open, redis broker overloaded",
				"status":  "degraded",
				"retryIn": "5s",
			})
		}
		return c.Status(fiber.StatusInternalServerError).JSON(fiber.Map{
			"error": "Failed to buffer telemetry into stream",
		})
	}

	return c.Status(fiber.StatusAccepted).JSON(fiber.Map{
		"status":   "accepted",
		"streamId": streamID,
		"packetId": payload.PacketID,
	})
}

// IngestBatchTelemetry handles array of frames: POST /api/v1/telemetry/batch
func (h *TelemetryHandler) IngestBatchTelemetry(c *fiber.Ctx) error {
	var payloads []*models.TelemetryPayload
	if err := c.BodyParser(&payloads); err != nil {
		return c.Status(fiber.StatusBadRequest).JSON(fiber.Map{
			"error":   "Malformed batch JSON payload",
			"details": err.Error(),
		})
	}

	if len(payloads) == 0 {
		return c.Status(fiber.StatusBadRequest).JSON(fiber.Map{
			"error": "Empty payload batch",
		})
	}

	headerSession := c.Get("X-Session-ID")
	for _, p := range payloads {
		if p.SessionID == "" && headerSession != "" {
			p.SessionID = headerSession
		}
	}

	ctx, cancel := context.WithTimeout(context.Background(), 1*time.Second)
	defer cancel()

	streamIDs, err := h.producer.PublishTelemetryBatch(ctx, payloads)
	if err != nil {
		return c.Status(fiber.StatusServiceUnavailable).JSON(fiber.Map{
			"error": "Failed to buffer telemetry batch",
		})
	}

	return c.Status(fiber.StatusAccepted).JSON(fiber.Map{
		"status":   "accepted",
		"count":    len(streamIDs),
		"firstId":  streamIDs[0],
		"lastId":   streamIDs[len(streamIDs)-1],
	})
}
