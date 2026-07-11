#!/usr/bin/env python3
"""Golden values for rotation-v1. Update deliberately when the formula version changes."""
from math import isclose
from pathlib import Path

FORMULA_VERSION = "rotation-v1"
prices = {
    "SPY": [100 + i for i in range(201)],
    "XLK": [100 + 2 * i for i in range(201)],
    "XLE": [300 - i for i in range(201)],
}

def snapshot(values):
    close = values[-1]
    ret_2w = (close / values[-11] - 1) * 100
    ma200 = sum(values[-200:]) / 200
    return close, ret_2w, close > ma200

rows = {symbol: snapshot(values) for symbol, values in prices.items()}
assert FORMULA_VERSION == "rotation-v1"
source = Path(__file__).parents[1] / "services/rotation-service/src/TradeDiary.Rotation/Program.cs"
assert f'const string FormulaVersion = "{FORMULA_VERSION}"' in source.read_text()
assert isclose(rows["SPY"][1], (300 / 290 - 1) * 100, rel_tol=1e-12)
assert [x[0] for x in sorted(rows.items(), key=lambda x: x[1][1], reverse=True)] == ["XLK", "SPY", "XLE"]
assert isclose(sum(row[2] for row in rows.values()) / len(rows) * 100, 200 / 3)
assert rows["SPY"][2] and "risk_on" == "risk_on"

# A 63-session series has valid short returns but cannot invent MA200/state.
short = [100 + i for i in range(63)]
assert len(short) < 200
print("rotation golden: ok")
