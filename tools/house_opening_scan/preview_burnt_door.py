"""Offline preview of the burnt-house door fix.
Analyzes where the painted dark void actually is in house_burned_near.png,
checks it against the DoorSpec (Fx=0.260, Fh=0.42) used to hang the leaf,
then composites a proposal: soft drop shadow + recessed dark void + thin
ink frame, using the REAL assets (no fake backgrounds). Saved to previews/.
"""
import os, math
import numpy as np
from PIL import Image, ImageFilter, ImageDraw

HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.abspath(os.path.join(HERE, "..", ".."))
PREV = os.path.join(HERE, "previews")
os.makedirs(PREV, exist_ok=True)

burnt = Image.open(os.path.join(ROOT, "assets/art/props/house_burned_near.png")).convert("RGBA")
leaf = Image.open(os.path.join(ROOT, "assets/art/props/door_leaf.png")).convert("RGBA")
W, H = burnt.size
arr = np.asarray(burnt).astype(np.float64)
lum = 0.299*arr[...,0] + 0.587*arr[...,1] + 0.114*arr[...,2]
alpha = arr[...,3]

# ---- locate the painted dark void in the lower band (door zone) ----
yb0 = int(H*0.45)
band = lum[yb0:, :]
amask = (alpha[yb0:, :] > 100)
col_lum = np.where(amask, band, 255.0).mean(axis=0)
void_cols = np.where(col_lum < 72)[0]
vx0, vx1 = (int(void_cols[0]), int(void_cols[-1])) if len(void_cols) else (-1, -1)
# vertical extent: for columns in void range, topmost dark row
vy0, vy1 = H, H
if vx1 > vx0:
    sub = lum[yb0:, vx0:vx1+1]
    sub_a = alpha[yb0:, vx0:vx1+1]
    dark = (sub < 72) & (sub_a > 100)
    rows = np.where(dark.any(axis=1))[0]
    if len(rows):
        vy0 = yb0 + int(rows[0]); vy1 = yb0 + int(rows[-1])
print(f"[void] texture px x=[{vx0},{vx1}] ({vx0/W:.3f}..{vx1/W:.3f})  y=[{vy0},{vy1}] ({vy0/H:.3f}..{vy1/H:.3f})")

# ---- leaf placement from DoorSpec (Fx=0.260, Fh=0.42) ----
Fx, Fh = 0.260, 0.42
lw_frac = Fh * (leaf.size[0] / leaf.size[1])
lx0, lx1 = (Fx - lw_frac/2)*W, (Fx + lw_frac/2)*W
ly0, ly1 = (1 - Fh)*H, H
print(f"[leaf] x frac {Fx-lw_frac/2:.3f}..{Fx+lw_frac/2:.3f} -> px [{lx0:.0f},{lx1:.0f}]  y px [{ly0:.0f},{ly1:.0f}]")
print(f"[align] leaf_x covers void_x? {lx0<=vx0 and lx1>=vx1}   leaf_y covers void_y? {ly0<=vy0 and ly1>=vy1}")

# ---- composite the proposal (mirrors the code change) ----
base = burnt.copy()
lw_px, dh_px = int(round(lx1-lx0)), int(round(ly1-ly0))

# cast shadow on the wall to the lower-right inside the reveal
shadow = Image.new("RGBA", base.size, (0,0,0,0))
sm = ImageDraw.Draw(shadow)
sm.rectangle([lx0+8, ly0+10, lx1+10, ly1+11], fill=(8,6,5,120))
shadow = shadow.filter(ImageFilter.GaussianBlur(5))
base = Image.alpha_composite(shadow, base)

# bottom contact darkness where the leaf meets the charred sill
contact = Image.new("RGBA", base.size, (0,0,0,0))
cd = ImageDraw.Draw(contact)
cd.rectangle([lx0-2, ly1-5, lx1+2, ly1+4], fill=(6,5,4,190))
contact = contact.filter(ImageFilter.GaussianBlur(2.5))
base = Image.alpha_composite(contact, base)

# the leaf itself, sooted tint (0.78,0.73,0.67), tiny -2.6deg skew
lt = leaf.resize((lw_px, dh_px))
lr, lg, lb, la = lt.split()
lt = Image.merge("RGBA", (
    lr.point(lambda v: min(255, int(v*0.78))),
    lg.point(lambda v: min(255, int(v*0.73))),
    lb.point(lambda v: min(255, int(v*0.67))),
    la))
lt = lt.rotate(2.6, expand=True, resample=Image.BICUBIC)
lx = int(lx0) - (lt.size[0]-lw_px)//2
ly = int(ly0) - (lt.size[1]-dh_px)//2
base.alpha_composite(lt, (lx, ly))

# thin ink jamb frame matching the charred frame's linework
fr = Image.new("RGBA", base.size, (0,0,0,0))
fd = ImageDraw.Draw(fr)
fd.rectangle([lx0, ly0, lx1, ly1], outline=(36,26,20,235), width=3)
fr = fr.filter(ImageFilter.GaussianBlur(0.6))
base = Image.alpha_composite(fr, base)

out = os.path.join(PREV, "v3.44_burnt_door_proposal.png")
base.save(out)
print(f"[saved] {out}")
