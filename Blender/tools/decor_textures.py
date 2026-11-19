"""2D textures for the decor props (Blender/tools/decor_props.py), run with the system Python (Pillow + numpy),
not inside Blender:

    python Blender/tools/decor_textures.py

Writes
  Blender/tex/Book_<Name>.png         a book's jacket: back cover | spine | front cover, at the book's own proportions
  Blender/tex/Book_PageText.png       an open book's two pages
  Assets/Generated/Textures/Props/Poster_{BaseColor,Normal,MetallicSmoothness}.png
                                      the photo poster (not baked in Blender: printed straight onto the paper's UV)
  Assets/Generated/Textures/Graffiti/Graffiti_<Name>.png
                                      spray paint with alpha, for URP decal projectors
"""
import math, os, random
import numpy as np
from PIL import Image, ImageDraw, ImageFilter, ImageFont

PROJECT = os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
SRC_TEX = os.path.join(PROJECT, "Blender", "tex")
PROP_TEX = os.path.join(PROJECT, "Assets", "Generated", "Textures", "Props")
GRAF_TEX = os.path.join(PROJECT, "Assets", "Generated", "Textures", "Graffiti")
FONTS = r"C:\Windows\Fonts"
PPM = 2400   # jacket pixels per metre

def F(name, size): return ImageFont.truetype(os.path.join(FONTS, name), int(size))

# ------------------------------------------------------------------ noise

def vnoise(h, w, cells, seed):
    rng = np.random.default_rng(seed)
    cy, cx = cells, max(1, int(round(cells * w / h)))
    g = rng.random((cy + 1, cx + 1))
    y = np.linspace(0, cy, h, endpoint=False); x = np.linspace(0, cx, w, endpoint=False)
    yi = y.astype(int); xi = x.astype(int); fy = y - yi; fx = x - xi
    fy = fy * fy * (3 - 2 * fy); fx = fx * fx * (3 - 2 * fx)
    a = g[yi][:, xi]; b = g[yi][:, xi + 1]; c = g[yi + 1][:, xi]; d = g[yi + 1][:, xi + 1]
    top = a + (b - a) * fx[None, :]; bot = c + (d - c) * fx[None, :]
    return top + (bot - top) * fy[:, None]

def fbm(h, w, cells, seed, octaves=4):
    out = np.zeros((h, w)); amp = 1.0; tot = 0.0
    for o in range(octaves):
        out += vnoise(h, w, cells * 2 ** o, seed + o * 17) * amp; tot += amp; amp *= 0.5
    return out / tot

def arr(im): return np.asarray(im, dtype=np.float64) / 255.0
def to_img(a, mode="RGB"): return Image.fromarray((np.clip(a, 0, 1) * 255 + 0.5).astype(np.uint8), mode)

def mask_of(size, draw_fn):
    m = Image.new("L", size, 0); draw_fn(ImageDraw.Draw(m)); return m

def blend(img, mask, col):
    m = np.clip(mask, 0, 1)[..., None]
    img[:] = img * (1 - m) + np.array(col, float)[None, None, :] * m

# ------------------------------------------------------------------ books

BOOKS = {
    # name: thickness, depth (cover width), height, cloth colour, ink colour, title lines, author, spine text, style
    "Encyclopedia": dict(t=0.05, d=0.175, h=0.26, col=(0.07, 0.1, 0.2), ink=(0.78, 0.62, 0.3),
                         title=["БОЛЬШАЯ", "ЭНЦИКЛОПЕДИЯ"], author="ТОМ 7", spine="ЭНЦИКЛОПЕДИЯ · 7", style="frame"),
    "Chemistry": dict(t=0.022, d=0.135, h=0.205, col=(0.72, 0.6, 0.22), ink=(0.12, 0.08, 0.05),
                      title=["СПРАВОЧНИК", "ХИМИКА"], author="Издание третье", spine="СПРАВОЧНИК ХИМИКА", style="band"),
    "Poems": dict(t=0.026, d=0.125, h=0.195, col=(0.42, 0.05, 0.04), ink=(0.85, 0.75, 0.55),
                  title=["СТИХИ"], author="И. Белов", spine="И. БЕЛОВ  СТИХИ", style="emblem"),
    "Collected": dict(t=0.035, d=0.14, h=0.215, col=(0.1, 0.2, 0.13), ink=(0.74, 0.6, 0.3),
                      title=["СОБРАНИЕ", "СОЧИНЕНИЙ"], author="Том третий", spine="СОБРАНИЕ СОЧИНЕНИЙ · III", style="frame"),
    "Anatomy": dict(t=0.04, d=0.17, h=0.245, col=(0.3, 0.18, 0.1), ink=(0.86, 0.8, 0.66),
                    title=["АНАТОМИЯ", "ЧЕЛОВЕКА"], author="Учебное пособие", spine="АНАТОМИЯ ЧЕЛОВЕКА", style="skull"),
    "Diary": dict(t=0.016, d=0.105, h=0.15, col=(0.06, 0.055, 0.05), ink=(0.55, 0.5, 0.45),
                  title=["ДНЕВНИК"], author="", spine="", style="plain"),
    "Atlas": dict(t=0.014, d=0.21, h=0.29, col=(0.42, 0.44, 0.42), ink=(0.1, 0.12, 0.15),
                  title=["АТЛАС", "МИРА"], author="Для средней школы", spine="АТЛАС МИРА", style="globe"),
}

def fit(d, s, file, size, maxw):
    """The font at `size`, or smaller so that s fits in maxw."""
    f = F(file, size); w = d.textlength(s, font=f)
    return F(file, size * maxw / w) if w > maxw else f

def text_center(d, cx, cy, s, font, fill):
    b = d.textbbox((0, 0), s, font=font); d.text((cx - (b[2] - b[0]) / 2 - b[0], cy - (b[3] - b[1]) / 2 - b[1]), s, font=font, fill=fill)

def book_jacket(name, b, seed):
    t, dep, hgt = b["t"], b["d"], b["h"]
    W = int(round((2 * dep + t) * PPM)); H = int(round(hgt * PPM))
    xs0 = int(dep * PPM); xs1 = int((dep + t) * PPM)   # the spine's columns
    img = np.ones((H, W, 3)) * np.array(b["col"])[None, None, :]
    # cloth: a fine weave and a blotchy fade
    yy, xx = np.mgrid[0:H, 0:W]
    weave = (np.sin(xx * 1.9) * np.sin(yy * 1.9)) * 0.04
    img *= (0.92 + 0.14 * fbm(H, W, 6, seed) + weave)[..., None]
    ink = tuple(int(c * 255) for c in b["ink"])
    lay = Image.new("RGBA", (W, H), (0, 0, 0, 0)); d = ImageDraw.Draw(lay)
    fx0, fx1 = xs1, W   # the front cover
    cx = (fx0 + fx1) / 2; fw = fx1 - fx0
    st = b["style"]
    if st in ("frame", "skull", "emblem"):
        m = int(fw * 0.07); d.rectangle((fx0 + m, m, fx1 - m, H - m), outline=ink, width=max(3, fw // 120))
        m2 = m + fw // 50; d.rectangle((fx0 + m2, m2, fx1 - m2, H - m2), outline=ink, width=max(2, fw // 220))
    if st == "band":
        d.rectangle((fx0, int(H * 0.28), fx1, int(H * 0.5)), fill=ink)
    fsz = fw * 0.16; ffile = "georgiab.ttf" if st != "band" else "arialbd.ttf"
    fnt = min((fit(d, line, ffile, fsz, fw * 0.72) for line in b["title"]), key=lambda f: f.size)
    fsz = fnt.size
    y = H * (0.3 if st != "band" else 0.33) if st != "plain" else H * 0.29
    for line in b["title"]:
        col = (int(b["col"][0] * 255), int(b["col"][1] * 255), int(b["col"][2] * 255)) if st == "band" else ink
        text_center(d, cx, y, line, fnt, col); y += fsz * 1.25
    if b["author"]: text_center(d, cx, H * 0.78, b["author"], F("georgiai.ttf" if st != "band" else "arial.ttf", fw * 0.07), ink)
    if st == "skull":   # a simple engraved skull outline
        r = fw * 0.13; sy = H * 0.6
        d.ellipse((cx - r, sy - r, cx + r, sy + r * 0.9), outline=ink, width=4)
        d.rectangle((cx - r * 0.55, sy + r * 0.7, cx + r * 0.55, sy + r * 1.25), outline=ink, width=4)
        for ex in (-0.42, 0.42): d.ellipse((cx + ex * r - r * 0.25, sy - r * 0.05, cx + ex * r + r * 0.25, sy + r * 0.4), fill=ink)
    if st == "emblem":
        r = fw * 0.1; sy = H * 0.6
        pts = [(cx + r * math.cos(a * math.pi / 5 * 2 - math.pi / 2) * (1 if a % 1 == 0 else 0.4),
                sy + r * math.sin(a * math.pi / 5 * 2 - math.pi / 2)) for a in range(5)]
        star = []
        for i in range(10):
            rr = r if i % 2 == 0 else r * 0.42; a = i * math.pi / 5 - math.pi / 2
            star.append((cx + rr * math.cos(a), sy + rr * math.sin(a)))
        d.polygon(star, fill=ink)
    if st == "globe":
        r = fw * 0.15; sy = H * 0.66
        d.ellipse((cx - r, sy - r, cx + r, sy + r), outline=ink, width=5)
        for k in (-0.6, 0, 0.6): d.ellipse((cx - r * abs(math.cos(k * 1.2)) , sy - r, cx + r * abs(math.cos(k * 1.2)), sy + r), outline=ink, width=3)
        for k in (-0.5, 0, 0.5): d.line((cx - r * math.sqrt(1 - k * k), sy + k * r, cx + r * math.sqrt(1 - k * k), sy + k * r), fill=ink, width=3)
    if st == "plain":   # an embossed blind-stamped label box
        d.rounded_rectangle((cx - fw * 0.4, H * 0.23, cx + fw * 0.4, H * 0.35), radius=12, outline=ink, width=3)
    # back cover: two rules
    d.line((fw * 0.08, H * 0.08, dep * PPM - fw * 0.08, H * 0.08), fill=ink, width=3)
    d.line((fw * 0.08, H * 0.92, dep * PPM - fw * 0.08, H * 0.92), fill=ink, width=3)
    # spine: bands top and bottom, the title running bottom to top
    sw = xs1 - xs0
    for yb in (0.06, 0.09, 0.91, 0.94): d.rectangle((xs0, int(H * yb), xs1, int(H * yb) + max(3, sw // 14)), fill=ink)
    if b["spine"]:
        sfs = min(sw * 0.55, H * 0.7 / max(1, len(b["spine"])) * 1.7)
        tl = Image.new("RGBA", (int(H * 0.8), sw), (0, 0, 0, 0)); td = ImageDraw.Draw(tl)
        text_center(td, tl.width / 2, sw / 2, b["spine"], fit(td, b["spine"], "georgiab.ttf", sfs, tl.width * 0.95), ink)
        tl = tl.rotate(90, expand=True)
        lay.alpha_composite(tl, (xs0, int(H * 0.1)))
    la = arr(lay)
    worn = 0.55 + 0.45 * fbm(H, W, 30, seed + 5)   # gilt and ink rubbed off in places
    img = img * (1 - la[..., 3:] * worn[..., None]) + la[..., :3] * la[..., 3:] * worn[..., None]
    # the spine faded by light, its hinges and edges rubbed pale
    img[:, xs0:xs1] = img[:, xs0:xs1] * 0.82 + 0.1
    for x in (xs0, xs1):
        img[:, max(0, x - 6):x + 6] = img[:, max(0, x - 6):x + 6] * 0.75 + 0.12
    edge = np.minimum(np.minimum(yy, H - 1 - yy), np.minimum(xx, W - 1 - xx))
    rub = np.clip(1 - edge / 18.0, 0, 1) * (0.5 + 0.5 * fbm(H, W, 40, seed + 9))
    blend(img, rub * 0.7, (0.6, 0.55, 0.45))
    stains = np.clip((fbm(H, W, 5, seed + 11) - 0.66) * 4, 0, 1) * 0.2
    img *= (1 - stains)[..., None]
    path = os.path.join(SRC_TEX, f"Book_{name}.png"); to_img(img).save(path)
    return path

def page_text(seed=4):
    W, H = 1400, 1000; rng = random.Random(seed)
    img = np.ones((H, W, 3)) * np.array((0.82, 0.77, 0.64))
    img *= (0.9 + 0.12 * fbm(H, W, 5, seed))[..., None]
    lay = Image.new("RGBA", (W, H), (0, 0, 0, 0)); d = ImageDraw.Draw(lay)
    for page in (0, 1):
        x0 = 90 + page * W // 2; x1 = W // 2 - 90 + page * W // 2
        y = 110
        text_center(d, (x0 + x1) / 2, 70, "ГЛАВА ЧЕТВЁРТАЯ" if page == 0 else "", F("georgiab.ttf", 26), (40, 34, 30, 230))
        while y < H - 120:
            x = x0 + (40 if rng.random() < 0.12 else 0)
            while x < x1 - 20:
                wl = rng.randint(12, 70)
                if x + wl > x1: break
                d.rectangle((x, y, x + wl, y + 9), fill=(45, 40, 36, rng.randint(150, 220)))
                x += wl + 9
            y += 26
            if rng.random() < 0.06: y += 20
        text_center(d, (x0 + x1) / 2, H - 60, str(118 + page), F("georgia.ttf", 22), (40, 34, 30, 230))
    # someone's note across the right page, in red pen
    note = Image.new("RGBA", (W // 2, 300), (0, 0, 0, 0)); nd = ImageDraw.Draw(note)
    nd.text((20, 40), "ОНИ ВНИЗУ", font=F("segoeprb.ttf", 80), fill=(150, 20, 18, 235))
    nd.line((30, 175, 470, 160), fill=(150, 20, 18, 220), width=6)
    lay.alpha_composite(note.rotate(8, expand=False, resample=Image.BICUBIC), (W // 2 + 60, 480))
    la = arr(lay)
    img = img * (1 - la[..., 3:]) + la[..., :3] * la[..., 3:]
    xx = np.arange(W)[None, :]; gutter = np.exp(-((xx - W / 2) / 55.0) ** 2) * 0.5
    img *= (1 - gutter)[..., None]
    yy = np.arange(H)[:, None]
    edge = np.minimum(np.minimum(yy, H - 1 - yy), np.minimum(xx, W - 1 - xx))
    img *= (0.8 + 0.2 * np.clip(edge / 60.0, 0, 1))[..., None]
    path = os.path.join(SRC_TEX, "Book_PageText.png"); to_img(img).save(path)
    return path

# ------------------------------------------------------------------ poster

def poster():
    W, H = 1024, 1472; seed = 21
    paper = np.ones((H, W, 3)) * np.array((0.86, 0.84, 0.78))
    photo = Image.open(os.path.join(SRC_TEX, "Poster_Photo.png")).convert("RGB")
    mx = 56; pw = W - 2 * mx; ph = int(pw * photo.height / photo.width)
    if ph > H - 2 * mx - 150: ph = H - 2 * mx - 150; pw = int(ph * photo.width / photo.height); mx = (W - pw) // 2
    ph_a = arr(photo.resize((pw, ph), Image.LANCZOS))
    # cheap print: a bit washed out, warm, a faint halftone
    yy, xx = np.mgrid[0:ph, 0:pw]
    halftone = (np.sin(xx * 1.3) * np.sin(yy * 1.3)) * 0.025
    ph_a = (ph_a * 0.82 + 0.08) * np.array((1.0, 0.97, 0.9)) + halftone[..., None]
    top = mx
    paper[top:top + ph, mx:mx + pw] = ph_a
    yy, xx = np.mgrid[0:H, 0:W]
    # yellowing, stains, folds into four, sun-faded to one side
    paper *= (0.88 + 0.16 * fbm(H, W, 4, seed))[..., None]
    paper = paper * np.array((1.0, 0.96, 0.86)) ** (0.6 + 0.8 * fbm(H, W, 3, seed + 1))[..., None]
    stain = np.clip((fbm(H, W, 6, seed + 2) - 0.6) * 4, 0, 1)
    ring = np.clip(1 - np.abs(np.hypot(xx - W * 0.72, yy - H * 0.86) - 70) / 5, 0, 1) * 0.5
    blend(paper, stain * 0.35 + ring, (0.45, 0.36, 0.2))
    fold = np.exp(-((xx - W / 2) / 3.0) ** 2) * 0.5 + np.exp(-((yy - H / 2) / 3.0) ** 2) * 0.5
    fold_hi = np.exp(-((xx - W / 2 - 5) / 3.0) ** 2) * 0.3 + np.exp(-((yy - H / 2 - 5) / 3.0) ** 2) * 0.3
    paper *= (1 - fold * 0.35)[..., None]; paper += (fold_hi * 0.15)[..., None]
    scratches = (vnoise(H, W, 200, seed + 3) > 0.985) * 0.4
    blend(paper, scratches, (0.9, 0.9, 0.86))
    grime = np.clip(1 - np.minimum(np.minimum(yy, H - 1 - yy), np.minimum(xx, W - 1 - xx)) / 40.0, 0, 1)
    blend(paper, grime * (0.4 + 0.4 * fbm(H, W, 20, seed + 4)), (0.3, 0.25, 0.18))
    # yellowed tape over the top corners
    for cx, ang in ((70, 40), (W - 70, -40)):
        t = Image.new("L", (W, H), 0); td = ImageDraw.Draw(t)
        c, s = math.cos(math.radians(ang)), math.sin(math.radians(ang)); L, Wd = 110, 34
        pts = [(cx + dx * c - dy * s, 50 + dx * s + dy * c) for dx, dy in ((-L, -Wd), (L, -Wd), (L, Wd), (-L, Wd))]
        td.polygon(pts, fill=255)
        m = arr(t) * (0.5 + 0.2 * fbm(H, W, 30, seed + 6))
        blend(paper, m, (0.78, 0.66, 0.36))
    os.makedirs(PROP_TEX, exist_ok=True)
    to_img(paper).save(os.path.join(PROP_TEX, "Poster_BaseColor.png"))
    # normal: paper fibre, the folds, a few wrinkles
    hgt = fbm(H, W, 60, seed + 7, 3) * 0.6 + fbm(H, W, 8, seed + 8, 3) * 1.5 - fold * 2.0
    gy, gx = np.gradient(hgt)
    k = 3.0; n = np.dstack((-gx * k, gy * k, np.ones_like(hgt))); n /= np.linalg.norm(n, axis=2, keepdims=True)
    to_img(n * 0.5 + 0.5).save(os.path.join(PROP_TEX, "Poster_Normal.png"))
    sm = 0.12 + 0.1 * fbm(H, W, 10, seed + 9)
    sm[top:top + ph, mx:mx + pw] += 0.15   # the glossy print over matte paper
    ms = np.dstack((np.zeros_like(sm), np.zeros_like(sm), np.zeros_like(sm), sm))
    to_img(ms, "RGBA").save(os.path.join(PROP_TEX, "Poster_MetallicSmoothness.png"))
    return W, H

# ------------------------------------------------------------------ graffiti

def spray(size, shape, col, seed, soft=2.5, over=0.22, drips=0, drip_len=(30, 160), drip_w=(4, 8)):
    """shape: L-mode mask. Returns an RGBA float layer: a hard core, soft edges, a speckled overspray halo and runs."""
    W, H = size; rng = np.random.default_rng(seed)
    m = shape.filter(ImageFilter.GaussianBlur(soft))
    core = np.clip((arr(m) - 0.3) * 2.2, 0, 1)
    halo = arr(shape.filter(ImageFilter.GaussianBlur(soft * 6)))
    speck = (rng.random((H, W)) < halo * 0.5).astype(float)
    a = np.maximum(core, halo * over + speck * 0.35 * halo)
    if drips:
        base = arr(shape) > 0.5
        lower = base & ~np.vstack((base[1:], np.zeros((1, W), bool)))   # pixels with nothing below
        ys, xs = np.nonzero(lower)
        if len(xs):
            dimg = Image.new("L", size, 0); dd = ImageDraw.Draw(dimg)
            for i in rng.choice(len(xs), size=min(drips, len(xs)), replace=False):
                x, y = int(xs[i]), int(ys[i]); L = rng.uniform(*drip_len); w = rng.uniform(*drip_w)
                wob = rng.uniform(-3, 3)
                dd.line((x, y - 2, x + wob, y + L), fill=255, width=int(w))
                dd.ellipse((x + wob - w * 0.75, y + L - w * 0.6, x + wob + w * 0.75, y + L + w * 0.9), fill=255)
            a = np.maximum(a, np.clip((arr(dimg.filter(ImageFilter.GaussianBlur(1.2))) - 0.2) * 1.6, 0, 1))
    rgb = np.ones((H, W, 3)) * np.array(col, float)[None, None, :]
    return np.dstack((rgb, a))

def over(dst, src):
    a = src[..., 3:]; dst[..., :3] = dst[..., :3] * (1 - a) + src[..., :3] * a
    dst[..., 3:] = dst[..., 3:] + a * (1 - dst[..., 3:])

def wall_break(layer, seed, amount=0.25):
    """Paint on rough plaster: it misses the pits and thins out in patches."""
    H, W = layer.shape[:2]; rng = np.random.default_rng(seed)
    pits = (rng.random((H, W)) < 0.05).astype(float) * 0.5
    thin = 1 - amount * fbm(H, W, 12, seed, 3)
    layer[..., 3] *= np.clip(thin - pits, 0, 1)

def text_mask(size, s, font, xy, stroke=0, anchor="mm", rot=0):
    m = Image.new("L", size, 0); d = ImageDraw.Draw(m)
    d.text(xy, s, font=font, fill=255, stroke_width=stroke, stroke_fill=255, anchor=anchor)
    if rot: m = m.rotate(rot, resample=Image.BICUBIC, center=xy)
    return m

def save_graffiti(name, layer):
    os.makedirs(GRAF_TEX, exist_ok=True)
    out = layer.copy(); out[..., :3] = np.clip(out[..., :3], 0, 1)
    to_img(out, "RGBA").save(os.path.join(GRAF_TEX, f"Graffiti_{name}.png"))

def g_lmm():
    """LMM in fat bubble letters: a black 3D drop, a white outline, a green-to-yellow fade, shine marks, runs."""
    S = (2048, 1024); seed = 101
    f = F("impact.ttf", 640); xy = (S[0] // 2, S[1] // 2 - 20)
    L = np.zeros((S[1], S[0], 4))
    fill = text_mask(S, "LMM", f, xy, rot=4)
    outline = text_mask(S, "LMM", f, xy, stroke=34, rot=4)
    drop = text_mask(S, "LMM", f, (xy[0] + 34, xy[1] + 30), stroke=34, rot=4)
    over(L, spray(S, drop, (0.03, 0.03, 0.035), seed, soft=3, drips=10))
    over(L, spray(S, outline, (0.92, 0.92, 0.9), seed + 1, soft=2.5))
    fl = spray(S, fill, (0, 0, 0), seed + 2, soft=2, drips=14, drip_len=(40, 220))
    yy = np.linspace(0, 1, S[1])[:, None, None]
    grad = np.array((0.15, 0.75, 0.2)) * (1 - yy) + np.array((0.95, 0.85, 0.1)) * yy
    fl[..., :3] = grad * np.ones((1, S[0], 1)) * (0.85 + 0.15 * fbm(S[1], S[0], 8, seed + 3))[..., None]
    over(L, fl)
    # shine: short white dashes on the letters' upper left
    sh = Image.new("L", S, 0); d = ImageDraw.Draw(sh); rng = random.Random(seed)
    for x in (560, 950, 1400):
        d.line((x, 330, x + 60, 300), fill=255, width=16); d.ellipse((x + 85, 280, x + 105, 300), fill=255)
    over(L, spray(S, Image.fromarray((np.minimum(arr(sh), arr(fill)) * 255).astype(np.uint8)), (1, 1, 1), seed + 4, soft=1.5, over=0.1))
    # a small crown over the first M
    cr = Image.new("L", S, 0); d = ImageDraw.Draw(cr)
    d.line([(1010, 180), (1040, 90), (1090, 160), (1140, 70), (1190, 160), (1240, 90), (1270, 180), (1010, 180)], fill=255, width=14, joint="curve")
    over(L, spray(S, cr, (0.95, 0.85, 0.1), seed + 5, soft=2, drips=3))
    wall_break(L, seed + 6, 0.2)
    save_graffiti("LMM", L)

def g_no_exit():
    """ВЫХОДА НЕТ: hasty red, uneven letters with long runs."""
    S = (2048, 768); seed = 202
    f = F("segoeprb.ttf", 260)
    m = text_mask(S, "ВЫХОДА НЕТ", f, (S[0] // 2, S[1] // 2 - 60), rot=-3)
    # thicken unevenly, as a fast can held too close
    m = m.filter(ImageFilter.MaxFilter(9))
    L = spray(S, m, (0.55, 0.03, 0.02), seed, soft=3.5, over=0.35, drips=26, drip_len=(40, 300), drip_w=(5, 10))
    L[..., :3] *= (0.75 + 0.35 * fbm(S[1], S[0], 10, seed))[..., None]
    wall_break(L, seed + 1, 0.3)
    save_graffiti("NoExit", L)

def g_tag():
    """A fast black marker-and-can tag with an underline swoosh and a star."""
    S = (1536, 768); seed = 303
    m = text_mask(S, "Krot", F("segoescb.ttf", 380), (S[0] // 2, S[1] // 2 - 40), rot=8)
    d = ImageDraw.Draw(m)
    pts = [(220 + i * 11, 610 - 40 * math.sin(i / 100 * math.pi) - i * 1.2) for i in range(101)]
    d.line(pts, fill=255, width=20, joint="curve")
    for i in range(10):
        a = i * math.pi / 5 - math.pi / 2; r = 70 if i % 2 == 0 else 28
    star = [(1290 + (70 if i % 2 == 0 else 28) * math.cos(i * math.pi / 5 - math.pi / 2),
             170 + (70 if i % 2 == 0 else 28) * math.sin(i * math.pi / 5 - math.pi / 2)) for i in range(10)]
    d.polygon(star, outline=255, width=12)
    L = spray(S, m, (0.025, 0.025, 0.03), seed, soft=1.8, over=0.15, drips=6, drip_len=(20, 90), drip_w=(3, 5))
    wall_break(L, seed + 1, 0.15)
    save_graffiti("Tag", L)

def g_smiley():
    """A dripping yellow smiley with crossed-out eyes and a stitched grin."""
    S = (1024, 1024); seed = 404
    m = Image.new("L", S, 0); d = ImageDraw.Draw(m)
    c = (512, 470); r = 330
    d.ellipse((c[0] - r, c[1] - r, c[0] + r, c[1] + r), outline=255, width=44)
    for ex in (-130, 130):
        x, y = c[0] + ex, c[1] - 90
        d.line((x - 60, y - 60, x + 60, y + 60), fill=255, width=40); d.line((x - 60, y + 60, x + 60, y - 60), fill=255, width=40)
    d.arc((c[0] - 200, c[1] - 120, c[0] + 200, c[1] + 220), 20, 160, fill=255, width=36)
    for k in range(-3, 4):
        a = math.radians(90 - k * 18); x = c[0] + 200 * math.cos(a); y = c[1] + 50 + 170 * math.sin(a)
        d.line((x, y - 40, x, y + 40), fill=255, width=14)
    L = spray(S, m, (0.93, 0.78, 0.08), seed, soft=3, over=0.3, drips=18, drip_len=(40, 200), drip_w=(5, 9))
    wall_break(L, seed + 1, 0.3)
    save_graffiti("Smiley", L)

def g_dont_look():
    """НЕ ОГЛЯДЫВАЙСЯ in thin white capitals under a long arrow pointing on down the corridor."""
    S = (2048, 768); seed = 505
    m = Image.new("L", S, 0); d = ImageDraw.Draw(m)
    y = 230
    d.line((120, y, 1780, y - 10), fill=255, width=30)
    d.line((1680, y - 120, 1800, y - 12, 1690, y + 100), fill=255, width=30, joint="curve")
    t = text_mask(S, "НЕ ОГЛЯДЫВАЙСЯ", F("bahnschrift.ttf", 200), (S[0] // 2 - 40, 500), rot=-1)
    m = Image.fromarray(np.maximum(arr(m), arr(t.filter(ImageFilter.MaxFilter(5)))).astype(np.float64).__mul__(255).astype(np.uint8))
    L = spray(S, m, (0.9, 0.9, 0.88), seed, soft=2.5, over=0.25, drips=16, drip_len=(30, 160), drip_w=(4, 7))
    wall_break(L, seed + 1, 0.35)
    save_graffiti("DontLook", L)

def g_eye():
    """A big eye in a triangle with rays, black over a faded red disc: something someone believed in."""
    S = (1024, 1024); seed = 606
    disc = Image.new("L", S, 0); ImageDraw.Draw(disc).ellipse((150, 160, 874, 884), fill=255)
    L = spray(S, disc, (0.4, 0.04, 0.03), seed, soft=8, over=0.4, drips=12, drip_len=(40, 140))
    L[..., 3] *= 0.75
    m = Image.new("L", S, 0); d = ImageDraw.Draw(m)
    tri = [(512, 190), (830, 760), (194, 760)]
    d.polygon(tri, outline=255, width=30)
    d.chord((330, 400, 694, 680), 180, 360, fill=255); d.chord((330, 400, 694, 680), 0, 180, fill=255)
    d.ellipse((362, 450, 662, 630), fill=0)
    d.ellipse((452, 480, 572, 600), fill=255)
    for i in range(14):
        a = math.radians(-160 + i * 10.5)
        r0, r1 = 400, 450 + (i % 2) * 40
        d.line((512 + r0 * math.cos(a), 540 + r0 * math.sin(a), 512 + r1 * math.cos(a), 540 + r1 * math.sin(a)), fill=255, width=16)
    over(L, spray(S, m, (0.02, 0.02, 0.025), seed + 1, soft=2.2, over=0.2, drips=10, drip_len=(30, 120), drip_w=(4, 6)))
    wall_break(L, seed + 2, 0.3)
    save_graffiti("Eye", L)

GRAFFITI = {"LMM": g_lmm, "NoExit": g_no_exit, "Tag": g_tag, "Smiley": g_smiley, "DontLook": g_dont_look, "Eye": g_eye}

if __name__ == "__main__":
    import sys
    what = sys.argv[1:] or ["books", "poster", "graffiti"]
    if "books" in what:
        for i, (n, b) in enumerate(BOOKS.items()): print(book_jacket(n, b, 50 + i * 7))
        print(page_text())
    if "poster" in what: print("poster", poster())
    if "graffiti" in what:
        for n, fn in GRAFFITI.items(): fn(); print("graffiti", n)
