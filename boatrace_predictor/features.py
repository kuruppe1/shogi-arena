"""特徴量抽出（学習モデル・予想で共有）。

1艇を、レース条件（水面・気象）も加味した数値ベクトルに変換する。
この同じ特徴量を使って、
  - 過去データから係数を学習（training.py）
  - 学習済み係数で予想（predictor.LearnedModel 経由）
の両方を行うため、学習と推論の食い違いが起きない。

条件（風・波・天候）は「コースとの相互作用」として入れる。ボートレースでは
向かい風・強風・高波はスタートやまくりの決まりやすさを通じてコース有利度を
変えるため、コース単独ではなく交互作用で効くのが自然。
"""
from __future__ import annotations

from typing import List, Optional

from .models import Race, RaceConditions, RacerEntry
from .scoring import (
    REF_AVG_ST,
    REF_BOAT_TOP2,
    REF_LOCAL_WIN_RATE,
    REF_MOTOR_TOP2,
    REF_NATIONAL_WIN_RATE,
)

REF_WIND = 3.0    # 風速の基準(m/s)
REF_WAVE = 3.0    # 波高の基準(cm)
REF_TEMP = 20.0   # 気温/水温の基準(℃)

# 特徴量名（この順序が係数ベクトルの順序と対応する）。
FEATURE_NAMES: List[str] = [
    "c1", "c2", "c3", "c4", "c5", "c6",          # コース one-hot
    "national_win_rate",
    "local_win_rate",
    "motor_top2",
    "boat_top2",
    "st_adv",                                     # STの速さ（大きいほど良い）
    "flying",
    "late",
    # 条件 × コースの相互作用
    "wind_x_inner1",       # 1コース × 風速
    "wind_x_outer",        # 外(4-6)コース × 風速
    "wave_x_inner1",       # 1コース × 波高
    "wave_x_outer",        # 外(4-6)コース × 波高
    "rain_x_inner1",       # 雨 × 1コース
]

N_FEATURES = len(FEATURE_NAMES)


def _val(v: Optional[float], fallback: Optional[float], ref: float) -> float:
    if v is not None:
        return v
    if fallback is not None:
        return fallback
    return ref


def feature_vector(
    entry: RacerEntry,
    field_avg,                     # scoring.FieldAverages
    conditions: Optional[RaceConditions] = None,
) -> List[float]:
    """1艇の特徴ベクトル（FEATURE_NAMES と同じ順）を返す。"""
    conditions = conditions or RaceConditions()
    course = entry.entry_course()

    x = [0.0] * N_FEATURES
    idx = {name: i for i, name in enumerate(FEATURE_NAMES)}

    # コース one-hot
    x[idx[f"c{course}"]] = 1.0

    national = _val(entry.national_win_rate, field_avg.national_win_rate, REF_NATIONAL_WIN_RATE)
    x[idx["national_win_rate"]] = national - REF_NATIONAL_WIN_RATE

    local = _val(entry.local_win_rate, field_avg.local_win_rate, national)
    x[idx["local_win_rate"]] = local - REF_LOCAL_WIN_RATE

    motor = _val(entry.motor_top2_rate, field_avg.motor_top2_rate, REF_MOTOR_TOP2)
    x[idx["motor_top2"]] = (motor - REF_MOTOR_TOP2) / 10.0   # 10%単位に正規化

    boat = _val(entry.boat_top2_rate, field_avg.boat_top2_rate, REF_BOAT_TOP2)
    x[idx["boat_top2"]] = (boat - REF_BOAT_TOP2) / 10.0

    st = _val(entry.avg_st, field_avg.avg_st, REF_AVG_ST)
    x[idx["st_adv"]] = (REF_AVG_ST - st) / 0.01              # 0.01秒単位、速いほど+

    x[idx["flying"]] = float(max(entry.flying_count, 0))
    x[idx["late"]] = float(max(entry.late_count, 0))

    # 条件（欠損は 0 のまま＝効果なし）
    is_inner1 = 1.0 if course == 1 else 0.0
    is_outer = 1.0 if course in (4, 5, 6) else 0.0

    if conditions.wind_speed is not None:
        wind_c = conditions.wind_speed - REF_WIND
        x[idx["wind_x_inner1"]] = is_inner1 * wind_c
        x[idx["wind_x_outer"]] = is_outer * wind_c

    if conditions.wave_height is not None:
        wave_c = conditions.wave_height - REF_WAVE
        x[idx["wave_x_inner1"]] = is_inner1 * wave_c
        x[idx["wave_x_outer"]] = is_outer * wave_c

    if conditions.weather and ("雨" in conditions.weather or "雪" in conditions.weather):
        x[idx["rain_x_inner1"]] = is_inner1

    return x


def race_feature_matrix(race: Race):
    """レース内の各艇の特徴ベクトルを {艇番: ベクトル} で返す。"""
    from .scoring import FieldAverages
    field_avg = FieldAverages.from_race(race)
    return {
        e.boat: feature_vector(e, field_avg, race.conditions) for e in race.entries
    }
