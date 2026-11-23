-- Tenant isolation. The table owner is subject to these policies.
-- The API sets app.user_id, app.unit_id and app.business_id for a signed-in request.
-- Login sets app.login_username for that one lookup. The weekly report sets app.bypass to on.
-- Reference data (categories, indicators, external series) stays shared.

BEGIN;

-- The Compose user owns the tables and is a superuser, so it bypasses row security.
-- Requests switch to this role before reading or writing.
DO $$
BEGIN
    IF NOT EXISTS (SELECT 1 FROM pg_roles WHERE rolname = 'randahead_app') THEN
        CREATE ROLE randahead_app NOLOGIN;
    END IF;
END $$;

GRANT randahead_app TO CURRENT_USER;
GRANT USAGE ON SCHEMA public TO randahead_app;
GRANT SELECT, INSERT, UPDATE, DELETE ON ALL TABLES IN SCHEMA public TO randahead_app;
ALTER DEFAULT PRIVILEGES IN SCHEMA public
    GRANT SELECT, INSERT, UPDATE, DELETE ON TABLES TO randahead_app;

CREATE OR REPLACE FUNCTION app_setting(setting_name text)
RETURNS text
LANGUAGE sql
STABLE
AS $$
    SELECT nullif(current_setting(setting_name, true), '');
$$;

CREATE OR REPLACE FUNCTION app_bypassed()
RETURNS boolean
LANGUAGE sql
STABLE
AS $$
    SELECT app_setting('app.bypass') = 'on';
$$;

-- business_unit is readable from the business policy, so this policy must not read business.
ALTER TABLE business_unit ENABLE ROW LEVEL SECURITY;
ALTER TABLE business_unit FORCE ROW LEVEL SECURITY;
DROP POLICY IF EXISTS tenant_isolation ON business_unit;
CREATE POLICY tenant_isolation ON business_unit
    USING (
        app_bypassed()
        OR unit_id = app_setting('app.unit_id')
        OR business_id = app_setting('app.business_id')
        OR unit_id = (SELECT a.unit_id FROM app_user a WHERE a.user_id = app_setting('app.user_id'))
        OR unit_id = (SELECT a.unit_id FROM app_user a WHERE a.username = app_setting('app.login_username'))
    )
    WITH CHECK (
        app_bypassed()
        OR unit_id = app_setting('app.unit_id')
        OR business_id = app_setting('app.business_id')
    );

-- app_user does not read business_unit, so the policies do not recurse.
ALTER TABLE app_user ENABLE ROW LEVEL SECURITY;
ALTER TABLE app_user FORCE ROW LEVEL SECURITY;
DROP POLICY IF EXISTS tenant_isolation ON app_user;
CREATE POLICY tenant_isolation ON app_user
    USING (
        app_bypassed()
        OR user_id = app_setting('app.user_id')
        OR username = app_setting('app.login_username')
        OR unit_id = app_setting('app.unit_id')
    )
    WITH CHECK (
        app_bypassed()
        OR user_id = app_setting('app.user_id')
        OR unit_id = app_setting('app.unit_id')
    );

ALTER TABLE business ENABLE ROW LEVEL SECURITY;
ALTER TABLE business FORCE ROW LEVEL SECURITY;
DROP POLICY IF EXISTS tenant_isolation ON business;
CREATE POLICY tenant_isolation ON business
    USING (
        app_bypassed()
        OR business_id = app_setting('app.business_id')
        OR business_id IN (
            SELECT bu.business_id
            FROM business_unit bu
            WHERE bu.unit_id = (SELECT a.unit_id FROM app_user a WHERE a.user_id = app_setting('app.user_id'))
               OR bu.unit_id = (SELECT a.unit_id FROM app_user a WHERE a.username = app_setting('app.login_username'))
        )
    )
    WITH CHECK (
        app_bypassed()
        OR business_id = app_setting('app.business_id')
    );

ALTER TABLE budget_submission ENABLE ROW LEVEL SECURITY;
ALTER TABLE budget_submission FORCE ROW LEVEL SECURITY;
DROP POLICY IF EXISTS tenant_isolation ON budget_submission;
CREATE POLICY tenant_isolation ON budget_submission
    USING (app_bypassed() OR unit_id = app_setting('app.unit_id'))
    WITH CHECK (app_bypassed() OR unit_id = app_setting('app.unit_id'));

ALTER TABLE financial_line_item ENABLE ROW LEVEL SECURITY;
ALTER TABLE financial_line_item FORCE ROW LEVEL SECURITY;
DROP POLICY IF EXISTS tenant_isolation ON financial_line_item;
CREATE POLICY tenant_isolation ON financial_line_item
    USING (
        EXISTS (
            SELECT 1 FROM budget_submission s
            WHERE s.submission_id = financial_line_item.submission_id
        )
    )
    WITH CHECK (
        EXISTS (
            SELECT 1 FROM budget_submission s
            WHERE s.submission_id = financial_line_item.submission_id
        )
    );

ALTER TABLE submission_row ENABLE ROW LEVEL SECURITY;
ALTER TABLE submission_row FORCE ROW LEVEL SECURITY;
DROP POLICY IF EXISTS tenant_isolation ON submission_row;
CREATE POLICY tenant_isolation ON submission_row
    USING (
        EXISTS (
            SELECT 1 FROM budget_submission s
            WHERE s.submission_id = submission_row.submission_id
        )
    )
    WITH CHECK (
        EXISTS (
            SELECT 1 FROM budget_submission s
            WHERE s.submission_id = submission_row.submission_id
        )
    );

ALTER TABLE import_mapping ENABLE ROW LEVEL SECURITY;
ALTER TABLE import_mapping FORCE ROW LEVEL SECURITY;
DROP POLICY IF EXISTS tenant_isolation ON import_mapping;
CREATE POLICY tenant_isolation ON import_mapping
    USING (app_bypassed() OR business_id = app_setting('app.business_id'))
    WITH CHECK (app_bypassed() OR business_id = app_setting('app.business_id'));

ALTER TABLE forecast_run ENABLE ROW LEVEL SECURITY;
ALTER TABLE forecast_run FORCE ROW LEVEL SECURITY;
DROP POLICY IF EXISTS tenant_isolation ON forecast_run;
CREATE POLICY tenant_isolation ON forecast_run
    USING (app_bypassed() OR unit_id = app_setting('app.unit_id'))
    WITH CHECK (app_bypassed() OR unit_id = app_setting('app.unit_id'));

ALTER TABLE forecast_value ENABLE ROW LEVEL SECURITY;
ALTER TABLE forecast_value FORCE ROW LEVEL SECURITY;
DROP POLICY IF EXISTS tenant_isolation ON forecast_value;
CREATE POLICY tenant_isolation ON forecast_value
    USING (
        EXISTS (
            SELECT 1 FROM forecast_run r
            WHERE r.run_id = forecast_value.run_id
        )
    )
    WITH CHECK (
        EXISTS (
            SELECT 1 FROM forecast_run r
            WHERE r.run_id = forecast_value.run_id
        )
    );

ALTER TABLE budget_selection ENABLE ROW LEVEL SECURITY;
ALTER TABLE budget_selection FORCE ROW LEVEL SECURITY;
DROP POLICY IF EXISTS tenant_isolation ON budget_selection;
CREATE POLICY tenant_isolation ON budget_selection
    USING (app_bypassed() OR unit_id = app_setting('app.unit_id'))
    WITH CHECK (app_bypassed() OR unit_id = app_setting('app.unit_id'));

ALTER TABLE forecast_input ENABLE ROW LEVEL SECURITY;
ALTER TABLE forecast_input FORCE ROW LEVEL SECURITY;
DROP POLICY IF EXISTS tenant_isolation ON forecast_input;
CREATE POLICY tenant_isolation ON forecast_input
    USING (
        EXISTS (
            SELECT 1 FROM forecast_run r
            WHERE r.run_id = forecast_input.run_id
        )
    )
    WITH CHECK (
        EXISTS (
            SELECT 1 FROM forecast_run r
            WHERE r.run_id = forecast_input.run_id
        )
    );

ALTER TABLE kpi_target ENABLE ROW LEVEL SECURITY;
ALTER TABLE kpi_target FORCE ROW LEVEL SECURITY;
DROP POLICY IF EXISTS tenant_isolation ON kpi_target;
CREATE POLICY tenant_isolation ON kpi_target
    USING (app_bypassed() OR unit_id = app_setting('app.unit_id'))
    WITH CHECK (app_bypassed() OR unit_id = app_setting('app.unit_id'));

ALTER TABLE rule_definition ENABLE ROW LEVEL SECURITY;
ALTER TABLE rule_definition FORCE ROW LEVEL SECURITY;
DROP POLICY IF EXISTS tenant_isolation ON rule_definition;
CREATE POLICY tenant_isolation ON rule_definition
    USING (app_bypassed() OR business_id = app_setting('app.business_id'))
    WITH CHECK (app_bypassed() OR business_id = app_setting('app.business_id'));

ALTER TABLE rule_alert ENABLE ROW LEVEL SECURITY;
ALTER TABLE rule_alert FORCE ROW LEVEL SECURITY;
DROP POLICY IF EXISTS tenant_isolation ON rule_alert;
CREATE POLICY tenant_isolation ON rule_alert
    USING (
        app_bypassed()
        OR unit_id = app_setting('app.unit_id')
        OR rule_id IN (SELECT d.rule_id FROM rule_definition d)
    )
    WITH CHECK (
        app_bypassed()
        OR unit_id = app_setting('app.unit_id')
        OR rule_id IN (SELECT d.rule_id FROM rule_definition d)
    );

ALTER TABLE audit_log ENABLE ROW LEVEL SECURITY;
ALTER TABLE audit_log FORCE ROW LEVEL SECURITY;
DROP POLICY IF EXISTS tenant_isolation ON audit_log;
CREATE POLICY tenant_isolation ON audit_log
    USING (app_bypassed() OR business_id = app_setting('app.business_id'))
    WITH CHECK (app_bypassed() OR business_id = app_setting('app.business_id'));

COMMIT;
