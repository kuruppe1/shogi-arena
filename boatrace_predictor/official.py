"""BOAT RACE 公式データ（番組表B / 競走成績K）の取得・解凍・解析。

公式は日単位の LZH テキストを配布している:
  - 番組表(B):  bYYMMDD.lzh  … 出走表（勝率・モーター2連率など）
  - 競走成績(K): kYYMMDD.lzh  … 着順・ST・進入・天候/風/波などの結果

このモジュールは
  1. ダウンロード（プロキシ/CA対応の urllib）
  2. LZH 解凍（lhafile があれば使用、無ければ system lha/7z にフォールバック）
  3. Shift-JIS テキストの解析 → 構造化レコード
を提供する。

⚠️ 重要:
  - 実行環境によっては boatrace.jp / mbrace.or.jp への通信が
    ネットワークポリシーで遮断される（このリポジトリのCIリモート環境では遮断）。
    その場合は到達可能な環境（手元PC等）で実行すること。
  - パーサは公式ファイルの標準レイアウト（NNKBGN/NNKEND の会場区切り等）を
    対象にした寛容な実装。最新の実ファイルで一度検証することを推奨。
"""
from __future__ import annotations

import os
import re
from dataclasses import dataclass, field
from datetime import date as _date
from typing import Dict, List, Optional, Tuple

from .models import Race, RaceConditions, RacerEntry

# ---- 会場コード ------------------------------------------------------------
VENUE_NAMES: Dict[str, str] = {
    "01": "桐生", "02": "戸田", "03": "江戸川", "04": "平和島", "05": "多摩川",
    "06": "浜名湖", "07": "蒲郡", "08": "常滑", "09": "津", "10": "三国",
    "11": "びわこ", "12": "住之江", "13": "尼崎", "14": "鳴門", "15": "丸亀",
    "16": "児島", "17": "宮島", "18": "徳山", "19": "下関", "20": "若松",
    "21": "芦屋", "22": "福岡", "23": "唐津", "24": "大村",
}


# ---- ダウンロード ----------------------------------------------------------
# mbrace.or.jp の従来配布パス。会社ポリシーで異なる場合は base を差し替える。
DEFAULT_BASE = "https://www1.mbrace.or.jp/od2"


def lzh_url(kind: str, d: _date, base: str = DEFAULT_BASE) -> str:
    """kind は 'b' か 'k'。"""
    kind = kind.lower()
    if kind not in ("b", "k"):
        raise ValueError("kind は 'b' か 'k'")
    sub = "B" if kind == "b" else "K"
    ym = f"{d.year:04d}{d.month:02d}"
    fname = f"{kind}{d.year % 100:02d}{d.month:02d}{d.day:02d}.lzh"
    return f"{base}/{sub}/{ym}/{fname}"


def download(url: str, timeout: int = 30) -> bytes:
    """URL を取得して bytes を返す。プロキシ/CA は環境変数に従う。

    ポリシー遮断（403/407）や到達不可のときは分かりやすい例外を投げる。
    """
    import urllib.error
    import urllib.request

    req = urllib.request.Request(url, headers={"User-Agent": "boatrace-predictor/0.1"})
    try:
        with urllib.request.urlopen(req, timeout=timeout) as resp:
            return resp.read()
    except urllib.error.HTTPError as e:
        raise RuntimeError(f"HTTP {e.code} for {url}: {e.reason}") from e
    except urllib.error.URLError as e:
        raise RuntimeError(
            f"取得失敗 {url}: {e.reason}. "
            f"ネットワークポリシーで遮断されている可能性があります"
            f"（到達可能な環境で実行してください）。"
        ) from e


# ---- LZH 解凍 --------------------------------------------------------------
def extract_lzh(data: bytes) -> str:
    """LZH バイト列を解凍し、Shift-JIS テキストとして返す。"""
    text_bytes = _extract_lzh_bytes(data)
    return text_bytes.decode("shift_jis", errors="replace")


def _extract_lzh_bytes(data: bytes) -> bytes:
    # 1) lhafile（pip install lhafile）
    try:
        import io
        import lhafile  # type: ignore

        lf = lhafile.Lhafile(io.BytesIO(data))  # type: ignore[attr-defined]
        names = lf.namelist()
        if not names:
            raise RuntimeError("LZH 内にファイルがありません")
        return lf.read(names[0])
    except ImportError:
        pass

    # 2) system の lha / 7z にフォールバック
    import shutil
    import subprocess
    import tempfile

    tmp = tempfile.mkdtemp(prefix="brlzh_")
    lzh_path = os.path.join(tmp, "data.lzh")
    with open(lzh_path, "wb") as f:
        f.write(data)
    for cmd in (["lha", "xw=" + tmp, lzh_path], ["7z", "x", "-o" + tmp, lzh_path]):
        if shutil.which(cmd[0]):
            subprocess.run(cmd, check=True, capture_output=True)
            for name in os.listdir(tmp):
                if name.lower().endswith((".txt",)):
                    with open(os.path.join(tmp, name), "rb") as f:
                        return f.read()
    raise RuntimeError(
        "LZH を解凍できません。`pip install lhafile` を実行するか、"
        "system に lha / 7z を用意してください。"
    )


# ---- 解析: 番組表(B) -------------------------------------------------------
_VENUE_BEGIN = re.compile(r"^\s*(\d{2})([BK])BGN")
_VENUE_END = re.compile(r"^\s*(\d{2})([BK])END")
_RACE_HEADER = re.compile(r"^\s*(\d{1,2})R\b")

# B の選手行: 例
#  1 4321 山田太郎     43 群馬 52 A1  6.50 45.30  5.80 40.10 38 55.20 25 48.30
_B_ROW = re.compile(
    r"^\s*([1-6])\s+(\d{4})(.+?)\s+\d{1,3}\s+\S+\s+\d{1,3}\s+([AB][12])\s+"
    r"([\d.]+)\s+([\d.]+)\s+([\d.]+)\s+([\d.]+)\s+\d+\s+([\d.]+)\s+\d+\s+([\d.]+)"
)


@dataclass
class BProgram:
    venue_code: str
    venue: str
    race_no: int
    date: Optional[str]
    entries: List[RacerEntry]


def parse_b_text(text: str, date_str: Optional[str] = None) -> List[BProgram]:
    """番組表テキストを会場×レース単位に解析する。"""
    programs: List[BProgram] = []
    venue_code: Optional[str] = None
    race_no: Optional[int] = None
    entries: List[RacerEntry] = []

    def flush():
        nonlocal entries, race_no
        if venue_code and race_no and entries:
            programs.append(BProgram(
                venue_code=venue_code,
                venue=VENUE_NAMES.get(venue_code, venue_code),
                race_no=race_no, date=date_str, entries=entries,
            ))
        entries = []

    for line in text.splitlines():
        mb = _VENUE_BEGIN.match(line)
        if mb and mb.group(2) == "B":
            flush(); venue_code = mb.group(1); race_no = None
            continue
        if _VENUE_END.match(line):
            flush(); race_no = None
            continue
        mh = _RACE_HEADER.match(line)
        if mh:
            flush(); race_no = int(mh.group(1))
            continue
        mr = _B_ROW.match(line)
        if mr and race_no:
            entries.append(RacerEntry(
                boat=int(mr.group(1)),
                reg_number=mr.group(2),
                name=mr.group(3).strip(),
                klass=mr.group(4),
                national_win_rate=float(mr.group(5)),
                national_top2_rate=float(mr.group(6)),
                local_win_rate=float(mr.group(7)),
                motor_top2_rate=float(mr.group(9)),
                boat_top2_rate=float(mr.group(10)),
            ))
    flush()
    return programs


# ---- 解析: 競走成績(K) -----------------------------------------------------
# レースヘッダから天候/風/波を拾う。表記ゆれに寛容にする。
_WEATHER = re.compile(r"(晴|曇|雨|雪|霧)")
_WIND = re.compile(r"風\s*([^\s0-9]*)\s*(\d+(?:\.\d+)?)\s*m")
_WAVE = re.compile(r"波\s*(\d+(?:\.\d+)?)\s*cm")
# K の結果行: 着 艇 登番 名前 ... 進入 ST ...
_K_ROW = re.compile(
    r"^\s*(0?[1-6]|[FLKS失転落妨])\s+([1-6])\s+(\d{4})\s+(\S+)"
)
_K_ST = re.compile(r"(\d)\s+([.\d]+)\s+\d\.\d\d\.\d")  # 進入 ST レースタイム 近辺


@dataclass
class KResult:
    venue_code: str
    venue: str
    race_no: int
    date: Optional[str]
    conditions: RaceConditions
    finishing_order: List[int]         # 1着→の艇番（着順が付いた艇のみ）
    start_timing: Dict[int, float] = field(default_factory=dict)   # 艇番→ST


def parse_k_text(text: str, date_str: Optional[str] = None) -> List[KResult]:
    results: List[KResult] = []
    venue_code: Optional[str] = None
    race_no: Optional[int] = None
    cond = RaceConditions()
    placed: List[Tuple[int, int]] = []   # (着, 艇)
    st: Dict[int, float] = {}

    def flush():
        nonlocal placed, st, cond, race_no
        if venue_code and race_no and placed:
            order = [b for _, b in sorted(placed, key=lambda t: t[0])]
            results.append(KResult(
                venue_code=venue_code, venue=VENUE_NAMES.get(venue_code, venue_code),
                race_no=race_no, date=date_str, conditions=cond,
                finishing_order=order, start_timing=dict(st),
            ))
        placed = []; st = {}; cond = RaceConditions()

    for line in text.splitlines():
        mb = _VENUE_BEGIN.match(line)
        if mb and mb.group(2) == "K":
            flush(); venue_code = mb.group(1); race_no = None
            continue
        if _VENUE_END.match(line):
            flush(); race_no = None
            continue
        mh = _RACE_HEADER.match(line)
        if mh:
            flush(); race_no = int(mh.group(1))
            cond = _parse_conditions(line)
            continue
        if race_no:
            mr = _K_ROW.match(line)
            if mr:
                chaku_raw, boat_s = mr.group(1), mr.group(2)
                boat = int(boat_s)
                if chaku_raw.isdigit():
                    placed.append((int(chaku_raw), boat))
                mst = _K_ST.search(line)
                if mst:
                    try:
                        st[boat] = float(mst.group(2))
                    except ValueError:
                        pass
    flush()
    return results


def _parse_conditions(header_line: str) -> RaceConditions:
    c = RaceConditions()
    w = _WEATHER.search(header_line)
    if w:
        c.weather = w.group(1)
    wind = _WIND.search(header_line)
    if wind:
        c.wind_direction = wind.group(1) or None
        c.wind_speed = float(wind.group(2))
    wave = _WAVE.search(header_line)
    if wave:
        c.wave_height = float(wave.group(1))
    return c


# ---- B と K の結合 ---------------------------------------------------------
def build_races_with_results(
    b_programs: List[BProgram], k_results: List[KResult]
) -> List[Tuple[Race, List[int]]]:
    """同一 (会場, レース番号) の B と K を結合し、(Race, 着順) を返す。"""
    k_index = {(k.venue_code, k.race_no): k for k in k_results}
    out: List[Tuple[Race, List[int]]] = []
    for b in b_programs:
        k = k_index.get((b.venue_code, b.race_no))
        if not k or len(k.finishing_order) < 2:
            continue
        # 着順が付いた艇のみ Race に含める（失格等を除外）。
        finished = set(k.finishing_order)
        entries = [e for e in b.entries if e.boat in finished]
        if len(entries) < 2:
            continue
        # K の ST を出走表側へ補完（分かる場合）。
        for e in entries:
            if e.boat in k.start_timing and e.avg_st is None:
                e.avg_st = k.start_timing[e.boat]
        race = Race(
            entries=entries, venue=b.venue, race_no=b.race_no, date=b.date,
            conditions=k.conditions,
        )
        out.append((race, k.finishing_order))
    return out
