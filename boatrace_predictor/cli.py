"""コマンドラインインターフェース。

例::

    # サンプル（第31回オーシャンカップ想定デモ）を予想
    python -m boatrace_predictor.cli --demo

    # JSON の出走表を予想
    python -m boatrace_predictor.cli --json data/sample_ocean_cup_31.json

    # CSV の出走表を予想
    python -m boatrace_predictor.cli --csv myrace.csv --title "決勝"

    # 各種確率を JSON で出力（機械可読）
    python -m boatrace_predictor.cli --json race.json --format json
"""
from __future__ import annotations

import argparse
import json
import os
import sys

from . import betting
from .loaders import load_race_from_csv, load_race_from_json
from .predictor import Predictor
from .report import format_prediction

_DEFAULT_SAMPLE = os.path.join(
    os.path.dirname(os.path.dirname(os.path.abspath(__file__))),
    "data",
    "sample_ocean_cup_31.json",
)


def _prediction_to_dict(pred) -> dict:
    tri = betting.trifecta_bets(pred, top=10)
    trio = betting.trio_bets(pred, top=6)
    return {
        "title": pred.race.title,
        "venue": pred.race.venue,
        "race_no": pred.race.race_no,
        "date": pred.race.date,
        "win": {str(b): round(p, 4) for b, p in pred.win.items()},
        "top3": {str(b): round(p, 4) for b, p in pred.top3.items()},
        "expected_rank": {str(b): round(p, 3) for b, p in pred.expected_rank.items()},
        "ranking": pred.ranking(),
        "trifecta_top": [
            {"selection": list(b.selection), "probability": round(b.probability, 4)}
            for b in tri
        ],
        "trio_top": [
            {"selection": list(b.selection), "probability": round(b.probability, 4)}
            for b in trio
        ],
        "formation": betting.formation(pred),
    }


def build_parser() -> argparse.ArgumentParser:
    p = argparse.ArgumentParser(
        prog="boatrace_predictor",
        description="ボートレースAI予想（Plackett-Luce モデル）",
    )
    src = p.add_mutually_exclusive_group()
    src.add_argument("--json", help="出走表 JSON ファイル")
    src.add_argument("--csv", help="出走表 CSV ファイル")
    src.add_argument("--demo", action="store_true", help="同梱サンプルで予想")
    p.add_argument("--title", default="", help="CSV 用のレースタイトル")
    p.add_argument("--venue", default="", help="CSV 用のレース場名")
    p.add_argument("--format", choices=["text", "json"], default="text",
                   help="出力形式（text=レポート / json=機械可読）")
    return p


def main(argv=None) -> int:
    args = build_parser().parse_args(argv)

    if args.csv:
        race = load_race_from_csv(args.csv, title=args.title, venue=args.venue)
    elif args.json:
        race = load_race_from_json(args.json)
    else:
        # デフォルト or --demo はサンプルを使う。
        race = load_race_from_json(_DEFAULT_SAMPLE)

    pred = Predictor().predict(race)

    if args.format == "json":
        print(json.dumps(_prediction_to_dict(pred), ensure_ascii=False, indent=2))
    else:
        print(format_prediction(pred))
    return 0


if __name__ == "__main__":
    sys.exit(main())
