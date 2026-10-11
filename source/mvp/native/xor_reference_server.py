"""Contained loopback bridge for the RAM-resident black/white XOR experiment."""
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
                           read_json_body, read_static, security_headers, write_json_atomic, json_response)
from urllib.parse import urlsplit
import webbrowser

import shared_epoch_xpu as backend

SCHEMA = "QUETTAFLOP/XOR-PAIR-NATIVE-REPORT/1"
STATIC_RUNTIME_FILES = {
    "runtime/number-raster.js", "runtime/individual-xpu.js", "runtime/recursive-xpu.js",
    "runtime/latest_10q_metrics.js", "runtime/rate-metrics.js", "runtime/flop-ledger.js",
    "runtime/full-domain.js", "runtime/full-viewer.js", "runtime/ram-bit-xpu.js",
    "runtime/virtual-worker-pool.js", "runtime/virtual-domain.js", "runtime/virtual-xpu.js",
}

def envelope(data):
    body = json.dumps(data, ensure_ascii=False, separators=(",", ":"), allow_nan=False)
    return {"schema": SCHEMA, "dataJson": body, "dataSha256": hashlib.sha256(body.encode("utf-8")).hexdigest()}

def normalize_config(value, seed_default=None):
    if not isinstance(value, dict):
        raise ValueError("Configuration must be an object.")
    allowed = {"budget_mib", "reserve_gib", "cpu_workers", "gpu_fraction", "seed", "duration_seconds", "epoch_interval_ms", "sample_count"}
    if set(value) - allowed:
        raise ValueError("Unknown XOR configuration field.")
    config = dict(value)
    budget = config.get("budget_mib", 0)
    if type(budget) is not int or budget < 0:
        raise ValueError("RAM budget must be integer MiB; 0 chooses currently available RAM.")
    config["auto_ram"] = budget == 0
    if budget == 0:
        config.pop("budget_mib", None)
    workers = config.get("cpu_workers", min(4, os.cpu_count() or 1))
    if type(workers) is not int or not 1 <= workers <= (os.cpu_count() or 1):
        raise ValueError("CPU worker count exceeds detected logical CPUs.")
    config["cpu_workers"] = workers
    reserve = config.get("reserve_gib", 2)
    if isinstance(reserve, bool) or not isinstance(reserve, (int, float)) or not 2 <= reserve <= 1024:
        raise ValueError("Reserve at least 2 GiB for the OS and running apps.")
    config["reserve_gib"] = reserve
    seed = config.get("seed", secrets.randbits(32) if seed_default is None else seed_default)
    if type(seed) is not int or not 0 <= seed <= 0xffffffff:
        raise ValueError("Seed must be an unsigned 32-bit integer.")
    config["seed"] = seed
    count = config.get("sample_count", 64)
    if type(count) is not int or not 2 <= count <= 1060:
        raise ValueError("Select between 2 and 1,060 samples per acknowledged epoch.")
    config["sample_count"] = count
    for field, default, low, high in (("duration_seconds", 2, 0.05, 60), ("epoch_interval_ms", 0, 0, 1000), ("gpu_fraction", .5, .01, .99)):
        value = config.get(field, default)
        if isinstance(value, bool) or not isinstance(value, (int, float)) or not low <= value <= high:
            raise ValueError(field + " is outside the bounded range.")
        config[field] = value
    return config

class Application:
    def __init__(self, seed_file=None):
        self.lock = threading.RLock()
        self.cancel = threading.Event()
        self.generation = 0
        self.busy = False
        self.phase = "idle"
        self.error = None
        self.hardware = None
        self.plan = None
        self.config = None
        self.selected_seed = None
        self.seed_file = Path(seed_file) if seed_file is not None else None
        if self.seed_file is not None:
            if self.seed_file.exists():
                try:
                    saved = json.loads(self.seed_file.read_text(encoding="utf-8"))
                    seed = saved.get("seed")
                    if saved.get("schema") != "XOR-PAIR-XPU/PRESERVED-SEED/1" or type(seed) is not int or not 0 <= seed <= 0xffffffff:
                        raise ValueError("Invalid preserved seed metadata.")
                    self.selected_seed = seed
                except (OSError, ValueError, AttributeError) as error:
                    raise ValueError("Cannot read the preserved seed file; its seed was not replaced: " + str(error)) from error
            self.preserve_seed(secrets.randbits(32) if self.selected_seed is None else self.selected_seed)
        self.session = None
        self.progress = None
        self.report = None
        self.samples = None
        self.thread = None

    def preserve_seed(self, value):
        if self.seed_file is not None:
            write_json_atomic(self.seed_file, {"schema": "XOR-PAIR-XPU/PRESERVED-SEED/1", "seed": value})
        self.selected_seed = value

    def state(self):
        with self.lock:
            return {"generation": self.generation, "busy": self.busy, "phase": self.phase,
                    "error": self.error, "hardware": self.hardware, "plan": self.plan,
                    "progress": self.progress, "report": self.report, "samples": self.samples, "config": self.config, "recordedSeed": self.selected_seed,
                    "backend": "actual native CPU + OpenCL GPU selected XOR samples", "nativeConnected": True}

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
                    raise ValueError("Release the previous RAM session before changing its plan.")
                config = normalize_config(payload.get("config", {}), self.selected_seed)
                planned = backend.plan_budget(config)
                self.config, self.plan, self.error = config, planned, None
                self.preserve_seed(config["seed"])
            return self.state()
        if command == "run":
            with self.lock:
                if self.busy:
                    raise ValueError("The XOR benchmark is already running.")
                config = normalize_config(payload.get("config", {}), self.selected_seed)
                if self.session is not None and config != self.config:
                    raise ValueError("Release RAM before changing the benchmark configuration.")
                self.cancel.clear()
                if self.session is None:
                    self.plan = backend.plan_budget(config)
                    self.config = config
                    self.preserve_seed(config["seed"])
                    token = self.generation
                    def progress(value):
                        with self.lock:
                            if token == self.generation:
                                self.progress = value
                                self.phase = str(value.get("phase", value.get("status", "running"))).lower()
                    self.session = backend.Session(self.plan, on_progress=progress, cancel_event=self.cancel)
                token, session = self.generation, self.session
                self.busy, self.phase, self.error = True, "starting", None
                self.samples = None
                def execute():
                    try:
                        session.execute(duration_seconds=config["duration_seconds"], epoch_interval_ms=config["epoch_interval_ms"])
                        report, progress_value = session.report(), session.snapshot()
                        with self.lock:
                            if token == self.generation:
                                self.report, self.progress = report, progress_value
                                self.phase = str(progress_value.get("status", "paused")).lower()
                                self.error = progress_value.get("error")
                    except Exception as exc:
                        with self.lock:
                            if token == self.generation:
                                self.error, self.phase = str(exc), "error"
                    finally:
                        with self.lock:
                            if token == self.generation:
                                self.busy = False
                self.thread = threading.Thread(target=execute, name="Contained-XOR-epoch-job", daemon=True)
                self.thread.start()
            return self.state()
        if command == "pause":
            self.cancel.set()
            return self.state()
        if command == "reset":
            with self.lock:
                if self.busy:
                    raise ValueError("Pause and wait for the current sample acknowledgments before releasing RAM.")
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
                    raise ValueError("Wait for the benchmark to pause before taking an inspection sample.")
                if self.session is None:
                    raise ValueError("No RAM-resident base state has been allocated.")
                ids = payload.get("bitIds")
                if ids is not None and (not isinstance(ids, list) or len(ids) > 1060 or any(not isinstance(v, (str, int)) or isinstance(v, bool) for v in ids)):
                    raise ValueError("Provide at most 1,060 integer bit addresses.")
                count = payload.get("count", 1060)
                if type(count) is not int or not 2 <= count <= 1060:
                    raise ValueError("Inspection sample count must be between 2 and 1,060.")
                self.samples = self.session.sample(bit_ids=ids, count=count)
                self.progress, self.report = self.session.snapshot(), self.session.report()
            return self.state()
        if command == "export":
            with self.lock:
                if self.busy:
                    raise ValueError("Wait for acknowledged samples before exporting the report.")
                if self.session is None or self.report is None:
                    raise ValueError("No measured native XOR benchmark report exists.")
                return envelope({"report": self.session.report(), "samples": self.samples,
                                 "plan": self.plan, "hardware": self.hardware})
        raise ValueError("Unknown contained XOR command.")

def create_server(viewer, seed_file=None):
    viewer = Path(viewer).resolve()
    if not viewer.is_file():
        raise ValueError("Viewer file does not exist.")
    root = viewer.parent
    application, token = Application(seed_file or root / "xor-seed-state.json"), secrets.token_urlsafe(32)
    allowed_pages = {viewer.name, "TENQ_AT_1MHZ_DASHBOARD.html", "BLACK_WHITE_XOR.html", "CPU_GPU_RAM_WAVE.html"}
    allowed_pages.update(p.name for p in root.glob("QuettaFLOP-XOR-Pair-XPU*.html"))
    allowed_pages.update(p.name for p in root.glob("OPEN_*.html"))
    allowed_pages.update({"RAM_BIT_EXECUTION.html", "PARALLEL_EXECUTION.html", "INDIVIDUAL_EXECUTION.html",
                          "FAST_FORWARD_REFERENCE.html", "VIRTUAL_DOMAIN_LEDGER.html", "FINITE_EXECUTION.html", "SAMPLING_LEDGER.html"})
    class Handler(BaseHTTPRequestHandler):
        def log_message(self, *_):
            pass
        def json_response(self, value, status=200):
            json_response(self, value, status)
        def permitted(self):
            return session_permitted(self, token, 'X-XOR-Token')
        def do_OPTIONS(self):
            self.json_response({"ok": False, "error": "Cross-origin access is disabled."}, 403)
        def do_POST(self):
            if not self.permitted():
                self.json_response({"ok": False, "error": "Native session token and same origin are required."}, 403)
                return
            command = urlsplit(self.path).path.removeprefix("/api/")
            if not self.path.startswith("/api/") or command not in {"status", "probe", "plan", "run", "pause", "sample", "reset", "export"}:
                self.json_response({"ok": False, "error": "Unknown contained XOR command."}, 404)
                return
            try:
                payload = read_json_body(self)
                if not isinstance(payload, dict) or set(payload) - {"config", "bitIds", "count"}:
                    raise ValueError("Unknown XOR request field.")
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
    parser.add_argument("--seed-file", type=Path, help="Optional local preserved-seed file; never served over HTTP.")
    args = parser.parse_args()
    work_root = Path(__file__).resolve().parent.parent
    viewer = args.viewer or work_root / "OPEN_SIMULATOR.html"
    if not viewer.is_file():
        viewer = next((p for p in [work_root / "TENQ_AT_1MHZ_DASHBOARD.html", work_root / "decoded/mvp/TENQ_AT_1MHZ_DASHBOARD.html"] if p.is_file()), viewer)
    server, application, token = create_server(viewer, args.seed_file)
    url = "http://127.0.0.1:" + str(server.server_port) + "/" + viewer.name + "#xor=" + token
    print("Contained CPU + GPU XOR server ready. Keep this launcher running.", flush=True)
    print(url, flush=True)
    if not args.no_browser:
        webbrowser.open(url)
    try:
        server.serve_forever(poll_interval=.2)
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
