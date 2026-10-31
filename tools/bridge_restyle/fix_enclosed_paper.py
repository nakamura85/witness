# -*- coding: utf-8 -*-
"""v3.53 fix: clear the paper trapped INSIDE the closed truss frame of
bridge.png. The v3.52 border-connected flood fill could not reach lattice
cells enclosed by the chords, leaving an opaque paper backing behind the
truss (user screenshot 2026-10-31).

Method: enclosed paperish components (not border-connected, min channel
> 200) whose bounding box lies fully above row 425 are background pockets
between truss members / under the deck - clear them. Components lower than
that are the snow caps sitting on the pier tops - keep. Feather 1px.

Runs on the current aligned texture (no re-warp), writes a preview first.
"""
import numpy as np
from PIL import Image, ImageFilter
from collections import deque

SRC = "assets/art/props/bridge.png"
PREVIEW = "tools/bridge_restyle/previews/bridge_transparent_fixed.png"

a = np.array(Image.open(SRC))
alpha = a[..., 3].copy()
rgb = a[..., :3].astype(int)
mn = rgb.min(axis=2)
paperish = (mn > 200) & (alpha > 128)
H, W = paperish.shape

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
lbl = np.zeros((H, W), dtype=bool)
cleared = 0
kept = 0
ys0, xs0 = np.where(enclosed)
for sy, sx in zip(ys0, xs0):
    if lbl[sy, sx]:
        continue
    q = deque([(sy, sx)])
    lbl[sy, sx] = True
    pix = []
    while q:
        y, x = q.popleft()
        pix.append((y, x))
        for dy, dx in ((1, 0), (-1, 0), (0, 1), (0, -1)):
            ny, nx = y + dy, x + dx
            if 0 <= ny < H and 0 <= nx < W and enclosed[ny, nx] and not lbl[ny, nx]:
                lbl[ny, nx] = True
                q.append((ny, nx))
    pa = np.array(pix)
    y1 = pa[:, 0].max()
    if y1 < 425:          # truss pocket / under-deck gap -> background
        alpha[pa[:, 0], pa[:, 1]] = 0
        cleared += len(pix)
    else:                 # low content (snow caps on piers) -> keep
        kept += len(pix)

print(f"cleared {cleared} px, kept {kept} px")
alpha_img = Image.fromarray(alpha, "L").filter(ImageFilter.GaussianBlur(1.0))
out = Image.open(SRC).convert("RGBA")
out.putalpha(alpha_img)
out.save(PREVIEW)
print("saved", PREVIEW)
