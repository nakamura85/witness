#!/usr/bin/env python3
"""
Offline composite to demonstrate the Jiangqiao near-tree / near-house depth bug
(v3.41) using the REAL game sprites -- no fake background, no fake soldier.

In the engine the near-house base is GroundY+8 = 663 and near trees were drawn
at base-y 672/676 but behind the houses (ZIndex -20 < -3). Because the tree's
base-y was LARGER (closer to camera) than the house base-y yet drawn behind it,
the tree trunk poked out beneath the house -> the house looked like it floated.

We reproduce the exact vertical relationship:
  - house base  = 663  (drawn ON TOP, ZIndex -3)
  - tree  base  = 672  BEFORE  (9px below house base -> trunk shows -> float)
  - tree  base  = 650  AFTER   (13px above house base -> no trunk below -> seated)

The preview maps world-y 1:1 to screen-y (both near objects share scale 1).
"""
import os
from PIL import Image, ImageDraw

ROOT = os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
PROPS = os.path.join(ROOT, "assets/art/props")
BACK  = os.path.join(ROOT, "assets/art/backdrop")
OUT   = os.path.join(ROOT, "docs/screenshots")
os.makedirs(OUT, exist_ok=True)

HOUSE = os.path.join(PROPS, "house_burned_near.png")
TREE  = os.path.join(BACK,  "tree_elm.png")

CW, CH = 760, 520
HOUSE_BASE_Y = 360          # screen-y of house bottom (world 663)
HOUSE_H      = 300          # on-screen house height
TREE_H       = 330          # on-screen tree height

def snow_bg():
    img = Image.new("RGBA", (CW, CH), (0, 0, 0, 0))
    px = img.load()
    for y in range(CH):
        # gentle top->bottom snow gradient
        t = y / (CH - 1)
        r = int(0.80 * (1 - t) * 255 + 0.86 * t * 255)
        g = int(0.83 * (1 - t) * 255 + 0.88 * t * 255)
        b = int(0.85 * (1 - t) * 255 + 0.90 * t * 255)
        for x in range(CW):
            px[x, y] = (r, g, b, 255)
    return img

def place(base_img, sprite, cx, base_y, h):
    """Paste sprite so its bottom-center sits at (cx, base_y), scaled to height h."""
    w = int(sprite.width * h / sprite.height)
    s = sprite.resize((w, h), Image.LANCZOS)
    x0 = int(cx - w / 2)
    y0 = int(base_y - h)
    base_img.alpha_composite(s, (x0, y0))
    return base_img

def build(tree_base_y, label):
    img = snow_bg()
    # tree drawn FIRST (behind, ZIndex -20)
    place(img, Image.open(TREE).convert("RGBA"), CW // 2 + 40, tree_base_y, TREE_H)
    # house drawn ON TOP (ZIndex -3)
    place(img, Image.open(HOUSE).convert("RGBA"), CW // 2 - 30, HOUSE_BASE_Y, HOUSE_H)
    d = ImageDraw.Draw(img)
    d.text((16, 14), label, fill=(40, 30, 24, 255))
    # mark the house base line
    d.line([(0, HOUSE_BASE_Y), (CW, HOUSE_BASE_Y)], fill=(180, 60, 50, 160), width=1)
    return img

before = build(HOUSE_BASE_Y + (672 - 663), "BEFORE  tree base 672 (9px below house base 663) -> FLOATS")
after  = build(HOUSE_BASE_Y + (650 - 663), "AFTER   tree base 650 (13px above house base 663) -> SEATED")

b = os.path.join(OUT, "v56_jq_treefloat_before.png")
a = os.path.join(OUT, "v56_jq_treefloat_after.png")
before.save(b)
after.save(a)

# side-by-side
both = Image.new("RGBA", (CW * 2 + 10, CH), (255, 255, 255, 255))
both.alpha_composite(before, (0, 0))
both.alpha_composite(after, (CW + 10, 0))
both.convert("RGB").save(os.path.join(OUT, "v56_jq_treefloat_compare.png"))
print("wrote:", b)
print("wrote:", a)
print("wrote:", os.path.join(OUT, "v56_jq_treefloat_compare.png"))
