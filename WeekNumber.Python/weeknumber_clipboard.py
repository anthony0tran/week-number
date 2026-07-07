"""Clipboard formatting helpers for WeekNumber.

This module is the single source of truth for values copied from the calendar.

Formats:
- Date: ``dd-mm-yyyy``
- ISO calendar week: ``yyyy CWww``

ISO week formatting intentionally uses the ISO week-year, not the Gregorian
calendar year. For example, 2021-01-01 belongs to ISO week ``2020 CW53``.
"""

from __future__ import annotations

import datetime as dt


def _ensure_date(on_date: dt.date) -> dt.date:
    if not isinstance(on_date, dt.date):
        raise TypeError("on_date must be a datetime.date instance")
    return on_date


def format_gui_date(on_date: dt.date) -> str:
    """Format a date for copying from the calendar UI."""
    return _ensure_date(on_date).strftime("%d-%m-%Y")


def format_iso_week(iso_year: int, iso_week: int) -> str:
    """Format an already-computed (ISO year, ISO week) pair.

    Every ``yyyy CWww`` string in the app must come through here -- both the
    date-based path below and the calendar's week-row cells, which carry the
    ISO pair directly. One format string, zero drift.
    """
    return f"{int(iso_year)} CW{int(iso_week):02d}"


def format_calendar_week(on_date: dt.date) -> str:
    """Format an ISO calendar week for copying from the calendar UI."""
    iso_year, iso_week, _ = _ensure_date(on_date).isocalendar()
    return format_iso_week(iso_year, iso_week)
