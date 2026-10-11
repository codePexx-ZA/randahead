# Backend — C# ASP.NET Core API

The main API for the SME budget forecasting system. It handles **login**, **users and business details**, **income statement uploads and their approval**, **forecasts and the chosen budget**, **KPI targets**, **the dashboard summary** (a year's figures, budget vs actual, target progress), **the combined report** for Decision Makers, **external data ingestion**, **warning rules** and **support messages**.

World Bank + OECD APIs → `BudgetApi` → PostgreSQL. `BudgetApi` → Python forecasting service (`../forecasting/`) for forecast runs; the browser never calls Python directly.

## What it does today

1. Fetches four South African indicators (annual % change, last 6 years — `IndicatorCatalog.HistoryYears`; each business later uses only the years inside its own income statement history):

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

- .NET SDK 10
- Docker Desktop (running)

## One-time setup (passwords are not in the code)

From the repo root, create `.env` for Docker Compose, then replace `JWT_KEY` with a long random string (at least 32 characters; it signs the sign-in tokens):

```powershell
Copy-Item .env.example .env
```

For `dotnet run`, store the full connection string and a token key in .NET user secrets (kept outside the project folder):

```powershell
cd backend\BudgetApi
dotnet user-secrets set "ConnectionStrings:DefaultConnection" "Host=localhost;Port=5432;Database=sme_budget;Username=budget;Password=<POSTGRES_PASSWORD from .env>"
dotnet user-secrets set "Jwt:Key" "<a long random string of at least 32 characters>"
```

The API refuses to start without a `Jwt:Key` of at least 32 characters.

## Login

| Endpoint | Body / header | Returns |
|---|---|---|
| `POST /api/auth/register` | `{ business_name, industry, year_end_month, full_name, email, password }` (no token; `industry` optional) | `201` with the same shape as login (signed in straight away); `400` invalid field or password under 8 characters; `409` email already has an account |
| `POST /api/auth/login` | `{ "email", "password" }` (email is not case-sensitive) | `200 { token, expiresAt, user: { id, fullName, email, role, unitId } }`; `401 { message }` wrong email or password; `403 { message }` account switched off |
| `GET /api/auth/me` | `Authorization: Bearer <token>` | The signed-in user, or `401` |
| `POST /api/auth/forgot-password` | `{ email }` (no token) | `200 { message }` whether or not the email has an account (no account leak); emails a one-time link to active accounts; `400` no email |
| `POST /api/auth/reset-password` | `{ token, password }` (no token header) | `200 { message }`; `400` password under 8 characters, or link unknown / used / expired / account switched off |
| `POST /api/auth/change-password` | `{ current_password, new_password }` + token | The user as in `/me`; `400` current password wrong (not `401`, so the web app doesn't sign out), new one too short or the same |

**Passwords.** Every user object has `mustChangePassword`. Passwords set by an Approver or Decision Maker (`POST /api/users`, or `PUT` with a `password`) are temporary (`true`); the web app sends the person to `password.html?change` until they choose their own. Registering, changing your own password and using a reset link set it to `false`. You can't set your own password through `PUT /api/users/{id}` (`400`). "Forgot password?" stores only a SHA-256 hash of a random 32-byte token in `password_reset`; the link (`Mail:WebAppUrl` + `/password.html?token=…`) works once, for 30 minutes, and asking again cancels the earlier link. Emails go through `Services/Mailer.cs` (SMTP, `Mail:*` settings): Docker Compose sends them to **Mailpit** (inbox at http://localhost:8025, nothing leaves your PC); `dotnet run` uses `localhost:1025`, so start Mailpit with `docker compose up -d mailpit`. If sending fails, or `Mail:Host` is empty, the email (with the link) is written to the API log. Existing tokens (JWTs) stay valid until they expire after a password change.

Registering creates, in one transaction, the `business` (country `ZAF`), its "Whole company" `business_unit` (ZAR), the person as its `decision_maker` and the 4 default warning rules (`Services/DefaultRules.cs`: headline and core CPI above 6%, fuel and electricity above 10%). They add their Approver and Submitters under Users & business.

Passwords are checked against BCrypt hashes in `app_user.password_hash`. Tokens are JWTs (HMAC-SHA256) that last `Jwt:ExpiryHours` (8) and carry the user id (`sub`), `email`, `name`, `role` and `unit_id`. The login and `/me` user also has `isAdmin`. Deleted users can't sign in. Demo sign-ins are listed in `../database/README.md`.

**Rate limits** (`Services/RateLimits.cs`, per client IP, sliding window): sign-in and change password 10 attempts per 5 minutes, register 5 per hour, forgot / reset password and support messages 5 per 15 minutes. Extra attempts get `429 { message }` with a `Retry-After` header (seconds) and never reach the controller, so they add no Activity log rows. Behind Docker every browser on the host shares one IP, so a wrong-password spree can briefly block other local sign-ins; restarting the API clears the counters.

CORS allows the web app at `http://127.0.0.1:5500` and `http://localhost:5500` (`Cors:AllowedOrigins` in `appsettings.json`).

**Every other endpoint needs a token** (`401` without one). `GET /api/indicators/...` is open to any signed-in user; `POST /api/ingestion/...` only to `approver` and `decision_maker` (`403` for a submitter). In Swagger, sign in with `POST /api/auth/login`, copy the `token`, click **Authorize** and paste it.

**Errors** (`Services/ErrorResponses.cs`): every failed request answers with a readable `{ message }` the web app can show as is, with CORS headers kept.

| Case | Status | Body |
|---|---|---|
| Body is empty, not JSON, or a field has the wrong type | `400` | `{ message, errors: { field: [messages] } }`, e.g. "The threshold value isn't in the right format." |
| `[Required]` field missing | `400` | Same shape, e.g. "The Password field is required." |
| Bare status code with no body (no token, wrong role, unknown route, wrong method, not JSON) | `401` / `403` / `404` / `405` / `415` … | `{ message }` with a fixed text per status |
| Database not reachable | `503` | `{ message, traceId }` |
| Unique or foreign key clash not caught by a controller, or a concurrent change | `409` | `{ message, traceId }` |
| Any other unhandled exception | `500` | `{ message, traceId }`; the full error is logged with the same `traceId`, never sent to the browser |

Controllers still return their own specific `{ message }` (e.g. "This rule no longer exists."); these are left untouched.

## Users and business

Everything is limited to the signed-in user's own business (found from their account). A switched-off account gets `401` even with a valid token. Errors come back as `{ message }`.

| Endpoint | Who | Body | Returns |
|---|---|---|---|
| `GET /api/business` | Any signed-in user | | `{ name, industry, country, yearEndMonth, unitId, unitName, currency, hasApprovedHistory }` |
| `PUT /api/business` | `decision_maker` | `{ legal_name, industry, year_end_month }` | The business as above; `400` missing name or month outside 1–12; `409` year-end changed once any upload is approved |
| `GET /api/users` | Any signed-in user | | `[{ id, fullName, email, role, active, isAdmin, mustChangePassword, deletable }]` |
| `POST /api/users` | `approver`, `decision_maker` | `{ full_name, email, role, active_status, password }` | The new user; `400` invalid field or password under 8 characters; `409` email already used (not case-sensitive) |
| `PUT /api/users/{id}` | `approver`, `decision_maker` | Same; `password` optional (empty keeps it, a new one becomes a temporary password; not for your own account) | The user; `409` duplicate email or it would remove the last active Approver / Decision Maker; `400` changing your own role or switching yourself off |
| `DELETE /api/users/{id}` | `approver`, `decision_maker` | Admin only: `{ password }` (their own) | `204`; `409` when uploads, decisions or remembered mappings point to the user (switch them off instead) or it would remove the last active Approver / Decision Maker; `400` your own account |

Rules: a change can't remove the business's last active `approver` or last active `decision_maker` (a newly registered business has no Approver until one is added). `deletable` is `true` when no `budget_submission`, `approval_action`, `import_mapping` or `budget_selection` row points to the user. Accounts are created for people by the owner or IT, who choose the first password and hand it over; passwords are stored as BCrypt hashes.

**Admin** (`app_user.is_admin`, e.g. `admin@gmail.com`; `isAdmin` in the login, `/me` and user responses) keeps its normal role for every other check. Differences:

- An admin can delete **any** user, including ones with history, after confirming with their own password (`400` missing, `403` wrong, so the web app doesn't sign them out). A user with history is kept as a deleted row (`deleted_at` set, switched off, password cleared) so their records stay; a user without history is removed. The safety rule above still applies.
- Only an admin can change or delete an admin account (`403` for everyone else).
- Deleted users can't sign in and don't appear in `GET /api/users`; their email can be reused.

## Uploads and approvals

The web app sends the Excel or CSV file to `preview`, which reads it with [ClosedXML](https://github.com/ClosedXML/ClosedXML) (`Services/StatementImporter.cs`) and suggests a category per line; the submitter checks and maps the lines and the app sends the mapped rows to `POST /api/submissions`. Everything is limited to the caller's business unit. An admin may also upload and decide.

| Endpoint | Who | Body | Returns |
|---|---|---|---|
| `GET /api/submissions?status=` | Any signed-in user | | Every upload that isn't a draft, newest first (`status` optional: `submitted`, `approved`, `rejected`, `superseded`): `{ id, fileName, years: [first, last], periods, scale, rows, submittedBy, submittedOn, status, replacesId, decision, decidedBy, decidedOn, comment, supersededBy }` |
| `POST /api/submissions/preview` | `submitter` | `multipart/form-data` with `file` (`.xlsx` or `.csv`, up to 5 MB) | Nothing is saved. `{ fileName, frequency: "annual", years, scale, scaleHint, rows: [{ id, label, key, values, kind, subtotal, status: "auto" \| "remembered" \| "cross" \| "unknown", mapping: { type: "single" \| "split" \| "ignore", category, parts }, candidates }], notes }`; `400` with a message for `.xls`, other file types, unreadable files, no year columns, monthly files, repeated years, more than 500 rows or no amounts |
| `GET /api/submissions/template` | Any signed-in user | | The standard Excel template (`Services/StatementTemplate.cs`): the 13 categories down, the last 5 completed financial years across (from the business's year-end), amounts in R'000, subtotal rows as formulas |
| `POST /api/submissions` | `submitter` | `{ fileName, frequency: "annual", scale: 1 \| 1000 \| 1000000, years: ["FY2022", …], replacesSubmissionId?, rows: [{ kind: "heading" \| "line" \| "subtotal" \| "total", label, subtotal?, values: [one per year], mapping?: [{ category, percent }], remember }] }` (`remember` = mapped by hand) | `201` the upload; `400` invalid file content (1 to 5 years, up to 500 rows, a revenue line, split percents adding up to 100); `409` the replaced upload isn't rejected or was already fixed |
| `POST /api/submissions/{id}/decision` | `approver` | `{ decision: "approved" \| "rejected", comments }` | The upload; `400` a rejection without comments; `404`; `409` already decided |

On upload the API saves the rows exactly as in the file (`submission_row`), one `financial_line_item` per category per year (all 13 categories; amount = sum of the mapped lines × split % × scale, as a positive figure) and the submitter's choices as remembered mappings (`import_mapping`). `import_method` is `mapped_import` when any line was mapped by hand, otherwise `template`.

On approval the rejected upload it fixes becomes `superseded`, as does any approved upload whose years are all covered by newer approved uploads (`supersededBy` names the upload that replaced it). For a year in more than one approved upload, the web app uses the newest.

## Forecasts and budgets

Forecasts run in the Python service (`ForecastService:BaseUrl`, `http://localhost:5090/` for `dotnet run`, `http://forecast:8000/` in Docker Compose); this API sends it the caller's unit and settings, then reads the saved runs back from the database. Runs come back in the Python service's shape (snake_case): `{ run_id, unit_id, forecasting_method, history_start, history_end, parameters, created_at, values: [{ category, display_name, forecast_period, baseline_amount, adjusted_amount }] }`, Moving Average first. Everything is limited to the caller's business unit; an admin may also choose and decide.

| Endpoint | Who | Body | Returns |
|---|---|---|---|
| `GET /api/forecasts/latest` | Any signed-in user | | The newest pair of runs `[MA, ES]`; `404` none yet |
| `POST /api/forecasts` | Any signed-in user | `{ frequency: "annual", horizon: 1–3, window_size: 2–3, alpha: 0.1–0.9 }` (defaults 1, 3, 0.5) | The two new runs; `400` setting out of range; `422` the history can't be forecast (Python's message); `502` the forecasting service isn't responding |
| `GET /api/budgets/latest` | Any signed-in user | | The newest budget that isn't `superseded`: `{ id, method, status, runIds, chosenBy, chosenOn, decision, decidedBy, decidedOn, comment, approvedBy, approvedOn, outOfDate, runs: [MA, ES] }`; `outOfDate` is `true` when the runs' `history_end` is not the latest approved period (a newer upload was approved since); `404` none yet |
| `POST /api/budgets` | `decision_maker` | `{ method: "moving_average" \| "exponential_smoothing" \| "combined", run_ids: [MA run, ES run] }` | The budget (`submitted`); `400` bad method or runs not from one forecast; `404` runs gone; `409` the approved history changed since the runs, or this option is already the budget |
| `POST /api/budgets/{id}/decision` | `approver` | `{ decision: "approved" \| "rejected", comments }` | The budget; `400` a rejection without comments; `404`; `409` already decided or replaced |

A new choice replaces a budget that is still waiting (`superseded`); approving one replaces the previously approved budget. The figures are not stored twice: the web app builds the budget from the two runs (`combined` = (MA + ES) ÷ 2 per category).

## KPI targets and warning rules

Targets belong to the caller's business unit, rules to the caller's business. Errors come back as `{ message }`.

| Endpoint | Who | Body | Returns |
|---|---|---|---|
| `GET /api/kpi-targets` | Any signed-in user | | `[{ id, metricName, targetValue, measurementUnit, targetPeriod }]` |
| `POST /api/kpi-targets`, `PUT /api/kpi-targets/{id}` | `decision_maker` | `{ metric_name, target_value, measurement_unit: "rand" \| "percent", target_period }`; metric = a line category, `net_profit` / `operating_profit` / `gross_profit` / `profit_before_tax` (rand) or `net_margin` / `operating_margin` / `gross_margin` (percent); period = first day of a financial year | The target (rands rounded to whole rands, percentages to 1 decimal); `400` unknown KPI, wrong unit, negative rands, margin outside ±100 or a date that isn't a year start; `404`; `409` this KPI and year already has a target |
| `DELETE /api/kpi-targets/{id}` | `decision_maker` | | `204`; `404` |
| `GET /api/rules` | Any signed-in user | | `[{ id, name, indicatorCode, threshold, active, deletable }]`; `deletable` is `false` once the rule has raised an alert |
| `POST /api/rules`, `PUT /api/rules/{id}` | `approver`, `decision_maker` | `{ rule_name, indicator_code, threshold_value: 0–100, active_status }` (the API fills in `condition` and `action`) | The rule; `400`; `404`; `409` the name is already used in this business (case-insensitive) |
| `DELETE /api/rules/{id}` | `approver`, `decision_maker` | | `204`; `404`; `409` the rule has raised alerts, so it can only be switched off |
| `GET /api/indicators/series` | Any signed-in user | | Validated indicator values per code and calendar year, e.g. `{ "cpi_headline": { "2024": 4.36 } }`; the web app checks each rule against the latest year inside the business's history |

## Dashboard and reports

Figures come from `Services/StatementFigures.cs` (shared by both endpoints, along with the KPI target checks): the unit's approved history per financial year (latest 5 years; where uploads overlap, the newest submitted upload wins per category and year, as in the Python service), the 4 subtotals worked out from the 13 categories, and budget figures from a budget's two runs (MA, ES or their average per category, rounded to whole rands).

| Endpoint | Who | Query | Returns |
|---|---|---|---|
| `GET /api/dashboard` | Any signed-in user | `year` (e.g. `FY2025`; default the latest year in the history) | `{ years, year, previousYear, budget, lines, targets }`. `lines`: the 13 categories and 4 subtotals in statement order, `{ code, name, kind: "income" \| "expense" \| "total", teamManaged, subtotal, actual, previous, change, budget, variance, variancePercent }` (`change` and `variancePercent` in % to 1 decimal, `null` without a figure to compare). `budget`: the most recently approved budget whose forecast covers `year`, `{ id, method, status, approvedBy, approvedOn }`, or `null` (then the budget fields are `null`). `targets`: every KPI target, newest year first, `{ id, metricName, name, unit, higherIsBetter, year, targetPeriod, targetValue, source: "actual" \| "budget" \| null, value, gap, reached, status: "met" \| "missed" \| "on_track" \| "off_track" \| "none" }`, checked against the year's actual, else the latest budget that isn't `superseded`. With no approved history: empty `years` and `lines`; `400` a year outside the history |
| `GET /api/reports` | Decision Maker (or admin) | none | The combined report: `{ business, unit, generatedOn, columns, changeFrom, changeTo, budget, lines, ratios, targets }`. `columns`: every approved year, then the approved budget's years after the last actual year, `{ year, source: "actual" \| "budget" }`. `lines`: the 13 categories and 4 subtotals, `{ code, name, kind, teamManaged, subtotal, values, change }` with one value per column and `change` in % from `changeFrom` to `changeTo` (first and last actual year; `null` with one year). `ratios`: revenue growth (vs the column before) and gross, operating and net margin (% of revenue, 1 decimal), `{ code, name, values }`. `budget`: the currently approved budget `{ id, method, status, approvedBy, approvedOn }` or `null`. `targets`: as for the dashboard. `403` for other roles |

## Activity log

Every sign-in (and failed sign-in on a known email), registration, upload, decision, forecast run, budget choice and change to targets, rules, business details and users adds an `audit_log` row through `Services/AuditLog.cs`, in the same save as the change. Edits record only the fields that changed (`{ field, before, after }`); a password set by someone else shows as "Temporary password set", never the value; reset requests, reset-link use and own password changes are `account` rows. Rows are never edited or deleted.

| Endpoint | Who | Query | Returns |
|---|---|---|---|
| `GET /api/audit` | Any signed-in user: the admin gets the whole business, everyone else only their own rows | `area` (`account`, `upload`, `forecast`, `budget`, `target`, `rule`, `business`, `user`), `user` (admin only), `from`, `to` (dates, South African time) | `{ scope: "business" \| "own", more, people (admin only: [{ id, name }]), entries: [{ id, at, who, area, action, summary, details }] }`, newest first, at most 200 (`more` is `true` when older rows exist); `400` unknown area or `from` after `to` |

## Contact support

| Endpoint | Who | Body | Returns |
|---|---|---|---|
| `POST /api/support` | Any signed-in user | `{ name, email, topic, message }`; `topic` is one of "Uploading a statement", "Forecasts and budgets", "Account and access", "Something else"; `message` up to 2 000 characters | `200 { message }`; `400` missing name or message, invalid email, unknown topic, message too long; `503` the email couldn't be sent |

Emails the message to `Mail:SupportTo` (`support@randahead.local`; lands in Mailpit in development) with the sender's email as the reply address, plus their business, account and role. Rate limited to 5 messages per 15 minutes per IP. Not stored in the database.

## Run

Everything in containers, from the repo root:

```powershell
docker compose up -d --build
```

Or only the database in Docker and the API locally (do not run both, they share port 5080):

```powershell
docker compose up -d postgres
cd backend\BudgetApi
dotnet run
```

Swagger: http://localhost:5080/swagger

## Trigger an ingest

`POST /api/ingestion/run` (all four indicators) or `POST /api/ingestion/{code}` (one indicator, e.g. `fuel_price`):

```powershell
$login = Invoke-RestMethod -Method Post -Uri http://localhost:5080/api/auth/login -ContentType "application/json" -Body '{"email":"owner@demo.co.za","password":"demo123"}'
Invoke-RestMethod -Method Post -Uri http://localhost:5080/api/ingestion/run -Headers @{ Authorization = "Bearer $($login.token)" }
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

It currently writes `data_source`, `external_indicator`, `rule_alert`, `rule_definition`, `kpi_target`, `business`, `app_user`, `budget_submission`, `submission_row`, `financial_line_item`, `import_mapping`, `budget_selection`, `approval_action`, `audit_log` and `password_reset`, and reads `indicator_type`, `indicator_category_link`, `line_category`, `business_unit`, `forecast_run` and `forecast_value` (the Python service writes those two).

## Structure

| Folder | Purpose |
|---|---|
| `BudgetApi/Models` | C# classes mapped to existing tables |
| `BudgetApi/Data` | `BudgetDbContext` (EF Core table and column mapping) |
| `BudgetApi/Services` | Business logic: API clients (World Bank, OECD, the forecasting service), ingestion, rule engine, sign-in tokens, statement figures (history, subtotals, budget figures, KPI values) |
| `BudgetApi/Controllers` | HTTP endpoints |
