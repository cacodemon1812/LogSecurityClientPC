#!/bin/bash
# Seed API key into the database after backend starts

set -e

API_KEY="${1:-dev-api-key-minimum-32-chars-here!!}"
DB_HOST="${2:-localhost}"
DB_PORT="${3:-5432}"
DB_NAME="${4:-policycollector}"
DB_USER="${5:-pcollector}"
DB_PASSWORD="${6:-devpassword}"

echo "Waiting for PostgreSQL to be ready..."
for i in {1..30}; do
    if pg_isready -h "$DB_HOST" -p "$DB_PORT" -U "$DB_USER" &>/dev/null; then
        echo "PostgreSQL is ready!"
        break
    fi
    echo "Attempt $i/30..."
    sleep 2
done

echo "Seeding API key..."
PGPASSWORD="$DB_PASSWORD" psql -h "$DB_HOST" -p "$DB_PORT" -U "$DB_USER" -d "$DB_NAME" << EOF
INSERT INTO api_keys (key_hash, prefix, active, created_at, expires_at, description)
VALUES (
    crypt('$API_KEY', gen_salt('bf')),
    '${API_KEY:0:8}',
    true,
    NOW(),
    NOW() + INTERVAL '1 year',
    'Development API Key'
) ON CONFLICT DO NOTHING;
EOF

echo "API key seeded successfully!"
