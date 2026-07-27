# shogi-arena / boatrace-predictor

ボートレース（競艇）の **AI予想エンジン**。第31回オーシャンカップのような
レースの出走表データから、各艇の1着確率・3連対率・3連単／3連複の
おすすめ買い目を算出します。

> ⚠️ **免責**: 本ツールは確率モデルに基づく **参考情報** を出力するものであり、
> 的中を保証しません。ボートレースには不確実性があります。投票は自己責任で
> お願いします。（20歳未満は舟券を購入できません。）

## 特長

- **依存ライブラリ不要**（Python 3.9+ の標準ライブラリのみ）
- **説明可能なモデル**: ブラックボックスではなく、コース有利・機力・勝率・ST
  などを重み付けした透明なスコアリング
- **理論的に一貫した着順確率**: [Plackett–Luce モデル](https://en.wikipedia.org/wiki/Plackett%E2%80%93Luce_model)
  で単勝・3連単・3連複・3連対率を厳密に算出
- **公式データでも手入力でも同じ流れ**: JSON / CSV から読み込み

## クイックスタート

```bash
# 同梱サンプル（第31回オーシャンカップ想定・架空データ）で予想
python -m boatrace_predictor.cli --demo

# 機械可読な JSON で出力
python -m boatrace_predictor.cli --demo --format json
```

出力例（抜粋）:

```
 🚤 第31回オーシャンカップ 優勝戦（サンプルデータ）
 【1着確率】
  1号艇 サンプルA      ████████████████····  79.3%
  3号艇 サンプルC      ██··················   8.6%
  ...
 【3連単 おすすめ買い目（確率上位）】
   1. 1-3-2      17.3%
   2. 1-2-3      14.7%
```

## 自分のレースを予想する

### CSV で（いちばん手軽）

`data/sample_race.csv` と同じヘッダで用意します（日本語ヘッダ対応・順不同OK）:

```csv
艇番,選手名,級別,全国勝率,全国2連対率,当地勝率,モーター2連対率,ボート2連対率,平均ST,F
1,選手名,A1,7.85,62.0,8.10,42.0,38.0,0.14,0
...
```

```bash
python -m boatrace_predictor.cli --csv data/sample_race.csv --title "第31回オーシャンカップ 優勝戦"
```

### JSON で

`data/sample_ocean_cup_31.json` を参考にしてください。`--json path.json` で読み込みます。

分からない項目は省略できます（レース内平均で補完します）。最低限 `艇番` があれば動きます。

### 進入予想（イン屋・前づけ）

艇番と実際の進入コースが異なる場合は `course`（CSVなら `進入`）を指定すると、
コース有利度をそちらで計算します。

## ライブラリとして使う

```python
from boatrace_predictor import Predictor, load_race_from_csv, format_prediction

race = load_race_from_csv("data/sample_race.csv")
pred = Predictor().predict(race)

print(format_prediction(pred))          # 整形レポート
print(pred.win)                          # {1: 0.79, 3: 0.086, ...} 1着確率
print(pred.ranking())                    # [1, 3, 2, 4, 5, 6] 予想着順
print(pred.trifecta[(1, 3, 2)])          # 3連単 1-3-2 の確率
```

### オッズを使った期待値（妙味）判定

```python
from boatrace_predictor import betting

odds = {(1, 3, 2): 8.5, (1, 2, 3): 6.0}          # 3連単オッズ
bets = betting.trifecta_bets(pred, top=20, odds=odds)
value = betting.value_bets(bets, min_ev=1.0)      # 期待値1.0以上のみ
for b in value:
    print(b.label(), f"{b.probability:.1%}", "EV", round(b.expected_value, 2))
```

## モデルの中身

各艇の効用（強さ）スコアを次の要素の重み付き和で算出します:

| 要素 | 説明 | 影響 |
|------|------|------|
| 進入コース | インコース(1)が圧倒的有利 | 最大 |
| モーター2連対率 | 機力 | 大 |
| 全国勝率 / 当地勝率 | 地力・当地相性 | 中 |
| 平均ST | スタート勘（小さいほど良い） | 中 |
| F / L | フライング・出遅れの減点 | 減点 |

コース別のベース効用は全国の平均1着率（1コース≈55%…6コース≈3%）の対数で
較正しており、**全艇の地力が同等ならこの勝率分布が再現されます**。
このスコアを Plackett–Luce モデルの効用として使い、着順確率へ変換します。

重みは `ScoreWeights` で調整できます:

```python
from boatrace_predictor import Predictor, ScoreWeights
w = ScoreWeights(motor_top2=0.03, avg_st=6.0)   # 機力とSTを重視
pred = Predictor(w).predict(race)
```

## 実際のレースデータについて

第31回オーシャンカップの正式な出走表・選手成績・モーター勝率などは
[BOAT RACE 公式サイト](https://www.boatrace.jp/) で公開されています。
公式の出走表の数値を上記の CSV / JSON 形式に転記して読み込ませてください。
（本リポジトリのサンプルは動作確認用の**架空データ**で、実在の選手・結果とは
関係ありません。）

## テスト

```bash
python -m unittest discover -s tests
```

## プロジェクト構成

```
boatrace_predictor/
├─ models.py         出走表のデータモデル
├─ scoring.py        強さスコア（説明可能な重み付け）
├─ plackett_luce.py  着順確率モデル
├─ predictor.py      予想の統合
├─ betting.py        買い目・期待値
├─ loaders.py        JSON / CSV 読み込み
├─ report.py         整形出力
└─ cli.py            コマンドライン
data/                サンプルデータ
tests/               ユニットテスト
```

## 注意

- 本ツールはギャンブルの勝利を保証しません。娯楽の範囲でご利用ください。
- 舟券の購入は20歳以上に限られます。
