"""過去データから予想モデルの係数を学習する。

モデル: 各艇の効用 u_i = β·x_i （x は features.feature_vector）。
着順は Plackett-Luce 分布に従うと仮定し、観測された着順の対数尤度

    LL = Σ_race Σ_k [ u_{o_k} − log Σ_{j∈R_k} exp(u_j) ]

を最大化する β を勾配法（Adam）で求める。これは「条件付きロジット／
ランキング学習」の標準的な最尤推定で、予想側（Plackett-Luce）と
まったく同じ確率モデルを、事前重みではなく実データから較正する。

依存ライブラリなし（純Python）。
"""
from __future__ import annotations

import json
import math
from dataclasses import dataclass, field
from typing import Dict, List, Sequence, Tuple

from .features import FEATURE_NAMES, N_FEATURES, race_feature_matrix
from .models import Race
from .predictor import RacePrediction
from . import plackett_luce as pl


@dataclass
class TrainExample:
    """学習1レコード。

    features: {艇番: 特徴ベクトル}
    order:    観測された着順（1着→最下位の艇番リスト）。
              失格・転覆などで着が付かない艇は除外（末尾切り詰め）でよい。
    """

    features: Dict[int, List[float]]
    order: List[int]


def example_from_race(race: Race, finishing_order: Sequence[int]) -> TrainExample:
    feats = race_feature_matrix(race)
    order = [b for b in finishing_order if b in feats]
    return TrainExample(features=feats, order=order)


def _dot(beta: List[float], x: List[float]) -> float:
    return sum(b * xi for b, xi in zip(beta, x))


def _example_ll_and_grad(ex: TrainExample, beta: List[float]) -> Tuple[float, List[float]]:
    """1レコードの対数尤度と勾配（∂LL/∂β）。"""
    utils = {b: _dot(beta, x) for b, x in ex.features.items()}
    ll = 0.0
    grad = [0.0] * len(beta)
    remaining = list(ex.features.keys())
    # 観測着順どおりに1着から順に「選ばれた」確率を掛けていく。
    for chosen in ex.order:
        if chosen not in remaining:
            continue
        m = max(utils[b] for b in remaining)
        exps = {b: math.exp(utils[b] - m) for b in remaining}
        z = sum(exps.values())
        probs = {b: exps[b] / z for b in remaining}
        ll += math.log(max(probs[chosen], 1e-300))
        # grad += x_chosen − Σ_j p_j x_j
        xc = ex.features[chosen]
        for i in range(len(beta)):
            expected = sum(probs[b] * ex.features[b][i] for b in remaining)
            grad[i] += xc[i] - expected
        remaining.remove(chosen)
        if len(remaining) <= 1:
            break
    return ll, grad


@dataclass
class TrainConfig:
    lr: float = 0.05
    epochs: int = 300
    l2: float = 1e-3          # 正則化（コース項以外を主対象に軽く）
    verbose: bool = False


@dataclass
class LearnedModel:
    """学習済み係数。予想器に渡して使う。"""

    coef: Dict[str, float]

    def beta(self) -> List[float]:
        return [self.coef.get(name, 0.0) for name in FEATURE_NAMES]

    def utilities(self, race: Race) -> Dict[int, float]:
        beta = self.beta()
        feats = race_feature_matrix(race)
        return {b: _dot(beta, x) for b, x in feats.items()}

    def to_json(self, path: str) -> None:
        with open(path, "w", encoding="utf-8") as f:
            json.dump({"feature_names": FEATURE_NAMES, "coef": self.coef},
                      f, ensure_ascii=False, indent=2)

    @classmethod
    def from_json(cls, path: str) -> "LearnedModel":
        with open(path, encoding="utf-8") as f:
            data = json.load(f)
        return cls(coef=data["coef"])


def train(examples: List[TrainExample], config: TrainConfig | None = None) -> LearnedModel:
    """Adam による Plackett-Luce 最尤推定。"""
    cfg = config or TrainConfig()
    n = N_FEATURES
    beta = [0.0] * n
    # Adam 状態
    m = [0.0] * n
    v = [0.0] * n
    b1, b2, eps = 0.9, 0.999, 1e-8

    if not examples:
        raise ValueError("学習データが空です。")

    t = 0
    for epoch in range(cfg.epochs):
        total_ll = 0.0
        grad = [0.0] * n
        for ex in examples:
            if len(ex.order) < 2:
                continue
            ll, g = _example_ll_and_grad(ex, beta)
            total_ll += ll
            for i in range(n):
                grad[i] += g[i]
        # L2 正則化（勾配は −λβ）。目的は最大化なので勾配上昇。
        for i in range(n):
            grad[i] -= cfg.l2 * beta[i]
        # 平均化
        inv = 1.0 / len(examples)
        for i in range(n):
            grad[i] *= inv

        t += 1
        for i in range(n):
            m[i] = b1 * m[i] + (1 - b1) * grad[i]
            v[i] = b2 * v[i] + (1 - b2) * grad[i] * grad[i]
            mhat = m[i] / (1 - b1 ** t)
            vhat = v[i] / (1 - b2 ** t)
            beta[i] += cfg.lr * mhat / (math.sqrt(vhat) + eps)  # 上昇方向

        if cfg.verbose and (epoch % max(1, cfg.epochs // 10) == 0 or epoch == cfg.epochs - 1):
            print(f"  epoch {epoch:4d}  avg_LL/race = {total_ll * inv:.4f}")

    return LearnedModel(coef={name: beta[i] for i, name in enumerate(FEATURE_NAMES)})


def mean_log_likelihood(examples: List[TrainExample], model: LearnedModel) -> float:
    """モデルの平均対数尤度（1レースあたり、大きいほど良い）。"""
    beta = model.beta()
    total, cnt = 0.0, 0
    for ex in examples:
        if len(ex.order) < 2:
            continue
        ll, _ = _example_ll_and_grad(ex, beta)
        total += ll
        cnt += 1
    return total / cnt if cnt else float("nan")


def top1_accuracy(examples: List[TrainExample], model: LearnedModel) -> float:
    """1着的中率（予想1着 = 実際の1着）。"""
    beta = model.beta()
    hit, cnt = 0, 0
    for ex in examples:
        if not ex.order:
            continue
        utils = {b: _dot(beta, x) for b, x in ex.features.items()}
        pred_top = max(utils, key=lambda b: utils[b])
        hit += 1 if pred_top == ex.order[0] else 0
        cnt += 1
    return hit / cnt if cnt else float("nan")
