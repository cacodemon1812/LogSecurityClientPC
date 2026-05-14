-- Add stable per-host UUID to host_latest.
-- hostname remains the natural unique key used for upsert conflict resolution;
-- host_id is the external routing identifier (used in API routes and dashboard URLs).
ALTER TABLE host_latest
    ADD COLUMN IF NOT EXISTS host_id UUID NOT NULL DEFAULT uuid_generate_v4();

CREATE UNIQUE INDEX IF NOT EXISTS uq_host_latest_host_id ON host_latest (host_id);
