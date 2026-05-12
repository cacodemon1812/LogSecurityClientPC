-- Extensions
CREATE EXTENSION IF NOT EXISTS "uuid-ossp";
CREATE EXTENSION IF NOT EXISTS timescaledb CASCADE;
CREATE EXTENSION IF NOT EXISTS pgcrypto;

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
-- Note: TimescaleDB hypertables require the partition column (collected_at)
-- to be part of every unique index. The PK is composite (id, collected_at).
-- Foreign keys from other tables store snapshot_id UUID without FK enforcement.
CREATE TABLE collection_snapshots (
    id              UUID        NOT NULL DEFAULT uuid_generate_v4(),
    collection_id   UUID        NOT NULL,
    hostname        TEXT        NOT NULL,
    domain          TEXT,
    os_version      TEXT,
    agent_version   TEXT,
    schema_version  TEXT        NOT NULL DEFAULT '1.0',
    collected_at    TIMESTAMPTZ NOT NULL,
    received_at     TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    payload         JSONB       NOT NULL
);

-- Create hypertable BEFORE adding unique constraints (TimescaleDB requirement)
SELECT create_hypertable('collection_snapshots', 'collected_at',
    chunk_time_interval => INTERVAL '7 days',
    if_not_exists => TRUE);

-- Composite PK: includes partition column
ALTER TABLE collection_snapshots ADD PRIMARY KEY (id, collected_at);

-- Unique per (collection_id, collected_at): same collection_id retried with identical
-- collected_at is treated as duplicate; different time = different record (edge case only)
CREATE UNIQUE INDEX idx_snapshots_collection_id ON collection_snapshots(collection_id, collected_at);
CREATE INDEX idx_snapshots_hostname          ON collection_snapshots(hostname);
CREATE INDEX idx_snapshots_collected_at_desc ON collection_snapshots(collected_at DESC);
CREATE INDEX idx_snapshots_payload           ON collection_snapshots USING GIN(payload);

-- Host latest (denormalized for fast /hosts queries)
-- snapshot_id has no FK: hypertable PK is (id, collected_at); single-column FK impossible
CREATE TABLE host_latest (
    hostname        TEXT PRIMARY KEY,
    domain          TEXT,
    last_seen       TIMESTAMPTZ NOT NULL,
    agent_version   TEXT,
    os_version      TEXT,
    snapshot_id     UUID,
    status          TEXT NOT NULL DEFAULT 'unknown'
);
CREATE INDEX idx_host_latest_domain ON host_latest(domain);
CREATE INDEX idx_host_latest_status ON host_latest(status);

-- Policy violations
-- snapshot_id has no FK (same reason as host_latest)
CREATE TABLE policy_violations (
    id              BIGSERIAL   PRIMARY KEY,
    snapshot_id     UUID        NOT NULL,
    hostname        TEXT        NOT NULL,
    detected_at     TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    rule_id         TEXT        NOT NULL,
    severity        TEXT        NOT NULL CHECK (severity IN ('critical','high','medium','low')),
    message         TEXT        NOT NULL,
    expected        TEXT,
    actual          TEXT,
    resolved        BOOLEAN     NOT NULL DEFAULT FALSE,
    resolved_at     TIMESTAMPTZ
);
CREATE INDEX idx_violations_hostname   ON policy_violations(hostname);
CREATE INDEX idx_violations_rule_id    ON policy_violations(rule_id);
CREATE INDEX idx_violations_unresolved ON policy_violations(hostname, detected_at)
    WHERE resolved = FALSE;
