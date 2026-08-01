"""公式B/Kパーサのテスト（標準レイアウトのフィクスチャ）。

実ファイルは取得できない環境があるため、公式の標準レイアウトを模した
フィクスチャで解析ロジックを検証する。
"""
import os
import sys
import unittest

sys.path.insert(0, os.path.dirname(os.path.dirname(os.path.abspath(__file__))))

from boatrace_predictor import official  # noqa: E402
from datetime import date  # noqa: E402


B_TEXT = """01BBGN
   1R                     H1800m  電話投票締切predict
 1 4321 山田太郎     43 群馬 52 A1  6.50 45.30  5.80 40.10 38 55.20 25 48.30
 2 4001 佐藤次郎     35 東京 51 A1  7.10 50.00  6.90 44.00 20 48.00 11 40.00
 3 3999 鈴木三郎     40 大阪 53 B1  5.20 35.00  5.00 33.00 15 30.00 30 35.00
 4 4500 高橋四郎     28 福岡 50 A2  6.00 42.00  5.80 39.00 44 60.00 12 37.00
 5 4777 田中五郎     33 愛知 52 A1  5.80 40.00  5.50 36.00 33 34.00 19 32.00
 6 4888 伊藤六郎     45 広島 55 B1  4.90 30.00  4.70 28.00 31 31.00 22 29.00
01BEND
"""

K_TEXT = """01KBGN
   1R   予選            H1800m  晴  風  北 3m  波   2cm
  着 艇 登番  選手名       展示 進入 ｽﾀｰﾄ  ﾚｰｽﾀｲﾑ
   1  2 4001 佐藤次郎     6.50  1  0.14  1.51.2
   2  1 4321 山田太郎     6.70  2  0.15  1.51.8
   3  4 4500 高橋四郎     6.60  4  0.16  1.52.0
   4  5 4777 田中五郎     6.80  5  0.17  1.52.5
   5  3 3999 鈴木三郎     6.90  3  0.18  1.53.0
   6  6 4888 伊藤六郎     7.00  6  0.20  1.54.0
01KEND
"""


class TestUrl(unittest.TestCase):
    def test_lzh_url(self):
        u = official.lzh_url("k", date(2026, 7, 26))
        self.assertTrue(u.endswith("/K/202607/k260726.lzh"))
        u2 = official.lzh_url("b", date(2026, 1, 3))
        self.assertTrue(u2.endswith("/B/202601/b260103.lzh"))


class TestParseB(unittest.TestCase):
    def test_parse_b(self):
        progs = official.parse_b_text(B_TEXT, "2026-07-26")
        self.assertEqual(len(progs), 1)
        p = progs[0]
        self.assertEqual(p.venue, "桐生")
        self.assertEqual(p.race_no, 1)
        self.assertEqual(len(p.entries), 6)
        e1 = [e for e in p.entries if e.boat == 1][0]
        self.assertEqual(e1.name, "山田太郎")
        self.assertEqual(e1.national_win_rate, 6.50)
        self.assertEqual(e1.motor_top2_rate, 55.20)
        self.assertEqual(e1.boat_top2_rate, 48.30)
        self.assertEqual(e1.klass, "A1")


class TestParseK(unittest.TestCase):
    def test_parse_k_conditions_and_order(self):
        results = official.parse_k_text(K_TEXT, "2026-07-26")
        self.assertEqual(len(results), 1)
        r = results[0]
        self.assertEqual(r.venue, "桐生")
        self.assertEqual(r.conditions.weather, "晴")
        self.assertEqual(r.conditions.wind_speed, 3.0)
        self.assertEqual(r.conditions.wave_height, 2.0)
        self.assertEqual(r.finishing_order, [2, 1, 4, 5, 3, 6])
        self.assertAlmostEqual(r.start_timing[2], 0.14)


class TestFetchProgram(unittest.TestCase):
    """ネットワークをモックして、番組表→Race 選択ロジックを検証。"""

    def setUp(self):
        self._dl = official.download
        self._ex = official.extract_lzh
        official.download = lambda url, timeout=30: b"DUMMY"
        official.extract_lzh = lambda data: B_TEXT

    def tearDown(self):
        official.download = self._dl
        official.extract_lzh = self._ex

    def test_fetch_by_name_and_code(self):
        r1 = official.fetch_program(date(2026, 7, 26), "桐生", 1)
        self.assertEqual(r1.venue, "桐生")
        self.assertEqual(len(r1.entries), 6)
        r2 = official.fetch_program(date(2026, 7, 26), "01", 1)
        self.assertEqual(r2.venue, "桐生")

    def test_missing_venue_lists_available(self):
        with self.assertRaises(RuntimeError) as ctx:
            official.fetch_program(date(2026, 7, 26), "児島", 1)
        self.assertIn("桐生", str(ctx.exception))

    def test_missing_race_lists_available(self):
        with self.assertRaises(RuntimeError) as ctx:
            official.fetch_program(date(2026, 7, 26), "桐生", 5)
        self.assertIn("1", str(ctx.exception))


class TestJoin(unittest.TestCase):
    def test_build_races_with_results(self):
        b = official.parse_b_text(B_TEXT, "2026-07-26")
        k = official.parse_k_text(K_TEXT, "2026-07-26")
        pairs = official.build_races_with_results(b, k)
        self.assertEqual(len(pairs), 1)
        race, order = pairs[0]
        self.assertEqual(order, [2, 1, 4, 5, 3, 6])
        self.assertEqual(race.conditions.weather, "晴")
        self.assertEqual(len(race.entries), 6)


if __name__ == "__main__":
    unittest.main()
