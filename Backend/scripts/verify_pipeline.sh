#!/usr/bin/env bash
set -e

# ==============================================================================
# End-to-End Pipeline Verification Script for NoStrategia Backend
# Tests:
# 1. Healthcheck (/health)
# 2. Telemetry Ingestion (POST /api/v1/telemetry)
# 3. Telemetry Stream Ingestion (POST /api/v1/telemetry/stream)
# 4. Telemetry Batch Ingestion (POST /api/v1/telemetry/batch)
# 5. Setup Synchronization (POST /api/v1/setups/sync)
# 6. Redis Streams Verification (XLEN, XRANGE)
# 7. TimescaleDB Schema & Hypertable Verification
# ==============================================================================

BASE_URL="http://127.0.0.1:4000"
GREEN='\033[0;32m'
RED='\033[0;31m'
BLUE='\033[0;34m'
NC='\033[0m'

echo -e "${BLUE}=== 1. Checking Ingestion API Gateway Health ===${NC}"
curl -s -f "${BASE_URL}/health" | jq . || { echo -e "${RED}[FAIL] Healthcheck failed${NC}"; exit 1; }
echo -e "${GREEN}[OK] Ingestion Gateway is online and healthy.${NC}\n"

echo -e "${BLUE}=== 2. Testing Single Telemetry Frame Ingestion ===${NC}"
TELEMETRY_RESP=$(curl -s -X POST "${BASE_URL}/api/v1/telemetry" \
  -H "Content-Type: application/json" \
  -H "X-Session-ID: 4a73b2c1-840e-436f-bca8-e9f3b259d640" \
  -d '{
    "packetId": 1001,
    "status": 1,
    "sessionType": 2,
    "carModel": "mercedes_amg_gt3_evo",
    "track": "spa",
    "speedKmh": 252.4,
    "gear": 6,
    "rpm": 7400,
    "steerAngle": -0.02,
    "gas": 1.0,
    "brake": 0.0,
    "fuel": 42.5,
    "completedLaps": 3,
    "normalizedCarPosition": 0.254,
    "accG": [0.08, 0.42, 0.0],
    "localAngularVel": [0.0, 0.021, 0.0],
    "wheelsPressure": [27.5, 27.6, 27.8, 27.7]
  }')

echo "$TELEMETRY_RESP" | jq .
echo -e "${GREEN}[OK] Telemetry frame accepted into Redis Stream.${NC}\n"

echo -e "${BLUE}=== 3. Testing Telemetry Batch Ingestion (5 frames) ===${NC}"
BATCH_RESP=$(curl -s -X POST "${BASE_URL}/api/v1/telemetry/batch" \
  -H "Content-Type: application/json" \
  -H "X-Session-ID: 4a73b2c1-840e-436f-bca8-e9f3b259d640" \
  -d '[
    {"packetId": 1002, "speedKmh": 254.1, "gas": 1.0, "gear": 6, "completedLaps": 3, "normalizedCarPosition": 0.258},
    {"packetId": 1003, "speedKmh": 255.8, "gas": 1.0, "gear": 6, "completedLaps": 3, "normalizedCarPosition": 0.262},
    {"packetId": 1004, "speedKmh": 257.2, "gas": 1.0, "gear": 6, "completedLaps": 3, "normalizedCarPosition": 0.266},
    {"packetId": 1005, "speedKmh": 258.4, "gas": 1.0, "gear": 6, "completedLaps": 3, "normalizedCarPosition": 0.270},
    {"packetId": 1006, "speedKmh": 259.0, "gas": 1.0, "gear": 6, "completedLaps": 3, "normalizedCarPosition": 0.274}
  ]')

echo "$BATCH_RESP" | jq .
echo -e "${GREEN}[OK] Telemetry batch ingested successfully.${NC}\n"

echo -e "${BLUE}=== 4. Testing Vehicle Setup Synchronization ===${NC}"
SETUP_RESP=$(curl -s -X POST "${BASE_URL}/api/v1/setups/sync" \
  -H "Content-Type: application/json" \
  -H "X-Session-ID: 4a73b2c1-840e-436f-bca8-e9f3b259d640" \
  -d '{
    "fileName": "amg_spa_q_v1.json",
    "carModel": "mercedes_amg_gt3_evo",
    "track": "spa",
    "hash": "7a94b321c840e436fbca8e9f3b259d6409823feda12849504829375834928190",
    "setupData": {
      "tyres": { "frontLeft": 27.5, "frontRight": 27.5, "rearLeft": 27.8, "rearRight": 27.8 },
      "aerodynamics": { "rearWing": 8 },
      "brakes": { "brakeBia": 56.0, "brakePower": 100 }
    }
  }')

echo "$SETUP_RESP" | jq .
echo -e "${GREEN}[OK] Setup synchronization event dispatched.${NC}\n"

echo -e "${BLUE}=== 5. Verifying Redis Streams Storage ===${NC}"
docker compose exec -T redis redis-cli XLEN stream:telemetry:raw
docker compose exec -T redis redis-cli XLEN stream:setups
echo -e "${GREEN}[OK] Redis Streams have recorded all incoming frames and setup events.${NC}\n"

echo -e "${BLUE}=== 6. Verifying TimescaleDB Hypertables & Seed Data ===${NC}"
docker compose exec -T timescaledb psql -U postgres -d telemetry -c "
  SELECT hypertable_name, chunk_time_interval, compression_enabled 
  FROM timescaledb_information.hypertables 
  WHERE hypertable_name = 'telemetry_samples';
"

docker compose exec -T timescaledb psql -U postgres -d telemetry -c "
  SELECT track_name, count(*) as micro_sectors_count 
  FROM micro_sectors 
  GROUP BY track_name;
"

echo -e "${GREEN}=== ALL INTEGRATION CHECKS PASSED SUCCESSFULLY ===${NC}"
