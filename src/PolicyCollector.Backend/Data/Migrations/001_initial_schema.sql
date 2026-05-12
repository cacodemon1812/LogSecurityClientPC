-- Extension
CREATE EXTENSION IF NOT EXISTS "uuid-ossp";
CREATE EXTENSION IF NOT EXISTS timescaledb CASCADE;

-- API keys
CREATE TABLE api_keys (
    id          SERIAL PRIMARY KEY,
    key_hash    TEXT NOT NULL,
    prefix      TEXT NOT NULL,
    active      BOOLEAN NOT NULL DEFAULT TRUE,
    created_at  TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    expires_at  TIMESTAMPTZ,
    description TEXT
);
CREATE INDEX idx_api_keys_prefix ON api_keys(prefix) WHERE active = TRUE;

-- Main snapshots table
CREATE TABLE collection_snapshots (
    id              UUID PRIMARY KEY DEFAULT uuid_generate_v4(),
    collection_id   UUID NOT NULL UNIQUE,
    hostname        TEXT NOT NULL,
    domain          TEXT,
    os_version      TEXT,
    agent_version   TEXT,
    schema_version  TEXT NOT NULL DEFAULT '1.0',
    collected_at    TIMESTAMPTZ NOT NULL,
    received_at     TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    payload         JSONB NOT NULL
);

CREATE INDEX idx_snapshots_hostname ON collection_snapshots(hostname);
CREATE INDEX idx_snapshots_collected_at ON collection_snapshots(collected_at DESC);
CREATE INDEX idx_snapshots_payload ON collection_snapshots USING GIN(payload);

-- TimescaleDB hypertable
SELECT create_hypertable('collection_snapshots', 'collected_at',
    chunk_time_interval => INTERVAL '7 days',
    if_not_exists => TRUE);

-- Host latest (denormalized for quick lookup)
CREATE TABLE host_latest (
    hostname        TEXT PRIMARY KEY,
    domain          TEXT,
    last_seen       TIMESTAMPTZ NOT NULL,
    agent_version   TEXT,
    os_version      TEXT,
    snapshot_id     UUID REFERENCES collection_snapshots(id),
    status          TEXT NOT NULL DEFAULT 'unknown'
);

-- Policy violations
CREATE TABLE policy_violations (
    id              BIGSERIAL PRIMARY KEY,
    snapshot_id     UUID NOT NULL REFERENCES collection_snapshots(id),
    hostname        TEXT NOT NULL,
    detected_at     TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    rule_id         TEXT NOT NULL,
    severity        TEXT NOT NULL CHECK (severity IN ('critical','high','medium','low')),
    message         TEXT NOT NULL,
    expected        TEXT,
    actual          TEXT,
    resolved        BOOLEAN NOT NULL DEFAULT FALSE,
    resolved_at     TIMESTAMPTZ
);

CREATE INDEX idx_violations_hostname ON policy_violations(hostname);
CREATE INDEX idx_violations_rule_id ON policy_violations(rule_id);
CREATE INDEX idx_violations_unresolved ON policy_violations(hostname, detected_at)
    WHERE resolved = FALSE;
