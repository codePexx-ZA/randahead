# Forecast algorithms: Moving Average and Exponential Smoothing (adapted from TheAlgorithms/Python, MIT)
from collections.abc import Sequence


def simple_moving_average(data: Sequence[float], window_size: int) -> list[float | None]:
    """
    Simple moving average at each point; None until a full window is available.

    >>> sma = simple_moving_average([10, 12, 15, 13, 14, 16, 18, 17, 19, 21], 3)
    >>> [round(value, 2) if value is not None else None for value in sma]
    [None, None, 12.33, 13.33, 14.0, 14.33, 16.0, 17.0, 18.0, 19.0]
    >>> simple_moving_average([10, 12, 15], 5)
    [None, None, None]
    >>> simple_moving_average([10, 12], 0)
    Traceback (most recent call last):
    ...
    ValueError: Window size must be a positive integer
    """
    if window_size < 1:
        raise ValueError("Window size must be a positive integer")

    return [
        None if i < window_size - 1 else sum(data[i - window_size + 1 : i + 1]) / window_size
        for i in range(len(data))
    ]


def moving_average_forecast(
    data: Sequence[float], window_size: int, horizon: int = 1
) -> list[float]:
    """
    Forecast = average of the last `window_size` periods, repeated for each future period.

    Annual revenue for 5 years, 3-year window, 2 years ahead:
    >>> moving_average_forecast([1000, 1100, 1250, 1300, 1420], 3, 2)
    [1323.33, 1323.33]
    >>> moving_average_forecast([1000, 1100], 3)
    Traceback (most recent call last):
    ...
    ValueError: Need at least 3 periods of history, got 2
    """
    _check_horizon(horizon)
    if len(data) < window_size:
        raise ValueError(f"Need at least {window_size} periods of history, got {len(data)}")

    last_average = simple_moving_average(data, window_size)[-1]
    return [round(last_average, 2)] * horizon


def exponential_smoothing(data: Sequence[float], alpha: float) -> list[float]:
    """
    Simple exponential smoothing: S_0 = x_0, then S_t = alpha * x_t + (1 - alpha) * S_(t-1).
    A higher alpha gives recent periods more weight.

    >>> exponential_smoothing([2, 5, 3, 8.2, 6, 9, 10], 0.5)
    [2, 3.5, 3.25, 5.725, 5.8625, 7.43125, 8.715625]
    >>> exponential_smoothing([10.1, 20.02, 30.003], 1)
    [10.1, 20.02, 30.003]
    >>> exponential_smoothing([1, 2], 0)
    Traceback (most recent call last):
    ...
    ValueError: alpha must be greater than 0 and at most 1
    """
    if not 0 < alpha <= 1:
        raise ValueError("alpha must be greater than 0 and at most 1")
    if not data:
        return []

    smoothed = [data[0]]
    for value in data[1:]:
        smoothed.append(alpha * value + (1 - alpha) * smoothed[-1])
    return smoothed


def exponential_smoothing_forecast(
    data: Sequence[float], alpha: float, horizon: int = 1
) -> list[float]:
    """
    Forecast = final smoothed level, repeated for each future period.

    >>> exponential_smoothing_forecast([1000, 1100, 1250, 1300, 1420], 0.5, 2)
    [1322.5, 1322.5]
    >>> exponential_smoothing_forecast([], 0.5)
    Traceback (most recent call last):
    ...
    ValueError: Need at least 1 period of history
    """
    _check_horizon(horizon)
    if not data:
        raise ValueError("Need at least 1 period of history")

    level = exponential_smoothing(data, alpha)[-1]
    return [round(level, 2)] * horizon


def _check_horizon(horizon: int) -> None:
    if horizon < 1:
        raise ValueError("horizon must be at least 1")


if __name__ == "__main__":
    import doctest

    doctest.testmod()
