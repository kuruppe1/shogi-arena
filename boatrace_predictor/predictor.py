"""予想の統合レイヤー。

Race を受け取り、各艇の効用スコア → Plackett-Luce → 各種確率、
という流れで RacePrediction を組み立てる。
"""
from __future__ import annotations

from dataclasses import dataclass, field
from typing import Dict, List, Tuple

from . import plackett_luce as pl
from .models import Race
from .scoring import FieldAverages, ScoreWeights, racer_strength


@dataclass
class RacePrediction:
    race: Race
    utilities: Dict[int, float]
    weights: Dict[int, float]
    win: Dict[int, float]
    top3: Dict[int, float]
    expected_rank: Dict[int, float]
    trifecta: Dict[Tuple[int, int, int], float]
    trio: Dict[Tuple[int, int, int], float]
    exacta: Dict[Tuple[int, int], float]

    def ranking(self) -> List[int]:
        """本命の予想着順（1着候補から順に）。"""
        return [b for b, _ in sorted(self.win.items(), key=lambda kv: kv[1], reverse=True)]

    def honmei(self) -> int:
        """本命（最有力の1着候補）の艇番。"""
        return self.ranking()[0]


class Predictor:
    """設定可能な重みでレースを予想する。"""

    def __init__(self, weights: ScoreWeights | None = None):
        self.weights = weights or ScoreWeights()

    def predict(self, race: Race) -> RacePrediction:
        field_avg = FieldAverages.from_race(race)
        utilities = {
            e.boat: racer_strength(e, self.weights, field_avg) for e in race.entries
        }
        weights = pl.utilities_to_weights(utilities)
        return RacePrediction(
            race=race,
            utilities=utilities,
            weights=weights,
            win=pl.win_probabilities(weights),
            top3=pl.top3_probabilities(weights),
            expected_rank=pl.expected_rank(weights),
            trifecta=pl.trifecta_probabilities(weights),
            trio=pl.trio_probabilities(weights),
            exacta=pl.exacta_probabilities(weights),
        )
