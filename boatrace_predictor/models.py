"""データモデル定義（ボートレース予想）。

このモジュールは外部ライブラリに依存しない純Pythonのデータクラスで、
1レース分の出走表を表現する。
"""
from __future__ import annotations

from dataclasses import dataclass, field
from typing import Optional


@dataclass
class RacerEntry:
    """1艇（=1選手）分の出走データ。

    数値項目は分かる範囲で入れればよい。未知の場合は None を渡すと、
    予想側で全体平均やニュートラル値にフォールバックする。

    Attributes:
        boat: 艇番 (1..6)。
        name: 選手名。
        reg_number: 登録番号（任意）。
        klass: 級別（A1/A2/B1/B2 など、任意）。
        national_win_rate: 全国勝率（例: 7.20）。1走あたり獲得平均点。
        national_top2_rate: 全国2連対率(%)（例: 55.3）。
        local_win_rate: 当地勝率（そのレース場での勝率、任意）。
        motor_top2_rate: モーター2連対率(%)。
        boat_top2_rate: ボート2連対率(%)。
        avg_st: 平均スタートタイミング（例: 0.15）。小さいほど良い。
        flying_count: 直近のフライング(F)回数（減点要素）。
        late_count: 出遅れ(L)回数（減点要素）。
        course: 進入コース(1..6)。未指定なら艇番=コースとみなす。
    """

    boat: int
    name: str = ""
    reg_number: Optional[str] = None
    klass: Optional[str] = None
    national_win_rate: Optional[float] = None
    national_top2_rate: Optional[float] = None
    local_win_rate: Optional[float] = None
    motor_top2_rate: Optional[float] = None
    boat_top2_rate: Optional[float] = None
    avg_st: Optional[float] = None
    flying_count: int = 0
    late_count: int = 0
    course: Optional[int] = None

    def entry_course(self) -> int:
        """実際の進入コースを返す（未指定なら艇番）。"""
        return self.course if self.course is not None else self.boat

    def __post_init__(self) -> None:
        if not (1 <= self.boat <= 6):
            raise ValueError(f"boat は 1..6 で指定してください: {self.boat}")
        if self.course is not None and not (1 <= self.course <= 6):
            raise ValueError(f"course は 1..6 で指定してください: {self.course}")


@dataclass
class Race:
    """1レース分の情報。

    Attributes:
        entries: 出走艇のリスト（通常6艇）。
        title: レースタイトル（例: 第31回オーシャンカップ 優勝戦）。
        venue: レース場名（例: 児島）。
        race_no: レース番号（例: 12）。
        date: 開催日（YYYY-MM-DD、任意）。
    """

    entries: list[RacerEntry]
    title: str = ""
    venue: str = ""
    race_no: Optional[int] = None
    date: Optional[str] = None

    def __post_init__(self) -> None:
        boats = [e.boat for e in self.entries]
        if len(set(boats)) != len(boats):
            raise ValueError(f"艇番が重複しています: {boats}")
        courses = [e.entry_course() for e in self.entries]
        if len(set(courses)) != len(courses):
            raise ValueError(f"進入コースが重複しています: {courses}")

    def sorted_by_boat(self) -> list[RacerEntry]:
        return sorted(self.entries, key=lambda e: e.boat)

    def by_boat(self, boat: int) -> RacerEntry:
        for e in self.entries:
            if e.boat == boat:
                return e
        raise KeyError(f"艇番 {boat} は存在しません")
