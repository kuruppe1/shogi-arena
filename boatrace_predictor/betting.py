"""買い目の提案。

予想確率から、本命・相手・押さえの買い目を組み立てる。
オッズが分かる場合は期待値(EV = 確率 × オッズ)も算出し、
「妙味のある買い目（EV > 1.0）」を抽出できる。

※ 出力はあくまで確率モデルに基づく参考情報です。的中を保証するもの
   ではありません。ボートレースには不確実性があり、投票は自己責任で。
"""
from __future__ import annotations

from dataclasses import dataclass
from typing import Dict, List, Optional, Tuple

from .predictor import RacePrediction


@dataclass
class Bet:
    kind: str                 # "3連単" / "3連複" / "2連単" など
    selection: Tuple[int, ...]
    probability: float
    odds: Optional[float] = None

    @property
    def expected_value(self) -> Optional[float]:
        if self.odds is None:
            return None
        return self.probability * self.odds

    def label(self) -> str:
        return "-".join(str(b) for b in self.selection)


def trifecta_bets(
    pred: RacePrediction,
    top: int = 6,
    odds: Optional[Dict[Tuple[int, int, int], float]] = None,
) -> List[Bet]:
    """3連単のおすすめ買い目（確率上位）。"""
    ranked = sorted(pred.trifecta.items(), key=lambda kv: kv[1], reverse=True)[:top]
    bets = []
    for sel, p in ranked:
        o = odds.get(sel) if odds else None
        bets.append(Bet("3連単", sel, p, o))
    return bets


def trio_bets(
    pred: RacePrediction,
    top: int = 4,
    odds: Optional[Dict[Tuple[int, int, int], float]] = None,
) -> List[Bet]:
    """3連複のおすすめ買い目（確率上位）。"""
    ranked = sorted(pred.trio.items(), key=lambda kv: kv[1], reverse=True)[:top]
    bets = []
    for sel, p in ranked:
        key = tuple(sorted(sel))
        o = odds.get(key) if odds else None
        bets.append(Bet("3連複", key, p, o))
    return bets


def value_bets(bets: List[Bet], min_ev: float = 1.0) -> List[Bet]:
    """期待値がしきい値以上（妙味あり）の買い目のみ抽出。"""
    picked = [b for b in bets if b.expected_value is not None and b.expected_value >= min_ev]
    return sorted(picked, key=lambda b: b.expected_value or 0.0, reverse=True)


def formation(pred: RacePrediction, axis_n: int = 1, second_n: int = 3, third_n: int = 4) -> Dict:
    """フォーメーション（軸-相手-押さえ）を提案する。

    軸: 1着確率の高い艇。
    相手: 2着に来やすい艇（本命が1着のときの条件付き上位）。
    押さえ: 3着候補。
    """
    ranking = pred.ranking()
    axis = ranking[:axis_n]

    # 相手候補は「3着内確率」から軸を除いた上位。
    others = [b for b, _ in sorted(pred.top3.items(), key=lambda kv: kv[1], reverse=True)
              if b not in axis]
    seconds = others[:second_n]
    thirds = others[:third_n]
    return {
        "軸(1着)": axis,
        "相手(2着)": seconds,
        "押さえ(3着)": thirds,
    }
