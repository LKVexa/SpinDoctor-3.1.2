"""Contained loopback HTTP bridge to the actual CPU/OpenCL RAM backend."""
from __future__ import annotations
import argparse
import hashlib
import hmac
import json
import os
from pathlib import Path
import secrets
import threading
from http.server import BaseHTTPRequestHandler
from loopback_http import (BoundedThreadingHTTPServer, host_permitted, session_permitted,
                           read_json_body, read_static, security_headers, json_response)
from urllib.parse import urlsplit
import webbrowser

import cpu_gpu_xpu as backend

SCHEMA = "QUETTAFLOP/CPU-GPU-NATIVE-REPORT/1"
STATIC_RUNTIME_FILES = {
    "runtime/number-raster.js", "runtime/individual-xpu.js", "runtime/recursive-xpu.js",
    "runtime/latest_10q_metrics.js", "runtime/rate-metrics.js", "runtime/flop-ledger.js",
    "runtime/full-domain.js", "runtime/full-viewer.js", "runtime/ram-bit-xpu.js",
    "runtime/virtual-worker-pool.js", "runtime/virtual-domain.js", "runtime/virtual-xpu.js",
}

def envelope(data):
    body = json.dumps(data, ensure_ascii=False, separators=(",", ":"), allow_nan=False)
    return {"schema": SCHEMA, "dataJson": body, "dataSha256": hashlib.sha256(body.encode("utf-8")).hexdigest()}

def normalize_config(value):
    if not isinstance(value, dict):
        raise ValueError("Configuration must be an object.")
    allowed = {"budget_mib", "reserve_gib", "cpu_workers", "gpu_fraction", "read_depth", "seed", "a", "b", "wave_count", "sample_count", "deadline_ms", "chunk_mib"}
    if set(value) - allowed:
        raise ValueError("Unknown native configuration field.")
    config = dict(value)
    budget = config.get("budget_mib", 0)
    if type(budget) is not int or budget < 0:
        raise ValueError("Buffer budget must be an integer MiB amount; 0 chooses available RAM.")
    workers = config.get("cpu_workers", 0)
    if type(workers) is not int or workers < 0 or workers > min(64, os.cpu_count() or 1):
        raise ValueError("CPU worker count must be 0 through detected logical CPUs.")
    config["cpu_workers"] = workers or min(64, os.cpu_count() or 1)
    config["auto_ram"] = budget == 0
    if budget == 0:
        config.pop("budget_mib", None)
    reserve = config.get("reserve_gib", 2)
    if not isinstance(reserve, (int, float)) or isinstance(reserve, bool) or not 2 <= reserve <= 1024:
        raise ValueError("System reserve must be at least 2 GiB.")
    for key, low, high in (("read_depth", 1, 16), ("seed", 0, 0xffffffff), ("a", 0, 1),
                          ("b", 0, 1), ("wave_count", 1, 16), ("sample_count", 1, 1060),
                          ("chunk_mib", 1, 0x7fffffff)):
        if key in config and (type(config[key]) is not int or not low <= config[key] <= high):
            raise ValueError(key + " is outside the bounded integer range.")
    for key, low, high in (("gpu_fraction", 0, 1), ("deadline_ms", 0, 1000)):
        if key in config:
            number = config[key]
            if isinstance(number, bool) or not isinstance(number, (int, float)) or not low < number <= high or key == "gpu_fraction" and number == 1:
                raise ValueError(key + " is outside the bounded finite range.")
    return config

class Application:
    def __init__(self):
        self.lock = threading.RLock()
        self.cancel = threading.Event()
        self.generation = 0
        self.busy = False
        self.phase = "idle"
        self.error = None
        self.hardware = None
        self.plan = None
        self.config = None
        self.session = None
        self.progress = None
        self.report = None
        self.samples = None
        self.thread = None

    def state(self):
        with self.lock:
            return {"generation": self.generation, "busy": self.busy, "phase": self.phase,
                    "error": self.error, "hardware": self.hardware, "plan": self.plan,
                    "progress": self.progress, "report": self.report, "samples": self.samples,
                    "backend": "actual native CPU + OpenCL GPU", "nativeConnected": True}

    def invoke(self, command, payload):
        if command == "status":
            return self.state()
        if command == "probe":
            result = backend.probe()
            with self.lock:
                self.hardware = result
            return self.state()
        if command == "plan":
            with self.lock:
                if self.busy or self.session is not None:
                    raise ValueError("Release the previous RAM session before making a new memory plan.")
                config = normalize_config(payload.get("config", {}))
                planned = backend.plan_budget(config)
                self.config, self.plan, self.error = config, planned, None
            return self.state()
        if command == "run":
            with self.lock:
                if self.busy:
                    raise ValueError("The native job is already running.")
                config = normalize_config(payload.get("config", {}))
                if self.session is not None and config != self.config:
                    raise ValueError("Reset/release RAM before changing the run configuration.")
                if self.session is None:
                    self.plan = backend.plan_budget(config)
                    self.config = config
                    token = self.generation
                    def progress(value):
                        with self.lock:
                            if token == self.generation:
                                self.progress = value
                                self.phase = str(value.get("phase", value.get("stage", value.get("status", "running"))))
                    self.session = backend.Session(self.plan, on_progress=progress, cancel_event=self.cancel)
                token = self.generation
                self.cancel.clear()
                self.busy, self.phase, self.error = True, "starting", None
                self.samples = None
                session = self.session
                def execute():
                    try:
                        session.execute()
                        report = session.report()
                        progress = session.snapshot()
                        with self.lock:
                            if token == self.generation:
                                self.report, self.progress = report, progress
                                self.phase = str(progress.get("status", "paused")).lower()
                                self.error = progress.get("error")
                    except Exception as exc:
                        with self.lock:
                            if token == self.generation:
                                self.error, self.phase = str(exc), "error"
                    finally:
                        with self.lock:
                            if token == self.generation:
                                self.busy = False
                self.thread = threading.Thread(target=execute, name="Contained-XPU-job", daemon=True)
                self.thread.start()
            return self.state()
        if command == "pause":
            self.cancel.set()
            return self.state()
        if command == "reset":
            with self.lock:
                if self.busy:
                    raise ValueError("Pause and wait for the current native chunk before releasing RAM.")
                if self.session is not None:
                    self.session.release()
                self.generation += 1
                self.session, self.plan, self.config = None, None, None
                self.report, self.progress, self.samples = None, None, None
                self.phase, self.error = "idle", None
            return self.state()
        if command == "sample":
            with self.lock:
                if self.busy:
                    raise ValueError("Pause and wait for native completion barriers before sampling RAM.")
                if self.session is None:
                    raise ValueError("No actual native RAM session is allocated.")
                self.samples = self.session.sample(payload.get("bitIds"))
                self.progress = self.session.snapshot()
                self.report = self.session.report()
            return self.state()
        if command == "export":
            with self.lock:
                if self.busy:
                    raise ValueError("Pause and wait before exporting the native RAM report.")
                if self.session is None or self.report is None:
                    raise ValueError("No acknowledged native run report exists.")
                return envelope({"report": self.session.report(), "samples": self.samples,
                                 "plan": self.plan, "hardware": self.hardware})
        raise ValueError("Unknown contained native command.")

def create_server(viewer):
    viewer = Path(viewer).resolve()
    if not viewer.is_file():
        raise ValueError("Viewer file does not exist.")
    application = Application()
    token = secrets.token_urlsafe(32)
    root = viewer.parent
    allowed_pages = {viewer.name}
    allowed_pages.update(p.name for p in root.glob("QuettaFLOP-CPU-GPU-XPU*.html"))
    allowed_pages.update(p.name for p in root.glob("OPEN_*.html"))
    allowed_pages.update({"RAM_BIT_EXECUTION.html", "PARALLEL_EXECUTION.html", "INDIVIDUAL_EXECUTION.html",
                          "FAST_FORWARD_REFERENCE.html", "VIRTUAL_DOMAIN_LEDGER.html", "FINITE_EXECUTION.html", "SAMPLING_LEDGER.html"})

    class Handler(BaseHTTPRequestHandler):
        def log_message(self, *_):
            pass
        def json_response(self, value, status=200):
            json_response(self, value, status)
        def permitted(self):
            return session_permitted(self, token, 'X-XPU-Token')
        def do_OPTIONS(self):
            self.json_response({"ok": False, "error": "Cross-origin access is disabled."}, 403)
        def do_POST(self):
            if not self.permitted():
                self.json_response({"ok": False, "error": "Native session token and same origin are required."}, 403)
                return
            command = urlsplit(self.path).path.removeprefix("/api/")
            if not self.path.startswith("/api/") or command not in {"status", "probe", "plan", "run", "pause", "sample", "reset", "export"}:
                self.json_response({"ok": False, "error": "Unknown contained native command."}, 404)
                return
            try:
                payload = read_json_body(self)
                if not isinstance(payload, dict) or set(payload) - {"config", "bitIds"}:
                    raise ValueError("Unknown native request field.")
                self.json_response({"ok": True, "data": application.invoke(command, payload)})
            except Exception as exc:
                self.json_response({"ok": False, "error": str(exc)}, 400)
        def do_GET(self):
            if not host_permitted(self):
                self.json_response({"ok": False, "error": "Loopback host is required."}, 403)
                return
            relative = urlsplit(self.path).path.lstrip("/") or viewer.name
            if relative not in allowed_pages and relative not in STATIC_RUNTIME_FILES:
                self.json_response({"ok": False, "error": "Unknown contained viewer file."}, 404)
                return
            target = root / relative
            if not target.is_file():
                self.json_response({"ok": False, "error": "Contained viewer file is unavailable."}, 404)
                return
            try:
                body = read_static(root, relative)
            except (OSError, ValueError):
                self.json_response({"ok": False, "error": "Contained viewer file is unavailable."}, 404)
                return
            self.send_response(200)
            self.send_header("Content-Type", "application/javascript; charset=utf-8" if relative.endswith(".js") else "text/html; charset=utf-8")
            self.send_header("Cache-Control", "no-store")
            self.send_header("X-Content-Type-Options", "nosniff")
            security_headers(self)
            self.send_header("Content-Length", str(len(body)))
            self.end_headers()
            self.wfile.write(body)
    server = BoundedThreadingHTTPServer(("127.0.0.1", 0), Handler)
    server.daemon_threads = True
    return server, application, token

def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--viewer", type=Path)
    parser.add_argument("--no-browser", action="store_true")
    args = parser.parse_args()
    work_root = Path(__file__).resolve().parent.parent
    viewer = args.viewer or (work_root / "OPEN_SIMULATOR.html")
    if not viewer.is_file():
        candidates = [work_root / "TENQ_AT_1MHZ_DASHBOARD.html", work_root / "decoded/mvp/TENQ_AT_1MHZ_DASHBOARD.html"]
        viewer = next((candidate for candidate in candidates if candidate.is_file()), viewer)
    server, application, token = create_server(viewer)
    url = "http://127.0.0.1:" + str(server.server_port) + "/" + viewer.name + "#xpu=" + token
    print("Contained native CPU + GPU server ready. Keep this launcher running.", flush=True)
    print(url, flush=True)
    if not args.no_browser:
        webbrowser.open(url)
    try:
        server.serve_forever(poll_interval=0.2)
    except KeyboardInterrupt:
        pass
    finally:
        application.cancel.set()
        if application.thread:
            application.thread.join(timeout=60)
        if not application.busy and application.session:
            application.session.release()
        server.server_close()

if __name__ == "__main__":
    main()
