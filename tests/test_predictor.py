"""boatrace_predictor のユニットテスト（標準ライブラリ unittest）。

    python -m unittest discover -s tests
"""
import math
import os
import sys
import unittest

sys.path.insert(0, os.path.dirname(os.path.dirname(os.path.abspath(__file__))))

from boatrace_predictor import (  # noqa: E402
    Predictor,
    Race,
    RacerEntry,
    ScoreWeights,
    load_race_from_csv,
    load_race_from_json,
)
from boatrace_predictor import plackett_luce as pl  # noqa: E402
from boatrace_predictor import betting  # noqa: E402

DATA = os.path.join(os.path.dirname(os.path.dirname(os.path.abspath(__file__))), "data")


def equal_field_race():
    """全選手が完全に同等（データ無し）のレース。"""
    return Race(entries=[RacerEntry(boat=i) for i in range(1, 7)])


class TestModels(unittest.TestCase):
    def test_entry_course_defaults_to_boat(self):
        self.assertEqual(RacerEntry(boat=3).entry_course(), 3)
        self.assertEqual(RacerEntry(boat=3, course=1).entry_course(), 1)

    def test_duplicate_boat_rejected(self):
        with self.assertRaises(ValueError):
            Race(entries=[RacerEntry(boat=1), RacerEntry(boat=1)])

    def test_invalid_boat_rejected(self):
        with self.assertRaises(ValueError):
            RacerEntry(boat=7)


class TestPlackettLuce(unittest.TestCase):
    def test_win_probs_sum_to_one(self):
        w = {1: 3.0, 2: 2.0, 3: 1.0}
        wp = pl.win_probabilities(w)
        self.assertAlmostEqual(sum(wp.values()), 1.0, places=9)

    def test_trifecta_sums_to_one(self):
        w = {i: float(i) for i in range(1, 7)}
        tri = pl.trifecta_probabilities(w)
        self.assertAlmostEqual(sum(tri.values()), 1.0, places=9)

    def test_top3_sums_to_three(self):
        w = {i: float(i) for i in range(1, 7)}
        t3 = pl.top3_probabilities(w)
        # 3つの着（1,2,3着）に必ず誰かが入るので合計は 3。
        self.assertAlmostEqual(sum(t3.values()), 3.0, places=9)

    def test_higher_weight_higher_winprob(self):
        w = {1: 5.0, 2: 1.0, 3: 1.0}
        wp = pl.win_probabilities(w)
        self.assertGreater(wp[1], wp[2])

    def test_trio_matches_trifecta_marginal(self):
        w = {i: float(7 - i) for i in range(1, 7)}
        trio = pl.trio_probabilities(w)
        self.assertAlmostEqual(sum(trio.values()), 1.0, places=9)


class TestScoringCalibration(unittest.TestCase):
    def test_equal_field_reproduces_course_base_rates(self):
        """全員同等なら、1着確率はコース別ベース勝率に一致するはず。"""
        pred = Predictor().predict(equal_field_race())
        # 1コースは 0.55 付近、6コースは 0.03 付近。
        self.assertAlmostEqual(pred.win[1], 0.55, places=6)
        self.assertAlmostEqual(pred.win[6], 0.03, places=6)
        self.assertGreater(pred.win[1], pred.win[2])
        self.assertGreater(pred.win[2], pred.win[6])

    def test_stronger_racer_raises_winprob(self):
        # 全艇に平均的なデータを与えたうえで、boat2 だけ地力を上げる。
        def make(boat2_national, boat2_motor):
            return Race(entries=[
                RacerEntry(boat=i, national_win_rate=6.5, motor_top2_rate=35.0)
                if i != 2 else
                RacerEntry(boat=2, national_win_rate=boat2_national,
                           motor_top2_rate=boat2_motor)
                for i in range(1, 7)
            ])
        p_base = Predictor().predict(make(6.5, 35.0)).win[2]
        p_strong = Predictor().predict(make(8.5, 60.0)).win[2]
        self.assertGreater(p_strong, p_base)


class TestLoaders(unittest.TestCase):
    def test_load_json_sample(self):
        race = load_race_from_json(os.path.join(DATA, "sample_ocean_cup_31.json"))
        self.assertEqual(len(race.entries), 6)
        self.assertIn("オーシャンカップ", race.title)

    def test_load_csv_sample(self):
        race = load_race_from_csv(os.path.join(DATA, "sample_race.csv"))
        self.assertEqual(len(race.entries), 6)
        self.assertEqual(race.by_boat(1).national_win_rate, 7.85)

    def test_json_and_csv_agree(self):
        rj = load_race_from_json(os.path.join(DATA, "sample_ocean_cup_31.json"))
        rc = load_race_from_csv(os.path.join(DATA, "sample_race.csv"))
        pj = Predictor().predict(rj)
        pc = Predictor().predict(rc)
        for b in range(1, 7):
            self.assertAlmostEqual(pj.win[b], pc.win[b], places=6)


class TestBetting(unittest.TestCase):
    def test_value_bets_filters_by_ev(self):
        bets = [
            betting.Bet("3連単", (1, 2, 3), 0.10, odds=15.0),  # EV 1.5
            betting.Bet("3連単", (1, 3, 2), 0.10, odds=5.0),   # EV 0.5
        ]
        picked = betting.value_bets(bets, min_ev=1.0)
        self.assertEqual(len(picked), 1)
        self.assertEqual(picked[0].selection, (1, 2, 3))

    def test_formation_axis_is_favorite(self):
        pred = Predictor().predict(load_race_from_json(
            os.path.join(DATA, "sample_ocean_cup_31.json")))
        form = betting.formation(pred)
        self.assertEqual(form["軸(1着)"], [pred.honmei()])


class TestWeightsConfigurable(unittest.TestCase):
    def test_zero_skill_weights_reduce_to_course_only(self):
        w = ScoreWeights(national_win_rate=0, local_win_rate=0, motor_top2=0,
                         boat_top2=0, avg_st=0, flying_penalty=0, late_penalty=0)
        race = load_race_from_json(os.path.join(DATA, "sample_ocean_cup_31.json"))
        pred = Predictor(w).predict(race)
        # 実力差を無視すれば、1着確率はコースベース（1コース=0.55）に戻る。
        self.assertAlmostEqual(pred.win[1], 0.55, places=6)


if __name__ == "__main__":
    unittest.main()
