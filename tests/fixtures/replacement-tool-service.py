#!/usr/bin/env python3
"""Tiny non-.NET Tool Service replacement used by the rewrite contract proof."""

from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer
import json
import os

ROUTES = {
    ("GET", "/health/live"), ("GET", "/health/ready"), ("GET", "/version"),
    ("POST", "/internal/tools/fire"),
    ("POST", "/internal/tools/position-sizing"),
    ("POST", "/internal/tools/relative-value"),
    ("POST", "/internal/tools/risk-reward"),
    ("POST", "/internal/tools/seasonality"),
}


def dispatch(method, path, payload=None):
    """Framework-neutral replacement boundary; the HTTP adapter calls this."""
    if (method, path) not in ROUTES:
        return 404, {"error": "not_found"}
    if method == "GET":
        if path == "/version":
            return 200, {"service": "tool-service-python-replacement", "version": "0.1.0"}
        return 200, {"status": "ok"}
    return 200, {"input": payload or {}, "result": {}}


class Handler(BaseHTTPRequestHandler):
    def _reply(self, status, body):
        data = json.dumps(body).encode()
        self.send_response(status)
        self.send_header("Content-Type", "application/json")
        self.send_header("Content-Length", str(len(data)))
        self.end_headers()
        self.wfile.write(data)

    def do_GET(self):
        self._reply(*dispatch("GET", self.path))

    def do_POST(self):
        if ("POST", self.path) not in ROUTES:
            return self._reply(*dispatch("POST", self.path))
        try:
            size = int(self.headers.get("Content-Length", "0"))
            payload = json.loads(self.rfile.read(size) or b"{}")
        except (ValueError, json.JSONDecodeError):
            return self._reply(400, {"error": "invalid_json"})
        self._reply(*dispatch("POST", self.path, payload))

    def log_message(self, *_):
        pass


if __name__ == "__main__":
    ThreadingHTTPServer(("127.0.0.1", int(os.environ.get("PORT", "18081"))), Handler).serve_forever()
