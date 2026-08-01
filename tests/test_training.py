"""学習・特徴量・データセットのテスト。"""
import os
import sys
import tempfile
import unittest

sys.path.insert(0, os.path.dirname(os.path.dirname(os.path.abspath(__file__))))

from boatrace_predictor import Predictor  # noqa: E402
from boatrace_predictor.dataset import load_history_json, to_examples  # noqa: E402
from boatrace_predictor.features import FEATURE_NAMES, N_FEATURES, feature_vector  # noqa: E402
from boatrace_predictor.models import Race, RaceConditions, RacerEntry  # noqa: E402
from boatrace_predictor.scoring import FieldAverages  # noqa: E402
from boatrace_predictor.simulate import make_dataset  # noqa: E402
from boatrace_predictor.training import (LearnedModel, TrainConfig,  # noqa: E402
                                         mean_log_likelihood, top1_accuracy, train)


class TestFeatures(unittest.TestCase):
    def test_vector_length_and_course_onehot(self):
        race = Race(entries=[RacerEntry(boat=i) for i in range(1, 7)])
        fa = FieldAverages.from_race(race)
        x = feature_vector(race.entries[0], fa, race.conditions)
        self.assertEqual(len(x), N_FEATURES)
        # 1号艇なので c1=1、他コースは0。
        self.assertEqual(x[FEATURE_NAMES.index("c1")], 1.0)
        self.assertEqual(x[FEATURE_NAMES.index("c2")], 0.0)

    def test_conditions_interaction_zero_when_missing(self):
        race = Race(entries=[RacerEntry(boat=1)])
        fa = FieldAverages.from_race(race)
        x = feature_vector(race.entries[0], fa, RaceConditions())
        self.assertEqual(x[FEATURE_NAMES.index("wind_x_inner1")], 0.0)

    def test_wind_interaction_nonzero_for_inner(self):
        race = Race(entries=[RacerEntry(boat=1)],
                    conditions=RaceConditions(wind_speed=6.0))
        fa = FieldAverages.from_race(race)
        x = feature_vector(race.entries[0], fa, race.conditions)
        self.assertNotEqual(x[FEATURE_NAMES.index("wind_x_inner1")], 0.0)


class TestTrainingRecovery(unittest.TestCase):
    def test_training_improves_over_uniform(self):
        pairs = make_dataset(n_races=300, seed=7)
        k = 240
        tr, te = to_examples(pairs[:k]), to_examples(pairs[k:])
        zero = LearnedModel(coef={n: 0.0 for n in FEATURE_NAMES})
        model = train(tr, TrainConfig(lr=0.05, epochs=250, l2=1e-3))
        ll_zero = mean_log_likelihood(te, zero)
        ll_learned = mean_log_likelihood(te, model)
        self.assertGreater(ll_learned, ll_zero)
        self.assertGreater(top1_accuracy(te, model), top1_accuracy(te, zero))

    def test_recovers_course_ordering(self):
        pairs = make_dataset(n_races=400, seed=11)
        model = train(to_examples(pairs), TrainConfig(lr=0.05, epochs=300))
        # 1コースの係数は6コースより大きい（インコース有利）を回復する。
        self.assertGreater(model.coef["c1"], model.coef["c6"])
        # 機力(motor)の符号は正。
        self.assertGreater(model.coef["motor_top2"], 0)


class TestLearnedModelInPredictor(unittest.TestCase):
    def test_predictor_uses_learned_model(self):
        pairs = make_dataset(n_races=200, seed=3)
        model = train(to_examples(pairs), TrainConfig(lr=0.05, epochs=200))
        race = pairs[0][0]
        pred = Predictor(learned_model=model).predict(race)
        self.assertAlmostEqual(sum(pred.win.values()), 1.0, places=6)

    def test_save_load_roundtrip(self):
        pairs = make_dataset(n_races=100, seed=1)
        model = train(to_examples(pairs), TrainConfig(lr=0.05, epochs=100))
        with tempfile.NamedTemporaryFile("w", suffix=".json", delete=False) as f:
            path = f.name
        model.to_json(path)
        loaded = LearnedModel.from_json(path)
        os.unlink(path)
        self.assertEqual(set(loaded.coef), set(model.coef))
        for n in FEATURE_NAMES:
            self.assertAlmostEqual(loaded.coef[n], model.coef[n], places=9)


class TestHistoryRoundtrip(unittest.TestCase):
    def test_history_json_load(self):
        data = {
            "races": [
                {
                    "venue": "テスト", "race_no": 1, "date": "2026-01-01",
                    "conditions": {"weather": "晴", "wind_speed": 2.0, "wave_height": 1.0},
                    "entries": [{"boat": i, "national_win_rate": 6.0} for i in range(1, 7)],
                    "finishing_order": [1, 2, 3, 4, 5, 6],
                }
            ]
        }
        import json
        with tempfile.NamedTemporaryFile("w", suffix=".json", delete=False, encoding="utf-8") as f:
            json.dump(data, f)
            path = f.name
        pairs = load_history_json(path)
        os.unlink(path)
        self.assertEqual(len(pairs), 1)
        race, order = pairs[0]
        self.assertEqual(order, [1, 2, 3, 4, 5, 6])
        self.assertEqual(race.conditions.weather, "晴")


if __name__ == "__main__":
    unittest.main()
