"""Comprehensive tests for Phases 2, 3, 4, 5, 6 (Tactical World Model, Style, Neural Response, M01-M03, Release Gate, Evaluation)."""
import json
import os
import sys
import tempfile
import unittest

import numpy as np

sys.path.insert(0, os.path.join(os.path.dirname(__file__), ".."))

from ai_service import server  # noqa: E402
from training.calibrate_m02 import calibrate_m02_tables, query_m02_table  # noqa: E402
from training.dataset_builder import build_pace_dataset, build_tactical_dataset, group_train_val_split  # noqa: E402
from training.release_gate import run_full_training_and_release_gate  # noqa: E402
from training.track_kinematics import CIRCUITS, rollout_duel_branch_outcome, template_response_probs  # noqa: E402
from evaluation.evaluate_all import OnnxM01Ensemble, OnnxTacticalPredictor, evaluate_hairpin_style_demo  # noqa: E402


class Phase2To6Tests(unittest.TestCase):
    def test_t04_group_split_has_zero_run_leakage(self):
        ds = build_tactical_dataset(seed=1, encounters_per_family=200)
        tr, va, ho = group_train_val_split(ds.run_ids, ds.is_holdout, val_frac=0.2, seed=1)
        tr_runs = {ds.run_ids[i] for i in tr}
        va_runs = {ds.run_ids[i] for i in va}
        ho_runs = {ds.run_ids[i] for i in ho}
        self.assertTrue(tr_runs.isdisjoint(va_runs))
        self.assertTrue(tr_runs.isdisjoint(ho_runs))
        self.assertTrue(va_runs.isdisjoint(ho_runs))
        # Holdout families strictly isolated
        ho_fams = {ds.families[i] for i in ho}
        tr_fams = {ds.families[i] for i in tr}
        self.assertTrue(ho_fams.isdisjoint(tr_fams))

    def test_phase2_and_3_template_action_conditioned_flip(self):
        circuit = CIRCUITS["MonzaScaled"]
        late = [0.10, 0.08, 0.66, 0.10, 0.06]
        sw = [0.12, 0.06, 0.10, 0.64, 0.08]
        p_hold_late = template_response_probs(False, 3.0, "HoldLine", late, 0.15)
        p_def_late = template_response_probs(False, 3.0, "DefendInside", late, 0.15)
        # Action conditioning: DefendInside must suppress LateBrake inside lunge vs HoldLine!
        self.assertLess(p_def_late[2], p_hold_late[2])
        # Against switchback-prone rival, DefendInside raises Switchback probability!
        p_def_sw = template_response_probs(False, 3.0, "DefendInside", sw, 0.15)
        self.assertGreater(p_def_sw[3], p_def_late[3])

        # Verify kinematic rollouts return finite distinct margins
        m_hold, _, _ = rollout_duel_branch_outcome(circuit, False, -9.0, 60.0, 63.0, 0.0, 0.0, 1.0, "HoldLine", 0.0, False, 2)
        m_def, _, _ = rollout_duel_branch_outcome(circuit, False, -9.0, 60.0, 63.0, 0.0, 0.0, 1.0, "DefendInside", 1.0, False, 2)
        self.assertGreater(m_def, m_hold)

    def test_m02_calibration_monotonic(self):
        with tempfile.TemporaryDirectory() as tmp:
            rep = calibrate_m02_tables(tmp, seed=5)
            self.assertTrue(rep["monotonic_verified"])
            with open(rep["table_path"], encoding="utf-8") as f:
                asset = json.load(f)
            t0, _, _, s0 = query_m02_table(asset, "MonzaScaled", 0.0, False)
            t1, _, _, s1 = query_m02_table(asset, "MonzaScaled", 1.0, False)
            t1_tr, _, _, _ = query_m02_table(asset, "MonzaScaled", 1.0, True)
            self.assertTrue(s0 and s1)
            self.assertGreater(t0, t1)
            self.assertGreater(t1_tr, t1)

    def test_deployed_artifacts_and_server_integration(self):
        py_root = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
        art_dir = os.path.join(py_root, "artifacts")
        if not os.path.exists(os.path.join(art_dir, "release_gate_report.json")):
            self.skipTest("Run training.release_gate first to populate artifacts/")
        with open(os.path.join(art_dir, "release_gate_report.json"), encoding="utf-8") as f:
            rep = json.load(f)
        self.assertTrue(rep["release_approved"])

        # Hairpin demo check on trained neural model!
        pred = OnnxTacticalPredictor(art_dir)
        demo = evaluate_hairpin_style_demo(pred)
        self.assertTrue(demo["ActionConditionedTemplate"]["decision_changed_from_forecasts"])
        self.assertTrue(demo["ActionConditionedNeural"]["decision_changed_from_forecasts"])

        # Server with fast clean laps (faster than d=0.50 ~42.6s on real Monza) recommends > 0.5, slow laps < 0.5
        server._RUNTIME_CACHE = None
        fast_req = {
            "schema": 1,
            "type": "recommend_difficulty",
            "current": 0.5,
            "track_id": "MonzaScaled",
            "laps": [
                {"lapTimeS": 41.2, "mixedDifficulty": False, "incident": False, "standingStart": False},
                {"lapTimeS": 41.0, "mixedDifficulty": False, "incident": False, "standingStart": False},
            ],
        }
        r_fast = json.loads(server.handle_line(json.dumps(fast_req).encode("utf-8")))
        self.assertTrue(r_fast["ok"] and r_fast["supported"])
        self.assertGreater(r_fast["difficulty"], 0.5)

        slow_req = {
            "schema": 1,
            "type": "recommend_difficulty",
            "current": 0.5,
            "track_id": "MonzaScaled",
            "laps": [
                {"lapTimeS": 46.8, "mixedDifficulty": False, "incident": False, "standingStart": False},
                {"lapTimeS": 46.5, "mixedDifficulty": False, "incident": False, "standingStart": False},
            ],
        }
        r_slow = json.loads(server.handle_line(json.dumps(slow_req).encode("utf-8")))
        self.assertTrue(r_slow["ok"] and r_slow["supported"])
        self.assertLess(r_slow["difficulty"], 0.5)

        # Server tactical prediction endpoint
        treq = {"schema": 1, "type": "predict_tactical_response", "features": [0.0] * 22}
        tr = json.loads(server.handle_line(json.dumps(treq).encode("utf-8")))
        self.assertTrue(tr["ok"] and len(tr["probabilities"]) == 5)
        self.assertAlmostEqual(sum(tr["probabilities"]), 1.0, places=4)


if __name__ == "__main__":
    unittest.main()
