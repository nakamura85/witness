# -*- coding: utf-8 -*-
"""v3.62: 清理 radio_station.png 的抠图残留。
两类残留（用户 2026-11-11 截图实锤，夜景下白色矩形刺眼）：
  1. 两个完全不透明的纯白矩形（天线杆两侧）——AI 生成时留下的实心白块；
  2. 旗布区域的「棋盘格假透明」（ImageGen 把透明画成了白/灰棋盘格，
     记忆里的老坑：假透明棋盘格）。
策略：32px tile 级判定——
  - 白矩形 tile：低饱和白像素占比 >0.82；
  - 棋盘 tile：白 + 棋盘灰(190~220)合计占比 >0.82 且两者都 >0.12；
命中 tile 内只抠除这两种像素（保留穿插的墨线），边缘 1px 羽化。
输出到 previews 先做深底对比，不直接覆盖。
"""
from PIL import Image
import sys, os

SRC = 'assets/art/props/radio_station.png'
OUT = 'tools/bridge_restyle/previews/radio_station_clean_preview.png'
TILE = 32

im = Image.open(SRC).convert('RGBA')
W, H = im.size
px = im.load()

def classify(p):
    r, g, b, a = p
    mx, mn = max(r, g, b), min(r, g, b)
    if a < 40:
        return None
    if mx - mn >= 16:
        return None          # 带色的内容（木、墨线、罩色）
    if mn >= 236:
        return 'w'           # 低饱和白
    if 188 <= mn <= 222:
        return 'c'           # 低饱和棋盘灰
    return None

# tile 统计
tiles = {}
for ty in range(0, H, TILE):
    for tx in range(0, W, TILE):
        w = c = tot = 0
        for y in range(ty, min(ty + TILE, H)):
            for x in range(tx, min(tx + TILE, W)):
                k = classify(px[x, y])
                if k == 'w': w += 1
                elif k == 'c': c += 1
                if k: tot += 1
        n = TILE * TILE
        if w / n > 0.82:
            tiles[(tx, ty)] = 'solid'
        elif (w + c) / n > 0.82 and w / n > 0.10 and c / n > 0.10:
            tiles[(tx, ty)] = 'checker'

print('hit tiles:', len(tiles))
solid = sum(1 for v in tiles.values() if v == 'solid')
print('  solid-white:', solid, ' checker:', len(tiles) - solid)

# 命中 tile 内抠白/棋盘灰像素 + 1px 羽化（把命中像素相邻的非命中低饱和像素压暗）
kill = set()
for (tx, ty) in tiles:
    for y in range(ty, min(ty + TILE, H)):
        for x in range(tx, min(tx + TILE, W)):
            if classify(px[x, y]) is not None:
                kill.add((x, y))

# 羽化：命中像素周围 1px 的其他低饱和像素降 alpha 到 90
soft = set()
for (x, y) in kill:
    for dy in (-1, 0, 1):
        for dx in (-1, 0, 1):
            nx, ny = x + dx, y + dy
            if 0 <= nx < W and 0 <= ny < H and (nx, ny) not in kill:
                r, g, b, a = px[nx, ny]
                mx, mn = max(r, g, b), min(r, g, b)
                if mx - mn < 16 and mn > 150:
                    soft.add((nx, ny))

for (x, y) in kill:
    r, g, b, a = px[x, y]
    px[x, y] = (r, g, b, 0)
for (x, y) in soft:
    r, g, b, a = px[x, y]
    px[x, y] = (r, g, b, 90)

# 深底预览（模拟夜景观感）
bg = Image.new('RGB', (W, H), (40, 36, 50))
bg.paste(im, (0, 0), im)
bg.save(OUT)
print('preview ->', OUT)
