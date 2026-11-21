# Frontend in HTML, CSS and JavaScript

Responsive web app for **RandAhead™** (*Clear forecasts. Confident decisions.*), the SME budget forecasting system. Plain HTML, CSS and JavaScript (no build step). It talks **only** to the C# API (`http://localhost:5080/api`), never to the Python service.

## Run it

The frontend is static files served outside Docker Compose, on port 5500. From the repo root:

```bash
python3 -m http.server 5500 --bind 127.0.0.1 --directory frontend
```

Then open http://127.0.0.1:5500. Any other static file server on the same port and address works.

Settings in `js/config.js`:

| Setting | Default | Meaning |
|---|---|---|
| `API_BASE` | `http://localhost:5080/api` | The C# API (needs `docker compose up`) |

The token is kept with the session and sent as `Authorization: Bearer …` on every API call; a `401` signs the user out. The session ends when the token expires (8 hours). Opening `dashboard.html` without a session goes to the login page, and the dashboard re-checks the user with `GET /api/auth/me` when it loads. Until the first statement is saved, the dashboard shows one "No income statement yet" message, Forecast & budget and KPI targets wait, and warning rules and Business work as usual. The industry list lives in `js/config.js`. The API allows the web app only from `http://127.0.0.1:5500` and `http://localhost:5500` (`Cors:AllowedOrigins` in the API's `appsettings.json`).

**Sign in** with the one account seeded by `database/init/03_seed.sql`; the credentials are in the [root README](../README.md#sign-in).

**This is a temporary development login.** There is exactly one account and it can do everything; there are no roles, no registration, no user management and no password reset or change. One shared account with a known password is not secure: revisit sign-in and permissions before any real use.

## Structure

| Path | Purpose |
|---|---|
| `index.html` | Login page (username and password; goes to the dashboard after signing in) |
| `dashboard.html` | The app itself: one page whose sidebar switches sections (`#upload`, `#forecast`, `#dashboard`, `#reports`, `#targets`, `#settings`, `#activity`, `#support`) without reloading. Section styles and behaviour live in their own files (`css/upload.css` + `js/upload.js`, `css/forecast-section.css` + `js/forecast-section.js`, `css/targets.css` + `js/targets.js`, `css/settings.css` + `js/settings.js`, `css/activity.css` + `js/activity.js`, `css/reports.css` + `js/reports.js`, `js/dashboard-section.js`, `js/support.js`) |
| `assets/images/favicon.svg` | Browser tab icon: "R" lettermark in brand blue and cream (the RandAhead™ wordmark is the only logo) |
| `css/base.css` | The only place colours are defined: the dark palette and the light print palette as CSS variables, `color-scheme`, reset, toasts |
| `css/login.css` | Login page styles and animations |
| `css/dashboard.css` | Dashboard layout: sidebar, top bar, metric cards, KPI cards, support form |
| `css/components.css` | Shared pieces used by several sections: buttons, panels, badges and tags, statement tables, chips, checks, upload-history tables and comments, target forms, icon buttons and inline delete confirms. Loaded straight after `css/dashboard.css` |
| `css/upload.css` | Upload statement section: steps, drop zone, mapping table, history plan |
| `css/forecast-section.css` | Forecast & budget section: settings, method cards, warnings, chart, comparison table, budget choice |
| `css/targets.css` | Targets & rules section: target table, rule cards with on/off switch, threshold box and preview |
| `css/settings.css` | Business section: business details form |
| `css/activity.css` | Activity log section: filters and the entries table with area icons and before → after details |
| `css/reports.css` | Reports section: report actions, targets and warnings panels, and the print layout (only the report, no sidebar or buttons) |
| `js/config.js` | API address and the industry list |
| `js/toast.js` | `showToast(message, type)` notifications |
| `js/session.js` | The logged-in session (`Session.save`, `get`, `update` after `GET /api/auth/me`, `clear`) |
| `js/api.js` | One `request()` helper (bearer token, one network-failure message, the server's `message` on errors, `401` signs out) and one function per endpoint (`login`, `getCurrentUser`, `getBusiness`, `updateBusiness`, `getStatementDefinitions`, `getActiveSubmissions`, `getSubmissions`, `previewUpload`, `checkUpload`, `downloadTemplate`, `submitUpload`, `getLatestBudget`, `chooseBudget`, `getLatestForecast`, `runForecasts`, `getDashboard`, `getReport`, `getRules`, `getIndicatorSeries`, `createRule`, `updateRule`, `deleteRule`, `getKpiTargets`, `createKpiTarget`, `updateKpiTarget`, `deleteKpiTarget`, `getActivity`, `sendSupportMessage`) |
| `js/format.js` | Formatters: rands, numbers, sheet amounts, dates, month names, initials, % change and its colour class, KPI values and gaps, `escapeHtml` |
| `js/ui.js` | The `html` tagged template (escapes every value unless wrapped in `raw()`; arrays are joined), `withBusy(button, fn)`, and the render pieces sections share (empty rows and notes, team-managed tag, lock icon, inline delete / keep confirm, change cells, column heads, "N of M on target" badge, KPI figure and gap, chips, checks, comment rows) |
| `js/store.js` | The dashboard's shared data (user, business, statement definitions, uploads, rules, statement, budget) with `on(topic, fn)` / `emit(topic)` change events, lookups into the definitions (`getCategory`, `subtotalName`, `getKpi`), and the loaders other sections reuse (rules and indicator series are fetched once per load) |
| `js/forecast.js` | Joins the two forecast runs with the API's comparison of them (`compareRuns`, also used for the budget figures via `budgetFromRuns`), financial-year labels, the indicators' short names (`INDICATOR_NAMES`) and the warning rule check (no DOM) |
| `js/importer.js` | Checks the server's preview of an upload and builds the upload checks; combines active uploads into one history (no DOM; the file itself is read and its statement worked out by the API) |
| `js/auth-form.js` | Login page helpers: floating labels, show-password toggle, card shake, loading buttons, field errors |
| `js/login.js` | Login page entry: form behaviour |
| `js/dashboard.js` | Dashboard page entry: signed-in user, sidebar navigation (opens on Dashboard), `loadData()`, then each section's `setup()` once and `refresh()` after every load |
| `js/dashboard-section.js` | Dashboard section: metric cards, KPI cards, budget vs actual, both statements, search, year picker |
| `js/upload.js` | Upload statement section behaviour |
| `js/forecast-section.js` | Forecast & budget section behaviour (chart via [Chart.js](https://www.chartjs.org/), loaded from a CDN with an integrity hash when needed) |
| `js/targets.js` | Targets & rules section: the target and rule screens |
| `js/settings.js` | Business section: business details |
| `js/activity.js` | Activity log section: loads `GET /api/audit` each time the section opens or a filter changes |
| `js/reports.js` | Reports section: loads `GET /api/reports` each time the section opens; CSV download and print / save as PDF |
| `js/support.js` | Contact support section: the support form |

Each page keeps its HTML, CSS and JS in separate files; no inline styles or scripts. The JS files are native ES modules: each page loads one entry module (`<script type="module">`), which imports the rest. Sections export `setup()` and, where needed, `refresh()` and `onShow()`, and talk to each other only through `js/store.js` events (`user`, `business`, `reload`, `uploads`, `rules`, `budget`, `targets`).

## Who can do what

The one signed-in user can open every section and do every action: upload statements, choose the budget, set KPI targets and warning rules, edit the business details and read the reports and the whole activity log. Nothing is read-only and nothing is checked by role in the web app or the API.

## Dashboard

| Part | What it does |
|---|---|
| Sidebar | In the order the system is used, in three labelled groups. **Set up and plan:** Business, Upload statement, Targets & rules, Forecast & budget (Targets & rules comes before the forecast because its rules set the forecast warnings). **Monitor and decide:** Dashboard, Reports, Activity log. **Help:** Contact support. The menu scrolls on short screens, and on phones it opens from the ☰ button over a dimmed page. Back/forward buttons work (URL hash). After signing in the app opens on Dashboard (a `#section` in the URL wins) |
| Top bar | Section title, financial year picker, signed-in user, sign out |
| Search | Filters the lines of both income statements |
| 4 metric cards | Each has a dropdown of the uploaded income statement lines (Sales, Rent, Water, Electricity…) plus totals (Gross profit… Net profit). Shows the amount for the chosen year and the change from the previous year (green = good, red = bad; a rise in an expense is bad). Choices are remembered in the browser |
| KPI targets | One card per target set in Targets & rules (newest year first): the KPI, its year, the figure it's checked against (**actual** once the year is in the history, else the **budget**), a progress bar, the target and difference, and the same status (Met / Missed / On track / Off track / No figures yet). The badge counts targets on target; the note links to Targets & rules. Checked by the API (`GET /api/dashboard`); reloads when a target or the budget changes |
| Budget vs actual | For the year in the picker, per category and subtotal: the previous year's actual, this year's actual and the % change, and, when a chosen budget forecast this year (a budget year that now has actual figures), the budget figure and the difference in rands and %. Green = better for the business, red = worse. The badge shows net profit against the budget. Titled "Year on year" when no chosen budget covers the year. From `GET /api/dashboard?year=` |
| Your income statement as uploaded | The history exactly as in the files (R'000, expenses in brackets, the sheets' own subtotals). When several uploads are combined, the heading lists each file and its years, and hovering a year shows the file it comes from |
| Processed income statement | Per category in rands: last year's **actual**, the **MA** and **ES** forecasts (muted, for reference) and the one **Budget** figure chosen in Forecast & budget (MA, ES or the average of both). The budget number is **green** (better for the business: more income/profit or lower costs) or **red** (worse), with the % change from the actual in brackets (same font size as the number). Forecasts are flat, so each column covers every budget year. Shows which file lines feed each category. Badge: Chosen budget (hover for who chose it and when) or Out of date; "No budget" with a pointer to Forecast & budget until one is chosen |

**Data flow.** A saved upload is in use at once: it shows as your income statement (dashboard left) and joins the history that Forecast & budget uses. The budget chosen there with **Use as budget** is the budget at once and fills the Budget column (dashboard right), Budget vs actual and the KPI targets.

## Reports

One combined report of every year in the history (up to 5) followed by the chosen budget's later years (shaded), from `GET /api/reports`.

| Part | What it does |
|---|---|
| Combined report | The 13 categories and 4 subtotals in rands per year, the % change from the first to the last actual year (green = better for the business, red = worse), then **Key ratios**: revenue growth and gross, operating and net margin per year. The note names the chosen budget's method and who chose it, or says there is none |
| KPI targets | Every target with its year, the target, the result (actual or budget), the gap and the status, as on the dashboard |
| Economic warnings | The active warning rules that are above their threshold in the history years; warnings never change the figures |
| Download CSV | The same report as a UTF-8 CSV (`randahead-report-<date>.csv`) that opens in Excel: statement, ratios, targets and warnings |
| Print or save as PDF | The browser's print dialog with a print layout that shows only the report |

## Other sections

| Section | What it does |
|---|---|
| Upload statement | See below |
| Forecast & budget | See below |
| Targets & rules | See below |
| Business | See below |
| Activity log | What was done and when: uploads, decisions, forecast runs, budget choices and every change to targets, rules and business details, newest first, with before → after values. Sign-in and sign-out are not listed. Filters: area, from and to dates. Shows the newest 200 matching entries; entries can't be edited or deleted |
| Contact support | "Get in touch" form (name filled in from the signed-in user, then email, topic, message) sent through `POST /api/support`, which emails the support inbox (Mailpit at http://localhost:8025 in development) |

## Upload statement

The page reads top to bottom in the order of the work, with a stepper showing **1 Choose your file → 2 Check and save**:

1. **Choose your file.** "No file yet? Download the template" comes first, then the drop zone. The file rules sit in a collapsed **What the file needs** list.
2. **Check and save**, in lettered steps: (a) pick the unit the amounts are in, (b) check the category of each line (change it or split it), (c) read our checks; then the history once saved, the confirm tick and **Save statement**. The statement is in use straight away and the dashboard reloads.
After saving, a short confirmation shows what changed in the history, with **Upload another file**.

**Your uploads** stays at the bottom: every version, In use or Replaced.

| Part | What it does |
|---|---|
| 1 Choose your file | Drag and drop or browse. Excel (.xlsx) or CSV up to 5 MB; old `.xls`, PDFs and images are refused with a message. **Download the template** (real API only) gives the standard Excel layout. To test, use the sample files below |
| Reading the file | Finds the first row with at least two year columns (`FY2025`, `2025`, `2024/25`, `Feb 2025`, `Year ended 28 Feb 2025`), sorts years oldest first and keeps the latest 5. Failing that, a **single-year file**: one year heading to the right of the labels with amounts under it. Monthly files are refused for now. Amounts accept brackets, spaces, `R`, commas and dashes (= 0). Scale is detected from `R'000` / `R million` text and confirmed by the user |
| 2 Check the category of each line | Every line is matched **by label**: known labels map automatically, choices made before are **remembered**, unknown labels must be given a category, and cross-category lines (e.g. "Water & electricity") must be assigned to one category or **split by %** (the user types the %, never guessed). Lines can also be marked "not part of the income statement". Total lines ("Total operating expenses") are skipped |
| Checks | Years in the file, then the **history once saved** (warning under 3 years, gaps, what the file adds / replaces / pushes out, years a newer upload already covers), blank amounts (treated as 0), unmapped lines and missing revenue (both block sending), and the sheet's subtotals vs our recalculation (per subtotal row, with the difference) |
| History once saved | One column per year of the combined history: "This file" (new year, or which upload it replaces) or the saved upload the year keeps coming from |
| Save statement | Needs every line mapped and the confirm tick. The file's years join the history at once; older uploads whose years are all covered become Replaced. User choices are remembered for the next upload |
| Your uploads | Every version with its years, who uploaded it and when, and its status: **In use** (with the years of the history it provides, e.g. "Used for FY2022–FY2025", or that it falls outside the latest 5 years) or **Replaced** (with the upload that replaced it) |

**Combining uploads.** Active uploads add up to one history, the same rule as the Python forecasting service (`forecasting/app/forecasting.py`): a newer upload adds its years, and where two uploads cover the same year the **more recently submitted upload wins**. Only the latest 5 years are used. An active upload becomes **Replaced** (`superseded`) when newer active uploads cover every one of its years; a partly covered upload stays in use. The dashboard's income statement as uploaded follows the newest upload's layout; lines only older uploads have are slotted in, and a line missing from a year's upload shows a dash. The helpers are pure functions in `js/importer.js` (`planHistory`, `historyChange`, `combineUploads`); the C# API marks covered uploads as replaced. Python merges per category and year, so the C# importer should save all 13 categories for every year of an upload (0 where there are no lines), so that a newer upload's year fully replaces an older one there too.

The file is read on the server (`POST /api/submissions/preview`, C# + ClosedXML, remembered mappings applied there); the mapping screen stays in the browser and only the mapped rows are sent. Category totals and subtotal checks come from the API: with the preview, then from `POST /api/submissions/check` after each mapping or scale change once every line has a category (an older answer is dropped when a newer change was made). Endpoints: `POST /api/submissions/preview`, `POST /api/submissions/check`, `GET /api/submissions/template`, `POST /api/submissions`, `GET /api/submissions`.

**Sample files** (`../samples/`): fictional Excel statements for Karoo Outdoor Supplies (Pty) Ltd (February year-end, R'000, expenses in brackets). Never put real business data there.

| File | Years | What it tests |
|---|---|---|
| `Karoo_Outdoor_IS_FY2021-FY2025.xlsx` | FY2021–FY2025 | Clean upload: every line maps automatically, all 4 subtotals match |
| `Karoo_Outdoor_IS_FY2022-FY2026.xlsx` | FY2022–FY2026 | The business's own layout: "Water & electricity" spans two categories (assign or split by %, e.g. 70 / 30), "Franchise levy" is unknown (choose a category), plus a "Total operating expenses" line to skip |
| `Karoo_Outdoor_IS_FY2021-FY2025_draft.xlsx` | FY2021–FY2025 | Error case: interest paid is blank for FY2023, so profit before tax and net profit don't match the recalculation (seeded as an earlier, replaced version) |

## Forecast & budget

Runs **Moving Average** and **Exponential Smoothing** side by side for each of the 13 categories, from the income statement history, and lets the user choose which one becomes the budget. No ARIMA, no machine learning.

The page is four labelled steps in order:

1. **Set up and run the forecast:** the history used, the prediction and how far ahead, then **Run forecasts**. **How the two methods work** is collapsed inside this step.
2. **Compare the forecasts:** the chart, then every category side by side.
3. **Check the economic warnings:** lines to look at again before choosing.
4. **Choose your budget:** pick a method and **Use as budget**.

| Part | What it does |
|---|---|
| 1 Set up and run | Three predictions: **Steady** (last 3 years, smoothing weight 0.3), **Balanced** (last 3 years, weight 0.5, the default) and **Recent** (last 2 years, weight 0.7). How far ahead is still 1, 2 or 3 years. **Run forecasts** runs both methods once (one saved run each). Changing a setting shows "Settings changed" until you run again. The exact window and weight are shown under the prediction and stored on the activity log |
| How the two methods work (collapsed in step 1) | Moving Average says it adds the last 2 or 3 years and divides, each year counting the same. Exponential Smoothing follows the prediction: **Steady** keeps older years in the picture, **Balanced** lets recent years count more without following only the latest year, **Recent** follows the latest years. An honest note says forecasts are **flat** (years 2–3 repeat year 1, no growth or inflation added) and how many categories come out below last year |
| 2 Compare: chart | Line chart for one category or total: actual figures (navy, solid), Moving Average (blue, dashed), Exponential Smoothing (sky, dashed) and their average (muted, dotted) starting from the last actual year. Pick it from the dropdown or click a table row |
| 2 Compare: every category side by side | Every category and subtotal: last actual year, both forecasts and their average, each with the % change (green = better for the business, red = worse). Team-managed categories (revenue, employee costs) are tagged |
| 3 Check the economic warnings | Each active warning rule (starting with one per external indicator: headline CPI, core CPI, electricity price, fuel price at 6 / 6 / 10 / 10%; set in Targets & rules) checked against the latest indicator year inside the history. A warning lists the categories to review and is also shown on those rows of the table. **Warnings never change the figures** |
| 4 Choose your budget | Three cards: **Moving Average**, **Exponential Smoothing** or **Both (average)** = (MA + ES) ÷ 2 per category, one number from both (revenue, total expenses, net profit on each). **Use as budget** saves the choice; it is the budget at once and sets the Budget column of the dashboard's processed statement straight away (badge "Chosen budget"). Both forecasts are always kept, so there is no need to re-run to compare |

On first open it shows the latest saved forecast when `matchesHistory` is true, or runs one with the default settings. A correction of an earlier year makes `matchesHistory` false. It uses `GET /api/forecasts/latest`, `POST /api/forecasts` (the C# API asks the Python service and returns its two runs), `POST /api/budgets` (`{ method, run_ids: [MA run, ES run] }`, method `moving_average` / `exponential_smoothing` / `combined`) and `GET /api/budgets/latest` (the budget plus both runs; the API sends each method's figures as `comparison`, and `budgetFromRuns` in `js/forecast.js` picks the chosen method's, so the average is worked out once, on the server). When the API marks the budget `outOfDate`, the dashboard badge, Forecast & budget and Targets say so and ask for a new choice; the figures are never changed. Economic warnings come from `GET /api/rules` and `GET /api/indicators/series`.

## Targets & rules

The page is two labelled steps: **1 Set KPI targets** (pick what to track and the year, set the goal, then see every target and its status) and **2 Set warning levels** (adjust the existing rules first; adding a new rule is in the collapsed **Add another warning rule** section).

| Part | What it does |
|---|---|
| Add / edit a target | KPI (net, operating, gross profit and profit before tax in rands; net, operating and gross **margin** as a % of revenue; or any of the 13 categories), financial year (the last actual year and the 3 after it) and the target (rands, or a % with one decimal). The hint says which way is good (profit, margins and income: **at least**; expenses: **at most**) and shows the current actual or budget figure for that year. One target per KPI per year |
| Target table | Grouped by year, newest first. Each target shows the figure it's checked against (the year's **actual** once it's in the history, else the **budget**), the difference (green when reached) and a status: **Met / Missed** (actual), **On track / Off track** (budget) or **No figures yet**. Edit fills the form; delete asks "Delete this target?" in the row. The badge counts targets on target |
| Warning rules | One card per rule: on/off switch, name, indicator, linked categories, the **Warn above** threshold and a live preview of what it means for the latest indicator value in the history ("6.3% in 2024, above 6%, so it warns"). Typing a new threshold shows **Save** / **Undo** (Enter saves). Rules that have raised alerts can only be switched off (lock icon); added rules can be deleted |
| Add another warning rule (collapsed) | Indicator, threshold and an optional name (suggested from the indicator and threshold); a rule name can only be used once. Several rules per indicator are allowed (e.g. a second fuel rule at 5%) |

Rule changes update the economic warnings in Forecast & budget straight away; they never change the figures. Endpoints: `GET/POST /api/kpi-targets`, `PUT/DELETE /api/kpi-targets/{id}` (`{ metric_name, target_value, measurement_unit: "rand" | "percent", target_period }`, period = first day of the financial year; unique per unit, metric and period) and `GET/POST /api/rules`, `PUT/DELETE /api/rules/{id}` (`{ rule_name, indicator_code, threshold_value, active_status }`; rules belong to the business; `deletable` is false once a rule has raised an alert, and `DELETE` then answers 409). The rules' latest values come from `GET /api/indicators/series`; `buildIndicatorWarnings` in `js/forecast.js` checks them against the history years (see `../backend/README.md`).

## Business

| Part | What it does |
|---|---|
| Business details | Registered name (required, shown in the sidebar straight away), industry (optional, picked from a list of SA SME sectors or "Other"), financial year-end month, plus country (South Africa) and how it's budgeted (Whole company, Rand) shown read-only. **Save** / **Undo** appear once something changes. The year-end is **locked once an income statement is uploaded or a KPI target is set**, because every financial year label and target date depends on it |

This section uses the C# API (endpoints in `../backend/README.md`).

## Colour palette (dark theme)

The app always uses one dark theme in soft charcoal slate. There is no light mode and no toggle, and it ignores the operating system's setting. `:root` sets `color-scheme: dark`, so native inputs, selects, date pickers and scrollbars are dark too, and `scrollbar-color` tints the scrollbars.

**Rule: every colour lives in `css/base.css`.** Page CSS and JS use only the variables (the forecast chart reads them with `getComputedStyle`). CI rejects a hex colour in any other CSS file.

| Variable | Hex | Use |
|---|---|---|
| `--bg` | `#232833` | Page background |
| `--surface` | `#2C3240` | Cards, panels, tables, the top bar, sidebar, toasts, fields |
| `--surface-hover` | `#363D4D` | Hovers, subtotal rows, raised strips |
| `--border` | `#444C5E` | The 1px edge on panels, fields and buttons; chart grid |
| `--text` | `#E8EBF1` | Body text |
| `--text-muted` | `#B4BCCC` | Secondary text, labels, chart axes |
| `--accent` | `#8DBBEE` | Links, brand name, primary buttons, active nav, main chart series |
| `--accent-dark` | `#A6C8F0` | Button hover and text on tags and avatars (on dark it is the stronger, lighter accent) |
| `--accent-light` | `#86C5DC` | Icons, highlights, second chart series |
| `--on-accent` | `#14202E` | Text on accent, success, warning and danger fills (buttons, nav count) |
| `--mint` | `#2F4B53` | Avatar and tag background |
| `--success` | `#62D0A6` | Better for the business, in use, met / on track |
| `--warning` | `#E9BD62` | Off track, out of date |
| `--danger` | `#F59EA5` | Worse for the business, missed |

Tints for badges and highlights are see-through versions of those colours (`--blue-tint`, `--sky-tint`, `--success-tint`, `--warning-tint`, `--danger-tint`). `--overlay` dims the page behind the mobile sidebar, and `--spinner-track` is the faint ring of the sign-in spinner.

**Contrast.** Every text colour above meets WCAG AA (at least 4.5:1) on `--bg`, `--surface` and `--surface-hover`, and on each badge tint over those surfaces. `--on-accent` meets it on every fill it sits on. The lowest pair is `--accent` text on `--sky-tint` over `--surface`, at 4.95:1.

**Print.** `@media print` in `base.css` swaps the variables back to a light palette (white page, dark text, darker status colours, `color-scheme: light`), so Reports → Print or save as PDF stays readable on paper. Every print pair also meets 4.5:1 (lowest 4.75:1).

The favicon keeps the brand blue (`#3368A0`) with a cream "R" on its own rounded square, so it reads on light and dark browser tabs alike. The RandAhead™ wordmark is text in `--accent`.

Text uses the system UI font (`--font`). Amounts, statement figures and metric values use a monospace face (`--font-num`) with `tabular-nums`, so columns of rands line up.

## Look

Panels, fields and buttons sit a step lighter than the charcoal page, with a 1px border (`--line` in `base.css`, and the old `--neu-*` names now point at that same line). Corners are 8px on panels and 6px on buttons and fields. Keyboard focus draws a 2px accent outline. Colour on a figure means something: green is better for the business, red is worse, amber means check it (off track or out of date). The login card uses the same border.
