-- Config change log (diff between snapshots)
CREATE TABLE config_changes (
    id              BIGSERIAL PRIMARY KEY,
    hostname        TEXT NOT NULL,
    changed_at      TIMESTAMPTZ NOT NULL,
    field_path      TEXT NOT NULL,
    old_value       TEXT,
    new_value       TEXT,
    snapshot_before UUID,
    snapshot_after  UUID
);

CREATE INDEX idx_changes_hostname ON config_changes(hostname, changed_at DESC);
CREATE INDEX idx_changes_field ON config_changes(field_path);
