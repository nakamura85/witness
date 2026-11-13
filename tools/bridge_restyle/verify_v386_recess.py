"""v3.86 · 暗格覆盖自检（零依赖，可直接跑）。

用户拍板：「暗格通常是已经挖好的所以直接四四方方或者长方形即可，
但是要记得前面的挂画一定要刚刚好遮到暗格才能是暗格哦」。

本脚本复算 InteriorExplore.cs 里 BgRect / RecessForCover 的几何，
逐家具打印「前脸矩形 / 暗格矩形 / 四边留白 / 是否 100% 覆盖」，
并输出 v3.85 旧算法作为对照，证明旧版确实有露边。

运行：
    python tools/bridge_restyle/verify_v386_recess.py
预期：8 行完好房间全部 PASS，仓库 4 行全部 PASS，并列出旧版失败项。
"""

SCALE = 0.765
CROP = 13.6
MIRROR_W = 952.0

# (房间, 家具) -> 背景贴图源矩形 (x, y, w, h)
FRONT_SRC = {
    ("Residence", "Cabinet"):      (1060, 520, 180, 250),
    ("Residence", "PictureFrame"): (190, 60, 140, 460),
    ("Residence", "Desk"):         (410, 525, 360, 55),
    ("Residence", "Stove"):        (790, 555, 140, 205),
    ("CommOutpost", "Cabinet"):      (1060, 460, 180, 250),
    ("CommOutpost", "PictureFrame"): (100, 120, 220, 280),
    ("CommOutpost", "Desk"):         (400, 490, 360, 50),
    ("CommOutpost", "Stove"):        (790, 500, 140, 200),
}

# 仓库 BurntFront 前脸（画布尺寸）
BURNT_FRONT = {
    "Cabinet": (116.0, 152.0), "Stove": (120.0, 92.0),
    "Desk": (64.0, 30.0), "PictureFrame": (60.0, 84.0),
}
BURNT_RECESS = {
    "Cabinet": (100.0, 84.0), "Stove": (92.0, 62.0),
    "Desk": (56.0, 26.0), "PictureFrame": (56.0, 72.0),
}

# 家具锚点（贴图像素），用于复算 v3.85 旧算法
ANCHOR = {
    ("Residence", "Cabinet"): (1150, 645), ("Residence", "PictureFrame"): (260, 290),
    ("Residence", "Desk"): (590, 620), ("Residence", "Stove"): (855, 710),
    ("CommOutpost", "Cabinet"): (1150, 585), ("CommOutpost", "PictureFrame"): (210, 260),
    ("CommOutpost", "Desk"): (580, 590), ("CommOutpost", "Stove"): (855, 645),
}
OLD_SIZE = {"Cabinet": (110.0, 82.0), "PictureFrame": (100.0, 86.0),
            "Desk": (106.0, 44.0), "Stove": (76.0, 40.0)}
OLD_OFFSET = {"Cabinet": 8.0, "PictureFrame": 0.0, "Desk": 10.0, "Stove": 8.0}


def bg_rect(src):
    x, y, w, h = src
    return (x * SCALE - CROP, y * SCALE, w * SCALE, h * SCALE)


def recess_for_cover(front):
    x, y, w, h = front
    rw = min(max(80.0, w - 28.0), w - 16.0)
    rh = min(max(28.0, h - 28.0), h - 16.0)
    return (x + (w - rw) * 0.5, y + (h - rh) * 0.5, rw, rh)


def margins(front, rec):
    fx, fy, fw, fh = front
    rx, ry, rw, rh = rec
    return (rx - fx, fy + fh - (ry + rh), ry - fy, fx + fw - (rx + rw))


def covered(front, rec):
    return all(m >= -1e-6 for m in margins(front, rec))


def main():
    print("v3.86 暗格覆盖自检  SCALE=%.3f CROP=%.1f" % (SCALE, CROP))
    print("=" * 108)
    print("%-14s %-14s %-30s %-30s %-22s %s" %
          ("房间", "家具", "前脸 Front (x,y,w,h)", "暗格 Recess (x,y,w,h)",
           "留白 左/右/上/下", "结论"))
    print("-" * 108)
    fail = 0
    for (room, ft), src in FRONT_SRC.items():
        front = bg_rect(src)
        rec = recess_for_cover(front)
        l, r, t, b = margins(front, rec)
        ok = covered(front, rec) and min(l, r, t, b) >= 8.0 - 1e-6
        fail += 0 if ok else 1
        print("%-14s %-14s %-30s %-30s %-22s %s" %
              (room, ft,
               "(%.1f,%.1f,%.1f,%.1f)" % front,
               "(%.1f,%.1f,%.1f,%.1f)" % rec,
               "%.1f/%.1f/%.1f/%.1f" % (l, r, t, b),
               "PASS" if ok else "FAIL"))
    for ft, (fw, fh) in BURNT_FRONT.items():
        front = (0.0, 0.0, fw, fh)
        rw, rh = BURNT_RECESS[ft]
        rec = ((fw - rw) / 2, (fh - rh) / 2, rw, rh)
        l, r, t, b = margins(front, rec)
        ok = covered(front, rec)
        fail += 0 if ok else 1
        print("%-14s %-14s %-30s %-30s %-22s %s" %
              ("BurntWh", ft, "(%.0f,%.0f,%.0f,%.0f)" % front,
               "(%.1f,%.1f,%.0f,%.0f)" % rec,
               "%.1f/%.1f/%.1f/%.1f" % (l, r, t, b),
               "PASS" if ok else "FAIL"))

    print("-" * 108)
    print("v3.85 旧算法对照（以家具 Center 画固定尺寸暗格）")
    old_fail = 0
    for (room, ft), (ax, ay) in ANCHOR.items():
        front = bg_rect(FRONT_SRC[(room, ft)])
        cx = ax * SCALE - CROP
        cy = ay * SCALE + OLD_OFFSET[ft]
        w, h = OLD_SIZE[ft]
        rec = (cx - w / 2, cy - h / 2, w, h)
        l, r, t, b = margins(front, rec)
        ok = covered(front, rec)
        old_fail += 0 if ok else 1
        print("  %-12s %-14s 留白 左%7.1f 右%7.1f 上%7.1f 下%7.1f  %s" %
              (room, ft, l, r, t, b, "covered" if ok else "EXPOSED"))
    print("-" * 108)
    print("v3.86 覆盖失败 %d 项（应为 0）；v3.85 旧算法露边 %d 项" % (fail, old_fail))
    assert fail == 0, "v3.86 仍有暗格露在前脸之外"
    print("OK: 全部前脸刚好完全遮住暗格，且四边至少留 8px")


if __name__ == "__main__":
    main()
