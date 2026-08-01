"""合成データ生成（学習ロジックの検証用）。

実データ（boatrace.jp）に到達できない環境でも、既知の「真の係数」から
Plackett-Luce で着順を生成し、学習が係数を回復し予測が改善することを
確認できるようにする。

決定論的な擬似乱数（線形合同法）を使い、seed から再現可能にする。
"""
from __future__ import annotations

import math
from typing import Dict, List, Tuple

from .features import FEATURE_NAMES, N_FEATURES, feature_vector, race_feature_matrix
from .models import Race, RaceConditions, RacerEntry
from .scoring import FieldAverages
from . import plackett_luce as pl


class _LCG:
    """再現可能な擬似乱数（Math.random 非依存）。"""

    def __init__(self, seed: int = 12345):
        self.state = seed & 0xFFFFFFFF

    def next(self) -> float:
        self.state = (1103515245 * self.state + 12345) & 0x7FFFFFFF
        return self.state / 0x7FFFFFFF

    def uniform(self, a: float, b: float) -> float:
        return a + (b - a) * self.next()

    def choice_by_weight(self, items: List[int], weights: Dict[int, float]) -> int:
        total = sum(weights[i] for i in items)
        r = self.next() * total
        acc = 0.0
        for i in items:
            acc += weights[i]
            if r <= acc:
                return i
        return items[-1]


# 検証用の「真の」係数（現実に近い符号感: インコース有利・機力+・ST速い+）
TRUE_COEF: Dict[str, float] = {
    "c1": 1.6, "c2": 0.4, "c3": 0.15, "c4": 0.0, "c5": -0.5, "c6": -1.1,
    "national_win_rate": 0.35,
    "local_win_rate": 0.10,
    "motor_top2": 0.30,
    "boat_top2": 0.10,
    "st_adv": 0.20,
    "flying": -0.8,
    "late": -0.5,
    "wind_x_inner1": -0.06,   # 1コースは風が強いほどやや不利（まくられやすい）
    "wind_x_outer": 0.05,     # 外は風が強いほどやや有利
    "wave_x_inner1": -0.03,
    "wave_x_outer": 0.02,
    "rain_x_inner1": -0.10,
}


def _true_beta() -> List[float]:
    return [TRUE_COEF.get(n, 0.0) for n in FEATURE_NAMES]


def random_race(rng: _LCG, idx: int) -> Race:
    entries = []
    for boat in range(1, 7):
        entries.append(RacerEntry(
            boat=boat,
            name=f"選手{idx}-{boat}",
            national_win_rate=round(rng.uniform(4.5, 8.5), 2),
            local_win_rate=round(rng.uniform(4.0, 8.5), 2),
            motor_top2_rate=round(rng.uniform(25.0, 60.0), 1),
            boat_top2_rate=round(rng.uniform(25.0, 55.0), 1),
            avg_st=round(rng.uniform(0.10, 0.22), 2),
            flying_count=1 if rng.next() < 0.05 else 0,
        ))
    cond = RaceConditions(
        weather="雨" if rng.next() < 0.2 else "晴",
        wind_speed=round(rng.uniform(0.0, 8.0), 1),
        wave_height=round(rng.uniform(0.0, 10.0), 1),
    )
    return Race(entries=entries, venue="合成", race_no=(idx % 12) + 1,
                date="2026-01-01", conditions=cond)


def simulate_finishing_order(race: Race, beta: List[float], rng: _LCG) -> List[int]:
    feats = race_feature_matrix(race)
    utils = {b: sum(bb * xi for bb, xi in zip(beta, x)) for b, x in feats.items()}
    weights = pl.utilities_to_weights(utils)
    remaining = list(weights.keys())
    order: List[int] = []
    while remaining:
        pick = rng.choice_by_weight(remaining, weights)
        order.append(pick)
        remaining.remove(pick)
    return order


def make_dataset(n_races: int = 400, seed: int = 42) -> List[Tuple[Race, List[int]]]:
    rng = _LCG(seed)
    beta = _true_beta()
    pairs = []
    for i in range(n_races):
        race = random_race(rng, i)
        order = simulate_finishing_order(race, beta, rng)
        pairs.append((race, order))
    return pairs
