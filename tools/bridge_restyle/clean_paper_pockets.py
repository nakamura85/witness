# -*- coding: utf-8 -*-
"""
clean_paper_pockets.py — 清除贴图中遗留的纸底白口袋（v3.63）

背景：v3.62 只清了外围纸底，物体之间/烧穿间隙里封闭的纸白口袋没掉。
特征判定：
  - 纸底残留核心：不透明、纯平白（min>=247 且色度<=6）
  - 合法积雪核心：同样纯平白，但周边环是亮白雪（有晕染）→ 跳过不动
处理方式：
  - 口袋触到外轮廓透明区 → 抠透明 + 1px 羽化
  - 封闭口袋（门洞/窗洞/椽架间隙/木架三角口）→ 从边界向内 BFS 回填周边
    深色洗染色 + 轻微抖动，保持水彩质感
用法：python tools/bridge_restyle/clean_paper_pockets.py [--dry-run]
"""
import sys
import os
import random
from collections import deque
from PIL import Image

random.seed(20261111)

# 处理文件清单（相对仓库根）
FILES = [
    'assets/art/props/house_intact_near.png',
    'assets/art/props/house_intact_near_autumn.png',
    'assets/art/props/house_burned_near.png',
    'assets/art/props/house_ruined_near.png',
    'assets/art/props/house_skeleton_near.png',
    'assets/art/props/house_posts.png',
    'assets/art/props/sandbags.png',
    'assets/art/props/telcrate.png',
    'assets/art/props/crate.png',
    'assets/art/props/rifle.png',
    'assets/art/props/radio_station.png',
    'assets/art/backdrop/village.png',
    'assets/art/backdrop/village_ruined.png',
    'assets/art/backdrop/mountains.png',
    'assets/art/backdrop/tree_elm_autumn.png',
    'assets/art/sprites/corpse_ija_prone.png',
    'assets/art/sprites/corpse_ija_supine.png',
    'assets/art/sprites/corpse_na_prone.png',
    'assets/art/sprites/corpse_na_supine.png',
]

CORE_MIN = 247      # 纸白核心：min channel 下限
CORE_CHROMA = 6     # 纸白核心：max-min 色度上限
FLOOD_MIN = 236     # 泛洪扩展：亮度下限
FLOOD_CHROMA = 14   # 泛洪扩展：色度上限
FLOOD_DIST = 22     # 泛洪扩展：距核心最远距离(px)
CORE_MINPX = 30     # 核心聚类最小像素数
RING_R = 3          # 周边环半径（小半径防扫到远处暗色物体误伤积雪）
RING_BRIGHT = 150   # 环像素「亮」判定（积雪环）
SKIP_BRIGHT_FRAC = 0.45  # 环中亮像素占比超过此值 → 判为积雪，跳过
TRANS_REACH = 6     # 口袋 6px 内碰到任意透明像素 → 判为露天，抠透明


def find_cores(px, w, h):
    """找纯平白核心聚类。返回 [(set_of_idx, x0,y0,x1,y1), ...]"""
    mask = bytearray(w * h)
    for y in range(h):
        row = y * w
        for x in range(w):
            r, g, b, a = px[x, y]
            if a > 200 and min(r, g, b) >= CORE_MIN and max(r, g, b) - min(r, g, b) <= CORE_CHROMA:
                mask[row + x] = 1
    seen = bytearray(w * h)
    comps = []
    for i in range(w * h):
        if mask[i] and not seen[i]:
            q = deque([i]); seen[i] = 1
            pts = []
            while q:
                p = q.popleft(); pts.append(p)
                x, y = p % w, p // w
                for dx, dy in ((1, 0), (-1, 0), (0, 1), (0, -1)):
                    nx, ny = x + dx, y + dy
                    if 0 <= nx < w and 0 <= ny < h:
                        j = ny * w + nx
                        if mask[j] and not seen[j]:
                            seen[j] = 1; q.append(j)
            if len(pts) >= CORE_MINPX:
                xs = [p % w for p in pts]; ys = [p // w for p in pts]
                comps.append((set(pts), min(xs), min(ys), max(xs), max(ys)))
    return comps


def ring_stats(px, w, h, core_set):
    """核心周边环：亮像素占比（积雪判定）。"""
    bright = total = 0
    for p in core_set:
        x, y = p % w, p // w
        for dy in range(-RING_R, RING_R + 1):
            ny = y + dy
            if not (0 <= ny < h):
                continue
            for dx in range(-RING_R, RING_R + 1):
                nx = x + dx
                if not (0 <= nx < w):
                    continue
                q = ny * w + nx
                if q in core_set:
                    continue
                a = px[nx, ny][3]
                if a <= 150:
                    continue
                total += 1
                if min(px[nx, ny][:3]) >= RING_BRIGHT:
                    bright += 1
    if total == 0:
        return 0.0
    return bright / total


def flood_pocket(px, w, h, core_set, x0, y0, x1, y1):
    """从核心向四周泛洪纸质感像素，返回口袋像素集合。"""
    pocket = set(core_set)
    dist = {p: 0 for p in core_set}
    q = deque(core_set)
    while q:
        p = q.popleft()
        d = dist[p]
        if d >= FLOOD_DIST:
            continue
        x, y = p % w, p // w
        for dx, dy in ((1, 0), (-1, 0), (0, 1), (0, -1)):
            nx, ny = x + dx, y + dy
            if not (0 <= nx < w and 0 <= ny < h):
                continue
            j = ny * w + nx
            if j in pocket:
                continue
            r, g, b, a = px[nx, ny]
            if a > 140 and min(r, g, b) >= FLOOD_MIN and max(r, g, b) - min(r, g, b) <= FLOOD_CHROMA:
                pocket.add(j)
                dist[j] = d + 1
                q.append(j)
    return pocket


def outer_bg(px, w, h):
    """外轮廓透明区（与图边界连通的 alpha<60 区域）。"""
    outer = set()
    q = deque()
    for x in range(w):
        for y in (0, h - 1):
            i = y * w + x
            if px[x, y][3] < 60 and i not in outer:
                outer.add(i); q.append(i)
    for y in range(h):
        for x in (0, w - 1):
            i = y * w + x
            if px[x, y][3] < 60 and i not in outer:
                outer.add(i); q.append(i)
    while q:
        p = q.popleft()
        x, y = p % w, p // w
        for dx, dy in ((1, 0), (-1, 0), (0, 1), (0, -1)):
            nx, ny = x + dx, y + dy
            if 0 <= nx < w and 0 <= ny < h:
                j = ny * w + nx
                if j not in outer and px[nx, ny][3] < 60:
                    outer.add(j); q.append(j)
    return outer


def touches_outer(pocket, outer, w, h):
    """口袋是否在 2px 内触及外轮廓透明区。"""
    for p in pocket:
        x, y = p % w, p // w
        for dy in (-2, -1, 0, 1, 2):
            ny = y + dy
            if not (0 <= ny < h):
                continue
            for dx in (-2, -1, 0, 1, 2):
                nx = x + dx
                if not (0 <= nx < w):
                    continue
                if ny * w + nx in outer:
                    return True
    return False


def touches_transparent(pocket, px, w, h):
    """口袋是否在 TRANS_REACH 内碰到任意透明像素（露天空隙）。"""
    for p in pocket:
        x, y = p % w, p // w
        for dy in range(-TRANS_REACH, TRANS_REACH + 1):
            ny = y + dy
            if not (0 <= ny < h):
                continue
            for dx in range(-TRANS_REACH, TRANS_REACH + 1):
                nx = x + dx
                if not (0 <= nx < w):
                    continue
                if px[nx, ny][3] < 60:
                    return True
    return False


def diffusion_fill(px, w, h, pocket, iters=300):
    """拉普拉斯扩散修补：口袋像素由边界向内平滑扩散，出水彩晕染感。"""
    import numpy as np
    xs = [p % w for p in pocket]; ys = [p // w for p in pocket]
    x0, x1 = max(0, min(xs) - 2), min(w, max(xs) + 3)
    y0, y1 = max(0, min(ys) - 2), min(h, max(ys) + 3)
    arr = np.zeros((y1 - y0, x1 - x0, 3), dtype=np.float32)
    fixed = np.zeros((y1 - y0, x1 - x0), dtype=bool)
    inPocket = np.zeros((y1 - y0, x1 - x0), dtype=bool)
    for yy in range(y0, y1):
        for xx in range(x0, x1):
            r, g, b, a = px[xx, yy]
            arr[yy - y0, xx - x0] = (r, g, b)
            j = yy * w + xx
            if j in pocket:
                inPocket[yy - y0, xx - x0] = True
            elif a > 150:
                fixed[yy - y0, xx - x0] = True
    # 未固定且不在口袋（半透明边）也作为可扩散区
    cur = arr.copy()
    for _ in range(iters):
        avg = (np.roll(cur, 1, 0) + np.roll(cur, -1, 0) +
               np.roll(cur, 1, 1) + np.roll(cur, -1, 1)) / 4.0
        cur[inPocket] = avg[inPocket]
    for yy in range(y0, y1):
        for xx in range(x0, x1):
            if inPocket[yy - y0, xx - x0]:
                j = yy * w + xx
                r, g, b = cur[yy - y0, xx - x0]
                nz = random.gauss(0, 2)
                px[xx, yy] = (max(0, min(255, int(r + nz))),
                              max(0, min(255, int(g + nz))),
                              max(0, min(255, int(b + nz))),
                              px[xx, yy][3])


def process(path, dry=False):
    im = Image.open(path).convert('RGBA')
    w, h = im.size
    px = im.load()
    cores = find_cores(px, w, h)
    if not cores:
        return 0, 0, 0
    outer = outer_bg(px, w, h)
    n_skip = n_trans = n_fill = 0
    for core_set, x0, y0, x1, y1 in cores:
        frac = ring_stats(px, w, h, core_set)
        if frac >= SKIP_BRIGHT_FRAC:
            n_skip += 1
            continue  # 积雪核心，保留
        pocket = flood_pocket(px, w, h, core_set, x0, y0, x1, y1)
        if touches_transparent(pocket, px, w, h):
            n_trans += 1
            if not dry:
                for p in pocket:
                    x, y = p % w, p // w
                    r, g, b, a = px[x, y]
                    px[x, y] = (r, g, b, 0)
                # 1px 羽化：口袋内与外部不透明像素相邻的像素给半透明
                for p in pocket:
                    x, y = p % w, p // w
                    edge = False
                    for dx, dy in ((1, 0), (-1, 0), (0, 1), (0, -1)):
                        nx, ny = x + dx, y + dy
                        if 0 <= nx < w and 0 <= ny < h and (ny * w + nx) not in pocket and px[nx, ny][3] > 200:
                            edge = True
                            break
                    if edge:
                        r, g, b, a = px[x, y]
                        px[x, y] = (r, g, b, 110)
        else:
            n_fill += 1
            if not dry:
                diffusion_fill(px, w, h, pocket)
    if not dry and (n_trans + n_fill) > 0:
        im.save(path)
    return len(cores), n_trans, n_fill


def main():
    dry = '--dry-run' in sys.argv
    total_c = total_t = total_f = 0
    for f in FILES:
        if not os.path.exists(f):
            print('MISS', f)
            continue
        c, t, fl = process(f, dry)
        total_c += c; total_t += t; total_f += fl
        print('%-46s cores=%3d transparent=%2d recolor=%2d' % (os.path.basename(f), c, t, fl))
    print('TOTAL cores=%d transparent=%d recolor=%d%s' % (total_c, total_t, total_f, '  (dry-run)' if dry else ''))


if __name__ == '__main__':
    main()
