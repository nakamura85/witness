import sys
from PIL import Image, ImageDraw, ImageFont

SRC = [
    ("_old_bridge_backup.png", "v3.28 原版\n(你记的'桥好·只铁轨错')"),
    ("_cand_c.png",            "C 早期最佳\n桥墩/中段偏暗"),
    ("_cand_f3.png",           "F3 加粗墨线版"),
    ("_cand_h1.png",           "H1 刚提交·你说差太多"),
    ("_cand_h2.png",           "H2 更通透轻盈"),
    ("_cand_h3.png",           "H3 墨线更密偏重"),
]

TW = 460          # thumb width
PAD = 10
LBLH = 38         # label bar height
FONT = ImageFont.load_default()

def build(src, label):
    im = Image.open(src).convert("RGBA")
    # scale to TW keeping aspect
    w, h = im.size
    th = int(h * TW / w)
    im = im.resize((TW, th), Image.LANCZOS)
    # make opaque bg (in case of transparency) for preview
    bg = Image.new("RGBA", (TW, th), (235, 235, 235, 255))
    bg.alpha_composite(im)
    im = bg.convert("RGB")
    # label bar
    full = Image.new("RGB", (TW, th + LBLH), (245, 245, 245))
    full.paste(im, (0, LBLH))
    d = ImageDraw.Draw(full)
    d.rectangle([0, 0, TW, LBLH], fill=(220, 225, 230))
    # draw label text (auto wrap on \n)
    lines = label.split("\n")
    y = 4
    for ln in lines:
        d.text((6, y), ln, fill=(20, 20, 20), font=FONT)
        y += 16
    return full

cells = [build(s, l) for s, l in SRC]
cols = 2
rows = (len(cells) + cols - 1) // cols
cw = TW + PAD
ch = cells[0].size[1] + PAD
sheet = Image.new("RGB", (cols * cw + PAD, rows * ch + PAD), (255, 255, 255))
for i, c in enumerate(cells):
    r, col = divmod(i, cols)
    x = PAD + col * cw
    y = PAD + r * ch
    sheet.paste(c, (x, y))

out = "_gallery_bridge.png"
sheet.save(out)
print("saved", out, sheet.size)
