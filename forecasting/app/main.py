# Forecasting API: endpoints to run and view forecasts
from datetime import date, datetime
from decimal import Decimal
from typing import Annotated, Any, Literal

import psycopg
from fastapi import Depends, FastAPI, HTTPException, Query, Response
from pydantic import BaseModel, Field

from app import forecasting
from app.db import get_connection

app = FastAPI(
    title="Budget Forecasting Service",
    description="Moving Average and Exponential Smoothing forecasts per income statement category.",
    version="1.0.0",
)

Connection = Annotated[psycopg.Connection, Depends(get_connection)]


class ForecastRequestBody(BaseModel):
    unit_id: str = Field(min_length=1, max_length=32)
    frequency: Literal["annual", "quarterly", "monthly"] = "annual"
    horizon: int = Field(default=1, ge=1, le=3, description="Number of future periods (1-3)")
    window_size: int = Field(default=3, ge=2, le=12, description="Moving Average window (annual: 2-3)")
    alpha: float = Field(default=0.5, gt=0, le=1, description="Exponential Smoothing weight on recent periods")


class ForecastValueOut(BaseModel):
    category: str
    display_name: str
    forecast_period: date
    baseline_amount: Decimal
    adjusted_amount: Decimal | None


class ForecastRunOut(BaseModel):
    run_id: str
    unit_id: str
    forecasting_method: str
    history_start: date
    history_end: date
    parameters: dict[str, Any]
    created_at: datetime


class ForecastRunDetail(ForecastRunOut):
    values: list[ForecastValueOut]


@app.get("/health")
def health() -> dict[str, str]:
    return {"status": "ok"}


@app.post("/api/forecasts", response_model=list[ForecastRunDetail], status_code=201)
def create_forecast(body: ForecastRequestBody, conn: Connection):
    try:
        return forecasting.run_forecast(conn, forecasting.ForecastRequest(**body.model_dump()))
    except forecasting.ForecastError as error:
        raise HTTPException(status_code=422, detail=str(error)) from error


@app.get("/api/forecasts", response_model=list[ForecastRunOut])
def list_forecasts(
    conn: Connection,
    unit_id: str | None = None,
    take: int = Query(default=20, ge=1, le=200),
):
    return forecasting.list_runs(conn, unit_id, take)


@app.get("/api/forecasts/{run_id}", response_model=ForecastRunDetail)
def get_forecast(run_id: str, conn: Connection):
    run = forecasting.get_run(conn, run_id)
    if run is None:
        raise HTTPException(status_code=404, detail="Forecast run not found")
    return run


@app.delete("/api/forecasts/{run_id}", status_code=204)
def delete_forecast(run_id: str, conn: Connection):
    if not forecasting.delete_run(conn, run_id):
        raise HTTPException(status_code=404, detail="Forecast run not found")
    return Response(status_code=204)
