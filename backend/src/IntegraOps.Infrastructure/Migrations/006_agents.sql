-- Aşama 8: transfer agents report their presence, version and queue depth.
CREATE TABLE agent_heartbeats (
    project_id uuid NOT NULL REFERENCES projects(id),
    name varchar(64) NOT NULL,
    version varchar(64) NOT NULL,
    host varchar(200) NOT NULL DEFAULT '',
    source_kind varchar(32) NOT NULL DEFAULT '',
    pending integer NOT NULL DEFAULT 0,
    sent_total bigint NOT NULL DEFAULT 0,
    dead_total bigint NOT NULL DEFAULT 0,
    retry_total bigint NOT NULL DEFAULT 0,
    last_error varchar(200),
    last_success_at timestamptz,
    config jsonb NOT NULL DEFAULT '{}'::jsonb,
    first_seen_at timestamptz NOT NULL DEFAULT clock_timestamp(),
    seen_at timestamptz NOT NULL DEFAULT clock_timestamp(),
    PRIMARY KEY (project_id, name)
);
