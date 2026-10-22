-- 03_continuous_aggregates.sql: TimescaleDB Continuous Aggregates for Instant Dashboard Queries

-- 1. Materialized view for per-second lap telemetry summaries
CREATE MATERIALIZED VIEW IF NOT EXISTS lap_telemetry_summary
WITH (timescaledb.continuous) AS
SELECT 
    lap_id,
    time_bucket('1 second', time) AS bucket,
    AVG(speed_kmh) AS avg_speed,
    MAX(speed_kmh) AS top_speed,
    AVG(gas) AS avg_throttle,
    AVG(brake) AS avg_brake,
    AVG(press_fl) AS avg_press_fl,
    AVG(press_fr) AS avg_press_fr,
    AVG(press_rl) AS avg_press_rl,
    AVG(press_rr) AS avg_press_rr
FROM telemetry_samples
GROUP BY lap_id, bucket
WITH NO DATA;

-- Automated refresh policy for lap summary
SELECT add_continuous_aggregate_policy(
    'lap_telemetry_summary',
    start_offset => INTERVAL '10 minutes',
    end_offset => INTERVAL '1 second',
    schedule_interval => INTERVAL '10 seconds',
    if_not_exists => TRUE
);

-- 2. Materialized view for session-level pace and fuel degradation
CREATE MATERIALIZED VIEW IF NOT EXISTS session_pace_summary
WITH (timescaledb.continuous) AS
SELECT 
    session_id,
    time_bucket('1 minute', time) AS bucket,
    AVG(speed_kmh) AS avg_speed,
    MAX(speed_kmh) AS top_speed,
    COUNT(*) AS samples_count,
    MIN(fuel) AS fuel_min,
    MAX(fuel) AS fuel_max
FROM telemetry_samples
GROUP BY session_id, bucket
WITH NO DATA;

-- Automated refresh policy for session pace summary
SELECT add_continuous_aggregate_policy(
    'session_pace_summary',
    start_offset => INTERVAL '1 hour',
    end_offset => INTERVAL '10 seconds',
    schedule_interval => INTERVAL '30 seconds',
    if_not_exists => TRUE
);
