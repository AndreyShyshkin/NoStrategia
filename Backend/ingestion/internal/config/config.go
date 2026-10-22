package config

import (
	"os"
	"strconv"
)

type Config struct {
	Port              string
	Host              string
	RedisAddr         string
	RedisPassword     string
	RedisDB           int
	TelemetryStream   string
	SetupStream       string
	StreamMaxLen      int64
	DatabaseURL       string
	Prefork           bool
	CircuitBreakerMax int
}

func Load() *Config {
	port := getEnv("PORT", "4000")
	host := getEnv("HOST", "0.0.0.0")
	redisAddr := getEnv("REDIS_ADDR", "redis:6379")
	redisPassword := getEnv("REDIS_PASSWORD", "")
	redisDB := getEnvInt("REDIS_DB", 0)
	telemetryStream := getEnv("REDIS_STREAM_TELEMETRY", "stream:telemetry:raw")
	setupStream := getEnv("REDIS_STREAM_SETUPS", "stream:setups")
	streamMaxLen := getEnvInt64("STREAM_MAXLEN", 10000)
	databaseURL := getEnv("DATABASE_URL", "postgres://postgres:postgres@timescaledb:5432/telemetry?sslmode=disable")
	prefork := getEnvBool("FIBER_PREFORK", false)
	cbMax := getEnvInt("CIRCUIT_BREAKER_MAX_FAILURES", 5)

	return &Config{
		Port:              port,
		Host:              host,
		RedisAddr:         redisAddr,
		RedisPassword:     redisPassword,
		RedisDB:           redisDB,
		TelemetryStream:   telemetryStream,
		SetupStream:       setupStream,
		StreamMaxLen:      streamMaxLen,
		DatabaseURL:       databaseURL,
		Prefork:           prefork,
		CircuitBreakerMax: cbMax,
	}
}

func getEnv(key, defaultVal string) string {
	if val, ok := os.LookupEnv(key); ok && val != "" {
		return val
	}
	return defaultVal
}

func getEnvInt(key string, defaultVal int) int {
	if val, ok := os.LookupEnv(key); ok {
		if i, err := strconv.Atoi(val); err == nil {
			return i
		}
	}
	return defaultVal
}

func getEnvInt64(key string, defaultVal int64) int64 {
	if val, ok := os.LookupEnv(key); ok {
		if i, err := strconv.ParseInt(val, 10, 64); err == nil {
			return i
		}
	}
	return defaultVal
}

func getEnvBool(key string, defaultVal bool) bool {
	if val, ok := os.LookupEnv(key); ok {
		if b, err := strconv.ParseBool(val); err == nil {
			return b
		}
	}
	return defaultVal
}
