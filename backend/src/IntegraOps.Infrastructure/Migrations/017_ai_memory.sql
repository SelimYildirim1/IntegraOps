-- Dalga 2 / Faz 1: resolution memory. An operator records why an alarm happened and how it was fixed; later alarms
-- on the same flow and step show that history, and the AI assistant cites it instead of restating the alarm.
ALTER TABLE alarms ADD COLUMN cause varchar(24)
    CHECK (cause IN ('source_outage', 'source_bug', 'data_error', 'threshold', 'manual_process', 'external', 'unknown'));
ALTER TABLE alarms ADD COLUMN fix varchar(1000);
ALTER TABLE alarms ADD COLUMN cause_set_at timestamptz;
ALTER TABLE alarms ADD COLUMN cause_set_by uuid REFERENCES app_users(id) ON DELETE SET NULL;
CREATE INDEX alarms_memory ON alarms(project_id, step_code, opened_at DESC) WHERE cause IS NOT NULL;

-- Analyses may now target the open-alarm triage (no single subject) and a flow's threshold review.
ALTER TABLE ai_analyses DROP CONSTRAINT ai_analyses_subject_kind_check;
ALTER TABLE ai_analyses ADD CONSTRAINT ai_analyses_subject_kind_check
    CHECK (subject_kind IN ('alarm', 'transaction', 'digest', 'triage', 'threshold'));
