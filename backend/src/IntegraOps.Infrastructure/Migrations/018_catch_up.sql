-- A transaction whose source delivers old events is "catching up": new alarms wait until deadline_at.
ALTER TABLE transaction_delays
    DROP CONSTRAINT transaction_delays_state_check,
    DROP CONSTRAINT transaction_delays_check;
ALTER TABLE transaction_delays
    ADD CONSTRAINT transaction_delays_state_check
        CHECK (state IN ('none', 'waiting', 'overdue', 'pending_processing', 'catching_up')),
    ADD CONSTRAINT transaction_delays_check CHECK (
        (state IN ('waiting', 'overdue') AND step_code IS NOT NULL
            AND started_at IS NOT NULL AND deadline_at IS NOT NULL AND deadline_at > started_at)
        OR (state IN ('none', 'pending_processing') AND step_code IS NULL
            AND started_at IS NULL AND deadline_at IS NULL)
        OR (state = 'catching_up' AND step_code IS NULL
            AND started_at IS NULL AND deadline_at IS NOT NULL)
    );
CREATE INDEX ix_delays_catching_up ON transaction_delays(deadline_at, transaction_id)
    WHERE state = 'catching_up';

-- /bootstrap/owner made the first owner a project member only; the organization was left without members.
INSERT INTO organization_members (organization_id, user_id, role)
SELECT DISTINCT p.organization_id, m.user_id, 'owner'
FROM project_members m JOIN projects p ON p.id = m.project_id
WHERE m.role = 'owner'
  AND NOT EXISTS (SELECT 1 FROM organization_members o WHERE o.organization_id = p.organization_id)
ON CONFLICT DO NOTHING;
