# Frontend — HTML, CSS and JavaScript

Responsive web app for **RandAhead™** (*Clear forecasts. Confident decisions.*), the SME budget forecasting system. Plain HTML, CSS and JavaScript (no build step). It talks **only** to the C# API (`http://localhost:5080/api`), never to the Python service.

## Run it

Open `frontend/index.html` with the VS Code / Cursor **Live Server** extension ("Go Live"), or any static file server.

Settings in `js/config.js`:

| Setting | Default | Meaning |
|---|---|---|
| `USE_MOCK_LOGIN` | `false` | `false`: sign in through the C# API (`POST /api/auth/login`, needs `docker compose up` and the demo users from `database/init/03_seed.sql`). `true`: sign in against the demo users in the browser, no API needed |
| `USE_MOCK_USERS` | `false` | `false`: Users & business uses the C# API (`/api/business`, `/api/users`). `true`: demo data in the browser (also used automatically while signed in with the mock login) |
| `USE_MOCK_SUBMISSIONS` | `false` | `false`: uploads, their approval and the statement history on the dashboard use the C# API (`/api/submissions`). `true`: demo uploads in the browser (also used automatically while signed in with the mock login) |
| `USE_MOCK_FORECASTS` | `false` | `false`: Forecast & budget, the budget on the dashboard and budget approvals use the C# API (`/api/forecasts`, `/api/budgets`). `true`: the maths runs in the browser on demo data (also used automatically while signed in with the mock login) |
| `USE_MOCK_TARGETS` | `false` | `false`: KPI targets, warning rules and the economic warnings use the C# API (`/api/kpi-targets`, `/api/rules`, `/api/indicators/series`). `true`: demo targets, rules and indicator values in the browser (also used automatically while signed in with the mock login) |
| `SHOW_DEMO_ACCOUNTS` | `true` | Read-only notes name the demo account that has the needed role (e.g. "sign in as approver@demo.co.za"). Turn off for a real business |

With the real login, the token is kept with the session and sent as `Authorization: Bearer …` on every API call; a `401` signs the user out. The session ends when the token expires (8 hours). Opening `dashboard.html` without a session goes to the login page, and the dashboard re-checks the user with `GET /api/auth/me` when it loads. **Register:** "Register here" on the login page opens `register.html` (`css/register.css`, `js/register.js`): business name, industry, year-end month, then the person's name, email and password (twice). `POST /api/auth/register` makes them the business's Decision Maker and signs them in. Until the first upload is approved, the dashboard shows one "No approved income statement yet" message, Forecast & budget and KPI targets wait, and Approvals, warning rules and Users & business work as usual. The industry list, email pattern and minimum password length live in `js/config.js`. The API allows the web app only from `http://127.0.0.1:5500` and `http://localhost:5500` (Live Server's default port).

These **fictional** demo accounts work in both login modes (password `demo123`):

| Email | Role |
|---|---|
| `submitter@demo.co.za` | Submitter |
| `approver@demo.co.za` | Approver |
| `owner@demo.co.za` | Decision Maker |
| `test@gmail.com` (password `test`) | Decision Maker (test account) |
| `admin@gmail.com` (password `admin`) | **Admin** (a Decision Maker account with the admin flag, for senior IT management): the web app lets it do everything every role can, with no read-only notes, and it can delete users with history. The password is a development placeholder |
| `thandi@demo.co.za` | Submitter, **switched off** (sign-in is refused until a user manager switches her back on in Users & business) |

Users added in Users & business sign in with the temporary password set when they were added. With `USE_MOCK_USERS` off they are saved in the database and sign in through the API; users added in demo data only sign in when `USE_MOCK_LOGIN` is `true`.

**Passwords** (`password.html`, `css/password.css`, `js/password.js`; one page, three forms):

- **Forgot password?** on the login page opens `password.html`: type your email and the API emails a one-time link (30 minutes). In development the email lands in Mailpit at http://localhost:8025. The link opens `password.html?token=…`, which asks for the new password twice and then goes to the login page (the token is removed from the address bar straight away).
- **Temporary passwords:** a password typed by an Approver or Decision Maker (adding a user, or a new one for someone who forgot theirs) is temporary. After signing in with it the person goes to `password.html?change` and can't open the dashboard until they choose their own (or sign out). The users list tags them "Temporary password".
- **Change password:** the **Password** button in the top bar opens `password.html?change` (current password + new one twice). Your own password isn't editable in Users & business.

## Structure

| Path | Purpose |
|---|---|
| `index.html` | Login page (goes to the dashboard after signing in, or to `password.html?change` on a temporary password) |
| `register.html` | Register a new business and its first Decision Maker |
| `password.html` | Forgot password (email a link), set a new password from the link, change your own password |
| `dashboard.html` | The app itself: one page whose sidebar switches sections (`#upload`, `#forecast`, `#approvals`, `#dashboard`, `#reports`, `#targets`, `#settings`, `#activity`, `#support`) without reloading. Section styles and behaviour live in their own files (`css/upload.css` + `js/upload.js`, `css/forecast-section.css` + `js/forecast-section.js`, `css/approvals.css` + `js/approvals.js`, `css/targets.css` + `js/targets.js`, `css/settings.css` + `js/settings.js`, `css/activity.css` + `js/activity.js`, `css/reports.css` + `js/reports.js`) |
| `assets/images/favicon.svg` | Browser tab icon: "R" lettermark in brand blue and cream (the RandAhead™ wordmark is the only logo) |
| `css/base.css` | Brand palette (`#3368A0`, `#66A3BF`, `#C8DFDB`, `#F2EFE7`) as CSS variables, reset, toasts |
| `css/login.css` | Login page styles and animations (also the card used by the register and password pages) |
| `css/register.css` | Register page extras |
| `css/password.css` | Password page extras (intro text, "check your email" panel) |
| `css/dashboard.css` | Dashboard layout: sidebar, top bar, metric cards, statement tables, support form |
| `css/upload.css` | Upload statement section: steps, drop zone, mapping table, checks, upload history |
| `css/forecast-section.css` | Forecast & budget section: settings, method cards, warnings, chart, comparison table, budget choice |
| `css/approvals.css` | Approvals section: waiting counts, review cards, comment + approve / reject, sidebar count |
| `css/targets.css` | Targets & rules section: target form and table, rule cards with on/off switch, threshold box and preview (its form, field and icon-button styles are reused by Users & business) |
| `css/settings.css` | Users & business section: business details form, users table with avatars and status |
| `css/activity.css` | Activity log section: filters and the entries table with area icons and before → after details |
| `css/reports.css` | Reports section: report actions, targets and warnings panels, and the print layout (only the report, no sidebar or buttons) |
| `js/config.js` | API address and mock switch |
| `js/toast.js` | `showToast(message, type)` notifications |
| `js/mock-data.js` | Fictional demo business, its users (incl. the Admin), its uploaded income statement, upload history (incl. one upload waiting for approval, built from a sample file), demo values for the 4 external indicators, the 4 starting warning rules and demo KPI targets (used while `USE_MOCK_TARGETS` is on or with the mock login) |
| `js/api.js` | API calls (`Api.login`, `register`, `forgotPassword`, `resetPassword`, `changePassword`, `getLatestSubmission`, `getLatestBudget`, `getDashboard`, `previewUpload`, `submitUpload`, `getSubmissions`, `getPendingSubmissions`, `decideSubmission`, `getLatestForecast`, `runForecasts`, `getIndicatorWarnings`, `chooseBudget`, `decideBudget`, `getRules`, `createRule`, `updateRule`, `deleteRule`, `getKpiTargets`, `createKpiTarget`, `updateKpiTarget`, `deleteKpiTarget`, `getBusiness`, `updateBusiness`, `getUsers`, `createUser`, `updateUser`, `deleteUser`, `getActivity`), the logged-in session (`Session`, incl. `update` after editing your own name or email) and mock-mode storage (`MockStore`) |
| `js/statement.js` | Maps a submitted income statement to the 13 categories (rands, recalculated subtotals) |
| `js/summary.js` | The dashboard summary and the combined report in the API's shape (`buildDashboardSummary`, `buildReport`), worked out in the browser in mock mode |
| `js/forecast.js` | Moving Average + Exponential Smoothing (same maths as the Python service), forecast runs in the Python service's shape, the 4 indicator types (`INDICATOR_TYPES`) and the warning rule check, budget figures (no DOM; the maths only runs in the browser in mock mode) |
| `js/importer.js` | Reads Excel/CSV, finds the year columns, matches labels to categories, detects the scale, checks the upload; turns a saved upload into the dashboard's statement (`submissionFromUpload`) and the approver's checks (`reviewUpload`) (no DOM; with the real API the file is read server-side with the same rules) |
| `js/login.js` | Login form behaviour |
| `js/register.js` | Register form behaviour |
| `js/password.js` | Password page: picks the form from the address (`?token=`, `?change`, or forgot) and sends it |
| `js/upload.js` | Upload statement section behaviour |
| `js/forecast-section.js` | Forecast & budget section behaviour (chart via [Chart.js](https://www.chartjs.org/), loaded from a CDN when needed) |
| `js/approvals.js` | Approvals section behaviour |
| `js/targets.js` | Targets & rules section: the KPI list and target checks (pure helpers at the top: `KPI_METRICS`, `evaluateTarget`, `parseKpiValue`), then the target and rule screens |
| `js/settings.js` | Users & business section: business details and users (add, edit, switch off / on, delete) |
| `js/activity.js` | Activity log section: loads `GET /api/audit` each time the section opens or a filter changes |
| `js/reports.js` | Reports section: loads `GET /api/reports` and the economic warnings each time the section opens; CSV download and print / save as PDF |
| `js/dashboard.js` | Dashboard behaviour: sections (each role lands on its own), metric cards, tables, search, support form; `loadData()` reloads everything after an upload is approved |

Each page keeps its HTML, CSS and JS in separate files; no inline styles or scripts.

## Who can do what

Every role can open every section except **Reports**, which only the Decision Maker (and the admin) sees: its sidebar link is hidden for other roles and `#reports` opens their home section instead (`data-permission` on the section). Where a role can't act, the section is read-only with a note at the top (in mock mode the note names the demo account to sign in with). The rules live in one place, `ROLE_PERMISSIONS` in `js/dashboard.js` (`can(action)`, `showRoleNote()`); the C# API must enforce the same rules. An **Admin** account (`isAdmin` on the signed-in user, e.g. `admin@gmail.com` / `admin`) passes every `can()` check and shows "Admin" in the top bar, so all sections can be used without switching users. The API still treats it as its database role (Decision Maker) except for the admin-only user actions.

| Action | Role | Others see |
|---|---|---|
| Upload a statement, Fix & re-upload | Submitter | Upload statement shows only the upload history, without the steps or Fix buttons |
| Choose the budget | Decision Maker | Forecast & budget works fully (run and compare forecasts), but **Use as budget** is locked |
| Approve or reject uploads and budgets | Approver | Approvals shows the queue without the comment box and buttons |
| Add, edit, delete KPI targets | Decision Maker | Targets & rules shows the targets and their status, without the form or the edit / delete buttons |
| Change warning rules (threshold, on/off, add, delete) | Approver, Decision Maker | Targets & rules shows the rules with the inputs locked and no add form |
| Add, edit, switch off / on, delete users | Approver, Decision Maker | Users & business shows the user list without the form or buttons |
| Change the business details | Decision Maker | Users & business shows the details with the fields locked |

## Dashboard

| Part | What it does |
|---|---|
| Sidebar | Upload statement, Targets & rules, Forecast & budget, Approvals (with a count of items waiting), Dashboard, Reports (Decision Maker only), Users & business, Activity log, Contact support (workflow order; Targets & rules sits before Forecast & budget because its rules set the forecast warnings). Back/forward buttons work (URL hash). After signing in each role opens on its own section: Submitter → Upload statement, Approver → Approvals, Decision Maker → Dashboard (a `#section` in the URL wins) |
| Top bar | Section title, financial year picker, signed-in user, sign out |
| Search | Filters the lines of both income statements |
| 4 metric cards | Each has a dropdown of the uploaded income statement lines (Sales, Rent, Water, Electricity…) plus totals (Gross profit… Net profit). Shows the amount for the chosen year and the change from the previous year (green = good, red = bad; a rise in an expense is bad). Choices are remembered in the browser |
| KPI targets | One card per target set in Targets & rules (newest year first): the KPI, its year, the figure it's checked against (**actual** once the year is in the approved history, else the **budget**), a progress bar, the target and difference, and the same status (Met / Missed / On track / Off track / No figures yet). The badge counts targets on target; the note links to Targets & rules. Checked by the API (`GET /api/dashboard`); reloads when a target or the budget changes |
| Budget vs actual | For the year in the picker, per category and subtotal: the previous year's actual, this year's actual and the % change, and, when an approved budget forecast this year (a budget year later uploaded and approved), the budget figure and the difference in rands and %. Green = better for the business, red = worse. The badge shows net profit against the budget. Titled "Year on year" when no approved budget covers the year. From `GET /api/dashboard?year=`; in mock mode from `js/summary.js` (no budget comparison there) |
| Submitted income statement | The approved history exactly as in the files (R'000, expenses in brackets, the sheets' own subtotals). When several uploads are combined, the heading lists each file and its years, and hovering a year shows the file it comes from |
| Processed income statement | Per category in rands: last year's **actual**, the **MA** and **ES** forecasts (muted, for reference) and the one **Budget** figure chosen in Forecast & budget (MA, ES or the average of both). The budget number is **green** (better for the business: more income/profit or lower costs) or **red** (worse), with the % change from the actual in brackets (same font size as the number). Forecasts are flat, so each column covers every budget year. Shows which file lines feed each category. Badge: Approved budget, Waiting for approval or Budget rejected (hover for the comment); "No budget" with a pointer to Forecast & budget until one is chosen |

**Data flow:** Upload statement → Approvals → submitted statement (dashboard left) and the history used by Forecast & budget → chosen budget → Approvals → processed statement (dashboard right). The dashboard only displays; the year picker and metric dropdowns are its only controls.

## Reports

For the Decision Maker: one combined report of every approved year (up to 5) followed by the approved budget's later years (shaded), from `GET /api/reports` (mock mode: `buildReport` in `js/summary.js`).

| Part | What it does |
|---|---|
| Combined report | The 13 categories and 4 subtotals in rands per year, the % change from the first to the last actual year (green = better for the business, red = worse), then **Key ratios**: revenue growth and gross, operating and net margin per year. The note names the approved budget's method and approver, or says there is none |
| KPI targets | Every target with its year, the target, the result (actual or budget), the gap and the status, as on the dashboard |
| Economic warnings | The active warning rules that are above their threshold in the approved years, worded as in Approvals; warnings never change the figures |
| Download CSV | The same report as a UTF-8 CSV (`randahead-report-<date>.csv`) that opens in Excel: statement, ratios, targets and warnings |
| Print or save as PDF | The browser's print dialog with a print layout that shows only the report |

## Other sections

| Section | What it does |
|---|---|
| Upload statement | See below |
| Forecast & budget | See below |
| Approvals | See below |
| Targets & rules | See below |
| Users & business | See below |
| Activity log | Who did what and when: sign-ins (and failed ones), uploads, decisions, forecast runs, budget choices and every change to targets, rules, business details and users, newest first, with before → after values. The **admin** sees everyone in the business and can filter by person; everyone else sees only their own actions. Filters: area, person (admin), from and to dates. Shows the newest 200 matching entries; entries can't be edited or deleted. Mock mode shows an empty log |
| Contact support | "Get in touch" form (name and email filled in from the signed-in user, topic, message) sent through `POST /api/support`, which emails the support inbox (Mailpit at http://localhost:8025 in development). Mock login only pretends to send |

## Upload statement

Three steps: **choose file → check & map lines → send for approval**, with the upload history underneath.

| Part | What it does |
|---|---|
| Choose file | Drag and drop or browse. Excel (.xlsx) or CSV up to 5 MB; old `.xls`, PDFs and images are refused with a message. **Download the template** (real API only) gives the standard Excel layout. To test, use the sample files below |
| Reading the file | Finds the first row with at least two year columns (`FY2025`, `2025`, `2024/25`, `Feb 2025`, `Year ended 28 Feb 2025`), sorts years oldest first and keeps the latest 5. Failing that, a **single-year file**: one year heading to the right of the labels with amounts under it. Monthly files are refused for now. Amounts accept brackets, spaces, `R`, commas and dashes (= 0). Scale is detected from `R'000` / `R million` text and confirmed by the user |
| Check & map lines | Every line is matched **by label**: known labels map automatically, choices made before are **remembered**, unknown labels must be given a category, and cross-category lines (e.g. "Water & electricity") must be assigned to one category or **split by %** (the user types the %, never guessed). Lines can also be marked "not part of the income statement". Total lines ("Total operating expenses") are skipped |
| Checks | Years in the file, then the **history once approved** (warning under 3 years, gaps, what the file adds / replaces / pushes out, years a newer upload already covers), blank amounts (treated as 0), unmapped lines and missing revenue (both block sending), and the sheet's subtotals vs our recalculation (per subtotal row, with the difference) |
| History once approved | One column per year of the combined history: "This file" (new year, or which upload it replaces) or the approved upload the year keeps coming from |
| Send for approval | Needs every line mapped and the confirm tick. Once approved, the file's years join the history. A file sent with **Fix & re-upload** is the next version of the rejected upload. User choices are remembered for the next upload |
| Your uploads | Every version with its years, status (Waiting for approval, Approved, Rejected, Replaced), who decided and when, which years of the history an approved upload provides ("Used for FY2022–FY2025"), what replaced a replaced upload, the approver's comment (red for a rejection, green for an approval), and **Fix & re-upload** on rejected uploads |

**Combining uploads.** Approved uploads add up to one history, the same rule as the Python forecasting service (`forecasting/app/forecasting.py`): a newer upload adds its years, and where two uploads cover the same year the **more recently submitted upload wins**. Only the latest 5 years are used. An approved upload becomes **Replaced** (`superseded`) when newer approved uploads cover every one of its years, or when it was a rejected upload that a corrected version has replaced; a partly covered upload stays approved. The dashboard's submitted statement follows the newest upload's layout; lines only older uploads have are slotted in, and a line missing from a year's upload shows a dash. The helpers are pure functions in `js/importer.js` (`planHistory`, `historyChange`, `combineUploads`, `fullyCoveredUploads`); the C# API must do the same. Python merges per category and year, so the C# importer should save all 13 categories for every year of an upload (0 where there are no lines), so that a newer upload's year fully replaces an older one there too.

With the real API the file is read on the server (`POST /api/submissions/preview`, C# + ClosedXML, same rules as `js/importer.js`, remembered mappings applied there); the checks and mapping screen stay in the browser and only the mapped rows are sent. With `USE_MOCK_SUBMISSIONS` on (or the demo admin), the file is read in the browser instead (Excel through [SheetJS](https://sheetjs.com/), loaded only when an Excel file is chosen), and uploads (with their rows, so an approver can review them) and remembered mappings are kept in `localStorage` (`mockUploads`, `mockMappings`; delete them to reset). Real endpoints: `POST /api/submissions/preview`, `GET /api/submissions/template`, `POST /api/submissions`, `GET /api/submissions`.

**Sample files** (`../samples/`): fictional Excel statements for Karoo Outdoor Supplies (Pty) Ltd (February year-end, R'000, expenses in brackets). Never put real business data there.

| File | Years | What it tests |
|---|---|---|
| `Karoo_Outdoor_IS_FY2021-FY2025.xlsx` | FY2021–FY2025 | Clean upload: every line maps automatically, all 4 subtotals match (same figures as the demo statement in `js/mock-data.js`) |
| `Karoo_Outdoor_IS_FY2022-FY2026.xlsx` | FY2022–FY2026 | The business's own layout: "Water & electricity" spans two categories (assign or split by %, e.g. 70 / 30), "Franchise levy" is unknown (choose a category), plus a "Total operating expenses" line to skip |
| `Karoo_Outdoor_IS_FY2021-FY2025_draft.xlsx` | FY2021–FY2025 | Error case: interest paid is blank for FY2023, so profit before tax and net profit don't match the recalculation (a reason to reject) |

## Forecast & budget

Runs **Moving Average** and **Exponential Smoothing** side by side for each of the 13 categories, from the approved income statement history, and lets the user choose which one becomes the budget. No ARIMA, no machine learning.

| Part | What it does |
|---|---|
| Forecast settings | Moving Average window (2 or 3 years, default 3), smoothing weight α (0.1–0.9, default 0.5), horizon (1–3 years). **Run forecasts** runs both methods once (one saved run each). Changing a setting shows "Settings changed" until you run again |
| How the two methods work | Plain-language explanation of each method, updated live with the chosen window and α (e.g. "the latest year counts about 50%, the year before 25%…"). An honest note says forecasts are **flat** (years 2–3 repeat year 1, no growth or inflation added) and how many categories come out below last year |
| Economic warnings | Each active warning rule (starting with one per external indicator: headline CPI, core CPI, electricity price, fuel price at 6 / 6 / 10 / 10%; set in Targets & rules) checked against the latest indicator year inside the history. A warning lists the categories to review and is also shown on those rows of the table. **Warnings never change the figures** |
| History vs forecasts | Line chart for one category or total: actual figures (navy, solid), Moving Average (blue, dashed), Exponential Smoothing (sky, dashed) and their average (muted, dotted) starting from the last actual year. Pick it from the dropdown or click a table row |
| Side-by-side comparison | Every category and subtotal: last actual year, both forecasts and their average, each with the % change (green = better for the business, red = worse). Team-managed categories (revenue, employee costs) are tagged |
| Choose your budget | Three cards: **Moving Average**, **Exponential Smoothing** or **Both (average)** = (MA + ES) ÷ 2 per category, one number from both (revenue, total expenses, net profit on each). **Use as budget** saves the choice; it goes to an approver and sets the Budget column of the dashboard's processed statement straight away ("Waiting for approval"). Both forecasts are always kept, so there is no need to re-run to compare |

On first open it shows the latest saved forecast if it was run on the current approved history, or runs one with the default settings. With `USE_MOCK_FORECASTS` off it uses `GET /api/forecasts/latest`, `POST /api/forecasts` (the C# API asks the Python service and returns its two runs), `POST /api/budgets` (`{ method, run_ids: [MA run, ES run] }`, method `moving_average` / `exponential_smoothing` / `combined`) and `GET /api/budgets/latest` (the budget plus both runs; `budgetFromRuns` in `js/api.js` turns them into the budget figures with `compareRuns`, so the average is worked out the same way everywhere). When the API marks the budget `outOfDate`, the dashboard badge, Forecast & budget, Targets and the budget's Approvals checks say so and ask for a new choice; the figures are never changed. In mock mode the maths runs in the browser (`js/forecast.js`) and the latest runs and chosen budget are kept in `localStorage` (`mockForecastRuns`, `mockBudget`; delete them to reset to the demo approved budget). Economic warnings come from `GET /api/rules` and `GET /api/indicators/series` (demo values and rules in mock mode).

## Approvals

Where an **Approver** approves or rejects what others send: uploads (the income statement history) and the chosen budget. Other roles see the queue read-only, with a note (in mock mode it names the demo approver account).

| Part | What it does |
|---|---|
| Waiting counts | Uploads and budget waiting; the same total shows as a count on the sidebar item |
| Uploads waiting | One card per upload: file, years, lines, scale, who uploaded it and when, and what approving does (its years join the history; "corrected version of … which was rejected"). Checks for the approver: the history once approved (length, gaps, what it adds, replaces or pushes out, years a newer upload already covers), lines the submitter **mapped by hand**, lines they **split** (with the %), lines **left out**, blanks, and each subtotal that **doesn't match** our recalculation (with the difference). **Show the statement** opens the file's lines with the category each one feeds and a match chip on each subtotal |
| Budget waiting | The method and settings, budget years, who chose it, revenue / total expenses / net profit with the % vs last actual, the **economic warnings** that apply (they never change the figures), a team-managed reminder, and **Show every category** (the same actual \| MA \| ES \| Budget table as the dashboard) |
| Decide | Comment box (**needed to reject**, optional to approve) + **Reject** / **Approve**. Approving an upload adds its years to the dashboard's submitted statement and the forecast history straight away, and marks **Replaced** the rejected upload it corrects and any approved upload whose years are now all covered by newer ones. Rejecting sends the comment back to the submitter (Upload statement → Your uploads → Fix & re-upload). Approving a budget makes it the approved budget; rejecting it shows "Budget rejected" on the dashboard and asks for a new choice in Forecast & budget |
| Recent decisions | The latest 6 decisions on uploads and budgets: decision, who, when, the comment, and "Replaced since" for older uploads |

Several uploads can be approved at once; they combine into one history (see Upload statement). The demo data (in the browser and in `03_seed.sql`) has one upload waiting (FY2022–FY2026, with a 70/30 split and one hand-mapped line; approving it replaces FY2022–FY2025 of the approved demo upload, adds FY2026 and pushes FY2021 out). Upload decisions use `GET /api/submissions?status=submitted` and `POST /api/submissions/{id}/decision` (`{ decision: "approved" | "rejected", comments }`, saved as an `approval_action`; a rejection needs comments), or `mockUploads` in demo mode. Budget decisions use `POST /api/budgets/{id}/decision` (same body, also saved as an `approval_action`), or `mockBudget` in demo mode.

## Targets & rules

Two panels: the **KPI targets** the Decision Maker sets, and the **economic warning rules** an Approver or Decision Maker tunes.

| Part | What it does |
|---|---|
| Add / edit a target | KPI (net, operating, gross profit and profit before tax in rands; net, operating and gross **margin** as a % of revenue; or any of the 13 categories), financial year (the last actual year and the 3 after it) and the target (rands, or a % with one decimal). The hint says which way is good (profit, margins and income: **at least**; expenses: **at most**) and shows the current actual or budget figure for that year. One target per KPI per year |
| Target table | Grouped by year, newest first. Each target shows the figure it's checked against (the year's **actual** once it's in the approved history, else the **budget**), the difference (green when reached) and a status: **Met / Missed** (actual), **On track / Off track** (budget) or **No figures yet**. Edit fills the form; delete asks "Delete this target?" in the row. The badge counts targets on target |
| Warning rules | One card per rule: on/off switch, name, indicator, linked categories, the **Warn above** threshold and a live preview of what it means for the latest indicator value in the history ("6.3% in 2024, above 6%, so it warns"). Typing a new threshold shows **Save** / **Undo** (Enter saves). Rules that have raised alerts can only be switched off (lock icon); added rules can be deleted |
| Add a rule | Indicator, threshold and an optional name (suggested from the indicator and threshold); a rule name can only be used once. Several rules per indicator are allowed (e.g. a second fuel rule at 5%) |

Rule changes update the economic warnings in Forecast & budget and the budget checks in Approvals straight away; they never change the figures. In mock mode rules and targets are kept in `localStorage` (`mockRules`, `mockKpiTargets`; delete them to reset). With the API: `GET/POST /api/kpi-targets`, `PUT/DELETE /api/kpi-targets/{id}` (`{ metric_name, target_value, measurement_unit: "rand" | "percent", target_period }`, period = first day of the financial year; unique per unit, metric and period) and `GET/POST /api/rules`, `PUT/DELETE /api/rules/{id}` (`{ rule_name, indicator_code, threshold_value, active_status }`; rules belong to the business; `deletable` is false once a rule has raised an alert, and `DELETE` then answers 409). The rules' latest values come from `GET /api/indicators/series`; `buildIndicatorWarnings` in `js/forecast.js` checks them against the history years the same way in both modes (see `../backend/README.md`).

## Users & business

Two panels: the **business details** (Decision Maker) and the **users** who can sign in (Approver or Decision Maker).

| Part | What it does |
|---|---|
| Business details | Registered name (required, shown in the sidebar straight away), industry (optional, picked from a list of SA SME sectors or "Other"), financial year-end month, plus country (South Africa) and how it's budgeted (Whole company, Rand) shown read-only. **Save** / **Undo** appear once something changes. The year-end is **locked once income statement history is approved**, because every financial year label and target date depends on it |
| Add / edit a user | Full name, email (unique, case-insensitive), role and password; the hint explains what the chosen role can do. Accounts are made for people (e.g. by the owner or IT), so a new user needs a password of at least 8 characters, handed over with their email. When editing, the password is optional: leave it empty to keep it, or type a new one to reset it. You can edit your own name and email (the top bar updates) but not your own role |
| Users table | Active users first, then by role and name, with a "You" tag on your own row and a badge counting active and switched-off users. **Switch off** stops a user signing in but keeps everything they uploaded or decided; **switch back on** undoes it. Only users with nothing on record can be **deleted** (asks "Delete …?" in the row); the others show a lock. You can't switch off or delete yourself |
| Admin delete | An **Admin** (`deleteAnyUser` in `ROLE_PERMISSIONS`, which no role has, so only admins pass) can delete **any** user, including ones with history, after typing their own password in a confirm row under the user. A wrong password deletes nothing and keeps the row open. The user leaves the list and can't sign in; their past uploads and decisions stay, with their name shown as "Name (former user)". Admin accounts show an "Admin" tag and a lock for everyone who isn't an admin. The safety rule below still applies |
| Safety rule | A role change, switch-off or delete that would remove the **last active Approver or last active Decision Maker** is refused with a message (a newly registered business starts with only its Decision Maker) |

This section uses the C# API (endpoints in `../backend/README.md`) unless `USE_MOCK_USERS` is on or you signed in with the mock login; in that demo mode the business and users are kept in `localStorage` (`mockBusiness`, `mockUsers`; delete them to reset) and the mock login reads the same users.

## Brand palette

Used across the whole app (and charts, logo, screenshots). Defined once as CSS variables in `css/base.css`; page CSS uses the variables, not hex values.

| Variable | Hex | Use |
|---|---|---|
| `--blue` / `--accent` | `#3368A0` | Buttons, links, brand name, main chart series |
| `--sky` / `--accent-light` | `#66A3BF` | Highlights, hover glow, second chart series |
| `--mint` | `#C8DFDB` | Avatars, tags, subtle highlights |
| `--cream` / `--bg` / `--surface` | `#F2EFE7` | Page background and cards (same colour) |

Supporting: text `#1F3550`, muted text `#5F7F96`, sunken cream `--surface-hover` `#EBE7DD` (subtotal rows, disabled inputs), danger `#C94A54`, warning `#D99A2B`, success `#2F8F6F`, plus see-through tints (`--blue-tint`, `--sky-tint`, `--warning-tint`, …) for hovers and highlights. `--border` is a faint see-through line used only for dividers inside panels.

## Look: neumorphism (soft UI)

Cards, inputs and buttons are the same cream as the page and are shaped only by two soft shadows (light top-left, dark bottom-right), defined once in `base.css`:

| Variable | Use |
|---|---|
| `--neu-raised`, `--neu-raised-sm`, `--neu-raised-lg` | Raised: panels, cards, buttons, toasts |
| `--neu-inset`, `--neu-inset-sm` | Pressed in: inputs, selects, search, drop zone, progress bars, switched-off rules, selected choice cards, pressed buttons |
| `--focus-ring` | Sky ring added to the inset shadow on focus |

No outlines around cards and no coloured accent strips on card edges (removed on purpose); status is shown by badges, icons, bar colours and tints instead.

The login card design started from an open-source login page and was reworked for this app.
