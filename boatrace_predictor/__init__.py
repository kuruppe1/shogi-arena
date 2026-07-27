"""boatrace_predictor — ボートレースAI予想エンジン（純Python・依存なし）。

使い方の概略::

    from boatrace_predictor import Predictor, load_race_from_json, format_prediction

    race = load_race_from_json("data/sample_ocean_cup_31.json")
    pred = Predictor().predict(race)
    print(format_prediction(pred))
"""
from .models import Race, RacerEntry
from .scoring import ScoreWeights
from .predictor import Predictor, RacePrediction
from .loaders import (
    load_race_from_csv,
    load_race_from_dict,
    load_race_from_json,
)
from .report import format_prediction

__all__ = [
    "Race",
    "RacerEntry",
    "ScoreWeights",
    "Predictor",
    "RacePrediction",
    "load_race_from_csv",
    "load_race_from_dict",
    "load_race_from_json",
    "format_prediction",
]

__version__ = "0.1.0"
