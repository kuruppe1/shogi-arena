"""出走表の読み込み（JSON / CSV）。

外部データを取り込むための入口。予想モデルは Race オブジェクトさえ
あれば動くので、公式データでも手入力でも同じ流れで扱える。

対応形式:
  - JSON: data/sample_ocean_cup_31.json と同じスキーマ。
  - CSV : 1行=1艇。ヘッダ名でカラムを対応づける（順不同OK）。
"""
from __future__ import annotations

import csv
import json
from typing import Optional

from .models import Race, RaceConditions, RacerEntry

# CSV / JSON のキー別名（日本語ヘッダにも対応）。
_ALIASES = {
    "boat": ["boat", "艇番", "枠", "枠番"],
    "name": ["name", "選手名", "名前", "氏名"],
    "reg_number": ["reg_number", "登録番号", "登番"],
    "klass": ["klass", "class", "級別", "級"],
    "national_win_rate": ["national_win_rate", "全国勝率", "勝率"],
    "national_top2_rate": ["national_top2_rate", "全国2連対率", "全国2連率", "2連対率"],
    "local_win_rate": ["local_win_rate", "当地勝率"],
    "motor_top2_rate": ["motor_top2_rate", "モーター2連対率", "モーター2連率", "機力"],
    "boat_top2_rate": ["boat_top2_rate", "ボート2連対率", "ボート2連率"],
    "avg_st": ["avg_st", "平均ST", "ST"],
    "flying_count": ["flying_count", "F", "フライング"],
    "late_count": ["late_count", "L", "出遅れ"],
    "course": ["course", "進入", "コース", "進入コース"],
}

_INT_FIELDS = {"boat", "flying_count", "late_count", "course"}
_FLOAT_FIELDS = {
    "national_win_rate", "national_top2_rate", "local_win_rate",
    "motor_top2_rate", "boat_top2_rate", "avg_st",
}


def _pick(row: dict, canonical: str):
    for key in _ALIASES[canonical]:
        if key in row and row[key] not in (None, ""):
            return row[key]
    return None


def _coerce(canonical: str, value):
    if value is None:
        return None
    if canonical in _INT_FIELDS:
        return int(float(value))
    if canonical in _FLOAT_FIELDS:
        return float(value)
    return str(value).strip()


def _row_to_entry(row: dict) -> RacerEntry:
    kwargs = {}
    for canonical in _ALIASES:
        raw = _pick(row, canonical)
        val = _coerce(canonical, raw)
        if val is not None:
            kwargs[canonical] = val
    if "boat" not in kwargs:
        raise ValueError(f"艇番(boat)が見つかりません: {row}")
    # flying/late のデフォルトは 0。
    kwargs.setdefault("flying_count", 0)
    kwargs.setdefault("late_count", 0)
    return RacerEntry(**kwargs)


def _conditions_from_dict(data: dict) -> RaceConditions:
    c = data.get("conditions") or {}
    return RaceConditions(
        weather=c.get("weather"),
        wind_speed=_as_float(c.get("wind_speed")),
        wind_direction=c.get("wind_direction"),
        wave_height=_as_float(c.get("wave_height")),
        temperature=_as_float(c.get("temperature")),
        water_temp=_as_float(c.get("water_temp")),
    )


def _as_float(v):
    return None if v in (None, "") else float(v)


def load_race_from_dict(data: dict) -> Race:
    entries = [_row_to_entry(e) for e in data["entries"]]
    return Race(
        entries=entries,
        title=data.get("title", ""),
        venue=data.get("venue", ""),
        race_no=data.get("race_no"),
        date=data.get("date"),
        conditions=_conditions_from_dict(data),
    )


def load_race_from_json(path: str) -> Race:
    with open(path, encoding="utf-8") as f:
        data = json.load(f)
    return load_race_from_dict(data)


def load_race_from_csv(
    path: str,
    title: str = "",
    venue: str = "",
    race_no: Optional[int] = None,
    date: Optional[str] = None,
) -> Race:
    with open(path, encoding="utf-8-sig") as f:
        rows = list(csv.DictReader(f))
    entries = [_row_to_entry(r) for r in rows]
    return Race(entries=entries, title=title, venue=venue, race_no=race_no, date=date)
