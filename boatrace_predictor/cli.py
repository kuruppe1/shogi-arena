"""コマンドラインインターフェース（サブコマンド）。

  predict   出走表を予想（事前モデル or 学習済みモデル）
  train     履歴データ / 公式取得データで係数を学習し保存
  fetch     公式(B/K)を期間取得して履歴JSONに保存
  selftest  合成データで学習ロジックを検証（実データ不要）

例::

    python -m boatrace_predictor.cli predict --demo
    python -m boatrace_predictor.cli predict --json race.json --model model.json
    python -m boatrace_predictor.cli fetch --start 2026-04-01 --end 2026-07-26 --out history.json
    python -m boatrace_predictor.cli train --history history.json --out model.json
    python -m boatrace_predictor.cli selftest
"""
from __future__ import annotations

import argparse
import json
import os
import sys
from datetime import date, datetime

from . import betting
from .loaders import load_race_from_csv, load_race_from_json
from .predictor import Predictor
from .report import format_prediction

_ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
_DEFAULT_SAMPLE = os.path.join(_ROOT, "data", "sample_ocean_cup_31.json")


# ----------------------------------------------------------------------------
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


def _load_race(args):
    if getattr(args, "fetch_date", None):
        from datetime import datetime
        from . import official
        d = datetime.strptime(args.fetch_date, "%Y-%m-%d").date()
        if not args.venue or not args.race:
            raise SystemExit("--fetch-date には --venue と --race も指定してください")
        return official.fetch_program(d, args.venue, int(args.race),
                                      base=args.base or official.DEFAULT_BASE)
    if args.csv:
        return load_race_from_csv(args.csv, title=args.title, venue=args.venue)
    if args.json:
        return load_race_from_json(args.json)
    return load_race_from_json(_DEFAULT_SAMPLE)


def cmd_predict(args) -> int:
    race = _load_race(args)
    learned = None
    if args.model:
        from .training import LearnedModel
        learned = LearnedModel.from_json(args.model)
    pred = Predictor(learned_model=learned).predict(race)
    if args.format == "json":
        print(json.dumps(_prediction_to_dict(pred), ensure_ascii=False, indent=2))
    else:
        if learned:
            print(f"（学習済みモデル {os.path.basename(args.model)} を使用）")
        print(format_prediction(pred))
    return 0


def cmd_fetch(args) -> int:
    from . import dataset, official

    start = _parse_date(args.start)
    end = _parse_date(args.end)
    base = args.base or official.DEFAULT_BASE
    print(f"公式データ取得: {start} 〜 {end}  base={base}")
    pairs = dataset.fetch_period(start, end, base=base, verbose=True)
    print(f"取得レース数: {len(pairs)}")
    _save_history(pairs, args.out)
    print(f"履歴を保存: {args.out}")
    return 0 if pairs else 1


def cmd_train(args) -> int:
    from . import dataset
    from .training import TrainConfig, mean_log_likelihood, top1_accuracy, train

    if args.history:
        pairs = dataset.load_history_json(args.history)
    elif args.start and args.end:
        from . import official
        pairs = dataset.fetch_period(_parse_date(args.start), _parse_date(args.end),
                                     base=args.base or official.DEFAULT_BASE)
    else:
        print("エラー: --history か（--start と --end）を指定してください", file=sys.stderr)
        return 2

    if not pairs:
        print("エラー: 学習データが空です（取得失敗/遮断の可能性）", file=sys.stderr)
        return 1

    examples = dataset.to_examples(pairs)
    print(f"学習レース数: {len(examples)}")
    model = train(examples, TrainConfig(lr=args.lr, epochs=args.epochs,
                                        l2=args.l2, verbose=True))
    model.to_json(args.out)
    print(f"平均LL/race = {mean_log_likelihood(examples, model):.4f}")
    print(f"1着的中率(学習内) = {top1_accuracy(examples, model):.3f}")
    print(f"モデルを保存: {args.out}")
    return 0


def cmd_selftest(args) -> int:
    from .features import FEATURE_NAMES
    from .simulate import TRUE_COEF, make_dataset
    from .dataset import to_examples
    from .training import (LearnedModel, TrainConfig, mean_log_likelihood,
                           top1_accuracy, train)

    pairs = make_dataset(n_races=args.n, seed=args.seed)
    k = int(len(pairs) * 0.8)
    tr, te = to_examples(pairs[:k]), to_examples(pairs[k:])
    print(f"合成データ: 学習{len(tr)} / 検証{len(te)} レース")
    zero = LearnedModel(coef={n: 0.0 for n in FEATURE_NAMES})
    model = train(tr, TrainConfig(lr=0.05, epochs=args.epochs, l2=1e-3))
    print("\n[検証セット]")
    print(f"  LL/race   一様={mean_log_likelihood(te, zero):+.3f}  学習後={mean_log_likelihood(te, model):+.3f}")
    print(f"  1着的中率 一様={top1_accuracy(te, zero):.3f}  学習後={top1_accuracy(te, model):.3f}")
    print("\n[係数の回復（学習 vs 真値）]")
    for n in FEATURE_NAMES:
        print(f"  {n:16s} 学習={model.coef[n]:+.2f}  真={TRUE_COEF.get(n, 0.0):+.2f}")
    return 0


# ----------------------------------------------------------------------------
def _parse_date(s: str) -> date:
    return datetime.strptime(s, "%Y-%m-%d").date()


def _save_history(pairs, path: str) -> None:
    races = []
    for race, order in pairs:
        races.append({
            "title": race.title,
            "venue": race.venue,
            "race_no": race.race_no,
            "date": race.date,
            "conditions": {
                "weather": race.conditions.weather,
                "wind_speed": race.conditions.wind_speed,
                "wind_direction": race.conditions.wind_direction,
                "wave_height": race.conditions.wave_height,
                "temperature": race.conditions.temperature,
                "water_temp": race.conditions.water_temp,
            },
            "entries": [
                {
                    "boat": e.boat, "name": e.name, "reg_number": e.reg_number,
                    "klass": e.klass, "national_win_rate": e.national_win_rate,
                    "national_top2_rate": e.national_top2_rate,
                    "local_win_rate": e.local_win_rate,
                    "motor_top2_rate": e.motor_top2_rate,
                    "boat_top2_rate": e.boat_top2_rate,
                    "avg_st": e.avg_st, "flying_count": e.flying_count,
                    "late_count": e.late_count, "course": e.course,
                }
                for e in race.entries
            ],
            "finishing_order": order,
        })
    with open(path, "w", encoding="utf-8") as f:
        json.dump({"races": races}, f, ensure_ascii=False, indent=2)


def build_parser() -> argparse.ArgumentParser:
    p = argparse.ArgumentParser(
        prog="boatrace_predictor",
        description="ボートレースAI予想（Plackett-Luce / 学習対応）",
    )
    sub = p.add_subparsers(dest="cmd")

    pp = sub.add_parser("predict", help="出走表を予想")
    src = pp.add_mutually_exclusive_group()
    src.add_argument("--json")
    src.add_argument("--csv")
    src.add_argument("--demo", action="store_true")
    src.add_argument("--fetch-date", help="公式番組表を取得して予想する日 YYYY-MM-DD")
    pp.add_argument("--race", help="--fetch-date 時のレース番号")
    pp.add_argument("--base", help="公式配布ベースURL（既定: mbrace）")
    pp.add_argument("--model", help="学習済みモデル(JSON)。指定時はこれで予想")
    pp.add_argument("--title", default="")
    pp.add_argument("--venue", default="", help="会場名(児島)または会場コード(16)")
    pp.add_argument("--format", choices=["text", "json"], default="text")
    pp.set_defaults(func=cmd_predict)

    pf = sub.add_parser("fetch", help="公式B/Kを期間取得して履歴JSON化")
    pf.add_argument("--start", required=True, help="YYYY-MM-DD")
    pf.add_argument("--end", required=True, help="YYYY-MM-DD")
    pf.add_argument("--base", help="配布ベースURL（既定: mbrace）")
    pf.add_argument("--out", default="history.json")
    pf.set_defaults(func=cmd_fetch)

    pt = sub.add_parser("train", help="履歴/取得データで係数を学習")
    pt.add_argument("--history", help="履歴JSON（fetch の出力）")
    pt.add_argument("--start", help="直接取得する場合の開始日")
    pt.add_argument("--end", help="直接取得する場合の終了日")
    pt.add_argument("--base")
    pt.add_argument("--out", default="model.json")
    pt.add_argument("--epochs", type=int, default=400)
    pt.add_argument("--lr", type=float, default=0.05)
    pt.add_argument("--l2", type=float, default=1e-3)
    pt.set_defaults(func=cmd_train)

    ps = sub.add_parser("selftest", help="合成データで学習を検証（実データ不要）")
    ps.add_argument("--n", type=int, default=500)
    ps.add_argument("--epochs", type=int, default=400)
    ps.add_argument("--seed", type=int, default=42)
    ps.set_defaults(func=cmd_selftest)

    return p


_SUBCOMMANDS = {"predict", "fetch", "train", "selftest"}


def main(argv=None) -> int:
    argv = list(sys.argv[1:] if argv is None else argv)
    # 後方互換: サブコマンド省略時（旧来の予想フラグ含む）は predict とみなす。
    if not argv or (argv[0] not in _SUBCOMMANDS and argv[0] not in ("-h", "--help")):
        argv = ["predict"] + argv
    args = build_parser().parse_args(argv)
    return args.func(args)


if __name__ == "__main__":
    sys.exit(main())
