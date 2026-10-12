# Forecasting API: one endpoint that forecasts an annual history sent by the C# API
from dataclasses import asdict
from datetime import date

from fastapi import FastAPI, HTTPException
from pydantic import BaseModel, Field

from app import forecasting

app = FastAPI(
    title="Budget Forecasting Service",
    description="Moving Average and Exponential Smoothing forecasts per income statement category.",
    version="2.0.0",
)


class HistoryPoint(BaseModel):
    period: date
    amount: float


class ForecastRequestBody(BaseModel):
    history: dict[str, list[HistoryPoint]] = Field(description="Approved annual amounts per category")
    horizon: int = Field(default=1, ge=1, le=3, description="Number of future years (1-3)")
    window_size: int = Field(default=3, ge=2, le=12, description="Moving Average window in years")
    alpha: float = Field(default=0.5, gt=0, le=1, description="Exponential Smoothing weight on recent years")


class ForecastResponse(BaseModel):
    history_start: date
    history_end: date
    periods: list[date]
    forecasts: dict[str, dict[str, list[float]]]
    warnings: list[str]


@app.get("/health")
def health() -> dict[str, str]:
    return {"status": "ok"}


@app.post("/api/forecasts", response_model=ForecastResponse)
def create_forecast(body: ForecastRequestBody):
    request = forecasting.ForecastRequest(
        history={category: [(p.period, p.amount) for p in points] for category, points in body.history.items()},
        horizon=body.horizon,
        window_size=body.window_size,
        alpha=body.alpha,
    )
    try:
        return asdict(forecasting.run_forecast(request))
    except forecasting.ForecastError as error:
        raise HTTPException(status_code=422, detail=str(error)) from error
