"""Plackett-Luce モデルによる着順確率の算出。

各艇 i に効用 u_i があるとき、強さ w_i = exp(u_i) を「重み」として、
まだ着いていない艇の中から次の着を w に比例して選ぶ、という過程を繰り返す
確率モデル。これにより

    P(1着=a, 2着=b, 3着=c, ...) = ∏ w / (残り艇の w 合計)

が定義でき、
  - 単勝（1着）確率
  - 3連単（1-2-3着の順列）確率
  - 3連複（上位3艇の組合せ）確率
  - 各艇の「3着内」確率
を厳密に列挙して求められる（6艇なら順列120通りで軽量）。

これは「地力（効用）から着順分布を導く」ボートレース予想に理論的に
自然なモデルであり、ヒューリスティックな配点よりも一貫性がある。
"""
from __future__ import annotations

import itertools
import math
from typing import Dict, Iterable, List, Tuple


def utilities_to_weights(utilities: Dict[int, float]) -> Dict[int, float]:
    """効用 u を数値安定な形で w = exp(u) に変換する。"""
    m = max(utilities.values())
    return {k: math.exp(v - m) for k, v in utilities.items()}


def permutation_probability(order: Iterable[int], weights: Dict[int, float]) -> float:
    """指定した完全順位（または先頭からの部分列）の確率。"""
    remaining = dict(weights)
    p = 1.0
    for boat in order:
        total = sum(remaining.values())
        if total <= 0:
            return 0.0
        p *= remaining[boat] / total
        del remaining[boat]
    return p


def win_probabilities(weights: Dict[int, float]) -> Dict[int, float]:
    """各艇の1着確率。"""
    total = sum(weights.values())
    return {b: w / total for b, w in weights.items()}


def trifecta_probabilities(weights: Dict[int, float]) -> Dict[Tuple[int, int, int], float]:
    """3連単（順序付き上位3艇）の確率。キーは (1着, 2着, 3着)。"""
    boats = list(weights.keys())
    result: Dict[Tuple[int, int, int], float] = {}
    for combo in itertools.permutations(boats, 3):
        result[combo] = permutation_probability(combo, weights)
    return result


def trio_probabilities(weights: Dict[int, float]) -> Dict[Tuple[int, int, int], float]:
    """3連複（順不同の上位3艇）の確率。キーは昇順タプル。"""
    tri = trifecta_probabilities(weights)
    combined: Dict[Tuple[int, int, int], float] = {}
    for order, p in tri.items():
        key = tuple(sorted(order))  # type: ignore[assignment]
        combined[key] = combined.get(key, 0.0) + p
    return combined


def exacta_probabilities(weights: Dict[int, float]) -> Dict[Tuple[int, int], float]:
    """2連単（順序付き上位2艇）の確率。キーは (1着, 2着)。"""
    boats = list(weights.keys())
    result: Dict[Tuple[int, int], float] = {}
    for combo in itertools.permutations(boats, 2):
        result[combo] = permutation_probability(combo, weights)
    return result


def top3_probabilities(weights: Dict[int, float]) -> Dict[int, float]:
    """各艇が「3着以内に入る」確率。"""
    tri = trifecta_probabilities(weights)
    inside: Dict[int, float] = {b: 0.0 for b in weights}
    for order, p in tri.items():
        for b in order:
            inside[b] += p
    return inside


def expected_rank(weights: Dict[int, float]) -> Dict[int, float]:
    """各艇の期待着順（小さいほど上位）。全順列を列挙して算出。"""
    boats = list(weights.keys())
    n = len(boats)
    acc: Dict[int, float] = {b: 0.0 for b in boats}
    for perm in itertools.permutations(boats):
        p = permutation_probability(perm, weights)
        for rank, b in enumerate(perm, start=1):
            acc[b] += p * rank
    return acc


def top_n(prob_map: Dict, n: int) -> List[Tuple]:
    """確率マップを降順に並べて上位 n 件を返す。"""
    return sorted(prob_map.items(), key=lambda kv: kv[1], reverse=True)[:n]
