-- Gemini trial profile: analyses may target a transaction as well as an alarm, a call is reserved as 'pending'
-- before the provider is contacted (atomic daily/spacing/concurrency limits across replicas), and the context hash
-- lets identical, unchanged contexts reuse a recent answer and marks outdated answers as stale.
ALTER TABLE ai_analyses DROP CONSTRAINT ai_analyses_subject_kind_check;
ALTER TABLE ai_analyses ADD CONSTRAINT ai_analyses_subject_kind_check CHECK (subject_kind IN ('alarm', 'transaction', 'digest'));
ALTER TABLE ai_analyses DROP CONSTRAINT ai_analyses_status_check;
ALTER TABLE ai_analyses ADD CONSTRAINT ai_analyses_status_check CHECK (status IN ('pending', 'completed', 'failed'));
ALTER TABLE ai_analyses ADD COLUMN context_hash varchar(64);
CREATE INDEX ai_analyses_context ON ai_analyses(project_id, context_hash, created_at DESC);
