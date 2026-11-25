"""Local AI service for the racing game: one JSON request per line in, one JSON reply per line out.

Unity's PythonAIClient calls this only at permitted boundaries and treats any timeout or invalid reply
as "keep the current approved setting". Replies are proposals; the game's FairnessGovernor decides.

Run:  python -m ai_service.server      (from the Python/ folder)
Listens on 127.0.0.1 only. Nothing is uploaded anywhere.
"""
import json
import math
import socketserver

SCHEMA = 1
SERVICE = "adaptive-racing-ai"
VERSION = "0.1.0"
HOST, PORT = "127.0.0.1", 47800


def error(code):
    return {"ok": False, "schema": SCHEMA, "error": code}


def is_unit(x):
    return isinstance(x, (int, float)) and not isinstance(x, bool) and math.isfinite(x) and 0.0 <= x <= 1.0


def recommend(msg):
    current = msg.get("current")
    if not is_unit(current):
        return error("bad_current")
    # Stub: hold the current setting with maximum uncertainty. The world model (M01-M03) + MPC replace this.
    return {"ok": True, "schema": SCHEMA, "difficulty": float(current), "uncertainty": 1.0,
            "model_version": "stub-0", "supported": False}


def handle(msg):
    if not isinstance(msg, dict):
        return error("bad_request")
    if msg.get("schema") != SCHEMA:
        return error("schema_mismatch")
    kind = msg.get("type")
    if kind == "ping":
        return {"ok": True, "schema": SCHEMA, "service": SERVICE, "version": VERSION}
    if kind == "recommend_difficulty":
        return recommend(msg)
    return error("unknown_type")


def handle_line(line):
    try:
        msg = json.loads(line)
    except (ValueError, UnicodeDecodeError):
        reply = error("bad_json")
    else:
        reply = handle(msg)
    return (json.dumps(reply) + "\n").encode("utf-8")


class Handler(socketserver.StreamRequestHandler):
    def handle(self):
        for line in self.rfile:
            self.wfile.write(handle_line(line))


def make_server(host=HOST, port=PORT):
    socketserver.ThreadingTCPServer.allow_reuse_address = True
    return socketserver.ThreadingTCPServer((host, port), Handler)


if __name__ == "__main__":
    with make_server() as server:
        print(f"{SERVICE} {VERSION} listening on {HOST}:{PORT}")
        server.serve_forever()
