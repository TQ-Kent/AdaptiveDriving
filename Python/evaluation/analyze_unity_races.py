"""Compare real Unity AI-vs-AI & Human-vs-AI races logged under Unity/Logs/encounters_archive/ and tactical_encounters.csv.

Usage:
    python evaluation/analyze_unity_races.py [unity_logs_dir]
"""
from __future__ import annotations

import collections
import csv
import os
import sys
from typing import Dict, List


def load_all_unity_encounters(logs_dir: str) -> List[Dict[str, str]]:
    paths = []
    arch = os.path.join(logs_dir, "encounters_archive")
    if os.path.isdir(arch):
        for fn in sorted(os.listdir(arch)):
            if fn.endswith(".csv"):
                paths.append(os.path.join(arch, fn))
    latest = os.path.join(logs_dir, "tactical_encounters.csv")
    if os.path.exists(latest):
        paths.append(latest)

    seen = set()
    rows = []
    for p in paths:
        with open(p, newline="", encoding="utf-8") as f:
            for r in csv.DictReader(f):
                key = (r.get("encounter_id", ""), r.get("t", ""), r.get("driver_id", ""), r.get("opponent_id", ""))
                if key in seen:
                    continue
                seen.add(key)
                rows.append(r)
    return rows


def summarize(logs_dir: str) -> None:
    rows = load_all_unity_encounters(logs_dir)
    if not rows:
        print(f"No Unity tactical encounter rows found in {logs_dir}.")
        return

    by_variant: Dict[str, List[Dict[str, str]]] = collections.defaultdict(list)
    for r in rows:
        by_variant[r.get("variant", "Unknown")].append(r)

    print(f"Total unique real Unity encounters: {len(rows)}\n")
    print(f"{'Variant':<28} {'N':>5} {'Completed':>10} {'Passed':>7} {'KeptFwd':>8} {'Aborted':>8} {'Contact':>8} {'OffTrk':>7} {'Ovrd>0':>7} {'AvgSupp':>8}")
    print("-" * 104)
    for var, rs in sorted(by_variant.items()):
        n = len(rs)
        aborted = sum(r.get("aborted") == "1" for r in rs)
        completed = n - aborted
        passed = sum(r.get("outcome") == "passed" for r in rs)
        kept = sum(r.get("outcome") == "kept_ahead" for r in rs)
        contacts = sum(r.get("had_contact") == "1" for r in rs)
        off = sum(r.get("had_off_track") == "1" for r in rs)
        ovrd = sum(float(r.get("safety_override_s", "0") or 0) > 0 for r in rs)
        avg_supp = sum(int(r.get("style_support", "0") or 0) for r in rs) / max(1, n)
        print(f"{var:<28} {n:5d} {completed:10d} {passed:7d} {kept:8d} {aborted:8d} {contacts:8d} {off:7d} {ovrd:7d} {avg_supp:8.2f}")

    print("\nProposed maneuvers overall:", dict(collections.Counter(r.get("proposed_maneuver") for r in rows)))
    print("Observed rival responses:  ", dict(collections.Counter(r.get("observed_response") for r in rows)))
    aborts = collections.Counter(r.get("abort_reason") for r in rows if r.get("aborted") == "1")
    if aborts:
        print("Abort reasons:             ", dict(aborts))


if __name__ == "__main__":
    here = os.path.dirname(os.path.abspath(__file__))
    default_logs = os.path.normpath(os.path.join(here, "..", "..", "Unity", "Logs"))
    summarize(sys.argv[1] if len(sys.argv) > 1 else default_logs)
