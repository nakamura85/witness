# -*- coding: utf-8 -*-
"""
v3.54: strip the leftover white in the tree sprites.

User report 2026-10-31: "树的缝隙还有白底" - the elm canopy carries bright
low-saturation snow patches between the branches; in game (light sky behind)
they read as un-cut background. Same family of fault as the bridge truss
enclosed-paper fix, but here the white is INTENTIONAL snow that simply reads
wrong against a pale sky, so it goes.

Method: luminance + saturation double gate with a soft ramp - fully white
pixels fade out completely, near-white pixels at branch boundaries fade
partially, so no hard jaggies are left behind (no flood-fill needed, the
background is already transparent).

  tree_elm   : min(RGB) > 205 ramp / sat < 40  -> kills the snow patches
               (17k px fully white at >235, ~42k at >200)
  tree_birch : min(RGB) > 238 ramp / sat < 30  -> only specular speckles;
               the birch bark IS white (49k px at 200-235) and must survive.

Run from the repo root:  python tools/bridge_restyle/remove_tree_white.py
Idempotent: already-transparent pixels contribute nothing.
"""
import numpy as np
from PIL import Image

JOBS = {
    'tree_elm':   dict(lum=205.0, lum_span=25.0, sat=40.0, sat_span=20.0),
    'tree_birch': dict(lum=238.0, lum_span=12.0, sat=30.0, sat_span=15.0),
}

for name, j in JOBS.items():
    p = f'assets/art/backdrop/{name}.png'
    im = Image.open(p).convert('RGBA')
    a = np.array(im).astype(np.float32)
    mn = a[..., :3].min(axis=2)
    mx = a[..., :3].max(axis=2)
    sat = mx - mn
    wl = np.clip((mn - j['lum']) / j['lum_span'], 0.0, 1.0)
    ws = np.clip((j['sat'] - sat) / j['sat_span'], 0.0, 1.0)
    w = wl * ws                      # 0 = keep, 1 = fully white
    a[..., 3] *= (1.0 - w)
    out = a.clip(0, 255).astype(np.uint8)
    Image.fromarray(out, 'RGBA').save(p)
    print(f'{name}: killed {int((w > 0.5).sum())} px fully, '
          f'faded {int(((w > 0) & (w <= 0.5)).sum())} px partially')
