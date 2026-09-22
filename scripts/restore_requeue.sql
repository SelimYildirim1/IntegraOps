-- ONLY on the restored database, while its worker is stopped and broker is empty.
-- The orchestrator sets this guard after checking the isolated Compose project.
BEGIN;
DO $$
BEGIN
    IF current_setting('integraops.restore_rehearsal', true) IS DISTINCT FROM 'yes' THEN
        RAISE EXCEPTION 'Use the isolated recovery orchestrator';
    END IF;
END $$;

-- Processed events retain their publication timestamps and projections.
-- A fresh broker no longer contains pointers published before the backup.
INSERT INTO outbox_messages(project_id, event_id)
SELECT e.project_id, e.event_id
FROM transaction_events e
WHERE e.processed_at IS NULL
ON CONFLICT (project_id, event_id) DO UPDATE SET published_at = NULL;
COMMIT;
