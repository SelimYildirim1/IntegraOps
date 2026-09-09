CREATE TABLE organizations (
    id uuid PRIMARY KEY,
    name varchar(160) NOT NULL,
    created_at timestamptz NOT NULL DEFAULT clock_timestamp()
);

CREATE TABLE projects (
    id uuid PRIMARY KEY,
    organization_id uuid NOT NULL REFERENCES organizations(id),
    name varchar(160) NOT NULL,
    created_at timestamptz NOT NULL DEFAULT clock_timestamp()
);

CREATE TABLE api_keys (
    id uuid PRIMARY KEY,
    project_id uuid NOT NULL REFERENCES projects(id),
    key_hash varchar(64) NOT NULL UNIQUE,
    key_prefix varchar(16) NOT NULL,
    created_at timestamptz NOT NULL DEFAULT clock_timestamp(),
    revoked_at timestamptz
);

CREATE TABLE flows (
    id uuid PRIMARY KEY,
    project_id uuid NOT NULL REFERENCES projects(id),
    code varchar(64) NOT NULL,
    name varchar(160) NOT NULL,
    created_at timestamptz NOT NULL DEFAULT clock_timestamp(),
    UNIQUE (project_id, code),
    UNIQUE (project_id, id)
);

CREATE TABLE flow_steps (
    flow_id uuid NOT NULL REFERENCES flows(id),
    code varchar(64) NOT NULL,
    name varchar(160) NOT NULL,
    position smallint NOT NULL CHECK (position BETWEEN 0 AND 29),
    max_delay_seconds integer,
    PRIMARY KEY (flow_id, code),
    UNIQUE (flow_id, position),
    CHECK ((position = 0 AND max_delay_seconds IS NULL) OR
           (position > 0 AND max_delay_seconds IS NOT NULL AND max_delay_seconds BETWEEN 1 AND 86400))
);

CREATE TABLE transactions (
    id uuid PRIMARY KEY,
    project_id uuid NOT NULL,
    flow_id uuid NOT NULL,
    business_id varchar(128) NOT NULL,
    status varchar(20) NOT NULL DEFAULT 'Pending'
        CHECK (status IN ('Pending', 'InProgress', 'Failed', 'Completed')),
    created_at timestamptz NOT NULL DEFAULT clock_timestamp(),
    updated_at timestamptz NOT NULL DEFAULT clock_timestamp(),
    UNIQUE (project_id, flow_id, business_id),
    UNIQUE (project_id, flow_id, id),
    FOREIGN KEY (project_id, flow_id) REFERENCES flows(project_id, id)
);

CREATE TABLE transaction_events (
    project_id uuid NOT NULL,
    event_id uuid NOT NULL,
    transaction_id uuid NOT NULL,
    flow_id uuid NOT NULL,
    step_code varchar(64) NOT NULL,
    status varchar(16) NOT NULL CHECK (status IN ('Completed', 'Failed')),
    occurred_at timestamptz NOT NULL,
    received_at timestamptz NOT NULL DEFAULT clock_timestamp(),
    processed_at timestamptz,
    error varchar(2000),
    fingerprint varchar(64) NOT NULL,
    PRIMARY KEY (project_id, event_id),
    FOREIGN KEY (project_id, flow_id, transaction_id) REFERENCES transactions(project_id, flow_id, id),
    FOREIGN KEY (flow_id, step_code) REFERENCES flow_steps(flow_id, code)
);

CREATE INDEX ix_events_timeline ON transaction_events (transaction_id, occurred_at, event_id);
CREATE INDEX ix_transactions_project_status ON transactions (project_id, status, updated_at DESC);

CREATE TABLE outbox_messages (
    id bigint GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    project_id uuid NOT NULL,
    event_id uuid NOT NULL,
    created_at timestamptz NOT NULL DEFAULT clock_timestamp(),
    published_at timestamptz,
    UNIQUE (project_id, event_id),
    FOREIGN KEY (project_id, event_id) REFERENCES transaction_events(project_id, event_id)
);

CREATE INDEX ix_outbox_pending ON outbox_messages (id) WHERE published_at IS NULL;
