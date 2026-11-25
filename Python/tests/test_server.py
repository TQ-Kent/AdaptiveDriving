import json
import os
import socket
import sys
import threading
import unittest

sys.path.insert(0, os.path.join(os.path.dirname(__file__), ".."))
from ai_service import server  # noqa: E402


def reply(line):
    return json.loads(server.handle_line(line.encode("utf-8")))


class HandleTests(unittest.TestCase):
    def test_ping(self):
        r = reply('{"schema": 1, "type": "ping"}')
        self.assertTrue(r["ok"])
        self.assertEqual(r["schema"], server.SCHEMA)

    def test_malformed_json(self):
        self.assertEqual(reply("{not json")["error"], "bad_json")

    def test_schema_mismatch(self):
        self.assertEqual(reply('{"schema": 99, "type": "ping"}')["error"], "schema_mismatch")

    def test_stub_recommendation_holds_current(self):
        r = reply('{"schema": 1, "type": "recommend_difficulty", "current": 0.4, "laps": []}')
        self.assertTrue(r["ok"])
        self.assertEqual(r["difficulty"], 0.4)
        self.assertFalse(r["supported"])

    def test_rejects_bad_current(self):
        for bad in ("NaN", "1.5", "-0.1", "true", '"x"'):
            r = reply('{"schema": 1, "type": "recommend_difficulty", "current": %s}' % bad)
            self.assertEqual(r["error"], "bad_current", bad)


class SocketTest(unittest.TestCase):
    def test_round_trip(self):
        srv = server.make_server(port=0)
        threading.Thread(target=srv.serve_forever, daemon=True).start()
        try:
            with socket.create_connection(srv.server_address, timeout=2) as s:
                s.sendall(b'{"schema": 1, "type": "ping"}\n')
                line = s.makefile("rb").readline()
            self.assertTrue(json.loads(line)["ok"])
        finally:
            srv.shutdown()
            srv.server_close()


if __name__ == "__main__":
    unittest.main()
