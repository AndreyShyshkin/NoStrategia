package handlers

import (
	"context"
	"time"

	"github.com/gofiber/fiber/v2"
	"github.com/nostrategia/backend/ingestion/internal/models"
	"github.com/nostrategia/backend/ingestion/internal/redis"
)

type SetupHandler struct {
	producer redis.StreamProducer
}

func NewSetupHandler(producer redis.StreamProducer) *SetupHandler {
	return &SetupHandler{producer: producer}
}

// SyncSetup handles vehicle setup sync events: POST /api/v1/setups/sync & POST /api/v1/setups
func (h *SetupHandler) SyncSetup(c *fiber.Ctx) error {
	var payload models.SetupPayload
	if err := c.BodyParser(&payload); err != nil {
		return c.Status(fiber.StatusBadRequest).JSON(fiber.Map{
			"error":   "Malformed setup JSON payload",
			"details": err.Error(),
		})
	}

	if payload.FileName == "" || payload.Hash == "" {
		return c.Status(fiber.StatusBadRequest).JSON(fiber.Map{
			"error": "Validation failed: fileName and hash are required",
		})
	}

	if payload.SessionID == "" {
		if headerSession := c.Get("X-Session-ID"); headerSession != "" {
			payload.SessionID = headerSession
		}
	}

	ctx, cancel := context.WithTimeout(context.Background(), 500*time.Millisecond)
	defer cancel()

	streamID, err := h.producer.PublishSetup(ctx, &payload)
	if err != nil {
		return c.Status(fiber.StatusServiceUnavailable).JSON(fiber.Map{
			"error": "Failed to buffer setup revision into stream",
		})
	}

	return c.Status(fiber.StatusOK).JSON(fiber.Map{
		"status":   "synced",
		"fileName": payload.FileName,
		"hash":     payload.Hash,
		"streamId": streamID,
	})
}
