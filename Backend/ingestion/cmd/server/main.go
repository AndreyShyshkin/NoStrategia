package main

import (
	"context"
	"fmt"
	"log"
	"os"
	"os/signal"
	"syscall"
	"time"

	"github.com/gofiber/fiber/v2"
	"github.com/gofiber/fiber/v2/middleware/cors"
	"github.com/gofiber/fiber/v2/middleware/logger"
	"github.com/gofiber/fiber/v2/middleware/recover"
	"github.com/nostrategia/backend/ingestion/internal/config"
	"github.com/nostrategia/backend/ingestion/internal/handlers"
	"github.com/nostrategia/backend/ingestion/internal/redis"
)

func main() {
	cfg := config.Load()

	log.Printf("[INIT] Starting Ingestion API Gateway on %s:%s", cfg.Host, cfg.Port)
	log.Printf("[INIT] Redis broker target: %s (Stream: %s)", cfg.RedisAddr, cfg.TelemetryStream)

	ctx, cancel := context.WithTimeout(context.Background(), 5*time.Second)
	defer cancel()

	producer, err := redis.NewStreamProducer(ctx, cfg)
	if err != nil {
		log.Fatalf("[FATAL] Could not initialize Redis stream producer: %v", err)
	}
	defer producer.Close()

	app := fiber.New(fiber.Config{
		ServerHeader:          "NoStrategia-Ingestion/1.0",
		DisableStartupMessage: false,
		Prefork:               cfg.Prefork,
		ReadTimeout:           2 * time.Second,
		WriteTimeout:          2 * time.Second,
		IdleTimeout:           10 * time.Second,
		BodyLimit:             16 * 1024 * 1024, // 16MB for batch payloads & full car setups
	})

	app.Use(recover.New())
	app.Use(cors.New(cors.Config{
		AllowOrigins: "*",
		AllowHeaders: "Origin, Content-Type, Accept, X-Session-ID",
	}))
	app.Use(logger.New(logger.Config{
		Format:     "[${time}] ${status} - ${latency} | ${method} ${path}\n",
		TimeFormat: "15:04:05.000",
		Output:     os.Stdout,
		Next: func(c *fiber.Ctx) bool {
			// Suppress high-frequency logging on telemetry stream to prevent I/O bottleneck
			path := c.Path()
			return path == "/health" || path == "/ready" || path == "/api/v1/telemetry" || path == "/api/v1/telemetry/stream"
		},
	}))

	telemetryHandler := handlers.NewTelemetryHandler(producer)
	setupHandler := handlers.NewSetupHandler(producer)
	healthHandler := handlers.NewHealthHandler(producer, cfg.DatabaseURL)
	defer healthHandler.Close()

	// Diagnostic & Health Endpoints
	app.Get("/health", healthHandler.Health)
	app.Get("/ready", healthHandler.Ready)
	app.Get("/metrics", healthHandler.Metrics)

	// Ingestion API Routes (v1)
	v1 := app.Group("/api/v1")
	{
		// Telemetry Streaming Routes
		v1.Post("/telemetry", telemetryHandler.IngestTelemetry)
		v1.Post("/telemetry/stream", telemetryHandler.IngestTelemetry)
		v1.Post("/telemetry/batch", telemetryHandler.IngestBatchTelemetry)

		// Vehicle Setup Revision Routes
		v1.Post("/setups/sync", setupHandler.SyncSetup)
		v1.Post("/setups", setupHandler.SyncSetup)
	}

	// Graceful Shutdown Channel
	shutdownChan := make(chan os.Signal, 1)
	signal.Notify(shutdownChan, os.Interrupt, syscall.SIGTERM)

	addr := fmt.Sprintf("%s:%s", cfg.Host, cfg.Port)
	go func() {
		if err := app.Listen(addr); err != nil {
			log.Printf("[INFO] Server stopped: %v", err)
		}
	}()

	<-shutdownChan
	log.Println("[INFO] Gracefully shutting down Ingestion API Gateway...")

	shutdownCtx, shutdownCancel := context.WithTimeout(context.Background(), 5*time.Second)
	defer shutdownCancel()

	if err := app.ShutdownWithContext(shutdownCtx); err != nil {
		log.Printf("[WARN] Forced shutdown error: %v", err)
	}

	log.Println("[INFO] Server exit complete.")
}
