# Multi-stage Dockerfile for high-performance Ingestion API Gateway
FROM golang:alpine AS builder

WORKDIR /build

# Pre-fetch dependencies for layer caching
COPY go.mod go.sum ./
RUN go mod download

# Copy source tree and compile static binary
COPY . .
RUN go mod tidy && CGO_ENABLED=0 GOOS=linux go build -ldflags="-s -w" -o /build/bin/ingestion-api ./cmd/server

# Minimal production image
FROM alpine:3.21

RUN apk --no-cache add ca-certificates curl tzdata && \
    addgroup -S appgroup && adduser -S appuser -G appgroup

WORKDIR /app
COPY --from=builder /build/bin/ingestion-api /app/ingestion-api

USER appuser

EXPOSE 4000

HEALTHCHECK --interval=5s --timeout=3s --start-period=5s --retries=3 \
    CMD curl -f http://127.0.0.1:4000/ready || exit 1

ENTRYPOINT ["/app/ingestion-api"]
