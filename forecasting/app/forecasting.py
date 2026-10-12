# Forecasting logic: checks an annual history and runs both forecasting methods on it
from dataclasses import dataclass, field
from datetime import date

from app import algorithms

RECOMMENDED_YEARS = 3


class ForecastError(Exception):
    pass


@dataclass
class ForecastRequest:
    history: dict[str, list[tuple[date, float]]]
    horizon: int = 1
    window_size: int = 3
    alpha: float = 0.5


@dataclass
class ForecastResult:
    history_start: date
    history_end: date
    periods: list[date]
    forecasts: dict[str, dict[str, list[float]]]
    warnings: list[str] = field(default_factory=list)


def add_years(day: date, years: int) -> date:
    return date(day.year + years, day.month, 1)


def run_forecast(request: ForecastRequest) -> ForecastResult:
    """
    >>> history = {"revenue": [(date(2022, 3, 1), 1000), (date(2023, 3, 1), 1100), (date(2024, 3, 1), 1250)]}
    >>> result = run_forecast(ForecastRequest(history, horizon=2, window_size=3, alpha=0.5))
    >>> result.periods
    [datetime.date(2025, 3, 1), datetime.date(2026, 3, 1)]
    >>> result.forecasts["moving_average"]["revenue"]
    [1116.67, 1116.67]
    >>> run_forecast(ForecastRequest({"revenue": [(date(2024, 3, 1), 1000)]}, window_size=2))  # doctest: +IGNORE_EXCEPTION_DETAIL
    Traceback (most recent call last):
    ...
    ForecastError: No category has at least 2 approved annual periods to forecast.
    """
    warnings: list[str] = []
    usable: dict[str, list[tuple[date, float]]] = {}
    for category, points in sorted(request.history.items()):
        points = sorted(points)
        if not points:
            continue
        if len(points) < request.window_size:
            warnings.append(
                f"{category}: skipped, only {len(points)} annual periods "
                f"(Moving Average window is {request.window_size})."
            )
            continue
        if add_years(points[0][0], len(points) - 1) != points[-1][0]:
            warnings.append(f"{category}: history has missing periods; forecast uses the periods available.")
        usable[category] = points

    if not usable:
        raise ForecastError(f"No category has at least {request.window_size} approved annual periods to forecast.")

    history_start = min(points[0][0] for points in usable.values())
    history_end = max(points[-1][0] for points in usable.values())
    shortest = min(len(points) for points in usable.values())
    if shortest < RECOMMENDED_YEARS:
        warnings.append(
            f"Short history: {shortest} annual periods "
            f"(recommended at least {RECOMMENDED_YEARS}). Treat forecasts with caution."
        )
    for category, points in usable.items():
        if points[-1][0] != history_end:
            warnings.append(f"{category}: no figure for the latest period ({history_end}).")

    amounts = {category: [amount for _, amount in points] for category, points in usable.items()}
    return ForecastResult(
        history_start=history_start,
        history_end=history_end,
        periods=[add_years(history_end, k) for k in range(1, request.horizon + 1)],
        forecasts={
            "moving_average": {
                category: algorithms.moving_average_forecast(values, request.window_size, request.horizon)
                for category, values in amounts.items()
            },
            "exponential_smoothing": {
                category: algorithms.exponential_smoothing_forecast(values, request.alpha, request.horizon)
                for category, values in amounts.items()
            },
        },
        warnings=warnings,
    )
