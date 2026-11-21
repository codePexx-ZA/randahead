# Python forecasting service

Runs **Moving Average** and **Exponential Smoothing** on an annual income statement history and returns the forecasts. It is a pure calculation service: it has no database connection and saves nothing.

The web app never calls this service. The C# API loads the signed-in user's active history, sends it to `POST /api/forecasts` (`ForecastService:BaseUrl`), then saves the two runs in `forecast_run` and `forecast_value` itself.

## What it does

1. Takes each category's annual amounts from the active history (the C# API has already picked the most recently submitted active upload where uploads overlap). The request field and the service's own messages still use the word "approved".
2. Runs **both** methods on every category with enough history, so the user can compare them side by side.
3. Returns warnings with the result (the C# API stores them in `forecast_run.parameters.warnings`): short history (under 3 years), missing years, skipped categories.

| Method | Forecast | Parameter |
|---|---|---|
| Moving Average | Average of the last `window_size` years | `window_size` (default 3; the C# API allows 2–3) |
| Exponential Smoothing | Final smoothed level, `S_t = α·x_t + (1 − α)·S_(t−1)`, `S_0 = x_0` | `alpha` (default 0.5; higher = recent years count more) |

Both forecasts are **flat**: every future year (1–3, `horizon`) gets the same value.

## Algorithm source

`app/algorithms.py` is adapted from [TheAlgorithms/Python](https://github.com/TheAlgorithms/Python) (MIT):
[`simple_moving_average.py`](https://github.com/TheAlgorithms/Python/blob/master/financial/simple_moving_average.py) and
[`exponential_moving_average.py`](https://github.com/TheAlgorithms/Python/blob/master/financial/exponential_moving_average.py).
Changes: forecast step added, α passed in with the request, cleaner start for short histories. The web app does not ask for α. It sends one of three predictions (Steady 0.3, Balanced 0.5, Recent 0.7) and the matching window. Details in the file header.

## Run it

Python does not need to be installed; it runs in Docker. From the repo root (where the compose file lives):

```bash
docker compose up -d --build forecast
```

API docs (try the endpoint): http://127.0.0.1:5090/docs

**Without Docker (optional).** Python 3.13 and a virtual environment in `forecasting/.venv` (gitignored):

```bash
cd forecasting
python3 -m venv .venv                                  # first time only
.venv/bin/python -m pip install -r requirements.txt
.venv/bin/python -m doctest app/algorithms.py app/forecasting.py -v  # tests
.venv/bin/python -m uvicorn app.main:app --port 5090                 # run the API
```

## Endpoints

| Endpoint | Purpose |
|---|---|
| `POST /api/forecasts` | Forecast a history. Body: `{"history": {"revenue": [{"period": "2023-03-01", "amount": 1000}, ...]}, "horizon": 2, "window_size": 3, "alpha": 0.5}`. Returns `history_start`, `history_end`, `periods` (the future years), `forecasts` (`moving_average` and `exponential_smoothing`, each category's amounts per future year) and `warnings`; `422` with `detail` when no category has enough history |
| `GET /health` | Service is up |

## Check the algorithms

The examples in `app/algorithms.py` and `app/forecasting.py` double as tests:

```bash
docker compose run --rm forecast python -m doctest app/algorithms.py app/forecasting.py -v
```

## Files

| File | Purpose |
|---|---|
| `app/algorithms.py` | The two forecasting methods (pure maths) |
| `app/forecasting.py` | Checks the history, applies the rules above and runs both methods |
| `app/main.py` | FastAPI endpoint and request/response shapes |
| `Dockerfile`, `requirements.txt` | Container build and Python packages |
