
BEGIN;

CREATE TABLE line_category (
    category_code VARCHAR(50)  PRIMARY KEY,
    display_name  VARCHAR(100) NOT NULL UNIQUE,
    sort_order    SMALLINT     NOT NULL,
    team_managed  BOOLEAN      NOT NULL DEFAULT false
);

CREATE TABLE indicator_type (
    indicator_code   VARCHAR(50)  PRIMARY KEY,
    display_name     VARCHAR(200) NOT NULL,
    description      VARCHAR(1000),
    measurement_unit VARCHAR(50)  NOT NULL DEFAULT 'percent',
    official_source  VARCHAR(500) NOT NULL
);

CREATE TABLE indicator_category_link (
    indicator_code VARCHAR(50) NOT NULL REFERENCES indicator_type (indicator_code) ON DELETE CASCADE,
    category_code  VARCHAR(50) NOT NULL REFERENCES line_category (category_code) ON DELETE CASCADE,
    PRIMARY KEY (indicator_code, category_code)
);

CREATE TABLE business (
    business_id    VARCHAR(32)  PRIMARY KEY DEFAULT replace(gen_random_uuid()::text, '-', ''),
    legal_name     VARCHAR(200) NOT NULL,
    industry       VARCHAR(100),
    country        CHAR(3)      NOT NULL DEFAULT 'ZAF',
    year_end_month SMALLINT     NOT NULL DEFAULT 2 CHECK (year_end_month BETWEEN 1 AND 12),
    created_at     TIMESTAMPTZ  NOT NULL DEFAULT now()
);

CREATE TABLE business_unit (
    unit_id       VARCHAR(32)  PRIMARY KEY DEFAULT replace(gen_random_uuid()::text, '-', ''),
    business_id   VARCHAR(32)  NOT NULL REFERENCES business (business_id) ON DELETE RESTRICT,
    unit_name     VARCHAR(200) NOT NULL,
    unit_type     VARCHAR(20)  NOT NULL DEFAULT 'company'
                  CHECK (unit_type IN ('company', 'store', 'branch', 'department')),
    currency      CHAR(3)      NOT NULL DEFAULT 'ZAR',
    UNIQUE (business_id, unit_name)
);

CREATE TABLE app_user (
    user_id       VARCHAR(32)  PRIMARY KEY DEFAULT replace(gen_random_uuid()::text, '-', ''),
    unit_id       VARCHAR(32)  NOT NULL REFERENCES business_unit (unit_id) ON DELETE RESTRICT,
    full_name     VARCHAR(200) NOT NULL,
    username      VARCHAR(50)  NOT NULL UNIQUE CHECK (username = lower(username)),
    password_hash VARCHAR(255) NOT NULL
);

CREATE TABLE budget_submission (
    submission_id          VARCHAR(32)  PRIMARY KEY DEFAULT replace(gen_random_uuid()::text, '-', ''),
    unit_id                VARCHAR(32)  NOT NULL REFERENCES business_unit (unit_id) ON DELETE RESTRICT,
    submitted_by           VARCHAR(32)  NOT NULL REFERENCES app_user (user_id) ON DELETE RESTRICT,
    period_start           DATE         NOT NULL,
    period_end             DATE         NOT NULL,
    status                 VARCHAR(20)  NOT NULL DEFAULT 'active'
                           CHECK (status IN ('active', 'superseded')),
    submitted_at           TIMESTAMPTZ  NOT NULL DEFAULT now(),
    import_method          VARCHAR(20)  NOT NULL DEFAULT 'template'
                           CHECK (import_method IN ('template', 'mapped_import', 'manual')),
    source_file_name       VARCHAR(255),
    source_scale           INTEGER      NOT NULL DEFAULT 1 CHECK (source_scale IN (1, 1000, 1000000)),
    CHECK (period_end >= period_start)
);

CREATE TABLE financial_line_item (
    line_item_id  VARCHAR(32)   PRIMARY KEY DEFAULT replace(gen_random_uuid()::text, '-', ''),
    submission_id VARCHAR(32)   NOT NULL REFERENCES budget_submission (submission_id) ON DELETE CASCADE,
    category      VARCHAR(50)   NOT NULL REFERENCES line_category (category_code) ON DELETE RESTRICT,
    period_start  DATE          NOT NULL,
    amount        NUMERIC(18,2) NOT NULL,
    frequency     VARCHAR(20)   NOT NULL DEFAULT 'annual'
                  CHECK (frequency IN ('monthly', 'quarterly', 'annual')),
    UNIQUE (submission_id, category, period_start)
);

CREATE TABLE submission_row (
    row_id        VARCHAR(32)     PRIMARY KEY DEFAULT replace(gen_random_uuid()::text, '-', ''),
    submission_id VARCHAR(32)     NOT NULL REFERENCES budget_submission (submission_id) ON DELETE CASCADE,
    row_order     SMALLINT        NOT NULL,
    row_kind      VARCHAR(20)     NOT NULL CHECK (row_kind IN ('heading', 'line', 'subtotal', 'total')),
    source_label  VARCHAR(200)    NOT NULL,
    subtotal_code VARCHAR(30)     CHECK (subtotal_code IN ('gross_profit', 'operating_profit', 'profit_before_tax', 'net_profit')),
    amounts       NUMERIC(20,6)[] NOT NULL,
    mapping       JSONB,
    hand_mapped   BOOLEAN         NOT NULL DEFAULT false,
    UNIQUE (submission_id, row_order),
    CHECK ((row_kind = 'subtotal') = (subtotal_code IS NOT NULL)),
    CHECK ((row_kind = 'line') = (mapping IS NOT NULL))
);

CREATE TABLE import_mapping (
    mapping_id    VARCHAR(32)  PRIMARY KEY DEFAULT replace(gen_random_uuid()::text, '-', ''),
    business_id   VARCHAR(32)  NOT NULL REFERENCES business (business_id) ON DELETE CASCADE,
    source_label  VARCHAR(200) NOT NULL,
    label_key     VARCHAR(200) NOT NULL,
    category_code VARCHAR(50)  NOT NULL REFERENCES line_category (category_code) ON DELETE RESTRICT,
    split_percent NUMERIC(5,2) NOT NULL DEFAULT 100 CHECK (split_percent > 0 AND split_percent <= 100),
    created_by    VARCHAR(32)  REFERENCES app_user (user_id) ON DELETE SET NULL,
    created_at    TIMESTAMPTZ  NOT NULL DEFAULT now(),
    UNIQUE (business_id, label_key, category_code)
);

CREATE TABLE data_source (
    source_id         VARCHAR(32)  PRIMARY KEY DEFAULT replace(gen_random_uuid()::text, '-', ''),
    source_name       VARCHAR(200) NOT NULL,
    source_identifier VARCHAR(500) NOT NULL UNIQUE,
    source_type       VARCHAR(50)  NOT NULL DEFAULT 'api'
                      CHECK (source_type IN ('api', 'spreadsheet', 'dataset', 'manual')),
    trusted_status    VARCHAR(50)  NOT NULL DEFAULT 'trusted'
                      CHECK (trusted_status IN ('trusted', 'untrusted'))
);

CREATE TABLE external_indicator (
    indicator_id      VARCHAR(32)   PRIMARY KEY DEFAULT replace(gen_random_uuid()::text, '-', ''),
    source_id         VARCHAR(32)   NOT NULL REFERENCES data_source (source_id) ON DELETE CASCADE,
    indicator_code    VARCHAR(50)   NOT NULL REFERENCES indicator_type (indicator_code) ON DELETE RESTRICT,
    indicator_name    VARCHAR(200)  NOT NULL,
    indicator_value   NUMERIC(18,6) NOT NULL,
    measurement_unit  VARCHAR(50)   NOT NULL,
    relevant_period   VARCHAR(20)   NOT NULL,
    validation_status VARCHAR(50)   NOT NULL DEFAULT 'pending'
                      CHECK (validation_status IN ('pending', 'validated', 'flagged', 'rejected')),
    collected_at      TIMESTAMPTZ   NOT NULL DEFAULT now(),
    UNIQUE (source_id, indicator_code, relevant_period)
);

CREATE INDEX ix_external_indicator_code_period ON external_indicator (indicator_code, relevant_period);

CREATE TABLE forecast_run (
    run_id             VARCHAR(32) PRIMARY KEY DEFAULT replace(gen_random_uuid()::text, '-', ''),
    unit_id            VARCHAR(32) NOT NULL REFERENCES business_unit (unit_id) ON DELETE RESTRICT,
    submission_id      VARCHAR(32) REFERENCES budget_submission (submission_id) ON DELETE RESTRICT,
    forecasting_method VARCHAR(30) NOT NULL
                       CHECK (forecasting_method IN ('moving_average', 'exponential_smoothing')),
    history_start       DATE        NOT NULL,
    history_end         DATE        NOT NULL,
    history_updated_at  TIMESTAMPTZ,
    parameters          JSONB       NOT NULL DEFAULT '{}'::jsonb,
    created_at          TIMESTAMPTZ NOT NULL DEFAULT now(),
    CHECK (history_end >= history_start)
);

CREATE TABLE forecast_value (
    value_id        VARCHAR(32)   PRIMARY KEY DEFAULT replace(gen_random_uuid()::text, '-', ''),
    run_id          VARCHAR(32)   NOT NULL REFERENCES forecast_run (run_id) ON DELETE CASCADE,
    category        VARCHAR(50)   NOT NULL REFERENCES line_category (category_code) ON DELETE RESTRICT,
    forecast_period DATE          NOT NULL,
    baseline_amount NUMERIC(18,2) NOT NULL,
    adjusted_amount NUMERIC(18,2),
    UNIQUE (run_id, category, forecast_period)
);

CREATE TABLE budget_selection (
    budget_id     VARCHAR(32) PRIMARY KEY DEFAULT replace(gen_random_uuid()::text, '-', ''),
    unit_id       VARCHAR(32) NOT NULL REFERENCES business_unit (unit_id) ON DELETE RESTRICT,
    ma_run_id     VARCHAR(32) NOT NULL REFERENCES forecast_run (run_id) ON DELETE RESTRICT,
    es_run_id     VARCHAR(32) NOT NULL REFERENCES forecast_run (run_id) ON DELETE RESTRICT,
    budget_method VARCHAR(30) NOT NULL
                  CHECK (budget_method IN ('moving_average', 'exponential_smoothing', 'combined')),
    status        VARCHAR(20) NOT NULL DEFAULT 'active'
                  CHECK (status IN ('active', 'superseded')),
    chosen_by     VARCHAR(32) NOT NULL REFERENCES app_user (user_id) ON DELETE RESTRICT,
    chosen_at     TIMESTAMPTZ NOT NULL DEFAULT now(),
    CHECK (ma_run_id <> es_run_id)
);

CREATE UNIQUE INDEX ux_budget_selection_active ON budget_selection (unit_id) WHERE status = 'active';

CREATE TABLE forecast_input (
    input_id              VARCHAR(32)   PRIMARY KEY DEFAULT replace(gen_random_uuid()::text, '-', ''),
    run_id                VARCHAR(32)   NOT NULL REFERENCES forecast_run (run_id) ON DELETE CASCADE,
    indicator_id          VARCHAR(32)   NOT NULL REFERENCES external_indicator (indicator_id) ON DELETE RESTRICT,
    influence_description VARCHAR(1000),
    UNIQUE (run_id, indicator_id)
);

CREATE TABLE kpi_target (
    target_id        VARCHAR(32)   PRIMARY KEY DEFAULT replace(gen_random_uuid()::text, '-', ''),
    unit_id          VARCHAR(32)   NOT NULL REFERENCES business_unit (unit_id) ON DELETE RESTRICT,
    metric_name      VARCHAR(100)  NOT NULL,
    target_value     NUMERIC(18,2) NOT NULL,
    measurement_unit VARCHAR(50)   NOT NULL,
    target_period    DATE          NOT NULL,
    UNIQUE (unit_id, metric_name, target_period)
);

CREATE TABLE rule_definition (
    rule_id         VARCHAR(32)   PRIMARY KEY DEFAULT replace(gen_random_uuid()::text, '-', ''),
    business_id     VARCHAR(32)   NOT NULL REFERENCES business (business_id) ON DELETE RESTRICT,
    rule_name       VARCHAR(200)  NOT NULL,
    condition       VARCHAR(500)  NOT NULL,
    action          VARCHAR(500)  NOT NULL,
    indicator_code  VARCHAR(50)   REFERENCES indicator_type (indicator_code) ON DELETE RESTRICT,
    threshold_value NUMERIC(18,6),
    active_status   VARCHAR(50)   NOT NULL DEFAULT 'active'
                    CHECK (active_status IN ('active', 'inactive'))
);

CREATE UNIQUE INDEX ux_rule_definition_name ON rule_definition (business_id, lower(rule_name));

CREATE TABLE rule_alert (
    alert_id          VARCHAR(32)   PRIMARY KEY DEFAULT replace(gen_random_uuid()::text, '-', ''),
    rule_id           VARCHAR(32)   NOT NULL REFERENCES rule_definition (rule_id) ON DELETE RESTRICT,
    unit_id           VARCHAR(32)   REFERENCES business_unit (unit_id) ON DELETE SET NULL,
    run_id            VARCHAR(32)   REFERENCES forecast_run (run_id) ON DELETE SET NULL,
    severity          VARCHAR(50)   NOT NULL DEFAULT 'info'
                      CHECK (severity IN ('info', 'warning', 'critical')),
    message           VARCHAR(1000) NOT NULL,
    resolution_status VARCHAR(50)   NOT NULL DEFAULT 'open'
                      CHECK (resolution_status IN ('open', 'acknowledged', 'resolved')),
    created_at        TIMESTAMPTZ   NOT NULL DEFAULT now()
);

CREATE TABLE audit_log (
    log_id      VARCHAR(32)  PRIMARY KEY DEFAULT replace(gen_random_uuid()::text, '-', ''),
    business_id VARCHAR(32)  NOT NULL REFERENCES business (business_id) ON DELETE RESTRICT,
    user_id     VARCHAR(32)  REFERENCES app_user (user_id) ON DELETE SET NULL,
    user_name   VARCHAR(200) NOT NULL,
    area        VARCHAR(20)  NOT NULL
                CHECK (area IN ('upload', 'forecast', 'budget', 'target', 'rule', 'business')),
    action      VARCHAR(20)  NOT NULL
                CHECK (action IN ('uploaded', 'ran', 'chosen', 'added', 'changed',
                                  'switched_off', 'switched_on', 'deleted')),
    summary     VARCHAR(500) NOT NULL,
    details     JSONB,
    occurred_at TIMESTAMPTZ  NOT NULL DEFAULT now()
);

CREATE INDEX ix_business_unit_business      ON business_unit (business_id);
CREATE INDEX ix_app_user_unit               ON app_user (unit_id);
CREATE INDEX ix_budget_submission_unit      ON budget_submission (unit_id, period_start);
CREATE INDEX ix_budget_submission_user      ON budget_submission (submitted_by);
CREATE INDEX ix_financial_line_item_cat     ON financial_line_item (category, period_start);
CREATE INDEX ix_import_mapping_category     ON import_mapping (category_code);
CREATE INDEX ix_import_mapping_user         ON import_mapping (created_by);
CREATE INDEX ix_forecast_run_unit           ON forecast_run (unit_id, created_at);
CREATE INDEX ix_forecast_run_submission     ON forecast_run (submission_id);
CREATE INDEX ix_forecast_value_category     ON forecast_value (category);
CREATE INDEX ix_budget_selection_unit       ON budget_selection (unit_id, chosen_at);
CREATE INDEX ix_budget_selection_ma_run     ON budget_selection (ma_run_id);
CREATE INDEX ix_budget_selection_es_run     ON budget_selection (es_run_id);
CREATE INDEX ix_budget_selection_user       ON budget_selection (chosen_by);
CREATE INDEX ix_forecast_input_indicator    ON forecast_input (indicator_id);
CREATE INDEX ix_indicator_category_link_cat ON indicator_category_link (category_code);
CREATE INDEX ix_rule_definition_indicator   ON rule_definition (indicator_code);
CREATE INDEX ix_rule_alert_rule             ON rule_alert (rule_id);
CREATE INDEX ix_rule_alert_unit             ON rule_alert (unit_id);
CREATE INDEX ix_rule_alert_run              ON rule_alert (run_id);
CREATE INDEX ix_rule_alert_created          ON rule_alert (created_at DESC);
CREATE INDEX ix_audit_log_business          ON audit_log (business_id, occurred_at DESC);
CREATE INDEX ix_audit_log_user              ON audit_log (user_id, occurred_at DESC);

COMMIT;
