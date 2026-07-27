"""予想結果の整形出力（テキストレポート）。"""
from __future__ import annotations

from typing import List

from . import betting
from .predictor import RacePrediction

_BAR = "─" * 52


def _pct(x: float) -> str:
    return f"{x * 100:5.1f}%"


def _bar(x: float, width: int = 20) -> str:
    n = int(round(x * width))
    return "█" * n + "·" * (width - n)


def format_prediction(pred: RacePrediction) -> str:
    race = pred.race
    lines: List[str] = []
    header = race.title or "レース予想"
    meta = " ".join(x for x in [race.venue, race.date,
                                f"{race.race_no}R" if race.race_no else ""] if x)
    lines.append(_BAR)
    lines.append(f" 🚤 {header}")
    if meta:
        lines.append(f"    {meta}")
    lines.append(_BAR)

    # 1着確率ランキング
    lines.append(" 【1着確率】")
    for boat in pred.ranking():
        e = race.by_boat(boat)
        p = pred.win[boat]
        name = e.name or f"{boat}号艇"
        course = e.entry_course()
        c_note = "" if course == boat else f"(進入{course})"
        lines.append(f"  {boat}号艇 {name:<10}{c_note} {_bar(p)} {_pct(p)}")

    # 3着内確率
    lines.append("")
    lines.append(" 【3連対率（3着内に入る確率）】")
    for boat, p in sorted(pred.top3.items(), key=lambda kv: kv[1], reverse=True):
        e = race.by_boat(boat)
        name = e.name or f"{boat}号艇"
        lines.append(f"  {boat}号艇 {name:<10} {_bar(p)} {_pct(p)}")

    # 本命の順位予想
    lines.append("")
    ranking = pred.ranking()
    lines.append(" 【本命の予想着順】 " + " → ".join(f"{b}" for b in ranking))

    # フォーメーション
    form = betting.formation(pred)
    lines.append("")
    lines.append(" 【フォーメーション】")
    lines.append(f"   軸(1着) : {_fmt_boats(form['軸(1着)'])}")
    lines.append(f"   相手(2着): {_fmt_boats(form['相手(2着)'])}")
    lines.append(f"   押さえ(3着): {_fmt_boats(form['押さえ(3着)'])}")

    # おすすめ3連単
    lines.append("")
    lines.append(" 【3連単 おすすめ買い目（確率上位）】")
    for i, bet in enumerate(betting.trifecta_bets(pred, top=6), 1):
        ev = f"  EV {bet.expected_value:.2f}" if bet.expected_value is not None else ""
        lines.append(f"   {i}. {bet.label():<9} {_pct(bet.probability)}{ev}")

    # おすすめ3連複
    lines.append("")
    lines.append(" 【3連複 おすすめ買い目（確率上位）】")
    for i, bet in enumerate(betting.trio_bets(pred, top=4), 1):
        ev = f"  EV {bet.expected_value:.2f}" if bet.expected_value is not None else ""
        lines.append(f"   {i}. {bet.label():<9} {_pct(bet.probability)}{ev}")

    lines.append(_BAR)
    lines.append(" ※ 確率モデルに基づく参考情報です。的中を保証するものではありません。")
    lines.append("    投票は自己責任でお願いします。")
    lines.append(_BAR)
    return "\n".join(lines)


def _fmt_boats(boats) -> str:
    return ", ".join(str(b) for b in boats) if boats else "-"
