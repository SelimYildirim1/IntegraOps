-- AI assistant as an optional add-on: NULL follows the plan default, true/false is a per-customer override.
ALTER TABLE organization_limits ADD COLUMN ai_enabled boolean;
