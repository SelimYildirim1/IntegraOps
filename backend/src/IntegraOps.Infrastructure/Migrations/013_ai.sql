-- AI assistance: each analysis keeps the evidence the model was given, its checked output and user feedback,
-- so answers can be audited and their usefulness measured during the pilot.
CREATE TABLE ai_analyses (
    id uuid PRIMARY KEY,
    project_id uuid NOT NULL REFERENCES projects(id),
    subject_kind varchar(16) NOT NULL CHECK (subject_kind IN ('alarm', 'digest')),
    subject_id uuid,
    requested_by uuid REFERENCES app_users(id) ON DELETE SET NULL,
    provider varchar(40) NOT NULL,
    model varchar(120) NOT NULL,
    prompt_version varchar(20) NOT NULL,
    status varchar(12) NOT NULL CHECK (status IN ('completed', 'failed')),
    evidence jsonb NOT NULL,
    result jsonb,
    error varchar(500),
    input_tokens integer,
    output_tokens integer,
    duration_ms integer NOT NULL,
    created_at timestamptz NOT NULL DEFAULT clock_timestamp()
);
CREATE INDEX ai_analyses_subject ON ai_analyses(project_id, subject_kind, subject_id, created_at DESC);
CREATE INDEX ai_analyses_project_time ON ai_analyses(project_id, created_at);

CREATE TABLE ai_feedback (
    analysis_id uuid NOT NULL REFERENCES ai_analyses(id) ON DELETE CASCADE,
    user_id uuid NOT NULL REFERENCES app_users(id) ON DELETE CASCADE,
    rating varchar(12) NOT NULL CHECK (rating IN ('useful', 'wrong', 'incomplete')),
    comment varchar(1000),
    created_at timestamptz NOT NULL DEFAULT clock_timestamp(),
    PRIMARY KEY (analysis_id, user_id)
);
