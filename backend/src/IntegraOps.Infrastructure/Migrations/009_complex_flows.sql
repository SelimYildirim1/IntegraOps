-- V2: parallel stages, optional and conditional steps, explicit skips and cancellations.
ALTER TABLE flow_version_steps ADD COLUMN stage smallint;
ALTER TABLE flow_version_steps ADD COLUMN kind varchar(16) NOT NULL DEFAULT 'required'
    CHECK (kind IN ('required', 'optional', 'conditional'));
ALTER TABLE flow_version_steps ADD COLUMN applies_to text[] NOT NULL DEFAULT '{}';
-- Existing versions are linear: each step is its own stage.
UPDATE flow_version_steps SET stage = position;
ALTER TABLE flow_version_steps ALTER COLUMN stage SET NOT NULL;
ALTER TABLE flow_version_steps ADD CONSTRAINT flow_version_steps_stage_check CHECK (stage BETWEEN 0 AND 29);
-- Optional steps may have no allowance; the first stage has none.
ALTER TABLE flow_version_steps DROP CONSTRAINT flow_version_steps_check;
ALTER TABLE flow_version_steps ADD CONSTRAINT flow_version_steps_check CHECK (
    (stage = 0 AND max_delay_seconds IS NULL)
    OR (stage > 0 AND (max_delay_seconds IS NULL AND kind = 'optional' OR max_delay_seconds BETWEEN 1 AND 2592000)));

ALTER TABLE transaction_events DROP CONSTRAINT transaction_events_status_check;
ALTER TABLE transaction_events ADD CONSTRAINT transaction_events_status_check CHECK (status IN ('Completed', 'Failed', 'Skipped', 'Cancelled'));
ALTER TABLE transactions DROP CONSTRAINT transactions_status_check;
ALTER TABLE transactions ADD CONSTRAINT transactions_status_check CHECK (status IN ('Pending', 'InProgress', 'Failed', 'Completed', 'Cancelled'));
