"""Diagnose the player's recorded laps (for tuning the AI and checking the car physics; the AI never copies them).

Reads Unity/Logs/player_input.csv (20 Hz human rows written by RaceLog) and, if present, ai_telemetry.csv.
Picks the player's fastest complete lap, splits it into corners at each throttle lift, and prints per corner:
lift point, brake use, minimum speed, apex line, throttle-back point and slide (slip angle), next to the AI's
minimum speed there, then a physics summary (0-100 / 0-200 km/h, top speed, peak braking and cornering g) to
compare with the provisional F1 targets. Local files only; nothing is uploaded.

Usage: python evaluation/analyze_player.py [logs_dir]
"""
import csv
import os
import statistics
import sys

G = 9.81
WINDOW_S = 0.25                    # finite-difference window for braking g (20 Hz rows are noisy)
LIFT_BELOW, LIFT_FROM = 0.2, 0.5   # throttle drops from >= 0.5 to < 0.2: a corner starts
BACK_ON = 0.8                      # throttle back above 0.8: the corner ends


def read_rows(path):
    with open(path, newline="") as f:
        return list(csv.DictReader(f))


def laps_of(rows, car):
    """{lap: [row]} for complete laps of one car (rows must span the whole lap)."""
    laps = {}
    for r in rows:
        if r["car"] == car:
            laps.setdefault(int(r["lap"]), []).append(r)
    if not laps:
        return {}
    length = max(float(r["lap_dist_m"]) for rs in laps.values() for r in rs)
    # Starts at the line and ends near it. Lap 1 starts on the grid behind the line, so it drops out.
    return {lap: rs for lap, rs in laps.items()
            if float(rs[0]["lap_dist_m"]) < 30 and float(rs[-1]["lap_dist_m"]) > length - 50}


def laps_by_number(rows, car):
    laps = {}
    for r in rows:
        if r["car"] == car:
            laps.setdefault(int(r["lap"]), []).append(r)
    return laps


def lap_summary(lap, rs):
    """Share of samples past the white line, braking, sliding (>15 deg) and going backwards (crash/spin)."""
    n = len(rs)
    off = sum(r["on_track"] == "0" for r in rs) / n
    brake = sum(float(r["brake"]) > 0.05 for r in rs) / n
    drift = sum(abs(float(r["slip_deg"])) > 15 and float(r["speed_ms"]) > 15 for r in rs) / n
    backwards = sum(float(r["speed_ms"]) < -2 for r in rs)
    return (f"lap {lap}: {n / 20:.1f} s  past line {off:.0%}  braking {brake:.0%}  "
            f"sliding {drift:.0%}  backwards samples {backwards}")


def lap_time(rs):
    return float(rs[-1]["t"]) - float(rs[0]["t"])


def fastest_lap(rows, car):
    laps = laps_of(rows, car)
    return min(laps.items(), key=lambda kv: lap_time(kv[1])) if laps else (None, None)


def physics_summary(rows):
    """Launch times from the first standing start, top speed, and peak braking / cornering g over all rows."""
    t = [float(r["t"]) for r in rows]
    v = [float(r["speed_ms"]) for r in rows]
    out = {"top_kmh": max(v) * 3.6}
    start = next((i for i, s in enumerate(v) if s > 0.5), None)
    for kmh in (100, 200):
        hit = next((i for i in range(start or 0, len(v)) if v[i] * 3.6 >= kmh), None) if start is not None else None
        out[f"t0_{kmh}_s"] = t[hit] - t[start] if hit is not None else None
    w = max(1, round(WINDOW_S / 0.05))
    out["brake_g"] = max(((v[i] - v[i + w]) / (t[i + w] - t[i]) / G for i in range(len(v) - w) if t[i + w] > t[i]), default=0.0)
    out["lateral_g"] = max((abs(float(r["speed_ms"]) * float(r["yaw_rate_dps"])) * 3.14159265 / 180 / G for r in rows), default=0.0)
    return out


def corners(rs):
    """Corners from the throttle trace: one per lift, ending when the throttle is back on."""
    found, cur, prev = [], None, 1.0
    for r in rs:
        thr = float(r["throttle"])
        if cur is None and prev >= LIFT_FROM and thr < LIFT_BELOW:
            cur = [r]
        elif cur is not None:
            cur.append(r)
            if thr > BACK_ON:
                found.append(cur)
                cur = None
        prev = thr
    out = []
    for c in found:
        apex = min(c, key=lambda r: float(r["speed_ms"]))
        out.append({
            "lift_m": float(c[0]["lap_dist_m"]),
            "entry_ms": float(c[0]["speed_ms"]),
            "braked": any(float(r["brake"]) > 0.05 for r in c),
            "apex_m": float(apex["lap_dist_m"]),
            "min_ms": float(apex["speed_ms"]),
            "apex_lateral_m": float(apex["lateral_m"]),
            "throttle_on_m": float(c[-1]["lap_dist_m"]),
            "max_slip_deg": max(abs(float(r["slip_deg"])) for r in c),
            "off_track": any(r["on_track"] == "0" for r in c),
        })
    return out


def ai_min_speed(ai_rows, lo, hi):
    v = [float(r["speed_ms"]) for r in ai_rows if lo <= float(r["lap_dist_m"]) <= hi]
    return min(v) if v else None


def analyze(logs_dir):
    player_rows = read_rows(os.path.join(logs_dir, "player_input.csv"))
    if not player_rows:
        print("player_input.csv is empty: race once with the new build first.")
        return None
    car = player_rows[0]["car"]
    lap, rs = fastest_lap(player_rows, car)
    if rs is None:
        print("No complete player lap recorded.")
        return None
    for n, lrs in sorted(laps_by_number(player_rows, car).items()):
        print(lap_summary(n, lrs))
    print(f"{car}: fastest complete lap {lap} in {lap_time(rs):.2f} s ({len(rs)} samples)")

    ai_rows = []
    ai_path = os.path.join(logs_dir, "ai_telemetry.csv")
    if os.path.exists(ai_path):
        rows = [r for r in read_rows(ai_path) if r["event"] != "human"]
        best = None
        for name in sorted({r["car"] for r in rows}):
            l, ars = fastest_lap(rows, name)
            if ars and (best is None or lap_time(ars) < lap_time(best[1])):
                best = (name, ars)
        if best:
            ai_rows = best[1]
            print(f"AI reference: {best[0]} best lap {lap_time(ai_rows):.2f} s (4 Hz)")

    cs = corners(rs)
    print(f"\n{'lift m':>7} {'entry':>6} {'brake':>5} {'apex m':>7} {'min':>5} {'AI min':>6} {'line':>6} {'thr on':>7} {'slip':>5}")
    for c in cs:
        ai = ai_min_speed(ai_rows, c["lift_m"], c["throttle_on_m"]) if ai_rows else None
        print(f"{c['lift_m']:7.0f} {c['entry_ms']:6.1f} {'yes' if c['braked'] else 'no':>5} {c['apex_m']:7.0f} {c['min_ms']:5.1f} "
              f"{(f'{ai:6.1f}' if ai is not None else '     -')} {c['apex_lateral_m']:+6.1f} {c['throttle_on_m']:7.0f} "
              f"{c['max_slip_deg']:5.1f}{'  OFF' if c['off_track'] else ''}")

    p = physics_summary(player_rows)
    fmt = lambda x: f"{x:.2f} s" if x is not None else "not reached"
    print(f"\nphysics: 0-100 {fmt(p['t0_100_s'])}  0-200 {fmt(p['t0_200_s'])}  top {p['top_kmh']:.0f} km/h  "
          f"peak braking {p['brake_g']:.1f} g  peak cornering {p['lateral_g']:.1f} g")
    return {"corners": cs, "physics": p}


if __name__ == "__main__":
    here = os.path.dirname(os.path.abspath(__file__))
    analyze(sys.argv[1] if len(sys.argv) > 1 else os.path.join(here, "..", "..", "Unity", "Logs"))
