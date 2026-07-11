#!/usr/bin/env sh
set -eu
cd "$(dirname "$0")/.."
python3 tests/golden-domain.py
python3 tests/golden-rotation.py
