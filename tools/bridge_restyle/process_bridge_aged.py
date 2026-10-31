# -*- coding: utf-8 -*-
"""v3.52: process the user-approved aged bridge candidate (A, heavy battle
damage) into the in-game texture.

Pipeline:
  1. two-band vertical warp onto the 1530x812 canvas so the deck line lands
     on row 355 - DrawBridge() addresses the texture by row landmarks
     (truss 0-355 stretched to BridgeH, deck+piers 355.. at original scale);
  2. paper background keyed out by BORDER-CONNECTED flood fill, so the
     bright snow caps on the pier tops survive (plain threshold would eat
     them);
  3. alpha feathered with a 1.2px gaussian.
"""
import numpy as np
from PIL import Image, ImageFilter

SRC = "tools/bridge_restyle/previews/bridge_aged_candidate_A_heavy.png"
DST = "assets/art/props/bridge.png"
PREVIEW = "tools/bridge_restyle/previews/bridge_aged_processed.png"

im = Image.open(SRC).convert("RGB")
a = np.array(im).astype(int)
H, W, _ = a.shape

# ---- landmarks (measured 2026-10-31) ----
truss_top, deck_row, pier_bottom = 20, 426, 959
out_truss_top, out_deck, out_bottom = 10, 355, 805
OW, OH = 1530, 812

band1 = im.crop((0, truss_top, W, deck_row)).resize((OW, out_deck - out_truss_top), Image.LANCZOS)
band2 = im.crop((0, deck_row, W, pier_bottom)).resize((OW, out_bottom - out_deck), Image.LANCZOS)
out = Image.new("RGB", (OW, OH), (255, 255, 255))
out.paste(band1, (0, out_truss_top))
out.paste(band2, (0, out_deck))

# ---- border-connected paper keyout ----
rgb = np.array(out).astype(int)
mn = rgb.min(axis=2)
paperish = mn > 205
# flood fill: iterative 1px dilation from the four borders
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
alpha = np.where(reach, 0, 255).astype(np.uint8)
alpha_img = Image.fromarray(alpha, "L").filter(ImageFilter.GaussianBlur(1.2))

rgba = out.convert("RGBA")
rgba.putalpha(alpha_img)
rgba.save(PREVIEW)
rgba.save(DST)
print("saved", DST, rgba.size)
