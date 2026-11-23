# Backend API in C# and ASP.NET Core

The main API for the SME budget forecasting system. It handles **login** (one temporary development account), **business details**, **income statement uploads**, **forecasts and the chosen budget**, **KPI targets**, **the dashboard summary** (a year's figures, budget vs actual, target progress), **the combined report**, **external data ingestion**, **warning rules** and **support messages**.

`BudgetApi` fetches indicators from the World Bank and OECD APIs and saves them in PostgreSQL. For forecasts it sends the active history to the Python forecasting service (`../forecasting/`), which only calculates, and saves the results itself. The browser never calls Python directly.

## What it does today

1. Fetches four South African indicators (annual % change for the last 6 years, set by `IndicatorCatalog.HistoryYears`; each business later uses only the years inside its own income statement history):

   | Code | Indicator | API |
   |---|---|---|
   | `cpi_headline` | Headline consumer inflation | World Bank `FP.CPI.TOTL.ZG` |
   | `cpi_core` | Core inflation (CPI excl. food and energy; replaces CPIX) | OECD `_TXCP01_NRG` |
   | `electricity_price` | Electricity price change | OECD `CP045` |
   | `fuel_price` | Fuel price change | OECD `CP0722` |

   Neither API needs a key. Where each one is fetched from is in `Services/IndicatorCatalog.cs`.
2. Validates country, year, and value range.
3. Upserts rows into `data_source` and `external_indicator`.
4. Runs one transparent rule per indicator: a value above the rule's `threshold_value` creates a `rule_alert` naming the income statement categories to review (from `indicator_category_link`), e.g. "South African fuel price change for 2022 is 34.26% (threshold 10.0%). Review forecasts for: Transport and fuel." Re-running the ingest does not create duplicate alerts (matched on the message text).
5. Read endpoints let you inspect indicators, alerts, the indicator catalog and the categories.

Sales and salaries are **team-managed** (set by the business, e.g. salaries at inflation + 1%), so no indicator applies to them.

## Prerequisites

- .NET SDK 10 (for `dotnet run` and `dotnet test`; the container build uses `mcr.microsoft.com/dotnet/sdk:10.0`)
- Docker Engine with the Compose plugin

## One-time setup (passwords are not in the code)

From the repo root, create `.env` for Docker Compose and set `POSTGRES_PASSWORD` and `JWT_KEY` (the key is a random string of at least 32 characters; it signs the sign-in tokens). Compose refuses to start while either is missing. Full start-up steps, including the external database volume, are in the [root README](../README.md#run-it):

```bash
cp .env.example .env
```

For `dotnet run`, store the full connection string and a token key in .NET user secrets (kept outside the project folder):

```bash
cd backend/BudgetApi
dotnet user-secrets set "ConnectionStrings:DefaultConnection" "Host=localhost;Port=5432;Database=sme_budget;Username=budget;Password=<POSTGRES_PASSWORD from .env>"
dotnet user-secrets set "Jwt:Key" "<a long random string of at least 32 characters>"
```

The API refuses to start without a `Jwt:Key` of at least 32 characters.

## Login

**Temporary development login.** There is exactly one account, seeded in `../database/init/03_seed.sql` as a BCrypt hash; its username and password are in the [root README](../README.md#sign-in). It has no role: the signed-in user may call every endpoint. One shared account with a known password is not secure; sign-in and permissions must be revisited before any real use.

| Endpoint | Body / header | Returns |
|---|---|---|
| `POST /api/auth/login` | `{ "username", "password" }` (username is trimmed and not case-sensitive) | `200 { token, expiresAt, user: { id, fullName, username, unitId } }`; `401 { message }` wrong username or password |
| `GET /api/auth/me` | `Authorization: Bearer <token>` | The signed-in user, or `401` |

There is no registration, user management, password reset or password change. Passwords are checked against the BCrypt hash in `app_user.password_hash`. Tokens are JWTs (HMAC-SHA256) that last `Jwt:ExpiryHours` (8) and carry the user id (`sub`), `username`, `name` and `unit_id`. The signed-in user is loaded once per request while the token is checked (`Services/CallerSession.cs`); a token whose user no longer exists gets `401`.

**Rate limits** (`Services/RateLimits.cs`, per client IP, sliding window): sign-in 10 attempts per 5 minutes, support messages and ingestion runs 5 per 15 minutes. Extra attempts get `429 { message }` with a `Retry-After` header (seconds) and never reach the controller, so they add no Activity log rows. Behind Docker every browser on the host shares one IP, so a wrong-password spree can briefly block other local sign-ins; restarting the API clears the counters.

CORS allows the web app at `http://127.0.0.1:5500` and `http://localhost:5500` (`Cors:AllowedOrigins` in `appsettings.json`).

**Every other endpoint needs a token** (`401` without one); with a token, nothing is limited by role. In Swagger, sign in with `POST /api/auth/login`, copy the `token`, click **Authorize** and paste it.

**Errors** (`Services/ErrorResponses.cs`): every failed request answers with a readable `{ message }` the web app can show as is, with CORS headers kept.

| Case | Status | Body |
|---|---|---|
| Body is empty, not JSON, or a field has the wrong type | `400` | `{ message, errors: { field: [messages] } }`, e.g. "The threshold value isn't in the right format." |
| `[Required]` field missing | `400` | Same shape, e.g. "The Password field is required." |
| Bare status code with no body (no token, unknown route, wrong method, not JSON) | `401` / `404` / `405` / `415` … | `{ message }` with a fixed text per status |
| Database not reachable | `503` | `{ message, traceId }` |
| Unique or foreign key clash not caught by a controller, or a concurrent change | `409` | `{ message, traceId }` |
| Any other unhandled exception | `500` | `{ message, traceId }`; the full error is logged with the same `traceId`, never sent to the browser |

Controllers still return their own specific `{ message }` (e.g. "This rule no longer exists."); these are left untouched.

## Business

Everything is limited to the signed-in user's own business. Errors come back as `{ message }`.

| Endpoint | Who | Body | Returns |
|---|---|---|---|
| `GET /api/business` | Signed-in user | | `{ name, industry, country, yearEndMonth, unitId, unitName, currency, hasHistory }` |
| `PUT /api/business` | Signed-in user | `{ legal_name, industry, year_end_month }` | The business as above; `400` missing name or month outside 1–12; `409` year-end changed once any upload or KPI target exists |

## Uploads

The web app sends the Excel or CSV file to `preview`, which reads it with [ClosedXML](https://github.com/ClosedXML/ClosedXML) (`Services/StatementImporter.cs`) and suggests a category per line; the user checks and maps the lines and the app sends the mapped rows to `POST /api/submissions`. Saving makes the statement active at once: there is no approval step. Everything is limited to the caller's business unit.

| Endpoint | Who | Body | Returns |
|---|---|---|---|
| `GET /api/submissions?status=` | Signed-in user | | Every upload, newest first (`status` optional: `active`, `superseded`): `{ id, fileName, years: [first, last], periods, scale, rows, submittedBy, submittedOn, status, supersededBy, statement }`; `statement` is the upload's statement table (below), `null` without rows |
| `POST /api/submissions/preview` | Signed-in user | `multipart/form-data` with `file` (`.xlsx` or `.csv`, up to 32 MB) | Unrecognised labels are sent, as names only, to DeepSeek V4.1 Flash (`deepseek-flash`) when `DEEPSEEK_API_KEY` is set. The upload page shows whether that call ran. Accepted categories are listed under AI API activity. Nothing is saved. `{ fileName, frequency: "annual", years, scale, scaleHint, rows: [{ id, label, key, values, kind, subtotal, status: "auto" \| "remembered" \| "cross" \| "unknown", mapping: { type: "single" \| "split" \| "ignore", category, parts }, candidates }], notes, statement, sheets, info, title }`; `statement` is `null` while a line has no category; `sheets` (`[{ name, used, detail }]`), `info` and `title` are filled only for monthly workbooks (below); `400` with a message for `.xls`, other file types, unreadable files, no year or month columns, no complete financial year in a monthly workbook, repeated years, more than 500 rows or no amounts |
| `GET /api/submissions/template` | Signed-in user | | The standard Excel template (`Services/StatementTemplate.cs`): the 13 categories down, the last 5 completed financial years across (from the business's year-end), amounts in R'000, subtotal rows as formulas |
| `POST /api/submissions` | Signed-in user | `{ fileName, frequency: "annual", scale: 1 \| 1000 \| 1000000, years: ["FY2022", …], rows: [{ kind: "heading" \| "line" \| "subtotal" \| "total", label, subtotal?, values: [one per year], mapping?: [{ category, percent }], remember }] }` (`remember` = mapped by hand) | `200` the saved upload, already `active`; `400` invalid file content (1 to 5 years, up to 500 rows, a revenue line, split percents adding up to 100) |
| `POST /api/submissions/check` | Signed-in user | Same as `POST /api/submissions` | Nothing is saved. The statement table the rows would give, while the user is still mapping; `400` invalid rows (as above, except a missing revenue line) |

### Monthly workbooks (management accounts)

A file with month columns instead of year columns goes through `Services/StatementImporter.Monthly.cs`. It is built for management-accounts packs: one sheet per financial year in any tab order, with clutter around the income statement. Its result is the same annual preview, so mapping, checks and saving work unchanged.

| Step | What happens |
|---|---|
| Choosing sheets | Every sheet with a row of at least three different months (real Excel dates, dates stored as formatted numbers, or text such as `Mar-26`) is a candidate. It is used when it has sales lines plus a cost or profit line. Hidden sheets are used only when no visible sheet qualifies. A sheet named Data, and any much shorter summary sheet, is left out when the year sheets have the statement. Loan schedules, summaries and similar sheets are listed with the reason they were not used |
| Choosing the block | A sheet can have more than one month header row (for example a prior-year summary above the statement). The block with the most statement lines wins |
| Account codes | When at least three rows carry an account code (`4000000`, `4000/000`) left of the label, only coded rows are lines and rows are matched across sheets by code, so a renamed account (`Accounting Fees - Bruce Morrison` → `Accounting Fees - Bma`) stays one line, labelled as in the newest sheet. An uncoded heading directly above coded accounts is a section: its figures are skipped (they are the section total), unless every account under it is empty, in which case the heading itself is the line (a `Cost of Sales` row with no detail). The heading also suggests a category for unknown accounts under it (`Tax` → income tax, `Dividends` → ignored) |
| Where the statement ends | After the net-profit-after-tax row, or at the first balance-sheet account (code 5000 and up, Pastel/Sage numbering) |
| Skipped rows | Percentages and ratios, VAT-inclusive sales, totals and other calculated rows. The sheet's gross profit, profit before tax and net profit rows are kept as subtotals to cross-check the mapping. Net profit is not checked when dividends are deducted before it. Accounts with no amounts in the years used are left out |
| Years | Months are added up into the business's financial years (from its year-end month). When two sheets carry the same month, the newer sheet wins. Only complete years (12 months with figures) are used; a part year (a store opening, or the current year) is left out with a note naming its months. The latest 5 full years are kept |

A **statement table** is `{ years, totals: { category: [one per year] }, subtotals: { gross_profit, operating_profit, profit_before_tax, net_profit }, totalExpenses, checks }` in rands. `checks` has one entry per subtotal row in the file, `{ matches, diffs }` (`diff` = worked out − file × scale; it matches when every diff is within one unit of the scale).

On save the API stores the rows exactly as in the file (`submission_row`), one `financial_line_item` per category per year (all 13 categories; amount = sum of the mapped lines × split % × scale, as a positive figure) and the user's choices as remembered mappings (`import_mapping`). `import_method` is `mapped_import` when any line was mapped by hand, otherwise `template`.

**Version history.** A saved upload is `active`. In the same save, any older active upload whose years are all covered by newer active uploads becomes `superseded` (`supersededBy` names the upload that replaced it); superseded uploads stay on record and are never deleted. For a year in more than one active upload, the newest is used.
## Forecasts and budgets

Forecasts run in the Python service (`ForecastService:BaseUrl`, `http://localhost:5090/` for `dotnet run`, `http://forecast:8000/` in Docker Compose); the Python service only calculates. This API loads the unit's active history, sends it with the settings, and saves the two `forecast_run` rows and their `forecast_value` rows itself, in the same save as the audit entry. Each run stores `history_updated_at`, the time of the unit's newest active upload when the forecast was made. Runs come back in snake_case: `{ run_id, unit_id, forecasting_method, history_start, history_end, parameters, created_at, values: [{ category, display_name, forecast_period, baseline_amount, adjusted_amount }] }`, Moving Average first, inside `{ runs, comparison, matchesHistory }`. `matchesHistory` is false when a later upload has been saved, including a correction of an earlier year. Everything is limited to the caller's business unit.

| Endpoint | Who | Body | Returns |
|---|---|---|---|
| `GET /api/forecasts/latest` | Signed-in user | | The newest pair, `{ runs: [MA, ES], comparison, matchesHistory }`; `comparison` is `{ years, methods: { moving_average, exponential_smoothing, combined } }`, each a statement table without `checks`; `matchesHistory` is false once a newer upload is saved; `404` none yet |
| `POST /api/forecasts` | Signed-in user | `{ frequency: "annual", horizon: 1–3, window_size: 2–3, alpha: 0.1–0.9 }` (defaults 1, 3, 0.5). The web app only sends the three predictions: Steady `3` / `0.3`, Balanced `3` / `0.5`, Recent `2` / `0.7` | The two new runs, shaped as for `latest`; `400` setting out of range; `422` the history can't be forecast (Python's message); `502` the forecasting service isn't responding |
| `GET /api/budgets/latest` | Signed-in user | | The chosen (`active`) budget: `{ id, method, status, runIds, chosenBy, chosenOn, outOfDate, runs: [MA, ES], comparison }` (`comparison` as for forecasts); `outOfDate` is `true` when the runs' `history_updated_at` is not the time of the unit's newest active upload, so a correction of an earlier year counts; `404` none yet |
| `POST /api/budgets` | Signed-in user | `{ method: "moving_average" \| "exponential_smoothing" \| "combined", run_ids: [MA run, ES run] }` | The budget, already `active` (the web app's **Use as budget**); `400` bad method or runs not from one forecast; `404` runs gone; `409` the history changed since the runs, or this option is already the budget |

Choosing a budget makes it `active` at once and the previous one `superseded`; there is no approval step. The figures are not stored twice: the API builds the budget from the two runs (`combined` = (MA + ES) ÷ 2 per category, rounded half away from zero).

## KPI targets and warning rules

Targets belong to the caller's business unit, rules to the caller's business. Errors come back as `{ message }`.

| Endpoint | Who | Body | Returns |
|---|---|---|---|
| `GET /api/kpi-targets` | Signed-in user | | `{ targets, figures }`: `targets` as for the dashboard (below); `figures` is every KPI's value per actual year, then per year of the chosen budget after them, `[{ year, source: "actual" \| "budget", values: { kpi: value } }]` |
| `POST /api/kpi-targets`, `PUT /api/kpi-targets/{id}` | Signed-in user | `{ metric_name, target_value, measurement_unit: "rand" \| "percent", target_period }`; metric = a line category, `net_profit` / `operating_profit` / `gross_profit` / `profit_before_tax` (rand) or `net_margin` / `operating_margin` / `gross_margin` (percent); period = first day of a financial year | The target (rands rounded to whole rands, percentages to 1 decimal); `400` unknown KPI, wrong unit, negative rands, margin outside ±100 or a date that isn't a year start; `404`; `409` this KPI and year already has a target |
| `DELETE /api/kpi-targets/{id}` | Signed-in user | | `204`; `404` |
| `GET /api/rules` | Signed-in user | | `[{ id, name, indicatorCode, threshold, active, deletable }]`; `deletable` is `false` once the rule has raised an alert |
| `POST /api/rules`, `PUT /api/rules/{id}` | Signed-in user | `{ rule_name, indicator_code, threshold_value: 0–100, active_status }` (the API fills in `condition` and `action`) | The rule; `400`; `404`; `409` the name is already used in this business (case-insensitive) |
| `DELETE /api/rules/{id}` | Signed-in user | | `204`; `404`; `409` the rule has raised alerts, so it can only be switched off |
| `GET /api/indicators/series` | Signed-in user | | Validated indicator values per code and calendar year, e.g. `{ "cpi_headline": { "2024": 4.36 } }`; the web app checks each rule against the latest year inside the business's history |

## Income statement definitions

`Services/IncomeStatement.cs` is the one place the statement's maths lives: the 13 categories' order and sides, the 4 subtotal formulas, the statement layout, the KPI list, category totals from mapped rows (each side's sign set by its overall sum, so a line signed against its side stays negative), subtotal checks and KPI values. The upload checks, template formulas, forecast comparison, budget, dashboard, reports and target checks all use it.

| Endpoint | Who | Returns |
|---|---|---|
| `GET /api/statement/definitions` | Signed-in user | `{ categories: [{ code, name, kind, teamManaged }], subtotals: [{ code, name }], layout: [{ category?, subtotal?, heading? }], kpis: [{ code, name, group, unit: "rand" \| "percent", higherIsBetter }], indicators: { indicator: [category, …] } }` |

## Dashboard and reports

Figures come from `Services/StatementFigures.cs` (shared by both endpoints, along with the KPI target checks): the unit's active history per financial year (latest 5 years; where uploads overlap, the newest submitted upload wins per category and year; the same history is what forecasts are run on), the 4 subtotals worked out from the 13 categories, and budget figures from a budget's two runs (MA, ES or their average per category, rounded to whole rands).

| Endpoint | Who | Query | Returns |
|---|---|---|---|
| `GET /api/dashboard` | Signed-in user | `year` (e.g. `FY2025`; default the latest year in the history) | `{ years, year, previousYear, budget, lines, targets }`. `lines`: the 13 categories and 4 subtotals in statement order, `{ code, name, kind: "income" \| "expense" \| "total", teamManaged, subtotal, actual, previous, change, budget, variance, variancePercent }` (`change` and `variancePercent` in % to 1 decimal, `null` without a figure to compare). `budget`: the most recently chosen budget whose forecast covers `year`, `{ id, method, status, chosenBy, chosenOn }`, or `null` (then the budget fields are `null`). `targets`: every KPI target, newest year first, `{ id, metricName, name, unit, higherIsBetter, year, targetPeriod, targetValue, source: "actual" \| "budget" \| null, value, gap, reached, status: "met" \| "missed" \| "on_track" \| "off_track" \| "none" }`, checked against the year's actual, else the chosen budget. With no history: empty `years` and `lines`; `400` a year outside the history |
| `GET /api/reports` | Signed-in user | none | The combined report: `{ business, unit, generatedOn, columns, changeFrom, changeTo, budget, lines, ratios, targets }`. `columns`: every year of the history, then the chosen budget's years after the last actual year, `{ year, source: "actual" \| "budget" }`. `lines`: the 13 categories and 4 subtotals, `{ code, name, kind, teamManaged, subtotal, values, change }` with one value per column and `change` in % from `changeFrom` to `changeTo` (first and last actual year; `null` with one year). `ratios`: revenue growth (vs the column before) and gross, operating and net margin (% of revenue, 1 decimal), `{ code, name, values }`. `budget`: the chosen budget `{ id, method, status, chosenBy, chosenOn }` or `null`. `targets`: as for the dashboard |

## Activity log

Every upload, decision, forecast run, budget choice and change to targets, rules and business details adds an `audit_log` row through `Services/AuditLog.cs`, in the same save as the change. Sign-in, a failed sign-in and sign-out do not. A forecast run records the prediction name (Steady, Balanced or Recent) when it matches one, plus the moving-average window and the smoothing weight. Edits record only the fields that changed (`{ field, before, after }`). Rows are never edited or deleted.

| Endpoint | Who | Query | Returns |
|---|---|---|---|
| `GET /api/audit` | Signed-in user | `area` (`upload`, `forecast`, `budget`, `target`, `rule`, `business`), `from`, `to` (dates, South African time) | `{ more, entries: [{ id, at, who, area, action, summary, details }] }`, newest first, at most 200 (`more` is `true` when older rows exist); `400` unknown area or `from` after `to` |

## Contact support

| Endpoint | Who | Body | Returns |
|---|---|---|---|
| `POST /api/support` | Signed-in user | `{ name, email, topic, message }`; `topic` is one of "Uploading a statement", "Forecasts and budgets", "Account and access", "Something else"; `message` up to 2 000 characters | `200 { message }`; `400` missing name or message, invalid email, unknown topic, message too long; `503` the email couldn't be sent |

Emails the message to `Mail:SupportTo` (`support@randahead.local`; lands in Mailpit in development) with the sender's email as the reply address, plus their business and account. Rate limited to 5 messages per 15 minutes per IP. Not stored in the database.

## Run

Everything in containers, from the repo root (the database volume must exist first, see the [root README](../README.md#run-it)):

```bash
docker compose up -d --build
```

Or only the database in Docker and the API locally (do not run both, they share port 5080):

```bash
docker compose up -d postgres
cd backend/BudgetApi
dotnet run
```

Swagger: http://127.0.0.1:5080/swagger

## Trigger an ingest

`POST /api/ingestion/run` (all four indicators) or `POST /api/ingestion/{code}` (one indicator, e.g. `fuel_price`):

```bash
TOKEN=$(curl -s -X POST http://127.0.0.1:5080/api/auth/login \
  -H "Content-Type: application/json" \
  -d '{"username":"demo123","password":"test"}' | python3 -c 'import sys,json; print(json.load(sys.stdin)["token"])')
curl -s -X POST http://127.0.0.1:5080/api/ingestion/run -H "Authorization: Bearer $TOKEN"
```

| Endpoint | Returns |
|---|---|
| `GET /api/indicators?code=fuel_price&take=10` | Saved indicator values (`code` optional) |
| `GET /api/indicators/alerts` | Warning alerts raised by the caller's business's rules |
| `GET /api/indicators/catalog` | The four indicators, their sources, the caller's business's thresholds and linked categories |
| `GET /api/indicators/categories` | The 13 income statement categories |

## Connection string

`appsettings.json` holds the connection string **without** the password. The full string comes from user secrets (`dotnet run`) or the `ConnectionStrings__DefaultConnection` environment variable (Docker Compose, using `POSTGRES_PASSWORD` from `.env`).

## Tables

This API does **not** create tables or seed data. Tables come from `../database/init/01_schema.sql`; categories, indicator types and links come from `02_reference_data.sql`; demo data, including the demo business's warning rules, from `03_seed.sql` (see `../database/README.md`).

It currently writes `data_source`, `external_indicator`, `rule_alert`, `rule_definition`, `kpi_target`, `business`, `app_user`, `budget_submission`, `submission_row`, `financial_line_item`, `import_mapping`, `budget_selection`, `forecast_run`, `forecast_value` and `audit_log`, and reads `indicator_type`, `indicator_category_link`, `line_category` and `business_unit`. The Python service has no database access.

## Structure

| Folder | Purpose |
|---|---|
| `BudgetApi/Models` | C# classes mapped to existing tables |
| `BudgetApi/Data` | `BudgetDbContext` (EF Core keys, relations and column types; table and column names come from the snake_case naming convention, and `SchemaTests` checks the model against `database/init/01_schema.sql`) |
| `BudgetApi/Services` | Business logic: API clients (World Bank, OECD, the forecasting service), forecast runs, ingestion, rule engine, sign-in tokens, the signed-in user (`CallerSession`), the income statement definitions and maths (`IncomeStatement`), statement figures (history, budget figures, target progress) |
| `BudgetApi/Controllers` | HTTP endpoints; all derive from `ApiController` (the signed-in user as `Caller`, `Forbidden(message)`) |
| `BudgetApi.Tests` | xUnit tests: reading a sample statement, the income statement maths (category totals, subtotals, checks, KPIs, target progress, template formulas), financial years, the EF model against the schema, and the single login: seed, sign-in, a token on every endpoint but login, no role limits (`dotnet test BudgetApi.Tests`) |
