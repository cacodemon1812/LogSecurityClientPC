-- Config change log (diff between snapshots)
CREATE TABLE config_changes (
    id              BIGSERIAL PRIMARY KEY,
    hostname        TEXT NOT NULL,
    changed_at      TIMESTAMPTZ NOT NULL,
    field_path      TEXT NOT NULL,
    old_value       TEXT,
    new_value       TEXT,
    snapshot_before UUID REFERENCES collection_snapshots(id),
    snapshot_after  UUID REFERENCES collection_snapshots(id)
);

CREATE INDEX idx_changes_hostname ON config_changes(hostname, changed_at DESC);
CREATE INDEX idx_changes_field ON config_changes(field_path);
