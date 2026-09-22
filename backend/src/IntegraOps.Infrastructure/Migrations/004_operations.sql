-- V1.1: daily operation features. Existing records keep their meaning.

-- Test data: transactions whose business id starts with TEST- are trial records
-- (panel flow tests, integration checks). They are excluded from operational numbers by default.
ALTER TABLE transactions ADD COLUMN is_test boolean NOT NULL DEFAULT false;
UPDATE transactions SET is_test = true WHERE business_id LIKE 'TEST-%';
CREATE INDEX ix_transactions_search ON transactions (project_id, business_id text_pattern_ops);
CREATE INDEX ix_transactions_created ON transactions (project_id, created_at DESC, id DESC);

-- Alarm ownership and investigation notes.
ALTER TABLE alarms ADD COLUMN assigned_to uuid REFERENCES app_users(id);
ALTER TABLE alarms ADD COLUMN assigned_at timestamptz;
CREATE TABLE alarm_notes (
    id uuid PRIMARY KEY,
    alarm_id uuid NOT NULL REFERENCES alarms(id),
    author_id uuid NOT NULL REFERENCES app_users(id),
    body varchar(2000) NOT NULL,
    created_at timestamptz NOT NULL DEFAULT clock_timestamp()
);
CREATE INDEX alarm_notes_alarm ON alarm_notes(alarm_id, created_at, id);

-- Saved list views, private to the user who saved them.
CREATE TABLE saved_views (
    id uuid PRIMARY KEY,
    project_id uuid NOT NULL REFERENCES projects(id),
    user_id uuid NOT NULL REFERENCES app_users(id),
    kind varchar(16) NOT NULL CHECK (kind IN ('transactions', 'alarms')),
    name varchar(80) NOT NULL,
    query varchar(1000) NOT NULL,
    created_at timestamptz NOT NULL DEFAULT clock_timestamp(),
    UNIQUE (project_id, user_id, kind, name)
);

-- One-time account links: invitations and password resets. Only the hash is stored.
CREATE TABLE account_tokens (
    token_hash char(64) PRIMARY KEY,
    user_id uuid NOT NULL REFERENCES app_users(id),
    kind varchar(16) NOT NULL CHECK (kind IN ('invite', 'reset')),
    expires_at timestamptz NOT NULL,
    used_at timestamptz,
    created_by uuid REFERENCES app_users(id),
    created_at timestamptz NOT NULL DEFAULT clock_timestamp()
);
CREATE INDEX account_tokens_user ON account_tokens(user_id, kind);
-- Invited accounts have no usable password until the invitation is completed.
ALTER TABLE app_users ADD COLUMN activated_at timestamptz;
UPDATE app_users SET activated_at = created_at;

-- Account e-mails are queued here and sent by the worker with the same SMTP settings as alarms.
CREATE TABLE outbound_emails (
    id uuid PRIMARY KEY,
    recipient varchar(254) NOT NULL,
    subject varchar(200) NOT NULL,
    body text NOT NULL,
    state varchar(16) NOT NULL DEFAULT 'pending' CHECK (state IN ('pending','sending','sent','dead')),
    attempts integer NOT NULL DEFAULT 0,
    available_at timestamptz NOT NULL DEFAULT clock_timestamp(),
    lease_token uuid,
    lease_until timestamptz,
    last_error varchar(120),
    sent_at timestamptz,
    created_at timestamptz NOT NULL DEFAULT clock_timestamp()
);
CREATE INDEX outbound_emails_due ON outbound_emails(available_at) WHERE state IN ('pending', 'sending');

-- Retention: NULL keeps records indefinitely. Sweeps run daily in the worker.
CREATE TABLE retention_policies (
    project_id uuid PRIMARY KEY REFERENCES projects(id),
    finished_transaction_days integer CHECK (finished_transaction_days BETWEEN 7 AND 3650),
    notification_days integer CHECK (notification_days BETWEEN 7 AND 3650),
    audit_days integer CHECK (audit_days BETWEEN 30 AND 3650),
    updated_at timestamptz NOT NULL DEFAULT clock_timestamp(),
    updated_by uuid REFERENCES app_users(id)
);
CREATE TABLE retention_runs (
    id bigint GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    project_id uuid NOT NULL REFERENCES projects(id),
    ran_at timestamptz NOT NULL DEFAULT clock_timestamp(),
    transactions_deleted integer NOT NULL,
    deliveries_deleted integer NOT NULL,
    audit_deleted integer NOT NULL
);
CREATE INDEX retention_runs_project ON retention_runs(project_id, ran_at DESC);
