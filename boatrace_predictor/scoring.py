"""選手の「強さスコア」算出。

ボートレースの勝敗を左右する要素を、透明で説明可能な重み付き線形和で
スコア化する。学習済みブラックボックスではなく、専門知に基づく事前分布
（エキスパートプライア）として重みを設定しているため、根拠を明示できる。

主要因子（影響の大きい順のイメージ）:
  1. 進入コース  … インコース(1)が圧倒的に有利。最大の要素。
  2. モーター2連対率 … 機力。SG級では特に効く。
  3. 全国勝率 / 当地勝率 … 選手の地力・当地相性。
  4. 平均ST … スタート勘。小さいほど良い。
  5. F/L … フライング・出遅れの減点。

スコアは Plackett-Luce モデルの効用（utility）として使い、
exp(score) に比例した確率へ変換される。
"""
from __future__ import annotations

import math
from dataclasses import dataclass

from .models import Race, RacerEntry

# コース別の平均的な1着率（全国概算）。log を取ってベース効用にすることで、
# 全選手の地力が同等なら、この勝率分布が再現されるよう較正している。
COURSE_WIN_RATE = {
    1: 0.55,
    2: 0.14,
    3: 0.12,
    4: 0.10,
    5: 0.06,
    6: 0.03,
}

# 各因子の基準値（この値からの差分をスコアに反映）。
REF_NATIONAL_WIN_RATE = 6.50   # A級平均の目安
REF_LOCAL_WIN_RATE = 6.50
REF_MOTOR_TOP2 = 35.0          # モーター2連対率(%) の平均目安
REF_BOAT_TOP2 = 35.0
REF_AVG_ST = 0.16              # 平均STの目安（小さいほど良い）


@dataclass
class ScoreWeights:
    """各因子の重み。デフォルトは専門知に基づく事前値。

    ``predictor`` に渡すことで、重視する要素を調整できる。
    過去データがある場合は ``training`` モジュールで学習し直すことも可能。
    """

    course: float = 1.00        # コースベース効用の反映率（1.0 = そのまま）
    national_win_rate: float = 0.55
    local_win_rate: float = 0.25
    motor_top2: float = 0.020   # 1% あたりの効用
    boat_top2: float = 0.008
    avg_st: float = 4.0         # ST 0.01秒あたり相当（差分×(-weight)）
    flying_penalty: float = 0.8  # F 1本あたりの減点
    late_penalty: float = 0.3    # L 1本あたりの減点


def _course_base_utility(course: int) -> float:
    return math.log(COURSE_WIN_RATE.get(course, 0.03))


def racer_strength(
    entry: RacerEntry,
    weights: ScoreWeights,
    field_avg: "FieldAverages",
) -> float:
    """1選手の強さスコア（効用）を返す。

    未入力の項目はレース内平均（field_avg）へフォールバックし、
    データ欠損が不当なペナルティ・ボーナスにならないようにする。
    """
    course = entry.entry_course()
    s = weights.course * _course_base_utility(course)

    national = _or(entry.national_win_rate, field_avg.national_win_rate, REF_NATIONAL_WIN_RATE)
    s += weights.national_win_rate * (national - REF_NATIONAL_WIN_RATE)

    local = _or(entry.local_win_rate, field_avg.local_win_rate, national)
    s += weights.local_win_rate * (local - REF_LOCAL_WIN_RATE)

    motor = _or(entry.motor_top2_rate, field_avg.motor_top2_rate, REF_MOTOR_TOP2)
    s += weights.motor_top2 * (motor - REF_MOTOR_TOP2)

    boat = _or(entry.boat_top2_rate, field_avg.boat_top2_rate, REF_BOAT_TOP2)
    s += weights.boat_top2 * (boat - REF_BOAT_TOP2)

    st = _or(entry.avg_st, field_avg.avg_st, REF_AVG_ST)
    # ST は小さいほど良い → 基準との差にマイナス。単位を 0.01 に換算。
    s += weights.avg_st * ((REF_AVG_ST - st) / 0.01) * 0.01

    s -= weights.flying_penalty * max(entry.flying_count, 0)
    s -= weights.late_penalty * max(entry.late_count, 0)

    return s


def _or(value, *fallbacks):
    if value is not None:
        return value
    for f in fallbacks:
        if f is not None:
            return f
    return 0.0


@dataclass
class FieldAverages:
    """レース内の平均値（欠損値フォールバック用）。"""

    national_win_rate: float | None = None
    local_win_rate: float | None = None
    motor_top2_rate: float | None = None
    boat_top2_rate: float | None = None
    avg_st: float | None = None

    @classmethod
    def from_race(cls, race: Race) -> "FieldAverages":
        def avg(attr: str):
            vals = [getattr(e, attr) for e in race.entries if getattr(e, attr) is not None]
            return sum(vals) / len(vals) if vals else None

        return cls(
            national_win_rate=avg("national_win_rate"),
            local_win_rate=avg("local_win_rate"),
            motor_top2_rate=avg("motor_top2_rate"),
            boat_top2_rate=avg("boat_top2_rate"),
            avg_st=avg("avg_st"),
        )
