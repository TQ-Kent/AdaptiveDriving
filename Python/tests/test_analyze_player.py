import csv
import os
import sys
import tempfile
import unittest

sys.path.insert(0, os.path.join(os.path.dirname(__file__), ".."))
from evaluation import analyze_player  # noqa: E402

HEADER = "t,car,lap,lap_dist_m,lateral_m,speed_ms,steer,throttle,brake,handbrake,drs,gear,yaw_rate_dps,slip_deg,on_track".split(",")


def lap_rows(lap, t0, slow_corner):
    """One 500 m lap at 20 Hz: full throttle, one lift at 200 m, apex at 250 m, back on at 300 m."""
    rows, t, d = [], t0, 0.0
    while d < 500:
        in_corner = 200 <= d < 300
        speed = (slow_corner if abs(d - 250) < 10 else 30.0) if in_corner else 50.0
        rows.append(dict(zip(HEADER, [f"{t:.3f}", "Me", lap, f"{d:.1f}", "-4.0" if abs(d - 250) < 10 else "0.0",
                                      f"{speed}", "0.5" if in_corner else "0", "0.0" if in_corner else "1.0", "0", "0", "0",
                                      "5", "0", "8.0" if in_corner else "0", "1"])))
        t += 0.05
        d += speed * 0.05
    return rows


class AnalyzePlayerTests(unittest.TestCase):
    def test_corner_and_fastest_lap(self):
        with tempfile.TemporaryDirectory() as tmp:
            grid = lap_rows(1, 0.0, 20.0)
            for r in grid:
                r["lap_dist_m"] = f"{float(r['lap_dist_m']) + 470 if float(r['lap_dist_m']) < 30 else float(r['lap_dist_m']):.1f}"
            rows = grid + lap_rows(2, 100.0, 12.0) + lap_rows(3, 200.0, 20.0)
            with open(os.path.join(tmp, "player_input.csv"), "w", newline="") as f:
                w = csv.DictWriter(f, HEADER)
                w.writeheader()
                w.writerows(rows)
            result = analyze_player.analyze(tmp)
            cs = result["corners"]
            self.assertEqual(len(cs), 1)                          # one lift -> one corner
            c = cs[0]
            self.assertAlmostEqual(c["min_ms"], 20.0)             # lap 3 (faster corner) chosen over lap 2
            self.assertAlmostEqual(c["apex_lateral_m"], -4.0)
            self.assertFalse(c["braked"])
            self.assertTrue(200 <= c["lift_m"] < 210 and 300 <= c["throttle_on_m"] < 310)
            self.assertFalse(os.path.exists(os.path.join(tmp, "player_reference.csv")))  # the AI no longer copies the player
            p = result["physics"]
            self.assertAlmostEqual(p["top_kmh"], 180.0)
            self.assertIsNone(p["t0_200_s"])
            self.assertGreater(p["brake_g"], 0.5)  # 50 -> 30 m/s in one row is a big deceleration


if __name__ == "__main__":
    unittest.main()
