-- V2–V3: reusable integrations. A connection is one source system's link to a project.
CREATE TABLE connections (
    id uuid PRIMARY KEY,
    project_id uuid NOT NULL REFERENCES projects(id),
    name varchar(120) NOT NULL,
    kind varchar(16) NOT NULL CHECK (kind IN ('direct', 'agent', 'sdk', 'mapped')),
    description varchar(500) NOT NULL DEFAULT '',
    template jsonb,                       -- event mapping template for kind = mapped
    template_code varchar(40),            -- catalog entry the template started from
    contract_version integer NOT NULL DEFAULT 1,
    client_name varchar(120),             -- User-Agent product of the last call
    client_version varchar(64),
    events_total bigint NOT NULL DEFAULT 0,
    rejected_total bigint NOT NULL DEFAULT 0,
    last_event_at timestamptz,
    last_ping_at timestamptz,
    last_error varchar(200),
    last_error_at timestamptz,
    created_at timestamptz NOT NULL DEFAULT clock_timestamp(),
    created_by uuid REFERENCES app_users(id),
    UNIQUE (project_id, name)
);
ALTER TABLE api_keys ADD COLUMN connection_id uuid REFERENCES connections(id);
ALTER TABLE api_keys ADD COLUMN last_used_at timestamptz;
ALTER TABLE api_keys ADD COLUMN expires_at timestamptz;      -- set on rotation: old key stays valid until then
ALTER TABLE api_keys ADD COLUMN rotated_to uuid REFERENCES api_keys(id);
CREATE INDEX api_keys_connection ON api_keys(connection_id) WHERE revoked_at IS NULL;
