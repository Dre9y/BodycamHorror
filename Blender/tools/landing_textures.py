"""2D textures for the PvE landing (Assets/Editor/KeyPropsBuilder.cs), run with the system Python (Pillow + numpy):

    python Blender/tools/landing_textures.py

Writes
  Assets/Generated/Textures/Graffiti/Graffiti_Num<N>.png   a flat number sprayed on its door (1..4), for decals
  Assets/Generated/Textures/Props/KeyTag_<N>.png            a key's plastic tag with the flat number in marker (1..4)
  Assets/Generated/Textures/Props/Door_NoNumber_BaseColor.png
                                                            the flat door's paint with its printed "37" painted out
"""
import os
import numpy as np
from PIL import Image, ImageDraw, ImageFilter

from decor_textures import F, fbm, arr, to_img, spray, over, wall_break, text_mask, save_graffiti, PROP_TEX, PROJECT

# ------------------------------------------------------------------ door numbers, sprayed

NUM_STYLE = {
    # number: paint colour, tilt, font, a slash under it
    1: ((0.9, 0.9, 0.86), -6, "impact.ttf", True),
    2: ((0.9, 0.9, 0.86), 4, "impact.ttf", False),
    3: ((0.62, 0.05, 0.03), -3, "segoeprb.ttf", True),
    4: ((0.9, 0.9, 0.86), 7, "impact.ttf", False),
}

def g_number(n):
    """A big hasty digit on a flat door: one pass of the can, a run or two, a slash under some."""
    col, rot, font, slash = NUM_STYLE[n]
    S = (768, 768); seed = 700 + n
    m = text_mask(S, str(n), F(font, 560), (S[0] // 2, S[1] // 2 - 30), rot=rot)
    if slash:
        d = ImageDraw.Draw(m)
        d.line((170, 660, 600, 625), fill=255, width=26)
    m = m.filter(ImageFilter.MaxFilter(5))
    L = spray(S, m, col, seed, soft=2.6, over=0.28, drips=9, drip_len=(30, 150), drip_w=(5, 9))
    L[..., :3] *= (0.82 + 0.25 * fbm(S[1], S[0], 8, seed))[..., None]
    wall_break(L, seed + 1, 0.22)
    save_graffiti(f"Num{n}", L)

# ------------------------------------------------------------------ key tags

TAG_COLOUR = {1: (0.78, 0.22, 0.12), 2: (0.16, 0.38, 0.62), 3: (0.86, 0.62, 0.12), 4: (0.22, 0.5, 0.26)}

def key_tag(n):
    """A worn plastic key tag: faded colour, scuffs, a hole at the top for the ring, the number in black marker."""
    W, H = 256, 384; seed = 900 + n
    base = np.array(TAG_COLOUR[n], float)
    noise = fbm(H, W, 6, seed)
    img = base[None, None, :] * (0.78 + 0.3 * noise[..., None])
    # scuffs: pale scratches
    sc = Image.new("L", (W, H), 0); d = ImageDraw.Draw(sc); rng = np.random.default_rng(seed)
    for _ in range(22):
        x, y = rng.uniform(0, W), rng.uniform(0, H); L = rng.uniform(8, 40); a = rng.uniform(0, np.pi)
        d.line((x, y, x + L * np.cos(a), y + L * np.sin(a)), fill=int(rng.uniform(60, 160)), width=1)
    img = img + arr(sc)[..., None] * 0.35
    # grime towards the edges
    yy, xx = np.mgrid[0:H, 0:W]
    edge = np.minimum(np.minimum(xx, W - 1 - xx), np.minimum(yy, H - 1 - yy)) / 40.0
    img *= (0.7 + 0.3 * np.clip(edge, 0, 1))[..., None]
    out = to_img(img)
    d = ImageDraw.Draw(out)
    # the ring hole (dark), and the number in marker, slightly crooked
    d.ellipse((W / 2 - 22, 26, W / 2 + 22, 70), fill=(18, 16, 14))
    mk = text_mask((W, H), str(n), F("segoeprb.ttf", 230), (W // 2 + 4, H // 2 + 40), rot=-5)
    mk = mk.filter(ImageFilter.GaussianBlur(1.2))
    ink = np.array([0.06, 0.05, 0.05])
    a = np.clip(arr(mk) * 1.3, 0, 1)[..., None] * (0.85 + 0.15 * fbm(H, W, 10, seed + 3))[..., None]
    o = arr(out) * (1 - a) + ink[None, None, :] * a
    sm = text_mask((W, H), "кв.", F("segoeprb.ttf", 54), (W // 2 - 30, 112), rot=-4).filter(ImageFilter.GaussianBlur(0.8))
    a2 = np.clip(arr(sm) * 1.2, 0, 1)[..., None]
    o = o * (1 - a2) + ink[None, None, :] * a2
    to_img(o).save(os.path.join(PROP_TEX, f"KeyTag_{n}.png"))

# ------------------------------------------------------------------ the flat door without its number

def door_without_number():
    """Door_BaseColor carries a printed "37" on the leaf; patch it over with the paint just above it, feathered."""
    src = Image.open(os.path.join(PROP_TEX, "Door_BaseColor.png")).convert("RGB")
    a = arr(src)
    x0, x1, y0, y1, dy = 186, 274, 1882, 1944, -62   # the digits (UV is upside down there), and where clean paint is
    patch = a[y0 + dy:y1 + dy, x0:x1].copy()
    h, w = patch.shape[:2]
    yy, xx = np.mgrid[0:h, 0:w]
    feather = np.clip(np.minimum(np.minimum(xx, w - 1 - xx), np.minimum(yy, h - 1 - yy)) / 10.0, 0, 1)[..., None]
    a[y0:y1, x0:x1] = a[y0:y1, x0:x1] * (1 - feather) + patch * feather
    to_img(a).save(os.path.join(PROP_TEX, "Door_NoNumber_BaseColor.png"))

if __name__ == "__main__":
    for n in (1, 2, 3, 4):
        g_number(n); key_tag(n); print("number", n)
    door_without_number(); print("door without number")
