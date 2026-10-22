-- 01_schema.sql: Initial schema definition for Telemetry Decision Support System
-- Extensions and core relational tables

CREATE EXTENSION IF NOT EXISTS timescaledb CASCADE;
CREATE EXTENSION IF NOT EXISTS "uuid-ossp";
CREATE EXTENSION IF NOT EXISTS pgcrypto;

-- 1. Reference: Racing Sessions (Довідник сесій заїздів)
CREATE TABLE IF NOT EXISTS sessions (
    session_id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    session_uid VARCHAR(128) UNIQUE,
    car_model VARCHAR(64) NOT NULL,
    track_name VARCHAR(64) NOT NULL,
    driver_name VARCHAR(128) NOT NULL,
    session_type VARCHAR(32) NOT NULL, -- Practice, Qualify, Race
    ambient_temp NUMERIC(4, 1),
    track_temp NUMERIC(4, 1),
    started_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    ended_at TIMESTAMPTZ
);

CREATE INDEX IF NOT EXISTS idx_sessions_track ON sessions(track_name);
CREATE INDEX IF NOT EXISTS idx_sessions_driver ON sessions(driver_name);
CREATE INDEX IF NOT EXISTS idx_sessions_started_at ON sessions(started_at DESC);

-- 2. Git-like Setup Revisions (Довідник ревізій конфігурацій боліда)
CREATE TABLE IF NOT EXISTS setup_revisions (
    revision_id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    session_id UUID REFERENCES sessions(session_id) ON DELETE CASCADE,
    revision_number INT NOT NULL DEFAULT 1,
    parent_revision_id UUID REFERENCES setup_revisions(revision_id),
    file_name VARCHAR(255) NOT NULL,
    content_hash VARCHAR(64) NOT NULL, -- SHA-256
    setup_payload JSONB NOT NULL,
    diff_payload JSONB, -- Difference against parent_revision_id
    created_at TIMESTAMPTZ NOT NULL DEFAULT NOW()
);

CREATE INDEX IF NOT EXISTS idx_setup_hash ON setup_revisions(content_hash);
CREATE INDEX IF NOT EXISTS idx_setup_session ON setup_revisions(session_id);

-- 3. Lap Metadata Catalog (Довідник кіл сесії)
CREATE TABLE IF NOT EXISTS laps (
    lap_id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    session_id UUID REFERENCES sessions(session_id) ON DELETE CASCADE,
    setup_revision_id UUID REFERENCES setup_revisions(revision_id),
    lap_number INT NOT NULL,
    lap_time_ms INT,
    sector1_ms INT,
    sector2_ms INT,
    sector3_ms INT,
    is_valid BOOLEAN NOT NULL DEFAULT TRUE,
    lap_type VARCHAR(16) NOT NULL DEFAULT 'HotLap', -- OutLap, HotLap, InLap
    fuel_used_liters NUMERIC(5, 2),
    created_at TIMESTAMPTZ NOT NULL DEFAULT NOW()
);

CREATE INDEX IF NOT EXISTS idx_laps_session_num ON laps(session_id, lap_number);

-- 4. High-frequency Raw & Processed Telemetry Table (60-100 Hz)
-- Converted to TimescaleDB Hypertable in 02_hypertables_and_compression.sql
CREATE TABLE IF NOT EXISTS telemetry_samples (
    time TIMESTAMPTZ NOT NULL,
    session_id UUID,
    lap_id UUID,
    packet_id BIGINT NOT NULL,
    distance_norm REAL NOT NULL, -- Spatial coordinate 0.0 .. 1.0
    speed_kmh REAL NOT NULL,
    gas REAL NOT NULL,
    brake REAL NOT NULL,
    steer_angle REAL NOT NULL,
    clutch REAL DEFAULT 0,
    gear SMALLINT NOT NULL,
    rpm INT NOT NULL,
    yaw_rate REAL NOT NULL,
    lat_g REAL NOT NULL,
    long_g REAL NOT NULL,
    fuel REAL,
    slip_fl REAL, slip_fr REAL, slip_rl REAL, slip_rr REAL,
    press_fl REAL, press_fr REAL, press_rl REAL, press_rr REAL,
    temp_fl REAL, temp_fr REAL, temp_rl REAL, temp_rr REAL,
    susp_fl REAL, susp_fr REAL, susp_rl REAL, susp_rr REAL,
    brake_temp_fl REAL, brake_temp_fr REAL, brake_temp_rl REAL, brake_temp_rr REAL,
    pos_x REAL, pos_y REAL, pos_z REAL
);

-- 5. Track Micro-Sectors (Довідник мікро-секторів траси: 30-50 зон)
CREATE TABLE IF NOT EXISTS micro_sectors (
    micro_sector_id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    track_name VARCHAR(64) NOT NULL,
    sector_index INT NOT NULL,
    sector_name VARCHAR(128) NOT NULL,
    start_distance REAL NOT NULL,
    end_distance REAL NOT NULL,
    sector_type VARCHAR(32) NOT NULL, -- Straight, HeavyBraking, TurnIn, Apex, Acceleration
    created_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    CONSTRAINT uq_track_micro_sector UNIQUE (track_name, sector_index)
);

CREATE INDEX IF NOT EXISTS idx_micro_sectors_track ON micro_sectors(track_name, start_distance);

-- 6. Sector Performance By Lap (Проходження мікро-секторів колами)
CREATE TABLE IF NOT EXISTS lap_micro_sector_times (
    id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    lap_id UUID REFERENCES laps(lap_id) ON DELETE CASCADE,
    micro_sector_id UUID REFERENCES micro_sectors(micro_sector_id),
    duration_ms INT NOT NULL,
    entry_speed REAL NOT NULL,
    apex_speed REAL NOT NULL,
    exit_speed REAL NOT NULL,
    delta_to_theoretical_ms INT,
    anomaly_flags INT DEFAULT 0 -- Bitmask: Lockup, Oversteer, Understeer, Scrub
);

CREATE INDEX IF NOT EXISTS idx_lap_micro_sector ON lap_micro_sector_times(lap_id, micro_sector_id);

-- 7. Tactical Decisions & Race Strategy Events (Журнал тактичних рішень та стратегій)
CREATE TABLE IF NOT EXISTS race_strategy_events (
    event_id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    session_id UUID REFERENCES sessions(session_id) ON DELETE CASCADE,
    lap_number INT NOT NULL,
    strategy_mode VARCHAR(32) NOT NULL, -- Plan_A, Plan_B_Undercut, Overcut, Rain_Defense
    pit_window_open_lap INT,
    pit_window_close_lap INT,
    fuel_target_per_lap REAL,
    recommended_fuel_to_add REAL,
    projected_rejoin_position INT,
    audio_callout_text TEXT,
    created_at TIMESTAMPTZ NOT NULL DEFAULT NOW()
);

CREATE INDEX IF NOT EXISTS idx_strategy_session_lap ON race_strategy_events(session_id, lap_number);

-- 8. Driver DNA Profiles (Довгостроковий профіль водія)
CREATE TABLE IF NOT EXISTS driver_dna_profiles (
    profile_id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    driver_name VARCHAR(128) NOT NULL,
    car_model VARCHAR(64) NOT NULL,
    track_name VARCHAR(64) NOT NULL,
    consistency_score REAL NOT NULL,      -- Sigma standard deviation index
    tyre_deg_slope REAL NOT NULL,         -- Tyre degradation per lap
    trail_braking_rating REAL NOT NULL,   -- Trail-braking execution quality [0..100]
    throttle_smoothness REAL NOT NULL,    -- Throttle application derivative smoothness [0..100]
    calculated_at TIMESTAMPTZ NOT NULL DEFAULT NOW()
);

CREATE INDEX IF NOT EXISTS idx_driver_dna_lookup ON driver_dna_profiles(driver_name, car_model, track_name);
