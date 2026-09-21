-- Apply only during recovery, while the worker is stopped.
-- A restored DB can remember a RabbitMQ publish whose queue no longer exists.
-- Re-publish only events whose projection has not been processed.
BEGIN;
UPDATE outbox_messages o
SET published_at = NULL
FROM transaction_events e
WHERE e.project_id = o.project_id
  AND e.event_id = o.event_id
  AND e.processed_at IS NULL;
COMMIT;
SELECT count(*) AS pending_publications FROM outbox_messages WHERE published_at IS NULL;
