
BEGIN;

INSERT INTO business (business_id, legal_name, industry, country, year_end_month) VALUES
    ('b0000000000000000000000000000001', 'Karoo Outdoor Supplies (Pty) Ltd', 'Retail', 'ZAF', 2);

INSERT INTO business_unit (unit_id, business_id, unit_name, unit_type, currency) VALUES
    ('a0000000000000000000000000000001', 'b0000000000000000000000000000001', 'Whole company', 'company', 'ZAR');

INSERT INTO app_user (user_id, unit_id, full_name, username, password_hash) VALUES
    ('c0000000000000000000000000000001', 'a0000000000000000000000000000001', 'Pexx', 'pexx',
     '$2a$11$PUX02Lk5Rimd3Eqc/0Xs7OBtO64QKWCsQTdfzCq7xc3RRf3WSf.2G');

INSERT INTO budget_submission
    (submission_id, unit_id, submitted_by, period_start, period_end, status, submitted_at,
     import_method, source_file_name, source_scale) VALUES
    ('d0000000000000000000000000000001', 'a0000000000000000000000000000001', 'c0000000000000000000000000000001',
     '2019-03-01', '2024-02-29', 'superseded', '2025-05-12 09:00+02',
     'template', 'Karoo_Outdoor_IS_FY2020-FY2024.xlsx', 1000),
    ('d0000000000000000000000000000002', 'a0000000000000000000000000000001', 'c0000000000000000000000000000001',
     '2020-03-01', '2025-02-28', 'superseded', '2026-09-18 09:00+02',
     'template', 'Karoo_Outdoor_IS_FY2021-FY2025_draft.xlsx', 1000),
    ('d0000000000000000000000000000003', 'a0000000000000000000000000000001', 'c0000000000000000000000000000001',
     '2020-03-01', '2025-02-28', 'active', '2026-09-25 09:00+02',
     'template', 'Karoo_Outdoor_IS_FY2021-FY2025.xlsx', 1000);

INSERT INTO financial_line_item (submission_id, category, period_start, amount)
SELECT 'd0000000000000000000000000000003', v.category, make_date(2019 + y.i, 3, 1), v.amounts[y.i] * 1000
FROM (VALUES
    ('revenue',         ARRAY[8200, 8950, 9600, 10350, 11100]::numeric[]),
    ('other_income',    ARRAY[40, 45, 52, 60, 66]::numeric[]),
    ('cost_of_sales',   ARRAY[4920, 5390, 5810, 6260, 6700]::numeric[]),
    ('employee_costs',  ARRAY[1350, 1440, 1530, 1630, 1740]::numeric[]),
    ('occupancy',       ARRAY[480, 500, 525, 550, 580]::numeric[]),
    ('electricity',     ARRAY[150, 175, 205, 240, 275]::numeric[]),
    ('other_utilities', ARRAY[84, 89, 94, 100, 107]::numeric[]),
    ('transport_fuel',  ARRAY[130, 155, 170, 190, 200]::numeric[]),
    ('marketing',       ARRAY[120, 135, 145, 160, 175]::numeric[]),
    ('other_operating', ARRAY[88, 85, 102, 110, 117]::numeric[]),
    ('depreciation',    ARRAY[90, 95, 100, 105, 110]::numeric[]),
    ('finance_costs',   ARRAY[45, 42, 40, 38, 35]::numeric[]),
    ('income_tax',      ARRAY[211, 240, 251, 277, 304]::numeric[])
) AS v (category, amounts)
CROSS JOIN generate_series(1, 5) AS y (i);

INSERT INTO financial_line_item (submission_id, category, period_start, amount)
SELECT 'd0000000000000000000000000000002', category, period_start,
       CASE WHEN category = 'finance_costs' AND period_start = '2022-03-01' THEN 0 ELSE amount END
FROM financial_line_item
WHERE submission_id = 'd0000000000000000000000000000003';

INSERT INTO financial_line_item (submission_id, category, period_start, amount)
SELECT 'd0000000000000000000000000000001', category, period_start, amount
FROM financial_line_item
WHERE submission_id = 'd0000000000000000000000000000003' AND period_start < '2024-03-01'
UNION ALL
SELECT 'd0000000000000000000000000000001', category, '2019-03-01', round(amount * 0.92, -3)
FROM financial_line_item
WHERE submission_id = 'd0000000000000000000000000000003' AND period_start = '2020-03-01';

INSERT INTO submission_row (submission_id, row_order, row_kind, source_label, subtotal_code, amounts, mapping)
SELECT 'd0000000000000000000000000000003', r.ord, r.kind, r.label, r.subtotal, r.amounts::numeric[],
       CASE WHEN r.kind = 'line' THEN jsonb_build_array(jsonb_build_object('category', r.category, 'percent', 100)) END
FROM (VALUES
    (1,  'line',     'Sales',                 NULL,                'revenue',         ARRAY[8200, 8950, 9600, 10350, 11100]),
    (2,  'line',     'Cost of sales',         NULL,                'cost_of_sales',   ARRAY[-4920, -5390, -5810, -6260, -6700]),
    (3,  'subtotal', 'Gross profit',          'gross_profit',      NULL,              ARRAY[3280, 3560, 3790, 4090, 4400]),
    (4,  'line',     'Interest received',     NULL,                'other_income',    ARRAY[40, 45, 52, 60, 66]),
    (5,  'heading',  'Operating expenses',    NULL,                NULL,              ARRAY[NULL, NULL, NULL, NULL, NULL]::int[]),
    (6,  'line',     'Salaries & wages',      NULL,                'employee_costs',  ARRAY[-1350, -1440, -1530, -1630, -1740]),
    (7,  'line',     'Rent',                  NULL,                'occupancy',       ARRAY[-480, -500, -525, -550, -580]),
    (8,  'line',     'Electricity',           NULL,                'electricity',     ARRAY[-150, -175, -205, -240, -275]),
    (9,  'line',     'Water',                 NULL,                'other_utilities', ARRAY[-36, -39, -42, -45, -49]),
    (10, 'line',     'Telephone & internet',  NULL,                'other_utilities', ARRAY[-48, -50, -52, -55, -58]),
    (11, 'line',     'Fuel & delivery',       NULL,                'transport_fuel',  ARRAY[-130, -155, -170, -190, -200]),
    (12, 'line',     'Advertising',           NULL,                'marketing',       ARRAY[-120, -135, -145, -160, -175]),
    (13, 'line',     'Bank charges',          NULL,                'other_operating', ARRAY[-28, -30, -32, -35, -37]),
    (14, 'line',     'Repairs & maintenance', NULL,                'other_operating', ARRAY[-60, -55, -70, -75, -80]),
    (15, 'line',     'Depreciation',          NULL,                'depreciation',    ARRAY[-90, -95, -100, -105, -110]),
    (16, 'subtotal', 'Operating profit',      'operating_profit',  NULL,              ARRAY[828, 931, 971, 1065, 1162]),
    (17, 'line',     'Interest paid',         NULL,                'finance_costs',   ARRAY[-45, -42, -40, -38, -35]),
    (18, 'subtotal', 'Profit before tax',     'profit_before_tax', NULL,              ARRAY[783, 889, 931, 1027, 1127]),
    (19, 'line',     'Income tax',            NULL,                'income_tax',      ARRAY[-211, -240, -251, -277, -304]),
    (20, 'subtotal', 'Net profit',            'net_profit',        NULL,              ARRAY[572, 649, 680, 750, 823])
) AS r (ord, kind, label, subtotal, category, amounts);

INSERT INTO import_mapping (business_id, source_label, label_key, category_code, split_percent, created_by) VALUES
    ('b0000000000000000000000000000001', 'Water & electricity', 'water & electricity', 'electricity',     70, 'c0000000000000000000000000000001'),
    ('b0000000000000000000000000000001', 'Water & electricity', 'water & electricity', 'other_utilities', 30, 'c0000000000000000000000000000001'),
    ('b0000000000000000000000000000001', 'Franchise levy',      'franchise levy',      'other_operating', 100, 'c0000000000000000000000000000001');

INSERT INTO forecast_run (run_id, unit_id, forecasting_method, history_start, history_end, history_updated_at, parameters, created_at) VALUES
    ('e0000000000000000000000000000001', 'a0000000000000000000000000000001', 'moving_average', '2020-03-01', '2024-03-01',
     '2026-09-25 09:00+02', '{"window_size": 3, "frequency": "annual", "horizon": 1, "warnings": []}', '2026-10-01 14:55+02'),
    ('e0000000000000000000000000000002', 'a0000000000000000000000000000001', 'exponential_smoothing', '2020-03-01', '2024-03-01',
     '2026-09-25 09:00+02', '{"alpha": 0.5, "frequency": "annual", "horizon": 1, "warnings": []}', '2026-10-01 14:55+02');

INSERT INTO forecast_value (run_id, category, forecast_period, baseline_amount)
SELECT 'e0000000000000000000000000000001', category, DATE '2025-03-01', round(avg(amount), 2)
FROM financial_line_item
WHERE submission_id = 'd0000000000000000000000000000003' AND period_start >= '2022-03-01'
GROUP BY category
UNION ALL
SELECT 'e0000000000000000000000000000002', category, DATE '2025-03-01',
       round(sum(amount * CASE WHEN period_start <= '2021-03-01' THEN 0.0625
                               ELSE 0.5 ^ (2024 - extract(year FROM period_start) + 1) END), 2)
FROM financial_line_item
WHERE submission_id = 'd0000000000000000000000000000003'
GROUP BY category;

INSERT INTO budget_selection (budget_id, unit_id, ma_run_id, es_run_id, budget_method, status, chosen_by, chosen_at) VALUES
    ('f0000000000000000000000000000001', 'a0000000000000000000000000000001', 'e0000000000000000000000000000001',
     'e0000000000000000000000000000002', 'exponential_smoothing', 'active', 'c0000000000000000000000000000001',
     '2026-10-01 15:00+02');

INSERT INTO rule_definition (business_id, rule_name, condition, action, indicator_code, threshold_value) VALUES
    ('b0000000000000000000000000000001', 'Headline CPI above threshold',
     'indicator == cpi_headline AND value > threshold_value',
     'Create warning alert: review linked expense forecasts',
     'cpi_headline', 6.0),
    ('b0000000000000000000000000000001', 'Core inflation above threshold',
     'indicator == cpi_core AND value > threshold_value',
     'Create warning alert: review linked expense forecasts',
     'cpi_core', 6.0),
    ('b0000000000000000000000000000001', 'Fuel price increase above threshold',
     'indicator == fuel_price AND value > threshold_value',
     'Create warning alert: review linked expense forecasts',
     'fuel_price', 10.0),
    ('b0000000000000000000000000000001', 'Electricity price increase above threshold',
     'indicator == electricity_price AND value > threshold_value',
     'Create warning alert: review linked expense forecasts',
     'electricity_price', 10.0);

INSERT INTO kpi_target (unit_id, metric_name, target_value, measurement_unit, target_period) VALUES
    ('a0000000000000000000000000000001', 'revenue',        11000000, 'rand',    '2024-03-01'),
    ('a0000000000000000000000000000001', 'revenue',        11800000, 'rand',    '2025-03-01'),
    ('a0000000000000000000000000000001', 'net_margin',     7,        'percent', '2025-03-01'),
    ('a0000000000000000000000000000001', 'electricity',    280000,   'rand',    '2025-03-01'),
    ('a0000000000000000000000000000001', 'employee_costs', 1800000,  'rand',    '2025-03-01');

INSERT INTO audit_log (business_id, user_id, user_name, area, action, summary, details, occurred_at) VALUES
    ('b0000000000000000000000000000001', 'c0000000000000000000000000000001', 'Pexx', 'upload', 'uploaded',
     'Uploaded Karoo_Outdoor_IS_FY2020-FY2024.xlsx (FY2020–FY2024)', NULL, '2025-05-12 09:00+02'),
    ('b0000000000000000000000000000001', 'c0000000000000000000000000000001', 'Pexx', 'upload', 'uploaded',
     'Uploaded Karoo_Outdoor_IS_FY2021-FY2025_draft.xlsx (FY2021–FY2025)', NULL, '2026-09-18 09:00+02'),
    ('b0000000000000000000000000000001', 'c0000000000000000000000000000001', 'Pexx', 'upload', 'uploaded',
     'Uploaded Karoo_Outdoor_IS_FY2021-FY2025.xlsx (FY2021–FY2025)', NULL, '2026-09-25 09:00+02'),
    ('b0000000000000000000000000000001', 'c0000000000000000000000000000001', 'Pexx', 'forecast', 'ran',
     'Ran the forecasts for 1 year ahead',
     '[{"field": "Moving Average window", "after": "3 years"}, {"field": "Smoothing weight α", "after": "0.5"}]',
     '2026-10-01 14:55+02'),
    ('b0000000000000000000000000000001', 'c0000000000000000000000000000001', 'Pexx', 'budget', 'chosen',
     'Chose Exponential Smoothing as the budget', NULL, '2026-10-01 15:00+02');

COMMIT;
