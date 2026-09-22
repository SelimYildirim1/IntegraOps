-- V2: source-to-IntegraOps reconciliation runs and their discrepancies.
CREATE TABLE reconciliation_runs (
    id uuid PRIMARY KEY,
    project_id uuid NOT NULL REFERENCES projects(id),
    name varchar(64) NOT NULL,                 -- reconciler instance name
    flow varchar(64) NOT NULL,
    window_from timestamptz NOT NULL,
    window_to timestamptz NOT NULL,
    grace_seconds integer NOT NULL,
    source_count integer NOT NULL,
    matched_count integer NOT NULL,
    new_count integer NOT NULL DEFAULT 0,      -- discrepancies first seen in this run
    repeated_count integer NOT NULL DEFAULT 0, -- already known, still present
    resolved_count integer NOT NULL DEFAULT 0, -- known before, absent now
    version varchar(64) NOT NULL DEFAULT '',
    created_at timestamptz NOT NULL DEFAULT clock_timestamp()
);
CREATE INDEX reconciliation_runs_project ON reconciliation_runs(project_id, flow, created_at DESC);

CREATE TABLE reconciliation_discrepancies (
    id uuid PRIMARY KEY,
    project_id uuid NOT NULL REFERENCES projects(id),
    flow varchar(64) NOT NULL,
    transaction_id varchar(128) NOT NULL,
    kind varchar(32) NOT NULL CHECK (kind IN ('missing_transaction', 'missing_step', 'status_mismatch')),
    step_code varchar(64) NOT NULL DEFAULT '',
    detail varchar(500) NOT NULL DEFAULT '',
    source_occurred_at timestamptz,
    first_run_id uuid NOT NULL REFERENCES reconciliation_runs(id),
    last_run_id uuid NOT NULL REFERENCES reconciliation_runs(id),
    first_seen_at timestamptz NOT NULL DEFAULT clock_timestamp(),
    last_seen_at timestamptz NOT NULL DEFAULT clock_timestamp(),
    resolved_at timestamptz,
    resolved_run_id uuid REFERENCES reconciliation_runs(id),
    seen_count integer NOT NULL DEFAULT 1,
    UNIQUE (project_id, flow, transaction_id, kind, step_code)
);
CREATE INDEX reconciliation_open ON reconciliation_discrepancies(project_id, first_seen_at DESC) WHERE resolved_at IS NULL;
