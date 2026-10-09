# Forecasting logic: loads history, runs forecasts and saves them
import uuid
from collections import defaultdict
from dataclasses import dataclass, field
from datetime import date

import psycopg
from psycopg.types.json import Jsonb

from app import algorithms

MONTHS_PER_PERIOD = {"monthly": 1, "quarterly": 3, "annual": 12}
RECOMMENDED_PERIODS = {"monthly": 24, "quarterly": 8, "annual": 3}


class ForecastError(Exception):
    pass


@dataclass
class ForecastRequest:
    unit_id: str
    frequency: str = "annual"
    horizon: int = 1
    window_size: int = 3
    alpha: float = 0.5


@dataclass
class History:
    series: dict[str, list[tuple[date, float]]]
    warnings: list[str] = field(default_factory=list)


def add_months(day: date, months: int) -> date:
    month_index = day.month - 1 + months
    return date(day.year + month_index // 12, month_index % 12 + 1, 1)


def financial_year_start(day: date, year_end_month: int) -> date:
    start_month = year_end_month % 12 + 1
    year = day.year if day.month >= start_month else day.year - 1
    return date(year, start_month, 1)


def load_history(conn: psycopg.Connection, request: ForecastRequest) -> History:
    unit = conn.execute(
        """
        SELECT b.year_end_month
        FROM business_unit u
        JOIN business b ON b.business_id = u.business_id
        WHERE u.unit_id = %s
        """,
        (request.unit_id,),
    ).fetchone()
    if unit is None:
        raise ForecastError(f"Business unit {request.unit_id} not found")
    year_end_month = unit["year_end_month"]

    rows = conn.execute(
        """
        SELECT DISTINCT ON (fli.category, fli.period_start, fli.frequency)
               fli.category, fli.period_start, fli.frequency, fli.amount
        FROM financial_line_item fli
        JOIN budget_submission bs ON bs.submission_id = fli.submission_id
        WHERE bs.unit_id = %s AND bs.status = 'approved'
        ORDER BY fli.category, fli.period_start, fli.frequency, bs.submitted_at DESC
        """,
        (request.unit_id,),
    ).fetchall()

    points: dict[str, dict[date, float]] = defaultdict(dict)
    monthly: dict[tuple[str, date], dict[date, float]] = defaultdict(dict)

    for row in rows:
        amount = float(row["amount"])
        if row["frequency"] == request.frequency:
            points[row["category"]][row["period_start"]] = amount
        elif request.frequency == "annual" and row["frequency"] == "monthly":
            fy_start = financial_year_start(row["period_start"], year_end_month)
            monthly[(row["category"], fy_start)][row["period_start"]] = amount

    warnings: list[str] = []
    rolled_up = 0
    for (category, fy_start), months in monthly.items():
        if fy_start in points[category]:
            continue
        if len(months) == 12:
            points[category][fy_start] = sum(months.values())
            rolled_up += 1
    if rolled_up:
        warnings.append(f"{rolled_up} annual figures were calculated from complete years of monthly data.")

    series = {
        category: sorted(periods.items())
        for category, periods in points.items()
        if periods
    }
    return History(series=series, warnings=warnings)


def run_forecast(conn: psycopg.Connection, request: ForecastRequest) -> list[dict]:
    history = load_history(conn, request)
    warnings = list(history.warnings)
    step = MONTHS_PER_PERIOD[request.frequency]

    usable: dict[str, list[tuple[date, float]]] = {}
    for category, points in sorted(history.series.items()):
        if len(points) < request.window_size:
            warnings.append(
                f"{category}: skipped, only {len(points)} {request.frequency} periods "
                f"(Moving Average window is {request.window_size})."
            )
            continue
        expected = add_months(points[0][0], step * (len(points) - 1))
        if expected != points[-1][0]:
            warnings.append(f"{category}: history has missing periods; forecast uses the periods available.")
        usable[category] = points

    if not usable:
        raise ForecastError(
            f"No category has at least {request.window_size} approved {request.frequency} periods to forecast."
        )

    history_start = min(points[0][0] for points in usable.values())
    history_end = max(points[-1][0] for points in usable.values())
    shortest = min(len(points) for points in usable.values())
    if shortest < RECOMMENDED_PERIODS[request.frequency]:
        warnings.append(
            f"Short history: {shortest} {request.frequency} periods "
            f"(recommended at least {RECOMMENDED_PERIODS[request.frequency]}). Treat forecasts with caution."
        )
    for category, points in usable.items():
        if points[-1][0] != history_end:
            warnings.append(f"{category}: no figure for the latest period ({history_end}).")

    future_periods = [add_months(history_end, step * k) for k in range(1, request.horizon + 1)]

    methods = {
        "moving_average": (
            {"window_size": request.window_size},
            lambda values: algorithms.moving_average_forecast(values, request.window_size, request.horizon),
        ),
        "exponential_smoothing": (
            {"alpha": request.alpha},
            lambda values: algorithms.exponential_smoothing_forecast(values, request.alpha, request.horizon),
        ),
    }

    run_ids = []
    with conn.transaction():
        for method, (method_params, forecast) in methods.items():
            run_id = uuid.uuid4().hex
            parameters = {
                **method_params,
                "frequency": request.frequency,
                "horizon": request.horizon,
                "warnings": warnings,
            }
            conn.execute(
                """
                INSERT INTO forecast_run (run_id, unit_id, forecasting_method, history_start, history_end, parameters)
                VALUES (%s, %s, %s, %s, %s, %s)
                """,
                (run_id, request.unit_id, method, history_start, history_end, Jsonb(parameters)),
            )
            for category, points in usable.items():
                values = [amount for _, amount in points]
                for period, amount in zip(future_periods, forecast(values)):
                    conn.execute(
                        """
                        INSERT INTO forecast_value (value_id, run_id, category, forecast_period, baseline_amount)
                        VALUES (%s, %s, %s, %s, %s)
                        """,
                        (uuid.uuid4().hex, run_id, category, period, amount),
                    )
            run_ids.append(run_id)

    return [get_run(conn, run_id) for run_id in run_ids]


def get_run(conn: psycopg.Connection, run_id: str) -> dict | None:
    run = conn.execute(
        """
        SELECT run_id, unit_id, forecasting_method, history_start, history_end, parameters, created_at
        FROM forecast_run
        WHERE run_id = %s
        """,
        (run_id,),
    ).fetchone()
    if run is None:
        return None

    values = conn.execute(
        """
        SELECT fv.category, lc.display_name, fv.forecast_period, fv.baseline_amount, fv.adjusted_amount
        FROM forecast_value fv
        JOIN line_category lc ON lc.category_code = fv.category
        WHERE fv.run_id = %s
        ORDER BY lc.sort_order, fv.forecast_period
        """,
        (run_id,),
    ).fetchall()
    return {**run, "values": values}


def list_runs(conn: psycopg.Connection, unit_id: str | None, take: int) -> list[dict]:
    return conn.execute(
        """
        SELECT run_id, unit_id, forecasting_method, history_start, history_end, parameters, created_at
        FROM forecast_run
        WHERE %(unit_id)s::varchar IS NULL OR unit_id = %(unit_id)s
        ORDER BY created_at DESC
        LIMIT %(take)s
        """,
        {"unit_id": unit_id, "take": take},
    ).fetchall()


def delete_run(conn: psycopg.Connection, run_id: str) -> bool:
    with conn.transaction():
        deleted = conn.execute("DELETE FROM forecast_run WHERE run_id = %s", (run_id,))
    return deleted.rowcount > 0
