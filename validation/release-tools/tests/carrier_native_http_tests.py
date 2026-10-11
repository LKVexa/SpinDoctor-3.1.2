"""Regression tests against live optional native loopback servers."""
import contextlib
import http.client
import io
import json
import os
import socket
import sys
import tempfile
import threading
import time
import unittest
from pathlib import Path
from unittest.mock import patch

if os.environ.get("SPINDOCTOR_NATIVE_ROOT"):
    NATIVE = Path(os.environ["SPINDOCTOR_NATIVE_ROOT"]).resolve()
else:
    candidates = [candidate for parent in Path(__file__).resolve().parents
                  for candidate in (parent / "native", parent / "SpinDoctor" / "native",
                                    parent / "work" / "mvp" / "mvp" / "native")]
    NATIVE = next((candidate for candidate in candidates if (candidate / "loopback_http.py").is_file()), None)
    if NATIVE is None:
        raise RuntimeError("Set SPINDOCTOR_NATIVE_ROOT to this release's native source folder.")
sys.path.insert(0, str(NATIVE))
import xpu_server
import xor_server
import xor_reference_server
from loopback_http import read_json_body, read_static, write_json_atomic


class NativeHttpTests(unittest.TestCase):
    def test_servers_accept_same_origin_and_reject_hostile_requests(self):
        for module in (xpu_server, xor_server, xor_reference_server):
            with self.subTest(module=module.__name__), tempfile.TemporaryDirectory(dir=Path(__file__).resolve().parent) as temp:
                root = Path(temp)
                viewer = root / "viewer.html"
                viewer.write_text("<html>local viewer</html>", encoding="utf-8")
                server, application, token = module.create_server(viewer)
                host = "127.0.0.1:" + str(server.server_port)
                token_header = "X-XPU-Token" if module is xpu_server else "X-XOR-Token"
                headers = {"Host": host, "Origin": "http://" + host,
                           token_header: token, "Content-Type": "application/json"}
                worker = threading.Thread(target=server.serve_forever, daemon=True)
                worker.start()
                try:
                    def request(method="POST", url="/api/status", body="{}", changed=None):
                        conn = http.client.HTTPConnection("127.0.0.1", server.server_port, timeout=5)
                        conn.request(method, url, body=body, headers={**headers, **(changed or {})})
                        response = conn.getresponse()
                        result = (response.status, response.read(), dict(response.getheaders()))
                        conn.close()
                        return result
                    status, body, returned = request()
                    self.assertEqual(status, 200)
                    self.assertTrue(json.loads(body)["ok"])
                    self.assertEqual(returned["X-Frame-Options"], "DENY")
                    self.assertEqual(request(changed={"Origin": "https://attacker.invalid"})[0], 403)
                    self.assertEqual(request(changed={"Host": "attacker.invalid"})[0], 403)
                    self.assertEqual(request(changed={token_header: "wrong"})[0], 403)
                    self.assertEqual(request(url="/api/unknown")[0], 404)
                    self.assertEqual(request(url="/api/unknown", body=" " * 65537)[0], 404)
                    rejected = request(body=" " * 65537, changed={token_header: "wrong"})
                    self.assertEqual(rejected[0], 403)
                    self.assertEqual(rejected[2]["Connection"], "close")
                    for hostile in ('{"config":{"reserve_gib":NaN}}', '{"config":{"reserve_gib":1e999}}',
                                    '{"config":{},"config":{}}', '[]'):
                        self.assertEqual(request(body=hostile)[0], 400, hostile)
                    self.assertEqual(request(body=" " * 65537)[0], 400)
                    self.assertEqual(request(changed={"Content-Type": "text/plain"})[0], 400)
                    self.assertEqual(request(method="GET", url="/viewer.html", body=None)[0], 200)
                    self.assertEqual(request(method="GET", url="/../native/xpu_server.py", body=None)[0], 404)
                    self.assertEqual(request(method="GET", url="/xor-seed-state.json", body=None)[0], 404)
                    conn = socket.create_connection(("127.0.0.1", server.server_port), timeout=5)
                    wire = ("POST /api/status HTTP/1.1\r\nHost: " + host + "\r\nOrigin: http://" + host +
                            "\r\n" + token_header + ": " + token + "\r\nContent-Type: application/json\r\n" +
                            "Content-Length: 2\r\nContent-Length: 2\r\n\r\n{}")
                    conn.sendall(wire.encode("ascii"))
                    self.assertIn(b"400", conn.recv(4096).split(b"\r\n", 1)[0])
                    conn.close()
                    for url, token_value, media, expected in (
                            ("/api/unknown", token, "application/json", 404),
                            ("/api/status", "wrong", "application/json", 403),
                            ("/api/status", token, "text/plain", 400)):
                        conn = socket.create_connection(("127.0.0.1", server.server_port), timeout=2)
                        wire = ("POST " + url + " HTTP/1.1\r\nHost: " + host + "\r\nOrigin: http://" + host +
                                "\r\n" + token_header + ": " + token_value + "\r\nContent-Type: " + media +
                                "\r\nContent-Length: 2\r\nConnection: keep-alive\r\n\r\n{")
                        started = time.monotonic()
                        conn.sendall(wire.encode("ascii"))
                        received = conn.recv(4096)
                        self.assertIn(str(expected).encode("ascii"), received.split(b"\r\n", 1)[0])
                        self.assertLess(time.monotonic() - started, 1.5, "Stalled rejected bodies must not hold a worker.")
                        conn.close()
                finally:
                    server.shutdown()
                    worker.join(5)
                    server.server_close()

    def test_read_json_rejects_framing_and_truncation(self):
        from email.message import Message
        class Fake:
            pass
        for extra, body in (([("Transfer-Encoding", "chunked")], b"{}"), ([], b"{")):
            fake = Fake()
            fake.headers = Message()
            for name, value in [("Content-Length", "2"), ("Content-Type", "application/json")] + extra:
                fake.headers[name] = value
            fake.rfile = io.BytesIO(body)
            with self.assertRaises(ValueError):
                read_json_body(fake)

    def test_configuration_rejected_before_hardware_probe(self):
        for bad in ({"seed": -1}, {"gpu_fraction": float("inf")}, {"deadline_ms": float("nan")},
                    {"a": 2}, {"read_depth": 17}, {"sample_count": 1061}):
            with self.assertRaises(ValueError):
                xpu_server.normalize_config(bad)
        with patch.object(xpu_server.os, "cpu_count", return_value=128):
            self.assertEqual(xpu_server.normalize_config({})["cpu_workers"], 64)
        for module in (xor_server, xor_reference_server):
            with patch.object(module.os, "cpu_count", return_value=1):
                self.assertEqual(module.normalize_config({}, 1)["cpu_workers"], 1)

    def test_static_path_remains_inside_root_and_bounded(self):
        with tempfile.TemporaryDirectory(dir=Path(__file__).resolve().parent) as temp:
            root = Path(temp) / "root"
            root.mkdir()
            with self.assertRaises(ValueError):
                read_static(root, "../outside")
            large = root / "large.js"
            with large.open("wb") as stream:
                stream.truncate(8 * 1024 * 1024 + 1)
            with self.assertRaises(ValueError):
                read_static(root, "large.js")

    def test_seed_replace_failure_preserves_previous_snapshot(self):
        with tempfile.TemporaryDirectory(dir=Path(__file__).resolve().parent) as temp:
            seed = Path(temp) / "seed.json"
            write_json_atomic(seed, {"seed": 1})
            with patch("loopback_http.os.replace", side_effect=OSError("publication failure")):
                with self.assertRaises(OSError):
                    write_json_atomic(seed, {"seed": 2})
            self.assertEqual(json.loads(seed.read_text(encoding="utf-8")), {"seed": 1})
            self.assertEqual(list(Path(temp).glob("*.tmp-*")), [])


if __name__ == "__main__":
    unittest.main(verbosity=2)
