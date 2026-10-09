# Database connection: opens one connection per request
import os
from collections.abc import Iterator

import psycopg
from psycopg.rows import dict_row


def get_connection() -> Iterator[psycopg.Connection]:
    url = os.environ.get("DATABASE_URL")
    if not url:
        raise RuntimeError("DATABASE_URL is not set (see forecasting/README.md)")

    with psycopg.connect(url, autocommit=True, row_factory=dict_row) as conn:
        yield conn
