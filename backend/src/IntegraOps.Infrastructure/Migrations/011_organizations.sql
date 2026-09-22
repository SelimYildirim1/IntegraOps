-- Enterprise use: organizations own projects, plans, limits, usage, security events, SSO and MFA.
ALTER TABLE organizations ADD COLUMN plan varchar(16) NOT NULL DEFAULT 'trial'
    CHECK (plan IN ('trial', 'starter', 'business', 'enterprise', 'suspended'));
ALTER TABLE organizations ADD COLUMN trial_ends_at timestamptz;
ALTER TABLE organizations ADD COLUMN plan_updated_at timestamptz;
ALTER TABLE organizations ADD COLUMN plan_note varchar(500) NOT NULL DEFAULT '';
ALTER TABLE organizations ADD COLUMN sso_issuer text;
ALTER TABLE organizations ADD COLUMN sso_client_id text;
ALTER TABLE organizations ADD COLUMN sso_client_secret_protected text;
ALTER TABLE organizations ADD COLUMN sso_domain varchar(120);
ALTER TABLE organizations ADD COLUMN sso_required boolean NOT NULL DEFAULT false;
ALTER TABLE organizations ADD COLUMN deletion_requested_at timestamptz;
ALTER TABLE organizations ADD COLUMN deletion_scheduled_at timestamptz;
ALTER TABLE organizations ADD COLUMN deletion_requested_by uuid REFERENCES app_users(id);
-- Existing organizations were created before plans: treat them as business accounts.
UPDATE organizations SET plan = 'business', plan_updated_at = clock_timestamp();
CREATE UNIQUE INDEX organizations_sso_domain ON organizations(lower(sso_domain)) WHERE sso_domain IS NOT NULL;

CREATE TABLE organization_members (
    organization_id uuid NOT NULL REFERENCES organizations(id),
    user_id uuid NOT NULL REFERENCES app_users(id),
    role varchar(16) NOT NULL CHECK (role IN ('owner', 'admin', 'member')),
    created_at timestamptz NOT NULL DEFAULT clock_timestamp(),
    PRIMARY KEY (organization_id, user_id)
);
-- Every project owner becomes an owner of the project's organization.
INSERT INTO organization_members (organization_id, user_id, role)
SELECT DISTINCT p.organization_id, m.user_id, 'owner' FROM project_members m JOIN projects p ON p.id = m.project_id WHERE m.role = 'owner'
ON CONFLICT DO NOTHING;

CREATE TABLE project_groups (
    id uuid PRIMARY KEY,
    organization_id uuid NOT NULL REFERENCES organizations(id),
    name varchar(120) NOT NULL,
    created_at timestamptz NOT NULL DEFAULT clock_timestamp(),
    UNIQUE (organization_id, name)
);
CREATE TABLE project_group_projects (
    group_id uuid NOT NULL REFERENCES project_groups(id) ON DELETE CASCADE,
    project_id uuid NOT NULL REFERENCES projects(id),
    PRIMARY KEY (group_id, project_id)
);
CREATE TABLE project_group_members (
    group_id uuid NOT NULL REFERENCES project_groups(id) ON DELETE CASCADE,
    user_id uuid NOT NULL REFERENCES app_users(id),
    role varchar(16) NOT NULL CHECK (role IN ('owner', 'operator', 'viewer')),
    PRIMARY KEY (group_id, user_id)
);

-- NULL means "plan default"; a system administrator may override per customer.
CREATE TABLE organization_limits (
    organization_id uuid PRIMARY KEY REFERENCES organizations(id),
    max_projects integer,
    max_users integer,
    max_events_per_day integer,
    max_retention_days integer,
    updated_at timestamptz NOT NULL DEFAULT clock_timestamp()
);
CREATE TABLE usage_daily (
    organization_id uuid NOT NULL REFERENCES organizations(id),
    day date NOT NULL,
    events bigint NOT NULL DEFAULT 0,
    rejected bigint NOT NULL DEFAULT 0,
    PRIMARY KEY (organization_id, day)
);

ALTER TABLE app_users ADD COLUMN is_system_admin boolean NOT NULL DEFAULT false;
ALTER TABLE app_users ADD COLUMN mfa_enabled boolean NOT NULL DEFAULT false;
ALTER TABLE app_users ADD COLUMN mfa_secret_protected text;
ALTER TABLE app_users ADD COLUMN mfa_recovery_hashes text[] NOT NULL DEFAULT '{}';
ALTER TABLE app_users ADD COLUMN sso_subject text;
ALTER TABLE app_users ADD COLUMN last_login_at timestamptz;

-- Security-relevant events across projects: sign-in, MFA, SSO, password and account changes.
CREATE TABLE security_events (
    id bigint GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    user_id uuid REFERENCES app_users(id),
    organization_id uuid REFERENCES organizations(id),
    kind varchar(40) NOT NULL,
    ip varchar(64),
    user_agent varchar(200),
    detail varchar(200),
    created_at timestamptz NOT NULL DEFAULT clock_timestamp()
);
CREATE INDEX security_events_user ON security_events(user_id, created_at DESC);
CREATE INDEX security_events_org ON security_events(organization_id, created_at DESC);

-- Short-lived tokens for the second sign-in factor and for SSO handshakes.
CREATE TABLE login_challenges (
    token_hash char(64) PRIMARY KEY,
    user_id uuid NOT NULL REFERENCES app_users(id),
    kind varchar(16) NOT NULL CHECK (kind IN ('mfa', 'sso')),
    expires_at timestamptz NOT NULL,
    used_at timestamptz,
    created_at timestamptz NOT NULL DEFAULT clock_timestamp()
);
CREATE TABLE sso_handshakes (
    state char(64) PRIMARY KEY,
    organization_id uuid NOT NULL REFERENCES organizations(id),
    nonce char(64) NOT NULL,
    return_to varchar(500),
    created_at timestamptz NOT NULL DEFAULT clock_timestamp(),
    used_at timestamptz
);

CREATE TABLE data_exports (
    id uuid PRIMARY KEY,
    organization_id uuid NOT NULL REFERENCES organizations(id),
    requested_by uuid REFERENCES app_users(id),
    state varchar(16) NOT NULL DEFAULT 'pending' CHECK (state IN ('pending', 'running', 'ready', 'failed')),
    path text,
    size_bytes bigint,
    error varchar(200),
    created_at timestamptz NOT NULL DEFAULT clock_timestamp(),
    completed_at timestamptz,
    expires_at timestamptz
);
CREATE INDEX data_exports_org ON data_exports(organization_id, created_at DESC);
