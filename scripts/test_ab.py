#!/usr/bin/env python3
"""Unit tests for ab.py's pure logic (issue #136 review follow-up).

No pytest in this repo's requirements — stdlib unittest, run with
`python3 scripts/test_ab.py` or `python3 -m unittest scripts.test_ab`.
Covers only what doesn't need a Unity build: the CI math, condition
validation, the deterministic default directory and the summary.json
gate on which matches get aggregated.
"""

import json
import os
import sys
import tempfile
import unittest

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))

import ab  # noqa: E402

Z95 = 1.959963984540054


class WilsonIntervalTests(unittest.TestCase):
    def test_point_estimate_between_bounds(self):
        point, lower, upper = ab.wilson_interval(60, 100, Z95)
        self.assertEqual(point, 0.6)
        self.assertLess(lower, point)
        self.assertLess(point, upper)

    def test_zero_trials_is_the_widest_interval(self):
        self.assertEqual(ab.wilson_interval(0, 0, Z95), (0.0, 0.0, 1.0))


class NewcombeDiffIntervalTests(unittest.TestCase):
    def test_matches_newcombe_1998_worked_example(self):
        # Newcombe (1998), Table II, method 10: 56/70 vs 48/80 -> diff 0.2,
        # 95% CI approximately (0.0524, 0.3339).
        diff, lower, upper = ab.newcombe_diff_interval(56, 70, 48, 80, Z95)
        self.assertAlmostEqual(diff, 0.2, places=6)
        self.assertAlmostEqual(lower, 0.0524, places=3)
        self.assertAlmostEqual(upper, 0.3339, places=3)

    def test_identical_groups_center_on_zero(self):
        diff, lower, upper = ab.newcombe_diff_interval(50, 100, 50, 100, Z95)
        self.assertEqual(diff, 0.0)
        self.assertAlmostEqual(lower, -upper, places=9)


class LoadConditionsTests(unittest.TestCase):
    def _write(self, conditions):
        handle = tempfile.NamedTemporaryFile(
            mode="w", suffix=".json", delete=False, encoding="utf-8")
        json.dump(conditions, handle)
        handle.close()
        self.addCleanup(os.remove, handle.name)
        return handle.name

    def test_rejects_duplicate_name(self):
        path = self._write([{"name": "a", "args": []}, {"name": "a", "args": []}])
        with self.assertRaises(ValueError):
            ab.load_conditions(path)

    def test_rejects_missing_name(self):
        path = self._write([{"args": ["--selector", "fsm"]}])
        with self.assertRaises(ValueError):
            ab.load_conditions(path)

    def test_rejects_reserved_flag(self):
        path = self._write([{"name": "bad", "args": ["--selector", "fsm", "--seed", "9"]}])
        with self.assertRaises(ValueError):
            ab.load_conditions(path)

    def test_accepts_well_formed_conditions(self):
        path = self._write([{"name": "fsm", "args": ["--selector", "fsm"]}])
        self.assertEqual(ab.load_conditions(path), [{"name": "fsm", "args": ["--selector", "fsm"]}])


class DefaultCampaignDirTests(unittest.TestCase):
    def test_identical_invocation_resolves_to_the_same_directory(self):
        conditions = [{"name": "fsm", "args": ["--selector", "fsm"]}]
        first = ab.default_campaign_dir("/m/x.onnx", "policy", "policy", 1001, 5, conditions)
        second = ab.default_campaign_dir("/m/x.onnx", "policy", "policy", 1001, 5, conditions)
        self.assertEqual(first, second)

    def test_different_conditions_resolve_to_different_directories(self):
        base = ("/m/x.onnx", "policy", "policy", 1001, 5)
        a = ab.default_campaign_dir(*base, [{"name": "fsm", "args": ["--selector", "fsm"]}])
        b = ab.default_campaign_dir(*base, [{"name": "fsm", "args": ["--selector", "random"]}])
        self.assertNotEqual(a, b)


class LoadConditionEventsTests(unittest.TestCase):
    def test_excludes_a_match_without_a_summary(self):
        with tempfile.TemporaryDirectory() as out:
            done = ab.match_dir(out, "fsm", 1)
            failed = ab.match_dir(out, "fsm", 2)
            os.makedirs(done)
            os.makedirs(failed)
            with open(os.path.join(done, "telemetry.jsonl"), "w", encoding="utf-8") as handle:
                handle.write(json.dumps({"type": "episode_summary", "winner": "NPC"}) + "\n")
            with open(os.path.join(done, "summary.json"), "w", encoding="utf-8") as handle:
                handle.write("{}")
            # A match that crashed after the player run but before eval_summary.py
            # wrote summary.json: telemetry exists, but with no finished episode.
            with open(os.path.join(failed, "telemetry.jsonl"), "w", encoding="utf-8") as handle:
                handle.write(json.dumps({"type": "mode_decision", "entity": "NPC"}) + "\n")

            events, completed = ab.load_condition_events(out, "fsm", matches=2)
            self.assertEqual(completed, 1)
            self.assertEqual(len(events), 1)
            self.assertEqual(events[0]["type"], "episode_summary")


if __name__ == "__main__":
    unittest.main()
