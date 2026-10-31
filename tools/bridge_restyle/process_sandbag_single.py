# -*- coding: utf-8 -*-
"""
v3.56: process the generated single-sandbag candidate into a clean RGBA asset.

Input : tools/bridge_restyle/previews/From_the_input_image_of_a_hand_*.png
        (ImageGen output: RGB, fake checkerboard "transparency", AI watermark)
Output: tools/bridge_restyle/previews/sandbag_single_clean.png
        (true alpha, watermark cropped, feathered edges)

Discipline: candidate stays in previews/ until the user approves it.
"""
import glob

import numpy as np
from PIL import Image, ImageFilter

SRC = sorted(glob.glob(
    'tools/bridge_restyle/previews/From_the_input_image_of_a_hand_*.png'))[-1]
DST = 'tools/bridge_restyle/previews/sandbag_single_clean.png'

im = Image.open(SRC).convert('RGB')
a = np.array(im).astype(int)

# ---- 1) crop the AI watermark (bottom-right stamp) ----
# the bag occupies the middle ~880px; crop generously inside the watermark band
a = a[0:960, 0:1010]

# ---- 2) checkerboard -> alpha ----
# measured: bg tiles mn 216..255 with sat 0..2 (pure neutral grey/white);
# body mn 80..174 with sat 36..67 -> huge margin. Threshold on LOW SAT first.
mx = a.max(axis=2)
mn = a.min(axis=2)
sat = mx - mn
bg = (sat < 12) & (mn > 208)
alpha = np.where(bg, 0, 255).astype(np.uint8)

# soft ramp at the boundary to avoid jaggies
edge = (sat < 20) & (mn > 192) & ~bg
alpha[edge] = 130

rgba = np.dstack([a.astype(np.uint8), alpha])
out = Image.fromarray(rgba, 'RGBA')

# feather the matte
out = out.filter(ImageFilter.GaussianBlur(0.8))
# re-solidify interior alpha lost to the blur (keep alpha where it was 255)
aa = np.array(out)
aa[..., 3] = np.maximum(aa[..., 3], np.where(np.array(Image.fromarray(rgba, 'RGBA'))[..., 3] > 250, 255, aa[..., 3]))
out = Image.fromarray(aa, 'RGBA')

# ---- 3) trim to content ----
bbox = out.getbbox()
if bbox:
    out = out.crop(bbox)

out.save(DST)
print('saved', DST, out.size)
print('opaque px:', int((np.array(out)[..., 3] > 200).sum()))
