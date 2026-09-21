-- One current expectation per transaction. Existing business records are untouched.
CREATE TABLE transaction_delays (
    transaction_id uuid PRIMARY KEY,
    project_id uuid NOT NULL,
    flow_id uuid NOT NULL,
    state varchar(24) NOT NULL CHECK (state IN ('none', 'waiting', 'overdue', 'pending_processing')),
    step_code varchar(64),
    started_at timestamptz,
    deadline_at timestamptz,
    checked_at timestamptz NOT NULL,
    FOREIGN KEY (project_id, flow_id, transaction_id) REFERENCES transactions(project_id, flow_id, id),
    FOREIGN KEY (flow_id, step_code) REFERENCES flow_steps(flow_id, code),
    CHECK (
        (state IN ('waiting', 'overdue') AND step_code IS NOT NULL
            AND started_at IS NOT NULL AND deadline_at IS NOT NULL AND deadline_at > started_at)
        OR (state IN ('none', 'pending_processing') AND step_code IS NULL
            AND started_at IS NULL AND deadline_at IS NULL)
    )
);

CREATE INDEX ix_delays_waiting ON transaction_delays(deadline_at, transaction_id)
    WHERE state = 'waiting';
CREATE INDEX ix_events_unprocessed ON transaction_events(transaction_id)
    WHERE processed_at IS NULL;
