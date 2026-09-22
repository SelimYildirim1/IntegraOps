-- Scale: indexes for the organization-wide audit view and for outbox housekeeping.
CREATE INDEX audit_log_project_time ON audit_log(project_id, created_at DESC);
CREATE INDEX outbox_messages_published ON outbox_messages(published_at) WHERE published_at IS NOT NULL;
