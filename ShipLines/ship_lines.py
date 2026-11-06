#!/usr/bin/env python3
"""예제선 오프셋 테이블 + 선도(Lines plan) 생성기  (표준 라이브러리만 사용)

출력 (기본: 이 스크립트가 있는 폴더의 output/)
  halfbreadths.csv  : 스테이션 x 수선(WL) 반폭 표 [m]
  heights.csv       : 스테이션별 키일 / 버톡 높이 / 갑판 높이 / 갑판 반폭 [m]
  lines_plan.svg    : 선도 (Sheer plan, Half-breadth plan, Body plan) + 오프셋 표
  lines_plan.dxf    : 같은 도면의 DXF R12 (1 m = 1000 mm)

선체는 아래 해석식으로 정의한다(좌표: x 선미 AP=0 -> 선수 FP=L, y 중심선에서 현측, z 기선에서 위).
다른 선박으로 바꾸려면 PARTICULARS 와 half_breadth()/z_keel()/z_deck()/x_end()만 고치면 된다.

사용법:  python3 ship_lines.py [출력폴더]
"""
import csv
import math
import os
import sys

# ----------------------------------------------------------------- 주요 치수
PARTICULARS = dict(
    name="EXAMPLE CARGO VESSEL",
    L=100.0,   # Lpp [m]  (AP=0, FP=L)
    B=16.0,    # 형폭 [m]
    T=6.0,     # 설계 흘수 [m]
    D=9.0,     # 형심 [m] (선체 중앙 갑판 높이)
)
L, B, T, D = (PARTICULARS[k] for k in ("L", "B", "T", "D"))

N_INT = 20                                   # 스테이션 구간 수 (0..20)
STATIONS = [float(i) for i in range(N_INT + 1)] + [20.5]
WATERLINES = [0.0, 1.0, 2.0, 3.0, 4.0, 5.0, 6.0, 7.0, 8.0, 9.0]
BUTTOCKS = [2.0, 4.0, 6.0]                   # 중심선에서의 거리 [m]


def st_x(s):
    """스테이션 번호 -> x [m]"""
    return s * L / N_INT


# ----------------------------------------------------------------- 선형 정의
def z_keel(xi):
    """키일 라인 (선미로 갈수록 올라감)"""
    return 1.2 * max(0.0, (0.20 - xi) / 0.20) ** 2


def z_deck(xi):
    """현측 갑판(sheer) 높이"""
    return (D + 1.5 * max(0.0, (xi - 0.55) / 0.45) ** 2
            + 0.7 * max(0.0, (0.35 - xi) / 0.35) ** 2)


def x_end(z):
    """높이 z 에서 선수 끝(스템) 위치 xi (선수 돌출)"""
    if z <= T:
        return 0.97 + 0.03 * max(z, 0.0) / T
    return 1.0 + 0.04 * (z - T) / (D - T)


def half_breadth(x, z):
    """위치 (x, z) 의 반폭 [m]. 선체 밖이면 0."""
    xi = x / L
    zk = z_keel(xi)
    xe = x_end(z)
    if xi < 0.0 or xi > xe or z <= zk:
        return 0.0
    # 평면 형상: 선미 트랜섬 폭 0.38, 평행부 0.30~0.62, 선수는 z 에 따라 늘어나는 끝점까지
    if xi < 0.30:
        f = 1.0 - (1.0 - 0.38) * ((0.30 - xi) / 0.30) ** 2.0
    elif xi <= 0.62:
        f = 1.0
    else:
        u = (xi - 0.62) / (xe - 0.62)
        f = 1.0 - min(u, 1.0) ** 2.3
    hb = 0.5 * B * f
    if z <= T:
        t = (z - zk) / (T - zk)
        p = 2.0 + 4.5 * f * f           # 중앙부: 각진 빌지, 선수미: 완만한 V
        s = 1.0 - (1.0 - t) ** p
    else:
        fl = (0.02 + 0.10 * max(0.0, (xi - 0.5) / 0.5) ** 1.5
              + 0.04 * max(0.0, (0.3 - xi) / 0.3))
        s = 1.0 + fl * (z - T) / T      # 건현부 플레어
    return hb * s


# ----------------------------------------------------------------- 기하 계산
def bisect(fn, lo, hi, n=50):
    """fn(lo)와 fn(hi)의 부호가 다르다고 보고 경계점을 찾는다."""
    flo = fn(lo)
    for _ in range(n):
        mid = 0.5 * (lo + hi)
        fm = fn(mid)
        if (fm > 0) == (flo > 0):
            lo, flo = mid, fm
        else:
            hi = mid
    return 0.5 * (lo + hi)


def z_on_buttock(x, b):
    """x 위치에서 중심선 거리 b 의 버톡이 지나는 높이. 없으면 None."""
    xi = x / L
    zk, zd = z_keel(xi), z_deck(xi)
    if half_breadth(x, zd - 1e-9) < b:
        return None
    return bisect(lambda z: half_breadth(x, z) - b, zk, zd)


def valid(x, z):
    xi = x / L
    return z_keel(xi) <= z <= z_deck(xi) and xi <= x_end(z) and half_breadth(x, z) > 0


def x_interval(z, step=0.25):
    """높이 z 에서 선체가 존재하는 x 구간 [xa, xb]"""
    xs = [i * step for i in range(int(1.12 * L / step) + 1)]
    ok = [valid(x + 1e-7, z) for x in xs]
    if not any(ok):
        return None
    i0 = ok.index(True)
    i1 = len(ok) - 1 - ok[::-1].index(True)
    xa = xs[i0] if i0 == 0 else bisect(lambda x: 1 if valid(x, z) else -1, xs[i0 - 1], xs[i0])
    xb = xs[i1] if i1 == len(xs) - 1 else bisect(lambda x: 1 if valid(x, z) else -1, xs[i1 + 1], xs[i1])
    return xa + 1e-7, xb - 1e-7


def cosine_samples(a, b, n):
    return [a + (b - a) * (1 - math.cos(math.pi * i / n)) / 2 for i in range(n + 1)]


def stem_top():
    """스템과 갑판선이 만나는 높이"""
    return bisect(lambda z: z_deck(x_end(z)) - z, T, 14.0)


def profile_outline():
    """Sheer plan 외곽선: 키일 -> 스템 -> 갑판선 -> 트랜섬"""
    zt = stem_top()
    xs0 = x_end(0.0) * L
    keel = [(x, z_keel(x / L)) for x in cosine_samples(0.0, xs0, 100)]
    stem = [(x_end(z) * L, z) for z in [zt * i / 80 for i in range(81)]]
    xt = stem[-1][0]
    sheer = [(x, z_deck(x / L)) for x in cosine_samples(xt, 0.0, 120)]
    return keel + stem[1:] + sheer + [(0.0, z_keel(0.0)), keel[0]][:1]


def buttock_curve(b):
    pts = []
    step = 0.25
    last_ok = None
    for i in range(int(1.12 * L / step) + 1):
        x = i * step
        z = z_on_buttock(x, b)
        if z is None:
            if last_ok is not None:
                x_edge = bisect(lambda xx: 1 if z_on_buttock(xx, b) is not None else -1, last_ok, x)
                z_edge = z_on_buttock(x_edge - 1e-7, b)
                if z_edge is not None:
                    pts.append((x_edge - 1e-7, z_edge))
                break
            continue
        last_ok = x
        pts.append((x, z))
    return pts


def waterline_curve(z):
    iv = x_interval(z)
    if iv is None:
        return []
    return [(x, half_breadth(x, z)) for x in cosine_samples(iv[0], iv[1], 160)]


def station_curve(s):
    """스테이션 단면 (y, z) 점들"""
    x = st_x(s)
    xi = x / L
    zk, zd = z_keel(xi), z_deck(xi)
    pts = [(half_breadth(x, z), z) for z in [zk + (zd - zk) * i / 120 for i in range(121)]]
    pts = [p for p in pts if p[0] > 1e-6]
    return pts


def displacement():
    """설계 흘수까지 배수 용적 [m^3]과 방형계수 Cb"""
    n, m = 400, 120
    areas = []
    for i in range(n + 1):
        x = L * i / n
        zk = z_keel(x / L)
        a = 0.0
        for j in range(m):
            z0 = zk + (T - zk) * j / m
            z1 = zk + (T - zk) * (j + 1) / m
            a += 0.5 * (half_breadth(x, z0) + half_breadth(x, z1)) * (z1 - z0)
        areas.append(2 * a)
    vol = sum(0.5 * (areas[i] + areas[i + 1]) for i in range(n)) * L / n
    return vol, vol / (L * B * T)


# ----------------------------------------------------------------- 오프셋 표
def fmt(v):
    return "" if v is None else "%.3f" % v


def build_tables():
    hb_rows = []
    for s in STATIONS:
        x = st_x(s)
        row = []
        for z in WATERLINES:
            h = half_breadth(x + (1e-7 if x == 0 else 0), z)
            row.append(h if h > 1e-6 else None)
        hb_rows.append(row)
    ht_rows = []
    for s in STATIONS:
        x = st_x(s)
        xi = x / L
        xx = x + (1e-7 if x == 0 else 0)
        zd = z_deck(xi)
        deck_exists = xi <= x_end(zd) and half_breadth(xx, zd - 1e-9) > 1e-6
        row = [z_keel(xi) if xi <= x_end(z_keel(xi)) and half_breadth(xx, z_keel(xi) + 1e-4) > 0 else None]
        for b in BUTTOCKS:
            row.append(z_on_buttock(xx, b))
        row.append(zd if deck_exists else None)
        row.append(half_breadth(xx, zd - 1e-9) if deck_exists else None)
        ht_rows.append(row)
    return hb_rows, ht_rows


def station_label(s):
    return ("%g" % s)


def write_csv(outdir, hb_rows, ht_rows):
    with open(os.path.join(outdir, "halfbreadths.csv"), "w", newline="", encoding="utf-8") as f:
        w = csv.writer(f)
        w.writerow(["Station", "x[m]"] + ["WL %g" % z for z in WATERLINES])
        for s, row in zip(STATIONS, hb_rows):
            w.writerow([station_label(s), "%.3f" % st_x(s)] + [fmt(v) for v in row])
    with open(os.path.join(outdir, "heights.csv"), "w", newline="", encoding="utf-8") as f:
        w = csv.writer(f)
        w.writerow(["Station", "x[m]", "Keel"] + ["Buttock %g" % b for b in BUTTOCKS]
                   + ["Deck height", "Deck half-breadth"])
        for s, row in zip(STATIONS, ht_rows):
            w.writerow([station_label(s), "%.3f" % st_x(s)] + [fmt(v) for v in row])


# ----------------------------------------------------------------- 도면 (미터 좌표계)
class Sheet:
    """도면 요소 모음. 좌표: m, x 오른쪽 / y 위쪽. 레이어 이름으로 선 종류 구분."""
    STYLE = {  # layer: (svg color, dxf aci, svg stroke width [m])
        "FRAME": ("#222222", 7, 0.10),
        "GRID": ("#b8b8b8", 8, 0.04),
        "STATION": ("#1f55c4", 5, 0.07),
        "WATERLINE": ("#d62828", 1, 0.07),
        "BUTTOCK": ("#0a8f6a", 4, 0.07),
        "PROFILE": ("#111111", 7, 0.12),
        "DECK": ("#8a2be2", 6, 0.08),
        "CL": ("#777777", 30, 0.05),
        "TABLE": ("#444444", 8, 0.04),
        "TEXT": ("#111111", 7, 0.0),
    }

    def __init__(self):
        self.polys = []   # (layer, [(x, y)...])
        self.texts = []   # (layer, x, y, height, string, anchor)  anchor: l|m|r

    def line(self, layer, x1, y1, x2, y2):
        self.polys.append((layer, [(x1, y1), (x2, y2)]))

    def poly(self, layer, pts):
        if len(pts) >= 2:
            self.polys.append((layer, list(pts)))

    def text(self, x, y, h, s, anchor="m", layer="TEXT"):
        self.texts.append((layer, x, y, h, s, anchor))

    # ---- SVG
    def to_svg(self, path, scale=9.0, pad=6.0, title=""):
        xs = [p[0] for _, pl in self.polys for p in pl]
        ys = [p[1] for _, pl in self.polys for p in pl]
        x0, x1, y0, y1 = min(xs) - pad, max(xs) + pad, min(ys) - pad, max(ys) + pad
        w, h = (x1 - x0) * scale, (y1 - y0) * scale
        out = ['<?xml version="1.0" encoding="UTF-8"?>',
               '<svg xmlns="http://www.w3.org/2000/svg" width="%.0f" height="%.0f" '
               'viewBox="%.3f %.3f %.3f %.3f">' % (w, h, x0, -y1, x1 - x0, y1 - y0),
               '<title>%s</title>' % title,
               '<rect x="%.3f" y="%.3f" width="%.3f" height="%.3f" fill="white"/>' % (x0, -y1, x1 - x0, y1 - y0)]
        for layer, pl in self.polys:
            col, _, sw = self.STYLE[layer]
            pts = " ".join("%.4f,%.4f" % (x, -y) for x, y in pl)
            out.append('<polyline points="%s" fill="none" stroke="%s" stroke-width="%.3f" '
                       'stroke-linejoin="round" stroke-linecap="round"/>' % (pts, col, sw))
        anch = {"l": "start", "m": "middle", "r": "end"}
        for layer, x, y, hgt, s, a in self.texts:
            s = s.replace("&", "&amp;").replace("<", "&lt;")
            out.append('<text x="%.3f" y="%.3f" font-size="%.3f" text-anchor="%s" '
                       'font-family="Arial, Helvetica, sans-serif" fill="%s">%s</text>'
                       % (x, -y + hgt * 0.35, hgt, anch[a], self.STYLE[layer][0], s))
        out.append("</svg>")
        with open(path, "w", encoding="utf-8") as f:
            f.write("\n".join(out))

    # ---- DXF R12 (1 m = 1000 mm)
    def to_dxf(self, path, mm=1000.0):
        g = []

        def put(code, val):
            g.append("%3d" % code)
            g.append(val if isinstance(val, str) else ("%.4f" % val))

        put(0, "SECTION"); put(2, "TABLES")
        put(0, "TABLE"); put(2, "LTYPE"); put(70, "1")
        put(0, "LTYPE"); put(2, "CONTINUOUS"); put(70, "0"); put(3, "Solid line"); put(72, "65"); put(73, "0"); put(40, 0.0)
        put(0, "ENDTAB")
        put(0, "TABLE"); put(2, "LAYER"); put(70, str(len(self.STYLE)))
        for name, (_, aci, _) in self.STYLE.items():
            put(0, "LAYER"); put(2, "LINES_" + name); put(70, "0"); put(62, str(aci)); put(6, "CONTINUOUS")
        put(0, "ENDTAB"); put(0, "ENDSEC")
        put(0, "SECTION"); put(2, "ENTITIES")
        for layer, pl in self.polys:
            put(0, "POLYLINE"); put(8, "LINES_" + layer); put(66, "1"); put(70, "0")
            for x, y in pl:
                put(0, "VERTEX"); put(8, "LINES_" + layer); put(10, x * mm); put(20, y * mm); put(30, 0.0)
            put(0, "SEQEND"); put(8, "LINES_" + layer)
        for layer, x, y, hgt, s, a in self.texts:
            s = s.encode("ascii", "replace").decode("ascii")
            put(0, "TEXT"); put(8, "LINES_" + layer)
            put(10, x * mm); put(20, (y - hgt * 0.5) * mm); put(30, 0.0)
            put(40, hgt * mm); put(1, s)
            if a != "l":
                put(72, "1" if a == "m" else "2")
                put(11, x * mm); put(21, (y - hgt * 0.5) * mm); put(31, 0.0)
        put(0, "ENDSEC"); put(0, "EOF")
        with open(path, "w", encoding="ascii", newline="\r\n") as f:
            f.write("\n".join(g) + "\n")


# 도면 배치 (m)
X_MAX = 1.07 * L          # 도면에 그리는 x 최대 (스템 끝까지)
PLAN_CL = -16.0           # Half-breadth plan 중심선 y
BODY_CX = 128.0           # Body plan 중심선 x
HALF_W = 9.0              # Body / Half-breadth 그리드 반폭
Z_TOP = 12.0              # 그리드 상단 높이


def build_sheet(hb_rows, ht_rows):
    sh = Sheet()
    tx = 0.75   # 일반 글자 높이

    # ---------- Sheer plan (Profile)
    sh.text(0, Z_TOP + 3.5, 1.6, "SHEER PLAN (PROFILE)", "l")
    sh.line("CL", 0, 0, X_MAX, 0)                                   # 기선
    for z in WATERLINES:
        sh.line("GRID", 0, z, X_MAX, z)
        sh.text(-1.0, z, tx, "WL %g" % z, "r")
    for s in STATIONS:
        x = st_x(s)
        sh.line("GRID", x, 0, x, Z_TOP)
        sh.text(x, Z_TOP + 1.0, tx, station_label(s))
    for b in BUTTOCKS:
        cur = buttock_curve(b)
        sh.poly("BUTTOCK", cur)
        if cur:
            sh.text(cur[-1][0] - 0.6, cur[-1][1] - 0.9, tx, "BUTTOCK %g" % b, "r", "BUTTOCK")
    sh.poly("PROFILE", profile_outline())
    sh.line("PROFILE", 0, z_keel(0), 0, z_deck(0))                  # 트랜섬

    # ---------- Half-breadth plan
    sh.text(0, PLAN_CL + HALF_W + 3.0, 1.6, "HALF-BREADTH PLAN", "l")
    sh.line("CL", 0, PLAN_CL, X_MAX, PLAN_CL)
    for yb in [0.0] + BUTTOCKS + [8.0]:
        sh.line("GRID", 0, PLAN_CL + yb, X_MAX, PLAN_CL + yb)
        sh.text(-1.0, PLAN_CL + yb, tx, "CL" if yb == 0 else "%g" % yb, "r")
    for s in STATIONS:
        x = st_x(s)
        sh.line("GRID", x, PLAN_CL, x, PLAN_CL + HALF_W)
        sh.text(x, PLAN_CL + HALF_W + 1.0, tx, station_label(s))
    for z in WATERLINES:
        cur = waterline_curve(z)
        sh.poly("WATERLINE", [(x, PLAN_CL + y) for x, y in cur])
        if cur and z in (0.0, T, WATERLINES[-1]):
            sh.text(cur[-1][0] + 0.8, PLAN_CL + cur[-1][1], tx, "WL %g" % z, "l", "WATERLINE")
    # 갑판 가장자리 (현측 sheer 의 평면 투영)
    deck = []
    for x in cosine_samples(0.0, x_end(stem_top()) * L, 160):
        zd = z_deck(x / L)
        deck.append((x, PLAN_CL + half_breadth(x + 1e-7, zd - 1e-9)))
    sh.poly("DECK", deck)
    sh.line("PROFILE", 0, PLAN_CL, 0, PLAN_CL + half_breadth(1e-7, z_deck(0) - 1e-9))   # 트랜섬

    # ---------- Body plan
    sh.text(BODY_CX - HALF_W, Z_TOP + 3.5, 1.6, "BODY PLAN", "l")
    sh.line("CL", BODY_CX, 0, BODY_CX, Z_TOP)
    sh.text(BODY_CX, Z_TOP + 1.0, tx, "CL")
    sh.line("CL", BODY_CX - HALF_W, 0, BODY_CX + HALF_W, 0)
    for z in WATERLINES:
        sh.line("GRID", BODY_CX - HALF_W, z, BODY_CX + HALF_W, z)
    for yb in BUTTOCKS + [8.0]:
        for sg in (-1, 1):
            sh.line("GRID", BODY_CX + sg * yb, 0, BODY_CX + sg * yb, Z_TOP)
    sh.text(BODY_CX - HALF_W - 0.5, T, tx, "DWL", "r", "WATERLINE")
    sh.text(BODY_CX - HALF_W / 2, -1.2, tx, "AFT  (STA 0-9)", "m")
    sh.text(BODY_CX + HALF_W / 2, -1.2, tx, "FWD  (STA 10-20.5)", "m")
    for s in STATIONS:
        cur = station_curve(s)
        sg = -1 if s < N_INT / 2 else 1
        sh.poly("STATION", [(BODY_CX + sg * y, z) for y, z in cur])
        if cur and int(s * 2) % 2 == 0 and int(s) % 2 == 0:
            y, z = cur[-1]
            sh.text(BODY_CX + sg * (y + 0.3), z + 0.2, 0.55, station_label(s), "r" if sg < 0 else "l", "STATION")

    # ---------- 오프셋 표
    row_h, tx_h = 1.6, 0.8
    col_w = 4.4
    lab_w = 12.0
    tab_x = 0.0
    top = PLAN_CL - 5.0

    def table(title, y_top, headers, rows):
        sh.text(tab_x, y_top + 1.2, 1.2, title, "l")
        ncol = len(headers)
        width = lab_w + col_w * len(STATIONS)
        nrow = len(rows) + 1
        for r in range(nrow + 1):
            sh.line("TABLE", tab_x, y_top - r * row_h, tab_x + width, y_top - r * row_h)
        for c in range(len(STATIONS) + 2):
            xx = tab_x + (0 if c == 0 else lab_w + col_w * (c - 1))
            sh.line("TABLE", xx, y_top, xx, y_top - nrow * row_h)
        sh.text(tab_x + lab_w / 2, y_top - row_h / 2, tx_h, headers[0], "m")
        for i, s in enumerate(STATIONS):
            sh.text(tab_x + lab_w + col_w * (i + 0.5), y_top - row_h / 2, tx_h, station_label(s), "m")
        for r, (lab, vals) in enumerate(rows):
            yy = y_top - row_h * (r + 1.5)
            sh.text(tab_x + lab_w / 2, yy, tx_h, lab, "m")
            for i, v in enumerate(vals):
                if v is not None:
                    sh.text(tab_x + lab_w + col_w * (i + 0.5), yy, tx_h * 0.9, "%.3f" % v, "m")
        return y_top - nrow * row_h

    rows1 = [("WL %g" % z, [hb_rows[i][j] for i in range(len(STATIONS))]) for j, z in enumerate(WATERLINES)]
    y_end = table("TABLE OF OFFSETS 1 : HALF-BREADTHS AT WATERLINES [m]", top, ["Station"], rows1)
    names2 = ["Keel"] + ["Buttock %g" % b for b in BUTTOCKS] + ["Deck height", "Deck half-br."]
    rows2 = [(names2[k], [ht_rows[i][k] for i in range(len(STATIONS))]) for k in range(len(names2))]
    y_end2 = table("TABLE OF OFFSETS 2 : HEIGHTS ABOVE BASELINE [m]", y_end - 4.0, ["Station"], rows2)

    # ---------- 표제란 / 주요 치수
    vol, cb = displacement()
    bx, by = BODY_CX - HALF_W - 2, top
    sh.line("FRAME", bx, by, bx + 40, by); sh.line("FRAME", bx, by - 21, bx + 40, by - 21)
    sh.line("FRAME", bx, by, bx, by - 21); sh.line("FRAME", bx + 40, by, bx + 40, by - 21)
    sh.text(bx + 1, by - 2.0, 1.4, "LINES PLAN - " + PARTICULARS["name"], "l")
    items = ["Lpp = %.2f m" % L, "B (moulded) = %.2f m" % B, "T (design) = %.2f m" % T,
             "D (moulded) = %.2f m" % D, "Displacement = %.0f m3 (moulded)" % vol,
             "Cb = %.3f" % cb, "Stations: AP=0 ... FP=20,  spacing %.2f m" % (L / N_INT)]
    for i, t in enumerate(items):
        sh.text(bx + 1, by - 4.6 - i * 2.2, 1.0, t, "l")
    return sh, (vol, cb)


def main():
    outdir = sys.argv[1] if len(sys.argv) > 1 else os.path.join(os.path.dirname(os.path.abspath(__file__)), "output")
    os.makedirs(outdir, exist_ok=True)
    hb_rows, ht_rows = build_tables()
    write_csv(outdir, hb_rows, ht_rows)
    sh, (vol, cb) = build_sheet(hb_rows, ht_rows)
    sh.to_svg(os.path.join(outdir, "lines_plan.svg"), title="Lines plan - " + PARTICULARS["name"])
    sh.to_dxf(os.path.join(outdir, "lines_plan.dxf"))
    print("Lpp=%.1f B=%.1f T=%.1f D=%.1f  V=%.0f m3  Cb=%.3f" % (L, B, T, D, vol, cb))
    print("저장:", outdir)


if __name__ == "__main__":
    main()
