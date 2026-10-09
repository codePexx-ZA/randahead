// Demo data: fictional business used in demo mode
const MOCK_BUSINESS = {
  name: "Karoo Outdoor Supplies (Pty) Ltd",
  industry: "Retail",
  country: "ZAF",
  yearEndMonth: 2,
  unitId: "a0000000000000000000000000000001",
  unitName: "Whole company",
  currency: "ZAR",
  hasApprovedHistory: true,
};

const MOCK_APP_USERS = [
  { id: "c0000000000000000000000000000001", fullName: "Sipho Ndlovu", email: "submitter@demo.co.za", password: "demo123", role: "submitter", active: true, deletable: false },
  { id: "c0000000000000000000000000000002", fullName: "Anika Pillay", email: "approver@demo.co.za", password: "demo123", role: "approver", active: true, deletable: false },
  { id: "c0000000000000000000000000000003", fullName: "Johan van Wyk", email: "owner@demo.co.za", password: "demo123", role: "decision_maker", active: true, deletable: false },
  { id: "c0000000000000000000000000000004", fullName: "Test User", email: "test@gmail.com", password: "test", role: "decision_maker", active: true, deletable: false },
  { id: "c0000000000000000000000000000005", fullName: "Thandi Mokoena", email: "thandi@demo.co.za", password: "demo123", role: "submitter", active: false, deletable: false },
  { id: "c0000000000000000000000000000006", fullName: "Admin", email: "admin@gmail.com", password: "admin", role: "decision_maker", isAdmin: true, active: true, deletable: true },
];

const MOCK_SUBMISSION = {
  fileName: "Karoo_Outdoor_IS_FY2021-FY2025.xlsx",
  status: "approved",
  submittedBy: "Sipho Ndlovu",
  approvedBy: "Anika Pillay",
  approvedOn: "2026-09-28",
  scale: 1000,
  years: ["FY2021", "FY2022", "FY2023", "FY2024", "FY2025"],
  rows: [
    { label: "Sales", category: "revenue", values: [8200, 8950, 9600, 10350, 11100] },
    { label: "Cost of sales", category: "cost_of_sales", values: [-4920, -5390, -5810, -6260, -6700] },
    { label: "Gross profit", subtotal: "gross_profit", values: [3280, 3560, 3790, 4090, 4400] },
    { label: "Interest received", category: "other_income", values: [40, 45, 52, 60, 66] },
    { heading: "Operating expenses" },
    { label: "Salaries & wages", category: "employee_costs", values: [-1350, -1440, -1530, -1630, -1740] },
    { label: "Rent", category: "occupancy", values: [-480, -500, -525, -550, -580] },
    { label: "Electricity", category: "electricity", values: [-150, -175, -205, -240, -275] },
    { label: "Water", category: "other_utilities", values: [-36, -39, -42, -45, -49] },
    { label: "Telephone & internet", category: "other_utilities", values: [-48, -50, -52, -55, -58] },
    { label: "Fuel & delivery", category: "transport_fuel", values: [-130, -155, -170, -190, -200] },
    { label: "Advertising", category: "marketing", values: [-120, -135, -145, -160, -175] },
    { label: "Bank charges", category: "other_operating", values: [-28, -30, -32, -35, -37] },
    { label: "Repairs & maintenance", category: "other_operating", values: [-60, -55, -70, -75, -80] },
    { label: "Depreciation", category: "depreciation", values: [-90, -95, -100, -105, -110] },
    { label: "Operating profit", subtotal: "operating_profit", values: [828, 931, 971, 1065, 1162] },
    { label: "Interest paid", category: "finance_costs", values: [-45, -42, -40, -38, -35] },
    { label: "Profit before tax", subtotal: "profit_before_tax", values: [783, 889, 931, 1027, 1127] },
    { label: "Income tax", category: "income_tax", values: [-211, -240, -251, -277, -304] },
    { label: "Net profit", subtotal: "net_profit", values: [572, 649, 680, 750, 823] },
  ],
};

const MOCK_BUDGET = {
  method: "exponential_smoothing",
  parameters: { window_size: 3, alpha: 0.5 },
  horizon: 1,
  status: "approved",
  approvedBy: "Anika Pillay",
  approvedOn: "2026-10-02",
};

const MOCK_WARNING_RULES = [
  { id: "rule-cpi-headline", name: "Headline CPI above threshold", indicatorCode: "cpi_headline", threshold: 6, active: true, deletable: false },
  { id: "rule-cpi-core", name: "Core inflation above threshold", indicatorCode: "cpi_core", threshold: 6, active: true, deletable: false },
  { id: "rule-electricity", name: "Electricity price increase above threshold", indicatorCode: "electricity_price", threshold: 10, active: true, deletable: false },
  { id: "rule-fuel", name: "Fuel price increase above threshold", indicatorCode: "fuel_price", threshold: 10, active: true, deletable: false },
];

const MOCK_KPI_TARGETS = [
  { id: "kpi-001", metricName: "revenue", targetValue: 11000000, measurementUnit: "rand", targetPeriod: "2024-03-01" },
  { id: "kpi-002", metricName: "revenue", targetValue: 11800000, measurementUnit: "rand", targetPeriod: "2025-03-01" },
  { id: "kpi-003", metricName: "net_margin", targetValue: 7, measurementUnit: "percent", targetPeriod: "2025-03-01" },
  { id: "kpi-004", metricName: "electricity", targetValue: 280000, measurementUnit: "rand", targetPeriod: "2025-03-01" },
  { id: "kpi-005", metricName: "employee_costs", targetValue: 1800000, measurementUnit: "rand", targetPeriod: "2025-03-01" },
];

const MOCK_INDICATORS = {
  cpi_headline: { 2020: 3.3, 2021: 4.6, 2022: 6.9, 2023: 6.0, 2024: 6.3 },
  cpi_core: { 2020: 3.3, 2021: 3.1, 2022: 4.3, 2023: 4.7, 2024: 4.9 },
  electricity_price: { 2020: 7.2, 2021: 14.6, 2022: 9.8, 2023: 15.1, 2024: 12.7 },
  fuel_price: { 2020: -6.1, 2021: 18.9, 2022: 30.4, 2023: 4.2, 2024: 7.8 },
};

function uploadRowsFromSubmission(submission) {
  const blank = submission.years.map(() => null);
  return submission.rows.map((r) => {
    const row = { label: r.label, kind: "line", subtotal: null, values: r.values, mapping: null, remember: false };
    if (r.heading) return { ...row, label: r.heading, kind: "heading", values: blank };
    if (r.subtotal) return { ...row, kind: "subtotal", subtotal: r.subtotal };
    return { ...row, mapping: [{ category: r.category, percent: 100 }] };
  });
}

function withDemoRows(u) {
  if (u.id !== "sub-003" || u.rows) return u;
  return { ...u, periods: MOCK_SUBMISSION.years, scale: MOCK_SUBMISSION.scale, rows: uploadRowsFromSubmission(MOCK_SUBMISSION) };
}

const MOCK_UPLOADS = [
  {
    id: "sub-003",
    fileName: "Karoo_Outdoor_IS_FY2021-FY2025.xlsx",
    years: ["FY2021", "FY2025"],
    periods: MOCK_SUBMISSION.years,
    scale: MOCK_SUBMISSION.scale,
    rows: uploadRowsFromSubmission(MOCK_SUBMISSION),
    submittedBy: "Sipho Ndlovu",
    submittedOn: "2026-09-25",
    status: "approved",
    decidedBy: "Anika Pillay",
    decidedOn: "2026-09-28",
    decision: "approved",
  },
  {
    id: "sub-002",
    fileName: "Karoo_Outdoor_IS_FY2021-FY2025_draft.xlsx",
    years: ["FY2021", "FY2025"],
    submittedBy: "Sipho Ndlovu",
    submittedOn: "2026-09-18",
    status: "rejected",
    decidedBy: "Anika Pillay",
    decidedOn: "2026-09-22",
    comment: "Interest paid is missing for FY2023, so net profit doesn't match the signed statements. Please add it and upload again.",
  },
  {
    id: "sub-001",
    fileName: "Karoo_Outdoor_IS_FY2020-FY2024.xlsx",
    years: ["FY2020", "FY2024"],
    submittedBy: "Sipho Ndlovu",
    submittedOn: "2025-05-12",
    status: "superseded",
    decidedBy: "Anika Pillay",
    decidedOn: "2025-05-14",
    decision: "approved",
    supersededBy: "sub-003",
  },
];

const MOCK_SAMPLE_FILE = {
  name: "Karoo_Outdoor_IS_FY2022-FY2026.csv",
  csv: [
    "Karoo Outdoor Supplies (Pty) Ltd",
    "Income statement for the years ended 28 February",
    "Amounts in R'000",
    "",
    "Line,FY2022,FY2023,FY2024,FY2025,FY2026",
    "Sales,8950,9600,10350,11100,11800",
    "Cost of sales,(5 390),(5 810),(6 260),(6 700),(7 120)",
    "Gross profit,3560,3790,4090,4400,4680",
    "Interest received,45,52,60,66,70",
    "Operating expenses,,,,,",
    "Salaries & wages,(1 440),(1 530),(1 630),(1 740),(1 850)",
    "Rent,(500),(525),(550),(580),(610)",
    "Water & electricity,(214),(247),(285),(324),(362)",
    "Telephone & internet,(50),(52),(55),(58),(61)",
    "Fuel & delivery,(155),(170),(190),(200),(215)",
    "Advertising,(135),(145),(160),(175),(185)",
    "Bank charges,(30),(32),(35),(37),(39)",
    "Repairs & maintenance,(55),(70),(75),(80),(84)",
    "Franchise levy,(60),(64),(68),(72),(76)",
    "Depreciation,(95),(100),(105),(110),(116)",
    "Total operating expenses,(2 734),(2 935),(3 153),(3 376),(3 598)",
    "Operating profit,871,907,997,1090,1152",
    "Interest paid,(42),(40),(38),(35),(32)",
    "Profit before tax,829,867,959,1055,1120",
    "Income tax,(224),(234),(259),(285),(302)",
    "Net profit,605,633,700,770,818",
  ].join("\r\n"),
};

function buildDemoPendingUpload() {
  const choices = {
    "water & electricity": [{ category: "electricity", percent: 70 }, { category: "other_utilities", percent: 30 }],
    "franchise levy": [{ category: "other_operating", percent: 100 }],
  };
  const preview = buildPreview([parseCsv(MOCK_SAMPLE_FILE.csv)], "Karoo_Outdoor_IS_FY2022-FY2026.xlsx", choices);
  const payload = buildSubmissionPayload(preview, null);
  return {
    id: "sub-004",
    fileName: payload.fileName,
    years: [payload.years[0], payload.years[payload.years.length - 1]],
    periods: payload.years,
    scale: payload.scale,
    rows: payload.rows,
    submittedBy: "Sipho Ndlovu",
    submittedOn: "2026-10-05",
    status: "submitted",
    replacesId: null,
  };
}
