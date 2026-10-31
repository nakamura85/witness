# -*- coding: utf-8 -*-
"""v3.53: key out the destroyed-sandbag pile for the cover mechanic.

Border-connected flood fill + enclosed paper pockets (gaps between torn bags
trap paper the same way the bridge truss did) -> fully transparent, then
1.2px feather. Saves to assets/art/props/sandbags_destroyed.png.
"""
import numpy as np
from PIL import Image, ImageFilter
from collections import deque

SRC = "tools/bridge_restyle/previews/sandbags_destroyed_candidate.png"
DST = "assets/art/props/sandbags_destroyed.png"

im = Image.open(SRC).convert("RGB")
a = np.array(im).astype(int)
mn = a.min(axis=2)
H, W = mn.shape

# crop to content with margin
rows = np.where((mn < 200).mean(axis=1) > 0.02)[0]
cols = np.where((mn < 200).mean(axis=0) > 0.02)[0]
y0, y1 = max(0, rows[0] - 12), min(H, rows[-1] + 13)
x0, x1 = max(0, cols[0] - 12), min(W, cols[-1] + 13)
im = im.crop((x0, y0, x1, y1))
a = np.array(im).astype(int)
mn = a.min(axis=2)
H, W = mn.shape
print("cropped to", (W, H))

paperish = mn > 205
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

# enclosed pockets (between torn bags) also go
enclosed = paperish & ~reach
# protect nothing here - no snow caps; but keep tiny specks that are actually
# paper-tinted highlights ON bags: only clear enclosed components >= 40 px
lbl = np.zeros((H, W), dtype=bool)
cleared_px = 0
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
    if len(pix) >= 40:
        pa = np.array(pix)
        reach[pa[:, 0], pa[:, 1]] = True
        cleared_px += len(pix)
print("enclosed pockets cleared px:", cleared_px)

alpha = np.where(reach, 0, 255).astype(np.uint8)
alpha_img = Image.fromarray(alpha, "L").filter(ImageFilter.GaussianBlur(1.2))
rgba = im.convert("RGBA")
rgba.putalpha(alpha_img)
rgba.save(DST)
print("saved", DST, rgba.size)
