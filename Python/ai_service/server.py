"""Local AI service for the racing game: one JSON request per line in, one JSON reply per line out.

Unity's PythonAIClient calls this only at permitted boundaries and treats any timeout or invalid reply
as "keep the current approved setting". Replies are proposals; the game's FairnessGovernor decides.

Upgraded for v1.2 Phases 4-5:
- Loads approved M01 (3-MLP pace ensemble) + M02 (calibrated AI pace table) + M03 (analytical transition) + MPC
  from Python/artifacts/ (falling back safely to hold/stub when no clean laps or artifacts exist).
- Also supports `predict_tactical_response` backed by the Phase 4 ONNX tactical response MLP.

Run:  python -m ai_service.server      (from the Python/ folder)
Listens on 127.0.0.1 only. Nothing is uploaded anywhere.
"""
import json
import math
import os
import socketserver

import numpy as np

SCHEMA = 1
SERVICE = "adaptive-racing-ai"
VERSION = "1.2.0"
HOST, PORT = "127.0.0.1", 47800

_ARTIFACTS_DIR = os.path.join(os.path.dirname(os.path.dirname(os.path.abspath(__file__))), "artifacts")
_RUNTIME_CACHE = None


def _load_runtime():
    global _RUNTIME_CACHE
    if _RUNTIME_CACHE is not None:
        return _RUNTIME_CACHE
    rt = {"m01": None, "m02": None, "tactical": None}
    try:
        import onnxruntime as ort
        m02_path = os.path.join(_ARTIFACTS_DIR, "m02_pace_table.json")
        if os.path.exists(m02_path):
            with open(m02_path, encoding="utf-8") as f:
                rt["m02"] = json.load(f)

        m01_path = os.path.join(_ARTIFACTS_DIR, "m01_pace_ensemble_manifest.json")
        if os.path.exists(m01_path):
            with open(m01_path, encoding="utf-8") as f:
                m = json.load(f)
            sessions = [
                ort.InferenceSession(os.path.join(_ARTIFACTS_DIR, mem["onnx_file"]), providers=["CPUExecutionProvider"])
                for mem in m["members"]
            ]
            rt["m01"] = (m, sessions)

        tac_path = os.path.join(_ARTIFACTS_DIR, "tactical_response_manifest.json")
        if os.path.exists(tac_path):
            with open(tac_path, encoding="utf-8") as f:
                tm = json.load(f)
            tsess = ort.InferenceSession(os.path.join(_ARTIFACTS_DIR, tm["members"][0]["onnx_file"]), providers=["CPUExecutionProvider"])
            rt["tactical"] = (tm, tsess)
    except Exception:
        pass
    _RUNTIME_CACHE = rt
    return rt


def error(code):
    return {"ok": False, "schema": SCHEMA, "error": code}


def is_unit(x):
    return isinstance(x, (int, float)) and not isinstance(x, bool) and math.isfinite(x) and 0.0 <= x <= 1.0


def _query_m02(m02, track_id, d, traffic=False):
    mode = "traffic" if traffic else "clean_air"
    table = next((t for t in m02["tables"] if t["track_id"] == track_id and t["traffic_mode"] == mode), None)
    if table is None:
        return None, 43.0, False
    xs = [e["difficulty"] for e in table["entries"]]
    ys = [e["expected_lap_s"] for e in table["entries"]]
    return float(np.interp(d, xs, ys)), float(table["reference_lap_s"]), True


def recommend(msg):
    current = msg.get("current")
    if not is_unit(current):
        return error("bad_current")
    laps = msg.get("laps") or []
    clean_times = []
    for l in laps:
        if not isinstance(l, dict):
            continue
        if l.get("mixedDifficulty") or l.get("incident") or l.get("standingStart"):
            continue
        t = l.get("lapTimeS")
        if isinstance(t, (int, float)) and math.isfinite(t) and t > 5.0:
            clean_times.append(float(t))

    rt = _load_runtime()
    if not clean_times or rt["m01"] is None or rt["m02"] is None:
        # Hold current setting with maximum uncertainty when no clean laps or models are unavailable.
        return {"ok": True, "schema": SCHEMA, "difficulty": float(current), "uncertainty": 1.0,
                "model_version": "stub-0" if rt["m01"] is None else "hold-cold-start", "supported": False}

    track_id = msg.get("track_id", "MonzaScaled")
    _, ref_s, sup_m02 = _query_m02(rt["m02"], track_id, float(current), False)
    if not sup_m02:
        return {"ok": True, "schema": SCHEMA, "difficulty": float(current), "uncertainty": 1.0,
                "model_version": "hold-unsupported-track", "supported": False}

    m_man, sessions = rt["m01"]
    norms = [t / ref_s for t in reversed(clean_times[-5:])]
    recent = [norms[i] if i < len(norms) else 0.0 for i in range(5)]
    masks = [1.0 if i < len(norms) else 0.0 for i in range(5)]
    n_eff = len(norms)
    rolling = (n_eff / (n_eff + 3.0)) * float(np.mean(norms)) + (3.0 / (n_eff + 3.0)) * 1.10
    trend = (norms[0] - norms[-1]) if n_eff >= 2 else 0.0
    var_n = float(np.std(norms)) if n_eff >= 2 else 0.03
    feat = np.array([*recent, *masks, rolling, np.clip(trend, -0.25, 0.25), np.clip(var_n, 0.0, 0.30),
                     min(1.0, n_eff / 20.0), 0.0, 0.0], dtype=np.float32)

    mean_f = np.asarray(m_man["feature_mean"], dtype=np.float32)
    std_f = np.asarray(m_man["feature_std"], dtype=np.float32)
    z = np.clip((feat - mean_f) / np.maximum(1e-6, std_f), -8.0, 8.0).astype(np.float32)[None, :]
    t_mean, t_std = float(m_man["target_mean"]), float(m_man["target_std"])

    m_means, m_vars = [], []
    for s in sessions:
        o = s.run(None, {s.get_inputs()[0].name: z})[0][0]
        m_means.append(float(o[0]) * t_std + t_mean)
        m_vars.append(math.exp(float(np.clip(o[1], -9.0, -1.0))))
    pred_player_s = float(np.clip(np.mean(m_means), 0.70, 1.60)) * ref_s
    disagree = float(np.std(m_means))

    # Short-horizon MPC search over difficulty grid [0..1]
    best_d, best_j = float(current), float("inf")
    for d in np.linspace(0.0, 1.0, 51):
        ai_s, _, _ = _query_m02(rt["m02"], track_id, float(d), False)
        band_err = max(0.0, abs(ai_s - pred_player_s) - 0.25)
        j = band_err ** 2 + 8.0 * ((float(d) - float(current)) ** 2)
        if j < best_j:
            best_j, best_d = j, float(d)

    return {
        "ok": True,
        "schema": SCHEMA,
        "difficulty": round(best_d, 4),
        "uncertainty": round(disagree, 5),
        "model_version": f"{m_man['model_name']}-{m_man['model_version']}",
        "supported": True,
    }


def predict_tactical(msg):
    feats = msg.get("features")
    if not isinstance(feats, list) or len(feats) != 22 or not all(isinstance(v, (int, float)) and math.isfinite(v) for v in feats):
        return error("bad_features")
    rt = _load_runtime()
    if rt["tactical"] is None:
        return error("model_unavailable")
    tm, sess = rt["tactical"]
    x = np.asarray(feats, dtype=np.float32)
    z = np.clip((x - np.asarray(tm["feature_mean"], dtype=np.float32)) / np.maximum(1e-6, np.asarray(tm["feature_std"], dtype=np.float32)), -8.0, 8.0)[None, :]
    logits = sess.run(None, {sess.get_inputs()[0].name: z})[0][0] / float(tm["temperature"])
    logits -= np.max(logits)
    p = np.exp(logits)
    p /= p.sum()
    return {
        "ok": True,
        "schema": SCHEMA,
        "probabilities": [round(float(v), 6) for v in p],
        "model_version": tm["model_version"],
        "supported": True,
    }


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
    if kind == "predict_tactical_response":
        return predict_tactical(msg)
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
