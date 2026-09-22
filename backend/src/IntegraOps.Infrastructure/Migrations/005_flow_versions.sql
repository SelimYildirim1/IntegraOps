-- V1.2: flow versioning, business calendars, group allowances, realised step durations.

CREATE TABLE business_calendars (
    id uuid PRIMARY KEY,
    project_id uuid NOT NULL REFERENCES projects(id),
    name varchar(120) NOT NULL,
    timezone varchar(64) NOT NULL,
    work_days smallint[] NOT NULL,           -- ISO weekdays, 1 = Monday .. 7 = Sunday
    start_time time NOT NULL,
    end_time time NOT NULL,
    created_at timestamptz NOT NULL DEFAULT clock_timestamp(),
    UNIQUE (project_id, name),
    CHECK (end_time > start_time)
);
CREATE TABLE calendar_holidays (
    calendar_id uuid NOT NULL REFERENCES business_calendars(id) ON DELETE CASCADE,
    day date NOT NULL,
    name varchar(120) NOT NULL DEFAULT '',
    PRIMARY KEY (calendar_id, day)
);

CREATE TABLE flow_versions (
    id uuid PRIMARY KEY,
    flow_id uuid NOT NULL REFERENCES flows(id),
    version integer NOT NULL CHECK (version >= 1),
    state varchar(16) NOT NULL CHECK (state IN ('draft', 'published')),
    effective_from timestamptz,              -- NULL while draft
    calendar_id uuid REFERENCES business_calendars(id),
    note varchar(500) NOT NULL DEFAULT '',
    created_by uuid REFERENCES app_users(id),
    created_at timestamptz NOT NULL DEFAULT clock_timestamp(),
    published_at timestamptz,
    UNIQUE (flow_id, version),
    CHECK ((state = 'draft') = (effective_from IS NULL))
);
-- At most one draft per flow.
CREATE UNIQUE INDEX flow_versions_one_draft ON flow_versions(flow_id) WHERE state = 'draft';
CREATE INDEX flow_versions_effective ON flow_versions(flow_id, effective_from DESC) WHERE state = 'published';

CREATE TABLE flow_version_steps (
    version_id uuid NOT NULL REFERENCES flow_versions(id) ON DELETE CASCADE,
    code varchar(64) NOT NULL,
    name varchar(160) NOT NULL,
    position smallint NOT NULL CHECK (position BETWEEN 0 AND 29),
    max_delay_seconds integer,
    PRIMARY KEY (version_id, code),
    UNIQUE (version_id, position),
    CHECK ((position = 0 AND max_delay_seconds IS NULL) OR
           (position > 0 AND max_delay_seconds IS NOT NULL AND max_delay_seconds BETWEEN 1 AND 2592000))
);
-- Group-specific allowances (for example customer group "vip"). 30 days max, like steps.
CREATE TABLE flow_step_overrides (
    version_id uuid NOT NULL,
    step_code varchar(64) NOT NULL,
    group_code varchar(64) NOT NULL,
    max_delay_seconds integer NOT NULL CHECK (max_delay_seconds BETWEEN 1 AND 2592000),
    PRIMARY KEY (version_id, step_code, group_code),
    FOREIGN KEY (version_id, step_code) REFERENCES flow_version_steps(version_id, code) ON DELETE CASCADE
);

-- Every existing flow becomes version 1, effective since its creation.
INSERT INTO flow_versions (id, flow_id, version, state, effective_from, created_at, published_at)
SELECT gen_random_uuid(), f.id, 1, 'published', f.created_at, f.created_at, f.created_at FROM flows f;
INSERT INTO flow_version_steps (version_id, code, name, position, max_delay_seconds)
SELECT v.id, s.code, s.name, s.position, s.max_delay_seconds
FROM flow_steps s JOIN flow_versions v ON v.flow_id = s.flow_id AND v.version = 1;

ALTER TABLE transactions ADD COLUMN flow_version_id uuid REFERENCES flow_versions(id);
ALTER TABLE transactions ADD COLUMN group_code varchar(64);
UPDATE transactions t SET flow_version_id = v.id FROM flow_versions v WHERE v.flow_id = t.flow_id AND v.version = 1;
ALTER TABLE transactions ALTER COLUMN flow_version_id SET NOT NULL;

-- Steps now live in versions; flow_steps stays only as the version-1 snapshot of old flows.
ALTER TABLE transaction_events DROP CONSTRAINT transaction_events_flow_id_step_code_fkey;
ALTER TABLE transaction_delays DROP CONSTRAINT transaction_delays_flow_id_step_code_fkey;

-- Realised step durations, written when a step completes; the source of breach reports.
CREATE TABLE transaction_step_stats (
    transaction_id uuid NOT NULL REFERENCES transactions(id),
    project_id uuid NOT NULL,
    step_code varchar(64) NOT NULL,
    started_at timestamptz NOT NULL,
    completed_at timestamptz NOT NULL,
    elapsed_seconds bigint NOT NULL,
    allowance_seconds integer NOT NULL,
    breached boolean NOT NULL,
    PRIMARY KEY (transaction_id, step_code)
);
CREATE INDEX step_stats_breaches ON transaction_step_stats(project_id, completed_at DESC) WHERE breached;
CREATE INDEX step_stats_project_step ON transaction_step_stats(project_id, step_code, completed_at DESC);
