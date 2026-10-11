"""Bounded HTTP transport for the optional local native viewers."""
from __future__ import annotations

import hmac
import json
import math
import os
import secrets
import socket
import threading
import time
from http.server import ThreadingHTTPServer
from pathlib import Path


class BoundedThreadingHTTPServer(ThreadingHTTPServer):
    daemon_threads = True
    request_queue_size = 16

    def __init__(self, *args, **kwargs):
        self._slots = threading.BoundedSemaphore(16)
        super().__init__(*args, **kwargs)

    def get_request(self):
        request, address = super().get_request()
        request.settimeout(3)
        return request, address

    def process_request(self, request, address):
        if not self._slots.acquire(blocking=False):
            self.shutdown_request(request)
            return
        try:
            super().process_request(request, address)
        except BaseException:
            self._slots.release()
            raise

    def process_request_thread(self, request, address):
        try:
            super().process_request_thread(request, address)
        finally:
            self._slots.release()


def single_header(handler, name):
    values = handler.headers.get_all(name, [])
    return values[0] if len(values) == 1 else None


def host_permitted(handler):
    return single_header(handler, "Host") == "127.0.0.1:" + str(handler.server.server_port)


def session_permitted(handler, token, token_header):
    origin = "http://127.0.0.1:" + str(handler.server.server_port)
    supplied = single_header(handler, token_header)
    return (host_permitted(handler) and single_header(handler, "Origin") == origin
            and isinstance(supplied, str) and len(supplied) == len(token)
            and hmac.compare_digest(supplied.encode("utf-8"), token.encode("ascii")))


def _reject_constant(value):
    raise ValueError("JSON numbers must be finite: " + value)


def _finite_float(value):
    result = float(value)
    if not math.isfinite(result):
        raise ValueError("JSON number is outside the finite range.")
    return result


def _unique_object(items):
    result = {}
    for key, value in items:
        if key in result:
            raise ValueError("Duplicate JSON field: " + key)
        result[key] = value
    return result


def read_json_body(handler):
    handler._spindoctor_body_read = 0
    if handler.headers.get_all("Transfer-Encoding", []):
        raise ValueError("Transfer-Encoding is unsupported.")
    raw_length = single_header(handler, "Content-Length")
    if raw_length is None or not raw_length.isascii() or not raw_length.isdecimal():
        raise ValueError("One decimal Content-Length is required.")
    if len(raw_length) > 6:
        raise ValueError("Request exceeds the bounded command size.")
    length = int(raw_length)
    if length > 65536:
        raise ValueError("Request exceeds the bounded command size.")
    content_type = single_header(handler, "Content-Type")
    if content_type is None or content_type.split(";", 1)[0].strip().lower() != "application/json":
        raise ValueError("Content-Type must be application/json.")
    body = handler.rfile.read(length)
    handler._spindoctor_body_read = len(body)
    if len(body) != length:
        raise ValueError("Truncated command body.")
    return json.loads(body.decode("utf-8") or "{}", parse_constant=_reject_constant,
                      parse_float=_finite_float, object_pairs_hook=_unique_object)


def discard_rejected_body(handler, limit=65537, deadline_seconds=0.25):
    """Drain only a small bounded body before closing an error response.

    Windows can reset a connection closed with unread inbound data. Discarding
    already-sent request bytes makes legitimate rejection responses readable,
    without parsing unauthenticated JSON or trusting attacker framing. Large or
    stalled senders cannot occupy a server slot beyond the short total deadline.
    """
    lengths = handler.headers.get_all("Content-Length", [])
    known = (lengths and len(set(lengths)) == 1 and len(lengths[0]) <= 6
             and lengths[0].isascii() and lengths[0].isdecimal()
             and not handler.headers.get_all("Transfer-Encoding", []))
    consumed = getattr(handler, "_spindoctor_body_read", 0)
    remaining = min(max(int(lengths[0]) - consumed, 0), limit) if known else limit
    previous_timeout = handler.connection.gettimeout()
    deadline = time.monotonic() + deadline_seconds
    try:
        while remaining:
            left = deadline - time.monotonic()
            if left <= 0:
                break
            handler.connection.settimeout(left)
            chunk = handler.rfile.read1(min(4096, remaining))
            if not chunk:
                break
            remaining -= len(chunk)
    except (OSError, ValueError):
        pass
    finally:
        handler.connection.settimeout(previous_timeout)


def json_response(handler, value, status=200):
    body = json.dumps(value, ensure_ascii=False, allow_nan=False).encode("utf-8")
    rejected = status >= 400
    if rejected:
        handler.close_connection = True
        discard_rejected_body(handler)
    handler.send_response(status)
    handler.send_header("Content-Type", "application/json; charset=utf-8")
    handler.send_header("Cache-Control", "no-store")
    handler.send_header("X-Content-Type-Options", "nosniff")
    security_headers(handler)
    handler.send_header("Content-Length", str(len(body)))
    if rejected:
        handler.send_header("Connection", "close")
    handler.end_headers()
    handler.wfile.write(body)
    if rejected:
        handler.wfile.flush()
        try:
            handler.connection.shutdown(socket.SHUT_WR)
        except OSError:
            pass


def read_static(root, relative):
    root = Path(root).resolve()
    target = (root / relative).resolve()
    try:
        target.relative_to(root)
    except ValueError:
        raise ValueError("Static file escaped viewer root.") from None
    with target.open("rb") as source:
        body = source.read(8 * 1024 * 1024 + 1)
    if len(body) > 8 * 1024 * 1024:
        raise ValueError("Static file exceeds bounded viewer size.")
    return body


def security_headers(handler):
    handler.send_header("X-Frame-Options", "DENY")
    handler.send_header("Referrer-Policy", "no-referrer")
    handler.send_header("Content-Security-Policy", "default-src 'self'; script-src 'self' 'unsafe-inline'; "
                        "style-src 'self' 'unsafe-inline'; connect-src 'self'; worker-src 'self' blob:; "
                        "img-src 'self' data: blob:; media-src 'self' data: blob:; "
                        "object-src 'none'; base-uri 'none'; frame-ancestors 'none'")


def write_json_atomic(path, value):
    """Replace a seed snapshot without truncating its previous contents."""
    path = Path(path)
    pending = path.with_name(path.name + ".tmp-" + secrets.token_hex(16))
    try:
        with pending.open("x", encoding="utf-8") as stream:
            json.dump(value, stream, ensure_ascii=False, allow_nan=False, indent=2)
            stream.write("\n")
            stream.flush()
            os.fsync(stream.fileno())
        os.replace(pending, path)
    finally:
        if pending.exists():
            pending.unlink()
