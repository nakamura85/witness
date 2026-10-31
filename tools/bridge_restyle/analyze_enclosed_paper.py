# -*- coding: utf-8 -*-
"""v3.53 fix: the v3.52 border-connected flood fill could not reach the paper
trapped INSIDE the closed truss frame (top/bottom chords + end posts enclose
every lattice cell), so the cells kept an opaque paper backing and the
mountains never showed through (user screenshot 2026-10-31).

This script analyses the CURRENT bridge.png: paperish components that are NOT
border-connected, reporting size and bbox, so we can decide which to clear
(truss pockets) and which to keep (snow caps on the pier tops).
"""
import numpy as np
from PIL import Image

a = np.array(Image.open("assets/art/props/bridge.png"))
alpha = a[..., 3]
rgb = a[..., :3].astype(int)
mn = rgb.min(axis=2)
paperish = (mn > 200) & (alpha > 128)
H, W = paperish.shape

# border reach via iterative dilation (same as process script)
reach = np.zeros_like(paperish)
reach[0, :] = paperish[0, :]
reach[-1, :] = paperish[-1, :]
reach[:, 0] = paperish[:, 0]
reach[:, -1] = paperish[:, -1]
while True:
    grow = reach.copy()
    grow[1:, :] |= reach[:-1, :]
    grow[:-1, :] |= reach[1:, :]
    grow[:, 1:] |= reach[:, :-1]
    grow[:, :-1] |= reach[:, 1:]
    grow &= paperish
    if (grow == reach).all():
        break
    reach = grow

enclosed = paperish & ~reach
# label enclosed components with BFS
lbl = np.zeros((H, W), dtype=np.int32)
cur = 0
comps = []
ys0, xs0 = np.where(enclosed)
from collections import deque
for sy, sx in zip(ys0, xs0):
    if lbl[sy, sx]:
        continue
    cur += 1
    q = deque([(sy, sx)])
    lbl[sy, sx] = cur
    pix = []
    while q:
        y, x = q.popleft()
        pix.append((y, x))
        for dy, dx in ((1,0),(-1,0),(0,1),(0,-1)):
            ny, nx = y+dy, x+dx
            if 0 <= ny < H and 0 <= nx < W and enclosed[ny, nx] and not lbl[ny, nx]:
                lbl[ny, nx] = cur
                q.append((ny, nx))
    pa = np.array(pix)
    comps.append((len(pix), pa[:,0].mean(), pa[:,0].min(), pa[:,0].max(),
                  pa[:,1].min(), pa[:,1].max()))

comps.sort(reverse=True)
print("enclosed paper components:")
for c in comps[:30]:
    print(f"  size={c[0]:7d} cy={c[1]:6.0f} y={c[2]:4d}-{c[3]:4d} x={c[4]:5d}-{c[5]:5d}")
print("total enclosed", len(comps), "px", sum(c[0] for c in comps))
