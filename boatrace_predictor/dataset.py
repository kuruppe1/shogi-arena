"""学習データセットの構築。

  - fetch_period(): 公式からB/Kを日次取得して (Race, 着順) を集める
  - load_history_json(): 手元の履歴（JSON）から (Race, 着順) を読む
  - to_examples(): (Race, 着順) を training.TrainExample に変換
"""
from __future__ import annotations

import json
from datetime import date, timedelta
from typing import List, Optional, Tuple

from . import official
from .loaders import load_race_from_dict
from .models import Race
from .training import TrainExample, example_from_race


def to_examples(pairs: List[Tuple[Race, List[int]]]) -> List[TrainExample]:
    return [example_from_race(r, order) for r, order in pairs]


def fetch_period(
    start: date,
    end: date,
    base: str = official.DEFAULT_BASE,
    verbose: bool = True,
    on_error: str = "skip",
) -> List[Tuple[Race, List[int]]]:
    """[start, end] の各日について B/K を取得・解析し、(Race, 着順) を集める。

    on_error='skip' なら取得失敗した日を飛ばして続行（休催日・遮断日など）。
    """
    pairs: List[Tuple[Race, List[int]]] = []
    d = start
    while d <= end:
        ymd = d.strftime("%Y-%m-%d")
        try:
            b_txt = official.extract_lzh(official.download(official.lzh_url("b", d, base)))
            k_txt = official.extract_lzh(official.download(official.lzh_url("k", d, base)))
            b = official.parse_b_text(b_txt, ymd)
            k = official.parse_k_text(k_txt, ymd)
            day_pairs = official.build_races_with_results(b, k)
            pairs.extend(day_pairs)
            if verbose:
                print(f"  {ymd}: {len(day_pairs)} races")
        except Exception as e:  # noqa: BLE001
            if on_error != "skip":
                raise
            if verbose:
                print(f"  {ymd}: skip ({e})")
        d += timedelta(days=1)
    return pairs


def load_history_json(path: str) -> List[Tuple[Race, List[int]]]:
    """履歴 JSON を読む。

    形式: {"races": [ {<Race と同じ>, "finishing_order": [1,3,2,...]}, ... ]}
    または (Race, 着順) の配列トップレベルにも寛容に対応。
    """
    with open(path, encoding="utf-8") as f:
        data = json.load(f)
    items = data["races"] if isinstance(data, dict) and "races" in data else data
    pairs: List[Tuple[Race, List[int]]] = []
    for item in items:
        order = item.get("finishing_order")
        if not order:
            continue
        race = load_race_from_dict(item)
        pairs.append((race, [int(b) for b in order]))
    return pairs
