#!/usr/bin/env python3
"""Stdlib-only golden checks for domain contracts with date/time edge cases."""

from calendar import monthrange
from datetime import date, datetime, time, timedelta, timezone
from decimal import Decimal, ROUND_HALF_EVEN
from hashlib import sha256
from uuid import UUID
from zoneinfo import ZoneInfo


def performance_month(rows, year, month):
    values = [amount for day, amount, _capital in rows if (day.year, day.month) == (year, month)]
    return {
        "total": sum(values, Decimal()),
        "recordedDays": len(values),
        "profitDays": sum(value > 0 for value in values),
        "lossDays": sum(value < 0 for value in values),
        "flatDays": sum(value == 0 for value in values),
        "bestDay": max(values, default=None),
        "worstDay": min(values, default=None),
    }


def pnl_percent(amount, capital):
    if capital is None:
        return None
    return (amount / capital * 100).quantize(Decimal("0.0001"), rounding=ROUND_HALF_EVEN)


performance_rows = [
    (date(2026, 1, 30), Decimal("999"), None),
    (date(2026, 2, 2), Decimal("125.50"), Decimal("10000")),
    (date(2026, 2, 3), Decimal("-25.25"), Decimal("5000")),
    (date(2026, 2, 6), Decimal("0"), None),
    (date(2026, 3, 1), Decimal("777"), None),
]
assert performance_month(performance_rows, 2026, 2) == {
    "total": Decimal("100.25"),
    "recordedDays": 3,
    "profitDays": 1,
    "lossDays": 1,
    "flatDays": 1,
    "bestDay": Decimal("125.50"),
    "worstDay": Decimal("-25.25"),
}
assert performance_month([], 2026, 2) == {
    "total": Decimal("0"), "recordedDays": 0, "profitDays": 0,
    "lossDays": 0, "flatDays": 0, "bestDay": None, "worstDay": None,
}  # Empty aggregate is metadata, not a fabricated daily zero.
assert pnl_percent(Decimal("1"), None) is None
assert pnl_percent(Decimal("125.50"), Decimal("10000")) == Decimal("1.2550")
assert pnl_percent(Decimal("1"), Decimal("6")) == Decimal("16.6667")


def recurrence(start, repeat_mode):
    if repeat_mode == "none":
        end = start
    elif repeat_mode == "week":
        end = start + timedelta(days=(4 - start.weekday()) % 7)
    elif repeat_mode == "month":
        end = start.replace(day=monthrange(start.year, start.month)[1])
    else:
        raise ValueError("invalid_repeat_mode")
    days, candidate = [], start
    while candidate <= end:
        if candidate.weekday() < 5:
            days.append(candidate)
        candidate += timedelta(days=1)
    return days


assert recurrence(date(2026, 7, 8), "none") == [date(2026, 7, 8)]
assert recurrence(date(2026, 7, 11), "none") == []  # Saturday does not spill past its end.
assert recurrence(date(2026, 7, 8), "week") == [date(2026, 7, 8), date(2026, 7, 9), date(2026, 7, 10)]
assert recurrence(date(2026, 7, 11), "week") == [date(2026, 7, 13), date(2026, 7, 14), date(2026, 7, 15), date(2026, 7, 16), date(2026, 7, 17)]
assert recurrence(date(2026, 1, 30), "month") == [date(2026, 1, 30)]  # Weekend month-end skipped.
assert recurrence(date(2024, 2, 28), "month") == [date(2024, 2, 28), date(2024, 2, 29)]  # Leap day.
assert recurrence(date(2026, 2, 28), "month") == []  # Weekend start and month-end.


def trigger_utc(local_day, local_time, zone):
    return datetime.combine(local_day, local_time, ZoneInfo(zone)).astimezone(timezone.utc)


# Same New York wall clock changes UTC offset across DST; the wall clock stays 09:00.
before_dst = trigger_utc(date(2026, 3, 6), time(9), "America/New_York")
after_dst = trigger_utc(date(2026, 3, 9), time(9), "America/New_York")
assert before_dst == datetime(2026, 3, 6, 14, tzinfo=timezone.utc)
assert after_dst == datetime(2026, 3, 9, 13, tzinfo=timezone.utc)
assert before_dst.astimezone(ZoneInfo("America/New_York")).time() == time(9)
assert after_dst.astimezone(ZoneInfo("America/New_York")).time() == time(9)


def discipline_index(user_id, local_day, count):
    material = f"{user_id.hex}:{local_day:%Y-%m-%d}".encode()
    return int.from_bytes(sha256(material).digest()[:8], "little") % count


user = UUID("00112233-4455-6677-8899-aabbccddeeff")
assert discipline_index(user, date(2026, 7, 12), 7) == 2
assert discipline_index(user, date(2026, 7, 12), 7) == discipline_index(user, date(2026, 7, 12), 7)
assert [discipline_index(user, date(2026, 7, day), 7) for day in range(12, 16)] == [2, 6, 2, 6]


def local_date(instant, zone):
    return instant.astimezone(ZoneInfo(zone)).date()


instant = datetime(2026, 1, 1, 0, 30, tzinfo=timezone.utc)
assert local_date(instant, "Pacific/Honolulu") == date(2025, 12, 31)
assert local_date(instant, "Asia/Taipei") == date(2026, 1, 1)
assert local_date(datetime(2026, 3, 8, 7, 30, tzinfo=timezone.utc), "America/New_York") == date(2026, 3, 8)
assert local_date(datetime(2026, 11, 1, 5, 30, tzinfo=timezone.utc), "America/New_York") == date(2026, 11, 1)

print("domain golden: ok")
