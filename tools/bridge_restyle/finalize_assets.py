#!/usr/bin/env python3
"""Finalize keyed assets: wall_niche edge-fade into wall, drawer_box choke white fringe, cabinet_interior crop."""
from PIL import Image
import numpy as np

PREV = "tools/bridge_restyle/previews"

def load_keyed(name):
    im = Image.open(f"{PREV}/_keyed_{name}.png").convert("RGBA")
    return np.asarray(im).astype(np.uint8)

def save(name, arr):
    Image.fromarray(arr, "RGBA").save(f"{PREV}/final_{name}.png")
    print(f"final_{name}.png {arr.shape[1]}x{arr.shape[0]} alpha mean={arr[:,:,3].mean():.1f}")

# --- wall_niche: fade the plaster-wall patch edges to 0 so it blends into game wall ---
a = load_keyed("wall_niche").copy()
h, w, _ = a.shape
alpha = a[:,:,3].astype(np.float32)
yy, xx = np.mgrid[0:h, 0:w]
# distance from nearest crop edge
dx = np.minimum(xx, w-1-xx); dy = np.minimum(yy, h-1-yy)
d = np.minimum(dx, dy).astype(np.float32)
fade = 26.0  # px feather band around the whole wall patch
edge = np.clip(d/fade, 0, 1)
edge = edge*edge*(3-2*edge)  # smoothstep
a[:,:,3] = np.clip(alpha*edge, 0, 255).astype(np.uint8)
save("wall_niche", a)

# --- drawer_box: erode alpha 2px to kill white fringe, then slight blur ---
a = load_keyed("drawer_box").copy()
alpha = a[:,:,3].copy()
mask = alpha > 90
er = mask.copy()
for _ in range(2):  # 2px erosion
    er = er & np.roll(er,1,0) & np.roll(er,-1,0) & np.roll(er,1,1) & np.roll(er,-1,1) \
           & np.roll(np.roll(er,1,0),1,1) & np.roll(np.roll(er,1,0),-1,1) \
           & np.roll(np.roll(er,-1,0),1,1) & np.roll(np.roll(er,-1,0),-1,1)
newalpha = np.where(er, alpha, 0).astype(np.uint8)
# 1px feather
af = newalpha.astype(np.float32)
blur = np.zeros_like(af)
for dy0 in (-1,0,1):
    for dx0 in (-1,0,1):
        blur += np.roll(np.roll(af,dy0,0),dx0,1)
a[:,:,3] = np.clip(blur/9.0,0,255).astype(np.uint8)
# crop to new alpha bbox
ys,xs=np.where(a[:,:,3]>8)
a=a[ys.min():ys.max()+1, xs.min():xs.max()+1]
save("drawer_box", a)

# --- cabinet_interior: it's a full rectangular cavity, just crop margins, keep opaque ---
im = Image.open(f"{PREV}/cabinet_interior.png").convert("RGBA")
a = np.asarray(im).astype(np.uint8)
R,G,B = a[:,:,0].astype(np.int16),a[:,:,1].astype(np.int16),a[:,:,2].astype(np.int16)
lum=(R+G+B)/3; sat=R-B
subject=(lum<205)|(sat>18)
ys,xs=np.where(subject)
a=a[ys.min():ys.max()+1, xs.min():xs.max()+1]
a[:,:,3]=255
save("cabinet_interior", a)
