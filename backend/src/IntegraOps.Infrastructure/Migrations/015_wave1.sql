-- Dalga 1: Slack and Microsoft Teams channels, step runbooks and source-system links.
ALTER TABLE notification_channels DROP CONSTRAINT notification_channels_kind_check;
ALTER TABLE notification_channels ADD CONSTRAINT notification_channels_kind_check
    CHECK (kind IN ('email', 'webhook', 'slack', 'teams'));

-- Runbook text shown with alarms and on the transaction page; link template opens the record in the source system.
ALTER TABLE flow_version_steps ADD COLUMN instructions text NOT NULL DEFAULT '';
ALTER TABLE flow_version_steps ADD COLUMN link_template varchar(1024) NOT NULL DEFAULT '';
ALTER TABLE flow_version_steps ADD CONSTRAINT flow_version_steps_instructions_check CHECK (length(instructions) <= 2000);
