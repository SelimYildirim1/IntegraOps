CREATE TABLE app_users (
    id uuid PRIMARY KEY,
    email varchar(254) NOT NULL UNIQUE,
    name varchar(120) NOT NULL,
    password_hash text NOT NULL,
    failed_logins integer NOT NULL DEFAULT 0,
    locked_until timestamptz,
    created_at timestamptz NOT NULL DEFAULT clock_timestamp()
);
CREATE TABLE project_members (
    project_id uuid NOT NULL REFERENCES projects(id),
    user_id uuid NOT NULL REFERENCES app_users(id),
    role varchar(16) NOT NULL CHECK (role IN ('owner', 'operator', 'viewer')),
    PRIMARY KEY (project_id, user_id)
);
CREATE TABLE user_sessions (
    token_hash char(64) PRIMARY KEY,
    user_id uuid NOT NULL REFERENCES app_users(id),
    expires_at timestamptz NOT NULL,
    created_at timestamptz NOT NULL DEFAULT clock_timestamp()
);
CREATE INDEX user_sessions_user ON user_sessions(user_id);
ALTER TABLE api_keys ADD COLUMN name varchar(120) NOT NULL DEFAULT 'Önceki proje anahtarı';
ALTER TABLE api_keys ADD COLUMN scope varchar(16) NOT NULL DEFAULT 'legacy'
    CHECK (scope IN ('legacy', 'ingest', 'read'));
ALTER TABLE transactions ADD COLUMN alarm_checked_at timestamptz;
CREATE INDEX transactions_alarm_backfill ON transactions(id) WHERE alarm_checked_at IS NULL;
CREATE TABLE alarms (
    id uuid PRIMARY KEY,
    project_id uuid NOT NULL REFERENCES projects(id),
    transaction_id uuid NOT NULL REFERENCES transactions(id),
    kind varchar(16) NOT NULL CHECK (kind IN ('overdue', 'failed')),
    step_code varchar(80) NOT NULL,
    error text,
    deadline_at timestamptz,
    opened_at timestamptz NOT NULL DEFAULT clock_timestamp(),
    last_seen_at timestamptz NOT NULL DEFAULT clock_timestamp(),
    resolved_at timestamptz,
    resolution varchar(40),
    acknowledged_at timestamptz,
    acknowledged_by uuid REFERENCES app_users(id)
);
CREATE UNIQUE INDEX alarms_one_active ON alarms(project_id, transaction_id, kind, step_code)
    WHERE resolved_at IS NULL;
CREATE INDEX alarms_project ON alarms(project_id, opened_at DESC, id);
CREATE TABLE alarm_events (
    id uuid PRIMARY KEY,
    alarm_id uuid NOT NULL REFERENCES alarms(id),
    kind varchar(32) NOT NULL,
    created_at timestamptz NOT NULL DEFAULT clock_timestamp(),
    actor_id uuid REFERENCES app_users(id)
);
CREATE TABLE notification_channels (
    id uuid PRIMARY KEY,
    project_id uuid NOT NULL REFERENCES projects(id),
    name varchar(120) NOT NULL,
    kind varchar(16) NOT NULL CHECK (kind IN ('email', 'webhook')),
    target_protected text NOT NULL,
    secret_protected text,
    enabled boolean NOT NULL DEFAULT true,
    created_at timestamptz NOT NULL DEFAULT clock_timestamp(),
    UNIQUE (project_id, name)
);
CREATE TABLE notification_deliveries (
    id uuid PRIMARY KEY,
    event_id uuid NOT NULL REFERENCES alarm_events(id),
    channel_id uuid NOT NULL REFERENCES notification_channels(id),
    body text NOT NULL,
    state varchar(16) NOT NULL DEFAULT 'pending' CHECK (state IN ('pending','sending','sent','dead','cancelled')),
    attempts integer NOT NULL DEFAULT 0,
    available_at timestamptz NOT NULL DEFAULT clock_timestamp(),
    lease_token uuid,
    lease_until timestamptz,
    last_error varchar(120),
    sent_at timestamptz,
    created_at timestamptz NOT NULL DEFAULT clock_timestamp(),
    UNIQUE (event_id, channel_id)
);
CREATE INDEX notification_delivery_due ON notification_deliveries(available_at)
    WHERE state IN ('pending', 'sending');
CREATE TABLE worker_heartbeats (
    name varchar(100) PRIMARY KEY,
    seen_at timestamptz NOT NULL
);
CREATE TABLE audit_log (
    id bigint GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    project_id uuid NOT NULL REFERENCES projects(id),
    actor_id uuid REFERENCES app_users(id),
    action varchar(100) NOT NULL,
    target varchar(200) NOT NULL,
    created_at timestamptz NOT NULL DEFAULT clock_timestamp()
);
