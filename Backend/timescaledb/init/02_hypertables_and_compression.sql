-- 02_hypertables_and_compression.sql: TimescaleDB Hypertable Setup and Compression Policies

-- 1. Convert telemetry_samples into a TimescaleDB Hypertable with 1-hour chunks
SELECT create_hypertable(
    'telemetry_samples',
    'time',
    chunk_time_interval => INTERVAL '1 hour',
    if_not_exists => TRUE
);

-- 2. Performance indexes on hypertable
CREATE INDEX IF NOT EXISTS idx_telemetry_lap_dist ON telemetry_samples(lap_id, distance_norm);
CREATE INDEX IF NOT EXISTS idx_telemetry_session_time ON telemetry_samples(session_id, time DESC);
CREATE INDEX IF NOT EXISTS idx_telemetry_packet ON telemetry_samples(session_id, packet_id);

-- 3. Enable TimescaleDB Columnar Compression (Gorilla float compression + Delta-of-delta)
ALTER TABLE telemetry_samples SET (
    timescaledb.compress,
    timescaledb.compress_segmentby = 'session_id, lap_id',
    timescaledb.compress_orderby = 'time ASC'
);

-- 4. Automatically compress chunks older than 2 hours (achieving 90-94% storage reduction)
SELECT add_compression_policy('telemetry_samples', INTERVAL '2 hours', if_not_exists => TRUE);
