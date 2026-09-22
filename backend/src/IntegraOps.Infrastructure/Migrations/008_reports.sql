-- V2: reporting. Out-of-order step times become a data-quality signal; digests are scheduled per project.
ALTER TABLE transaction_step_stats ADD COLUMN out_of_order boolean NOT NULL DEFAULT false;
CREATE INDEX step_stats_completed ON transaction_step_stats(project_id, completed_at DESC);
CREATE INDEX ix_events_received ON transaction_events(project_id, received_at DESC);
CREATE INDEX alarms_opened ON alarms(project_id, opened_at DESC);

CREATE TABLE project_settings (
    project_id uuid PRIMARY KEY REFERENCES projects(id),
    digest_period varchar(8) NOT NULL DEFAULT 'none' CHECK (digest_period IN ('none', 'daily', 'weekly')),
    digest_hour smallint NOT NULL DEFAULT 7 CHECK (digest_hour BETWEEN 0 AND 23),
    digest_timezone varchar(64) NOT NULL DEFAULT 'Europe/Istanbul',
    updated_at timestamptz NOT NULL DEFAULT clock_timestamp(),
    updated_by uuid REFERENCES app_users(id)
);
CREATE TABLE report_digests (
    id uuid PRIMARY KEY,
    project_id uuid NOT NULL REFERENCES projects(id),
    period varchar(8) NOT NULL,
    period_from timestamptz NOT NULL,
    period_to timestamptz NOT NULL,
    recipients integer NOT NULL,
    sent_at timestamptz NOT NULL DEFAULT clock_timestamp(),
    triggered_by varchar(16) NOT NULL DEFAULT 'schedule'
);
CREATE INDEX report_digests_project ON report_digests(project_id, sent_at DESC);
