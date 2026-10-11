# Forecasting Service (Python)

Runs **Moving Average** and **Exponential Smoothing** on a business's approved income statement history and saves the forecasts to PostgreSQL.

Approved history (`financial_line_item`) → forecast per category → `forecast_run` + `forecast_value`.

The web app never calls this service: the C# API calls `POST /api/forecasts` for the signed-in user's unit (`ForecastService:BaseUrl`) and reads the saved runs back itself. The other endpoints are for checking runs by hand.

## What it does

1. Loads line items from **approved** submissions for one business unit. If two approved uploads overlap, the latest one wins.
2. Uses one frequency per run (`annual`, `quarterly` or `monthly`). For annual runs, complete financial years of monthly data (all 12 months, using `business.year_end_month`) are added up into annual figures. Annual figures are never split into months.
3. Runs **both** methods on every category with enough history and saves one `forecast_run` per method, so the user can compare them side by side.
4. Returns warnings with the result (also stored in `forecast_run.parameters.warnings`): short history (under 3 years / 24 months), missing periods, skipped categories.

| Method | Forecast | Parameter |
|---|---|---|
| Moving Average | Average of the last `window_size` periods | `window_size` (default 3; annual 2–3) |
| Exponential Smoothing | Final smoothed level, `S_t = α·x_t + (1 − α)·S_(t−1)`, `S_0 = x_0` | `alpha` (default 0.5; higher = recent periods count more) |

Both forecasts are **flat**: every future period (1–3, `horizon`) gets the same value.

`adjusted_amount` is left empty here; it is for rule-based / management adjustments in the C# API later.

## Algorithm source

`app/algorithms.py` is adapted from [TheAlgorithms/Python](https://github.com/TheAlgorithms/Python) (MIT):
[`simple_moving_average.py`](https://github.com/TheAlgorithms/Python/blob/master/financial/simple_moving_average.py) and
[`exponential_moving_average.py`](https://github.com/TheAlgorithms/Python/blob/master/financial/exponential_moving_average.py).
Changes: forecast step added, α set directly by the user, cleaner start for short histories. Details in the file header.

## Run it

Python does not need to be installed; it runs in Docker. From the repo root (where the compose file lives):

```powershell
docker compose up -d --build forecast
```

API docs (try every endpoint): http://localhost:5090/docs

The database password comes from the root `.env`, same as the C# API.

**Without Docker (optional):** Python 3.13 + a virtual environment in `forecasting/.venv` (gitignored):

```powershell
cd forecasting
python -m venv .venv                                   # first time only
.\.venv\Scripts\python -m pip install -r requirements.txt
.\.venv\Scripts\python -m doctest app/algorithms.py -v # algorithm tests
```

Running the API locally also needs `DATABASE_URL` pointing at the Postgres container (`localhost:5432`).

## Endpoints

| Endpoint | Purpose |
|---|---|
| `POST /api/forecasts` | Run both methods for a unit. Body: `{"unit_id": "...", "frequency": "annual", "horizon": 3, "window_size": 3, "alpha": 0.5}` |
| `GET /api/forecasts?unit_id=...` | List saved runs (newest first) |
| `GET /api/forecasts/{run_id}` | One run with its forecast values per category |
| `DELETE /api/forecasts/{run_id}` | Delete a run and its values (refused by the database while a `budget_selection` uses the run) |
| `GET /health` | Service is up |

## Check the algorithms

The examples in `app/algorithms.py` double as tests:

```powershell
docker compose run --rm forecast python -m doctest app/algorithms.py -v
```

## Tables

Reads `business`, `business_unit`, `budget_submission`, `financial_line_item`, `line_category`. Writes `forecast_run`, `forecast_value`. It does **not** create tables (see `../database/README.md`).

## Files

| File | Purpose |
|---|---|
| `app/algorithms.py` | The two forecasting methods (pure maths, no database) |
| `app/forecasting.py` | Loads approved history, applies the rules above, saves runs |
| `app/db.py` | Database connection (`DATABASE_URL`) |
| `app/main.py` | FastAPI endpoints |
| `Dockerfile`, `requirements.txt` | Container build and Python packages |
