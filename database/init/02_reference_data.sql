-- Reference data: inserts categories and indicators

BEGIN;

INSERT INTO line_category (category_code, display_name, sort_order, team_managed) VALUES
    ('revenue',          'Revenue',                              1,  true),
    ('other_income',     'Other income',                         2,  false),
    ('cost_of_sales',    'Cost of sales',                        3,  false),
    ('employee_costs',   'Employee costs',                       4,  true),
    ('occupancy',        'Occupancy',                            5,  false),
    ('electricity',      'Electricity',                          6,  false),
    ('other_utilities',  'Water, telecoms and other utilities',  7,  false),
    ('transport_fuel',   'Transport and fuel',                   8,  false),
    ('marketing',        'Marketing',                            9,  false),
    ('other_operating',  'Other operating expenses',             10, false),
    ('depreciation',     'Depreciation and amortisation',        11, false),
    ('finance_costs',    'Finance costs',                        12, false),
    ('income_tax',       'Income tax',                           13, false);

INSERT INTO indicator_type (indicator_code, display_name, description, measurement_unit, official_source) VALUES
    ('cpi_headline',
     'Consumer inflation (headline CPI)',
     'Annual % change in CPI for all urban areas, all items. Applied to general goods and stock purchases.',
     'percent',
     'Stats SA P0141 Consumer Price Index (headline); World Bank FP.CPI.TOTL.ZG (annual)'),
    ('cpi_core',
     'Core inflation (replaces CPIX)',
     'Annual % change in CPI excluding food and non-alcoholic beverages, fuel and energy. CPIX was discontinued in January 2009; core inflation is the closest current measure of general price increases.',
     'percent',
     'Stats SA P0141 Consumer Price Index (core); OECD Consumer Prices API series _TXCP01_NRG (annual)'),
    ('fuel_price',
     'Fuel price change',
     'Annual % change in the CPI fuel index (petrol and diesel).',
     'percent',
     'Stats SA P0141 Consumer Price Index (fuel); OECD Consumer Prices API series CP0722 (annual); monthly adjustments announced by the Department of Mineral and Petroleum Resources'),
    ('electricity_price',
     'Electricity price change',
     'Annual % change in the CPI electricity, gas and other fuels index (mainly electricity in South Africa).',
     'percent',
     'Stats SA P0141 Consumer Price Index (electricity); OECD Consumer Prices API series CP045 (annual); tariffs approved by NERSA');

INSERT INTO indicator_category_link (indicator_code, category_code) VALUES
    ('cpi_headline',      'cost_of_sales'),
    ('cpi_core',          'occupancy'),
    ('cpi_core',          'other_utilities'),
    ('cpi_core',          'marketing'),
    ('cpi_core',          'other_operating'),
    ('fuel_price',        'transport_fuel'),
    ('electricity_price', 'electricity');

COMMIT;
