"""Khrushchevka apartment props for BodycamHorror: a chipped enamel mug with a rose decal, a clipboard with a
form and a pen, a small worn sofa with a tapestry seat and a plaid throw, and a wall shelf with books, a tin
and a trailing ivy. Run inside Blender over the bridge, after propkit.py:

    exec(open(r"D:\\BodycamHorror\\Blender\\tools\\propkit.py").read())
    exec(open(r"D:\\BodycamHorror\\Blender\\tools\\apartment_props.py").read())
    build_all()            # meshes + procedural materials + print textures, then bakes in the background
    finish_all()           # once Blender/bake_log.txt says DONE: join, baked material, FBX export

Metres, Z up, front facing -Y. Print textures go to Blender/tex, baked maps to Assets/Generated/Textures/Props,
FBX files to Assets/Generated/Imported.
"""
import bpy, bmesh, math, os, random
import numpy as np
from mathutils import Vector, Matrix

APT_PROPS = ["Mug", "Clipboard", "Sofa", "WallShelf"]
FBX_OUT = os.path.join(PROJECT, "Assets", "Generated", "Imported")
BAKE_LOG = os.path.join(PROJECT, "Blender", "bake_log.txt")

# ------------------------------------------------------------------ print textures (numpy)

def _vnoise(h, w, cells, seed):
    rng = np.random.default_rng(seed)
    cy, cx = cells, max(1, int(round(cells * w / h)))
    g = rng.random((cy + 1, cx + 1))
    y = np.linspace(0, cy, h, endpoint=False); x = np.linspace(0, cx, w, endpoint=False)
    yi = y.astype(int); xi = x.astype(int); fy = y - yi; fx = x - xi
    fy = fy * fy * (3 - 2 * fy); fx = fx * fx * (3 - 2 * fx)
    a = g[yi][:, xi]; b = g[yi][:, xi + 1]; c = g[yi + 1][:, xi]; d = g[yi + 1][:, xi + 1]
    top = a + (b - a) * fx[None, :]; bot = c + (d - c) * fx[None, :]
    return top + (bot - top) * fy[:, None]

def _fbm(h, w, cells, seed, octaves=4):
    out = np.zeros((h, w)); amp = 1.0; tot = 0.0
    for o in range(octaves):
        out += _vnoise(h, w, cells * 2 ** o, seed + o * 17) * amp; tot += amp; amp *= 0.5
    return out / tot

def _save(name, img):
    h, w = img.shape[:2]
    px = np.ones((h, w, 4), np.float32); px[..., :3] = np.clip(img, 0, 1)
    px = px[::-1]   # Blender images start at the bottom row
    if name in bpy.data.images: bpy.data.images.remove(bpy.data.images[name])
    im = bpy.data.images.new(name, w, h, alpha=False)
    im.pixels.foreach_set(px.ravel())
    path = os.path.join(SRC_TEX, name + ".png"); os.makedirs(SRC_TEX, exist_ok=True)
    im.filepath_raw = path; im.file_format = 'PNG'; im.save()
    bpy.data.images.remove(im)
    return path

def _blend(img, mask, col):
    m = np.clip(mask, 0, 1)[..., None]
    img[:] = img * (1 - m) + np.array(col)[None, None, :] * m

def _rose(img, cx, cy, R, rng, dark=(0.32, 0.03, 0.04), light=(0.78, 0.22, 0.24)):
    h, w = img.shape[:2]
    x0, x1 = int(max(cx - R * 1.4, 0)), int(min(cx + R * 1.4, w)); y0, y1 = int(max(cy - R * 1.4, 0)), int(min(cy + R * 1.4, h))
    if x1 <= x0 or y1 <= y0: return
    yy, xx = np.mgrid[y0:y1, x0:x1].astype(float)
    dx, dy = xx - cx, yy - cy; r = np.hypot(dx, dy); th = np.arctan2(dy, dx) + rng.random() * 6
    edge = R * (1 + 0.13 * np.cos(5 * th) + 0.05 * np.cos(11 * th))
    mask = np.clip((edge - r) / 2.0, 0, 1)
    spiral = np.abs(np.sin(th * 1.0 + r / R * 13.0))
    shade = np.clip(r / R, 0, 1) * 0.7 + 0.3 * spiral
    col = np.array(dark)[None, None, :] * (1 - shade[..., None]) + np.array(light)[None, None, :] * shade[..., None]
    col *= (0.55 + 0.45 * np.clip(spiral * 3, 0, 1))[..., None]   # petal edges drawn in dark
    sub = img[y0:y1, x0:x1]; m = mask[..., None]
    img[y0:y1, x0:x1] = sub * (1 - m) + col * m

def _leaf(img, cx, cy, L, ang, col=(0.2, 0.36, 0.16)):
    h, w = img.shape[:2]; ext = int(L * 1.2)
    x0, x1 = int(max(cx - ext, 0)), int(min(cx + ext, w)); y0, y1 = int(max(cy - ext, 0)), int(min(cy + ext, h))
    if x1 <= x0 or y1 <= y0: return
    yy, xx = np.mgrid[y0:y1, x0:x1].astype(float)
    dx, dy = xx - cx, yy - cy; ca, sa = math.cos(ang), math.sin(ang)
    u = dx * ca + dy * sa; v = -dx * sa + dy * ca
    t = np.clip(u / L, 0, 1)
    halfw = L * 0.32 * np.sin(np.pi * t) ** 0.8
    mask = np.clip((halfw - np.abs(v)) / 1.5, 0, 1) * (u > 0) * (u < L)
    vein = np.clip(1 - np.abs(v) / 1.2, 0, 1) * 0.5
    c = np.array(col)[None, None, :] * (1 - 0.35 * np.clip(np.abs(v) / (halfw + 1e-3), 0, 1))[..., None] * (1 - vein)[..., None]
    sub = img[y0:y1, x0:x1]; m = mask[..., None]
    img[y0:y1, x0:x1] = sub * (1 - m) + c * m

def tex_mug():
    """Around the mug (u) by its height (v). Plain white, a rose spray on the front (u = 0.25: facing -Y)."""
    h, w = 768, 2048; img = np.ones((h, w, 3)); rng = np.random.default_rng(7)
    cx, cy = int(w * 0.25), int(h * 0.5)
    for i in range(9):
        a = rng.random() * 6.28; d = 130 + rng.random() * 110
        _leaf(img, cx + math.cos(a) * d * 0.6, cy + math.sin(a) * d * 0.5, 100 + rng.random() * 70, a)
    _rose(img, cx, cy, 120, rng)
    _rose(img, cx - 220, cy + 100, 76, rng); _rose(img, cx + 205, cy - 85, 66, rng)
    for i in range(5):   # buds
        a = rng.random() * 6.28
        _rose(img, cx + math.cos(a) * 280, cy + math.sin(a) * 170, 26, rng)
    # worn print: the decal has faded and crazed
    fade = _fbm(h, w, 6, 3)
    img[:] = 1 - (1 - img) * (0.62 + 0.38 * fade)[..., None]
    craze = (_fbm(h, w, 60, 9, 2) > 0.62) * 0.18
    img[:] = 1 - (1 - img) * (1 - craze)[..., None]
    # a thin faded red band under the rim
    _blend(img, ((np.arange(h) > h * 0.075) & (np.arange(h) < h * 0.1))[:, None].repeat(w, 1) * 0.55, (0.6, 0.15, 0.13))
    return _save("Mug_Print", img)

def tex_paper():
    """A typed form on A4: heading, numbered lines with filled-in handwriting, a table, a stamp, stains."""
    h, w = 1448, 1024; img = np.ones((h, w, 3)) * 0.97; rng = np.random.default_rng(21)
    ink = (0.16, 0.15, 0.14)
    def words(y, x0, x1, hgt, dens=1.0, col=ink):
        x = x0
        while x < x1:
            ww = int(rng.integers(14, 70) * dens); ww = min(ww, x1 - x)
            if ww > 6:
                blk = np.ones((hgt, ww)) * (0.75 + 0.25 * rng.random())
                blk *= (rng.random((hgt, ww)) > 0.25) * 0.5 + 0.5
                _blend(img[y:y + hgt, x:x + ww], blk, col)
            x += ww + int(rng.integers(8, 14))
    def line(y, x0, x1, t=2, col=ink): _blend(img[y:y + t, x0:x1], np.ones((t, x1 - x0)) * 0.8, col)
    def scrawl(y, x0, x1, col=(0.12, 0.16, 0.42)):
        xs = np.arange(x0, x1); ys = y + 6 * np.sin(xs * 0.21 + rng.random() * 6) * np.sin(xs * 0.031) + rng.normal(0, 0.6, xs.size).cumsum() * 0.05
        for dx in range(-1, 2):
            for x, yv in zip(xs, ys): img[int(yv) + dx, x] = col
    words(70, 330, 700, 22, 1.3)                  # heading
    words(108, 260, 770, 12, 0.8)
    line(140, 90, 934, 3)
    yv = 190
    for i in range(9):
        words(yv, 90, 90 + int(rng.integers(160, 320)), 12)
        line(yv + 18, 420, 934, 1)
        if rng.random() < 0.75: scrawl(yv + 6, 440, 440 + int(rng.integers(120, 420)))
        yv += 48
    # the table
    ty0, ty1 = yv + 30, yv + 30 + 7 * 52
    for k in range(8): line(ty0 + k * 52, 90, 934, 2)
    for xc in (90, 200, 560, 760, 934): _blend(img[ty0:ty1, xc:xc + 2], np.ones((ty1 - ty0, 2)) * 0.8, ink)
    for k in range(7):
        words(ty0 + k * 52 + 20, 110, 180, 11, 0.5); words(ty0 + k * 52 + 20, 215, 540, 11)
        if k < 5: scrawl(ty0 + k * 52 + 28, 780, 900)
    words(ty1 + 60, 90, 934, 12); words(ty1 + 90, 90, 640, 12)
    # signature and a faded violet stamp
    scrawl(ty1 + 200, 620, 860)
    sy, sx = ty1 + 190, 300
    yy, xx = np.mgrid[0:h, 0:w]; rr = np.hypot(yy - sy, xx - sx)
    ring = ((np.abs(rr - 92) < 4) | (np.abs(rr - 70) < 2)) * (0.45 + 0.4 * _fbm(h, w, 30, 4, 2))
    _blend(img, ring, (0.36, 0.22, 0.52))
    # yellowing, a damp tide mark and a mug ring
    yel = _fbm(h, w, 4, 11)
    img *= (1 - 0.12 * yel)[..., None] * np.array([1.0, 0.97, 0.88])[None, None, :] ** yel[..., None]
    tide = np.clip(1 - np.abs(np.hypot((yy - h * 0.92) / 1.3, xx - w * 0.85) - 260 + 40 * _fbm(h, w, 8, 5)) / 8, 0, 1) * 0.35
    _blend(img, tide, (0.55, 0.42, 0.25))
    cup = np.clip(1 - np.abs(np.hypot(yy - h * 0.2, xx - w * 0.82) - 115) / 6, 0, 1) * (0.25 + 0.35 * _fbm(h, w, 20, 6, 2))
    _blend(img, cup, (0.45, 0.3, 0.15))
    return _save("Clipboard_Print", img)

def tex_tapestry():
    """A tiling Soviet tapestry upholstery: dark wine ground, rows of faded ochre-pink flowers with leaves."""
    s = 1024; img = np.zeros((s, s, 3)); rng = np.random.default_rng(5)
    img[:] = np.array((0.22, 0.07, 0.055))
    weave = (np.sin(np.arange(s) * 1.6)[:, None] * np.sin(np.arange(s) * 1.6)[None, :]) * 0.05
    img *= (0.9 + weave + 0.15 * _fbm(s, s, 16, 2))[..., None]
    # a lozenge lattice
    yy, xx = np.mgrid[0:s, 0:s].astype(float)
    lat = np.abs(((xx + yy) % 256) - 128) < 3
    lat |= np.abs(((xx - yy) % 256) - 128) < 3
    _blend(img, lat * 0.55, (0.52, 0.36, 0.18))
    for gy in range(4):
        for gx in range(4):
            cx = gx * 256 + (128 if gy % 2 else 0); cy = gy * 256 + 128
            for ox in (0, -s, s):
                for oy in (0, -s, s):
                    X, Y = cx + ox, cy + oy
                    if -150 < X < s + 150 and -150 < Y < s + 150:
                        rr = random.Random(gx * 7 + gy)
                        for i in range(6):
                            a = i * 1.047 + 0.5; _leaf(img, X + math.cos(a) * 34, Y + math.sin(a) * 34, 46, a, (0.22, 0.27, 0.12))
                        _rose(img, X, Y, 44, rng, dark=(0.34, 0.1, 0.06), light=(0.62, 0.38, 0.26))
                        for i in range(4):
                            a = i * 1.571
                            _rose(img, X + math.cos(a) * 92, Y + math.sin(a) * 92, 14, rng, dark=(0.36, 0.25, 0.1), light=(0.58, 0.46, 0.27))
    img *= (0.8 + 0.2 * _fbm(s, s, 5, 8))[..., None]
    return _save("Sofa_Tapestry", img)

def tex_plaid():
    s = 1024; rng = np.random.default_rng(3)
    def stripes(n):
        a = np.zeros((n, 3)); a[:] = (0.36, 0.08, 0.06)
        for p0, wdt, col in ((0, 40, (0.12, 0.05, 0.04)), (90, 14, (0.62, 0.48, 0.28)), (130, 60, (0.18, 0.12, 0.06)),
                             (220, 8, (0.62, 0.48, 0.28)), (256 - 30, 20, (0.12, 0.05, 0.04))):
            for k in range(0, n, 256): a[k + p0:k + p0 + wdt] = col
        return a
    a = stripes(s)
    img = (a[:, None, :] + a[None, :, :]) * 0.5
    tw = ((np.arange(s)[:, None] + np.arange(s)[None, :]) % 4 < 2)[..., None]
    img = np.where(tw, a[:, None, :], a[None, :, :]) * 0.6 + img * 0.4   # twill
    img *= (0.85 + 0.15 * _fbm(s, s, 12, 4))[..., None]
    return _save("Sofa_Plaid", img)

# ------------------------------------------------------------------ UV helpers

def print_uv_planar(o, axis_u=0, axis_v=1, box=None):
    """A 'Print' UV laid flat: u, v from two object axes, fitted to box ((u0, u1), (v0, v1))."""
    me = o.data; uv = me.uv_layers.get("Print") or me.uv_layers.new(name="Print")
    for loop in me.loops:
        co = me.vertices[loop.vertex_index].co
        (u0, u1), (v0, v1) = box
        uv.data[loop.index].uv = ((co[axis_u] - u0) / (u1 - u0), (co[axis_v] - v0) / (v1 - v0))

def print_uv_cube(o, size):
    """A 'Print' UV from a box projection (fabric that tiles), in object space, size metres per tile."""
    me = o.data; uv = me.uv_layers.get("Print") or me.uv_layers.new(name="Print")
    for p in me.polygons:
        n = p.normal; ax = max(range(3), key=lambda i: abs(n[i]))
        a, b = [(1, 2), (0, 2), (0, 1)][ax]
        for li in p.loop_indices:
            co = me.vertices[me.loops[li].vertex_index].co
            uv.data[li].uv = (co[a] / size, co[b] / size)

def print_uv_const(o, u=0.01, v=0.99):
    me = o.data; uv = me.uv_layers.get("Print") or me.uv_layers.new(name="Print")
    for d in uv.data: d.uv = (u, v)

def pillow(name, size, loc, mat, parent, puff=0.025, cuts=8, rot=None, sag=0.0):
    """A soft cushion: a subdivided box with its faces bulged out and its top sagging in the middle."""
    bm = bmesh.new(); bmesh.ops.create_cube(bm, size=1.0)
    bmesh.ops.subdivide_edges(bm, edges=bm.edges[:], cuts=cuts, use_grid_fill=True)
    hx, hy, hz = size[0] / 2, size[1] / 2, size[2] / 2
    for v in bm.verts:
        x, y, z = v.co.x * 2, v.co.y * 2, v.co.z * 2          # -1..1
        fx, fy, fz = 1 - x * x, 1 - y * y, 1 - z * z
        v.co.x = x * hx + math.copysign(puff * fy * fz, x) * 0.6
        v.co.y = y * hy + math.copysign(puff * fx * fz, y) * 0.6
        v.co.z = z * hz + math.copysign(puff * fx * fy, z) - (sag * fx * fy if z > 0 else 0)
    o = from_bm(name, bm, mat, parent); o.location = loc
    if rot: o.rotation_euler = rot
    for p in o.data.polygons: p.use_smooth = True
    m = o.modifiers.new("Sub", "SUBSURF"); m.levels = 1; m.render_levels = 1
    return o

def revolve(name, profile, seg, mat, parent, loc=(0, 0, 0)):
    """Spins an (r, z) profile round Z."""
    bm = bmesh.new(); prev = None; verts = []
    for r, z in profile: verts.append(bm.verts.new((r, 0, z)))
    for a, b in zip(verts, verts[1:]): bm.edges.new((a, b))
    bmesh.ops.spin(bm, geom=bm.verts[:] + bm.edges[:], axis=(0, 0, 1), cent=(0, 0, 0), steps=seg, angle=math.tau, use_duplicate=False)
    bmesh.ops.remove_doubles(bm, verts=bm.verts[:], dist=1e-6)
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces[:])   # the spin can leave them facing in
    o = from_bm(name, bm, mat, parent); o.location = loc
    for p in o.data.polygons: p.use_smooth = True
    return o

def smooth_path(pts, n):
    """Catmull-Rom through pts, n points."""
    P = [Vector(p) for p in pts]; P = [P[0]] + P + [P[-1]]; out = []
    for i in range(n):
        t = i / (n - 1) * (len(P) - 3); k = min(int(t), len(P) - 4); f = t - k
        p0, p1, p2, p3 = P[k:k + 4]
        out.append(0.5 * ((2 * p1) + (-p0 + p2) * f + (2 * p0 - 5 * p1 + 4 * p2 - p3) * f * f + (-p0 + 3 * p1 - 3 * p2 + p3) * f ** 3))
    return out

# ------------------------------------------------------------------ the mug

def build_mug():
    rt = root("Mug", (0, 0, 0))
    ceramic = role("ceramic")
    prof = [(0.0, 0.0015), (0.03, 0.0015), (0.034, 0.0), (0.037, 0.0005), (0.0395, 0.004), (0.041, 0.012),
            (0.041, 0.09), (0.0408, 0.0945), (0.0396, 0.0955), (0.0378, 0.0945), (0.0372, 0.09), (0.0372, 0.013),
            (0.035, 0.0095), (0.0, 0.0085)]
    body = revolve("Mug_Body", prof, 48, ceramic, rt)
    # the decal runs round the outside only
    me = body.data; uv = me.uv_layers.new(name="Print")
    for p in me.polygons:
        c = p.center; outer = math.hypot(c.x, c.y) > 0.0405 and 0.01 < c.z < 0.092
        for li in p.loop_indices:
            co = me.vertices[me.loops[li].vertex_index].co
            if outer:
                u = (math.atan2(co.y, co.x) + math.pi) / math.tau
                if math.atan2(c.y, c.x) > 2.5 and u < 0.5: u += 1.0   # the seam at -X
                if math.atan2(c.y, c.x) < -2.5 and u > 0.5: u -= 1.0
                uv.data[li].uv = (u, co.z / 0.0955)
            else:
                uv.data[li].uv = (0.01, 0.99)
    pts = smooth_path([(0.038, 0, 0.079), (0.058, 0, 0.081), (0.071, 0, 0.068), (0.073, 0, 0.046),
                       (0.066, 0, 0.028), (0.05, 0, 0.022), (0.038, 0, 0.024)], 24)
    h = tube("Mug_Handle", [tuple(p) for p in pts], 0.0058, ceramic, rt)
    for v in h.data.vertices: v.co.y *= 0.75     # a flat oval section, wider than deep
    print_uv_const(h)
    return rt

CFG_MUG = {
    "ceramic": dict(base=((0.80, 0.78, 0.72), (0.72, 0.70, 0.64)), rough=0.2, metal=0.0,
                    wear=((0.28, 0.27, 0.25), 0.75, 0.0, 0.9), chips=0.55, chip_scale=16,
                    dirt=((0.16, 0.10, 0.05), 0.8, 0.0, 0.9), dust=0.35, edge_radius=0.0015, ao_dist=0.03,
                    image=None, bump=0.15),
}

# ------------------------------------------------------------------ the clipboard

def rounded_rect(w, d, r, n=6):
    pts = []
    for cx, cy, a0 in ((w / 2 - r, d / 2 - r, 0), (-w / 2 + r, d / 2 - r, 90), (-w / 2 + r, -d / 2 + r, 180), (w / 2 - r, -d / 2 + r, 270)):
        for i in range(n + 1):
            a = math.radians(a0 + 90 * i / n); pts.append((cx + r * math.cos(a), cy + r * math.sin(a)))
    return pts

def slab(name, outline, z0, z1, mat, parent):
    bm = bmesh.new()
    vs = [bm.verts.new((x, y, z0)) for x, y in outline]
    f = bm.faces.new(vs)
    ext = bmesh.ops.extrude_face_region(bm, geom=[f])
    for v in [e for e in ext["geom"] if isinstance(e, bmesh.types.BMVert)]: v.co.z = z1
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces[:])
    o = from_bm(name, bm, mat, parent)
    return o

def build_clipboard():
    rt = root("Clipboard", (1.0, 0, 0))
    board, metal, paper, pen = role("board"), role("clipmetal"), role("paper"), role("pen")
    W, D, T = 0.23, 0.32, 0.0035
    b = slab("Clip_Board", rounded_rect(W, D, 0.012), 0.0, T, board, rt); bevel(b, 0.0008, 2)
    # the sheet: lies flat, its lower right corner lifted and curled
    pw, pd = 0.21, 0.297; py0 = -D / 2 + 0.008
    bm = bmesh.new(); nx, ny = 14, 20
    grid = [[bm.verts.new((-pw / 2 + pw * i / nx, py0 + pd * j / ny, T + 0.0004)) for i in range(nx + 1)] for j in range(ny + 1)]
    for j in range(ny):
        for i in range(nx): bm.faces.new((grid[j][i], grid[j][i + 1], grid[j + 1][i + 1], grid[j + 1][i]))
    for v in bm.verts:
        u = (v.co.x + pw / 2) / pw; t = (v.co.y - py0) / pd
        k = max(0.0, (u - 0.62) / 0.38) * max(0.0, (0.3 - t) / 0.3)
        v.co.z += 0.012 * k ** 2.4
    sheet = from_bm("Clip_Paper", bm, paper, rt)
    for p in sheet.data.polygons: p.use_smooth = True
    print_uv_planar(sheet, 0, 1, ((-pw / 2, pw / 2), (py0, py0 + pd)))
    sm = sheet.modifiers.new("Solid", "SOLIDIFY"); sm.thickness = 0.0003; sm.offset = 1
    # a second, older sheet underneath, a little skew
    under = box("Clip_PaperUnder", (pw - 0.004, pd - 0.006, 0.0003), (0.003, py0 + pd / 2 + 0.002, T + 0.0002), paper, rt, bv=0)
    under.rotation_euler = (0, 0, math.radians(-1.2)); print_uv_const(under, 0.5, 0.02)
    # the clip: a base plate, the lever curving over it, the rivets and a ring to hang it by
    cy = D / 2 - 0.028
    box("Clip_Plate", (0.11, 0.04, 0.0016), (0, cy, T + 0.0008), metal, rt, bv=0.0006)
    # the lever: a wide strip of sheet metal rolled over from the jaw back to the rivets
    bm = bmesh.new(); arc = []
    for i in range(15):
        a = math.radians(-100 + 250 * i / 14)
        arc.append((cy + 0.004 + 0.011 * math.cos(a), T + 0.0075 + 0.0055 * math.sin(a)))
    cols = [[bm.verts.new((x, y, z)) for (y, z) in arc] for x in (-0.042, 0.0, 0.042)]
    for c in range(2):
        for i in range(len(arc) - 1): bm.faces.new((cols[c][i], cols[c + 1][i], cols[c + 1][i + 1], cols[c][i + 1]))
    L = from_bm("Clip_Lever", bm, metal, rt)
    for p in L.data.polygons: p.use_smooth = True
    sm = L.modifiers.new("Solid", "SOLIDIFY"); sm.thickness = 0.0012
    lev = box("Clip_Jaw", (0.09, 0.022, 0.0014), (0, cy - 0.012, T + 0.0028), metal, rt, bv=0.0005)
    lev.rotation_euler = (math.radians(-6), 0, 0)
    for x in (-0.04, 0.04): cyl("Clip_Rivet", 0.0028, 0.0024, 0.0016, (x, cy + 0.008, T + 0.0018), metal, rt, seg=12)
    ring = []
    for i in range(25):
        a = math.tau * i / 24; ring.append((0.0, cy + 0.03 + 0.012 * math.cos(a), T + 0.0045 + 0.004 * math.sin(a) * 0.3))
    tube("Clip_Ring", ring, 0.0012, metal, rt, cyclic=True)
    # the pen, lying across the lower right of the sheet
    a, bb = Vector((0.015, -0.115, T + 0.0045)), Vector((0.095, 0.01, T + 0.0045))
    d = (bb - a).normalized()
    rod("Pen_Body", a, bb, 0.0045, pen, rt, seg=16)
    cyl("Pen_Tip", 0.0045, 0.0008, 0.016, a - d * 0.008, metal, rt, seg=16, axis=-d)
    cyl("Pen_End", 0.0046, 0.0042, 0.008, bb + d * 0.004, metal, rt, seg=16, axis=d)
    side = Vector((-d.y, d.x, 0))
    beam("Pen_Clip", bb - d * 0.035 + side * 0.0 + Vector((0, 0, 0.005)), bb - d * 0.002 + Vector((0, 0, 0.005)), 0.0028, 0.0012, metal, rt, bv=0.0003)
    for o in rt.children:
        if o.type == 'MESH' and "Print" not in o.data.uv_layers: print_uv_const(o)
    return rt

CFG_CLIP = {
    "board": dict(base=((0.21, 0.12, 0.055), (0.15, 0.085, 0.04)), rough=0.55, metal=0.0, scratch=0.7,
                  wear=((0.34, 0.24, 0.14), 0.8, 0.0, 0.9), dirt=((0.05, 0.035, 0.02), 0.85, 0.0, 0.8), dust=0.45, edge_radius=0.002),
    "clipmetal": dict(base=((0.62, 0.62, 0.6), (0.5, 0.5, 0.48)), rough=0.32, metal=1.0, scratch=0.4,
                      wear=((0.75, 0.74, 0.72), 0.22, 1.0, 0.5), dirt=((0.2, 0.09, 0.035), 0.9, 0.0, 1.0), dust=0.3, edge_radius=0.001, ao_dist=0.02),
    "paper": dict(base=((0.86, 0.83, 0.72), (0.80, 0.76, 0.63)), rough=0.88, metal=0.0, wrinkle=0.12, bump=0.2,
                  wear=((0.7, 0.64, 0.5), 0.9, 0.0, 0.7), dirt=((0.32, 0.25, 0.15), 0.9, 0.0, 0.5), dust=0.25, wet=0.35, image=None),
    "pen": dict(base=((0.035, 0.035, 0.04), (0.02, 0.02, 0.025)), rough=0.3, metal=0.0,
                wear=((0.14, 0.14, 0.15), 0.5, 0.0, 0.5), dirt=((0.05, 0.04, 0.03), 0.8, 0.0, 0.5), dust=0.2, edge_radius=0.001, ao_dist=0.02),
}

# ------------------------------------------------------------------ the sofa

def build_sofa():
    rt = root("Sofa", (2.5, 0, 0))
    fabric, vinyl, wood, throw = role("tapestry"), role("vinyl"), role("legwood"), role("throw")
    W, D = 1.45, 0.80
    box("Sofa_Base", (1.18, 0.74, 0.21), (0, 0.0, 0.205), vinyl, rt, bv=0.02)
    box("Sofa_BackFrame", (1.45, 0.1, 0.56), (0, 0.35, 0.41), vinyl, rt, bv=0.025)
    for sx in (-1, 1):
        x = sx * 0.65
        box(f"Sofa_Arm{sx}", (0.15, 0.80, 0.44), (x, 0.0, 0.33), vinyl, rt, bv=0.02)
        roll = cyl(f"Sofa_ArmRoll{sx}", 0.082, 0.082, 0.80, (x + sx * 0.008, 0.0, 0.57), vinyl, rt, seg=24, axis=(0, 1, 0))
        bevel(roll, 0.012, 3)
        # the arm's front panel: a little proud, a scroll shape
        cyl(f"Sofa_ArmFront{sx}", 0.07, 0.07, 0.02, (x + sx * 0.008, -0.405, 0.57), vinyl, rt, seg=24, axis=(0, 1, 0), bv=0.006)
    pillow("Sofa_SeatL", (0.575, 0.62, 0.14), (-0.29, -0.065, 0.375), fabric, rt, puff=0.028, sag=0.012)
    pillow("Sofa_SeatR", (0.575, 0.62, 0.14), (0.29, -0.065, 0.373), fabric, rt, puff=0.03, sag=0.02, rot=(0, math.radians(-0.8), math.radians(0.6)))
    pillow("Sofa_Back", (1.16, 0.2, 0.44), (0, 0.235, 0.66), fabric, rt, puff=0.035, rot=(math.radians(-9), 0, 0))
    for sx in (-1, 1):
        for sy in (-1, 1):
            cyl("Sofa_Leg", 0.022, 0.015, 0.1, (sx * 0.66, sy * 0.33, 0.05), wood, rt, seg=12)
    # the throw: over the right arm, part on the seat, hanging down the outside
    path = smooth_path([(0.30, 0.448), (0.46, 0.452), (0.535, 0.49), (0.565, 0.58), (0.6, 0.645), (0.66, 0.66),
                        (0.72, 0.635), (0.752, 0.56), (0.76, 0.42), (0.762, 0.26), (0.764, 0.16)], 40)
    bm = bmesh.new(); ny = 26; y0, y1 = -0.39, 0.26; rng = random.Random(4)
    rows = []
    for j in range(ny + 1):
        y = y0 + (y1 - y0) * j / ny; row = []
        for i, p in enumerate(path):
            t = i / (len(path) - 1)
            nxt = path[min(i + 1, len(path) - 1)] - path[max(i - 1, 0)]; nrm = Vector((-nxt.y, nxt.x)).normalized()
            if nrm.y < 0: nrm = -nrm
            fold = 0.012 * math.sin(y * 24 + t * 4) * t + 0.006 * math.sin(y * 57 + t * 9)
            lift = 0.012 + fold
            hang = max(0.0, t - 0.55) / 0.45
            row.append(bm.verts.new((p.x + nrm.x * lift + hang * 0.012 * math.sin(y * 31), y + hang * 0.02 * math.sin(t * 7), p.y + nrm.y * lift)))
        rows.append(row)
    for j in range(ny):
        for i in range(len(path) - 1): bm.faces.new((rows[j][i], rows[j][i + 1], rows[j + 1][i + 1], rows[j + 1][i]))
    th = from_bm("Sofa_Throw", bm, throw, rt)
    for p in th.data.polygons: p.use_smooth = True
    sm = th.modifiers.new("Solid", "SOLIDIFY"); sm.thickness = 0.005; sm.offset = 0
    # fringe along its hanging edge
    end = path[-1]
    for j in range(42):
        y = y0 + 0.01 + (y1 - y0 - 0.02) * j / 41
        x = end.x + 0.012 * math.sin(y * 31) + 0.004
        rod("Sofa_Fringe", (x, y, end.y + 0.005), (x + rng.uniform(-0.006, 0.006), y + rng.uniform(-0.004, 0.004), end.y - rng.uniform(0.035, 0.055)), 0.0018, throw, rt, seg=5)
    for o in rt.children:
        if o.type != 'MESH': continue
        if o.data.materials[0] in (fabric, throw): print_uv_cube(o, 0.75 if o.data.materials[0] == fabric else 0.5)
        else: print_uv_const(o)
    return rt

CFG_SOFA = {
    "tapestry": dict(base=((1.0, 1.0, 1.0), (0.9, 0.88, 0.86)), rough=0.95, metal=0.0, image=None,
                     wear=((0.5, 0.40, 0.33), 0.95, 0.0, 0.8), dirt=((0.07, 0.045, 0.03), 0.95, 0.0, 0.9), dust=0.45,
                     bottom=0.4, wrinkle=0.25, bump=0.4, edge_radius=0.02, ao_dist=0.15),
    "vinyl": dict(base=((0.095, 0.042, 0.026), (0.07, 0.03, 0.018)), rough=0.5, metal=0.0, chips=0.45, chip_scale=6,
                  wear=((0.32, 0.22, 0.15), 0.85, 0.0, 1.0), dirt=((0.04, 0.03, 0.02), 0.85, 0.0, 0.85), dust=0.4,
                  bottom=0.6, wrinkle=0.25, bump=0.35, edge_radius=0.015, ao_dist=0.15),
    "legwood": dict(base=((0.11, 0.055, 0.028), (0.07, 0.035, 0.018)), rough=0.5, metal=0.0, wood=True,
                    wear=((0.26, 0.17, 0.1), 0.7, 0.0, 0.9), dirt=((0.03, 0.02, 0.015), 0.85, 0.0, 0.8), dust=0.3, edge_radius=0.004),
    "throw": dict(base=((1.0, 1.0, 1.0), (0.92, 0.9, 0.88)), rough=0.97, metal=0.0, image=None,
                  wear=((0.55, 0.42, 0.32), 0.95, 0.0, 0.6), dirt=((0.08, 0.05, 0.035), 0.95, 0.0, 0.7), dust=0.35,
                  wrinkle=0.35, bump=0.45, edge_radius=0.01, ao_dist=0.1),
}

# ------------------------------------------------------------------ the shelf

def leaf_mesh(bm, at, fwd, up, size):
    """A small ivy leaf (a five-lobed flat shape, its tip bent down), added to bm."""
    side = fwd.cross(up).normalized(); up = side.cross(fwd).normalized()
    outline = [(0, 0), (0.25, 0.35), (0.55, 0.42), (0.7, 0.2), (1.0, 0.0), (0.7, -0.2), (0.55, -0.42), (0.25, -0.35)]
    c = bm.verts.new(at + fwd * size * 0.45)
    ring = []
    for u, v in outline:
        p = at + fwd * (u * size) + side * (v * size * 0.9) - up * (0.25 * size * u * u)
        ring.append(bm.verts.new(p))
    for a, b in zip(ring, ring[1:] + ring[:1]): bm.faces.new((c, a, b))

def build_shelf():
    rt = root("WallShelf", (4.5, 0, 0))
    board, bracket, pages = role("shelfwood"), role("bracket"), role("pages")
    books = [role("book_red"), role("book_cream"), role("book_brown"), role("book_green")]
    tin, pot, soil, ivy = role("tin"), role("pot"), role("soil"), role("ivy")
    BW, BD, BT = 0.82, 0.21, 0.022
    box("Shelf_Board", (BW, BD, BT), (0, -BD / 2, BT / 2), board, rt, bv=0.0025)
    for x in (-0.3, 0.3):
        box("Shelf_BracketV", (0.022, 0.012, 0.16), (x, -0.006, -0.08), bracket, rt, bv=0.0015)
        box("Shelf_BracketH", (0.022, 0.18, 0.012), (x, -0.09, -0.006), bracket, rt, bv=0.0015)
        beam("Shelf_Brace", (x, -0.012, -0.145), (x, -0.165, -0.012), 0.016, 0.009, bracket, rt, bv=0.0012)
        for z in (-0.04, -0.12): cyl("Shelf_Screw", 0.0045, 0.004, 0.003, (x, -0.0135, z), bracket, rt, seg=10, axis=(0, -1, 0))
    # books standing at the left, the last one leaning
    rng = random.Random(11); x = -BW / 2 + 0.03
    for i in range(7):
        t = rng.uniform(0.024, 0.042); hgt = rng.uniform(0.19, 0.245); dep = rng.uniform(0.14, 0.165)
        m = books[i % len(books)]
        lean = math.radians(18) if i == 6 else 0
        cx = x + t / 2 + (0.035 if i == 6 else 0)
        bk = root("Shelf_BookPivot"); bk.parent = rt
        bk.location = (cx, -0.012 - dep / 2, BT + hgt / 2 * math.cos(lean) + 0.0005)
        bk.rotation_euler = (0, lean, rng.uniform(-0.02, 0.02))
        for sx in (-1, 1): box("Shelf_BookBoard", (0.0028, dep, hgt), (sx * (t / 2 - 0.0014), 0, 0), m, bk, bv=0.0008)
        box("Shelf_BookSpine", (t, 0.004, hgt), (0, -dep / 2 + 0.002, 0), m, bk, bv=0.0012)
        box("Shelf_BookPages", (t - 0.0056, dep - 0.008, hgt - 0.008), (0, 0.001, 0), pages, bk, bv=0)
        x += t + 0.002
    # a red tin with a lid and a smaller box on it
    box("Shelf_Tin", (0.17, 0.115, 0.06), (0.02, -0.095, BT + 0.03), tin, rt, bv=0.004)
    box("Shelf_TinLid", (0.176, 0.121, 0.016), (0.02, -0.095, BT + 0.06), tin, rt, bv=0.004)
    b2 = box("Shelf_Box", (0.12, 0.08, 0.04), (0.03, -0.1, BT + 0.088), books[1], rt, bv=0.003); b2.rotation_euler = (0, 0, math.radians(8))
    # the pot and its ivy, trailing over the front edge
    px = 0.29
    pt = revolve("Shelf_Pot", [(0.0, 0.002), (0.042, 0.002), (0.045, 0.0), (0.056, 0.085), (0.061, 0.088), (0.062, 0.1),
                              (0.056, 0.1), (0.052, 0.09), (0.0, 0.09)], 28, pot, rt, loc=(px, -0.1, BT))
    cyl("Shelf_Soil", 0.052, 0.052, 0.004, (px, -0.1, BT + 0.086), soil, rt, seg=24)
    bm = bmesh.new(); rng = random.Random(3)
    vines = []
    for k in range(6):
        a = rng.uniform(-2.6, -0.4) if k < 4 else rng.uniform(0.4, 2.4)
        start = Vector((px + math.cos(a) * 0.03, -0.1 + math.sin(a) * 0.03, BT + 0.09))
        drop = rng.uniform(0.18, 0.42) if k < 4 else rng.uniform(0.06, 0.12)
        edge = Vector((px + rng.uniform(-0.09, 0.09), -BD - 0.012, BT + 0.06)) if k < 4 else start + Vector((rng.uniform(-0.12, 0.12), rng.uniform(-0.02, 0.05), 0.08))
        pts = [start, start + Vector((0, 0, 0.03)), (start + edge) * 0.5 + Vector((0, 0, 0.04)), edge]
        if k < 4:
            pts += [edge + Vector((rng.uniform(-0.03, 0.03), -0.015, -drop * 0.5)), edge + Vector((rng.uniform(-0.06, 0.06), -0.01, -drop))]
        path = smooth_path([tuple(p) for p in pts], 30)
        vines.append(path)
        for i in range(2, len(path) - 1, 2):
            fwd = (path[i + 1] - path[i - 1]).normalized()
            out = Vector((rng.uniform(-1, 1), rng.uniform(-1, -0.2), rng.uniform(-0.3, 0.6))).normalized()
            leaf_mesh(bm, path[i], (out - fwd * 0.3).normalized(), Vector((0, -0.3, 1)).normalized(), rng.uniform(0.022, 0.034))
    for i in range(22):   # a bush over the pot
        a = rng.uniform(0, math.tau); r = rng.uniform(0.0, 0.05)
        at = Vector((px + math.cos(a) * r, -0.1 + math.sin(a) * r, BT + 0.1 + rng.uniform(0, 0.06)))
        out = Vector((math.cos(a), math.sin(a), rng.uniform(0.2, 1.0))).normalized()
        leaf_mesh(bm, at, out, Vector((0, 0, 1)), rng.uniform(0.026, 0.036))
    lv = from_bm("Shelf_IvyLeaves", bm, ivy, rt)
    sm = lv.modifiers.new("Solid", "SOLIDIFY"); sm.thickness = 0.0008
    for k, path in enumerate(vines): tube(f"Shelf_Vine{k}", [tuple(p) for p in path], 0.0012, ivy, rt)
    for o in rt.children_recursive:
        if o.type == 'MESH': print_uv_const(o)
    return rt

def _book(col):
    return dict(base=(col, tuple(c * 0.8 for c in col)), rough=0.8, metal=0.0, chips=0.2, chip_scale=20,
                wear=((0.55, 0.5, 0.4), 0.9, 0.0, 1.0), dirt=((0.05, 0.035, 0.02), 0.9, 0.0, 0.7), dust=0.7, edge_radius=0.003, ao_dist=0.04)

CFG_SHELF = {
    "shelfwood": dict(base=((0.22, 0.13, 0.07), (0.14, 0.08, 0.04)), rough=0.6, metal=0.0, wood=True, scratch=0.5,
                      wear=((0.36, 0.25, 0.15), 0.7, 0.0, 0.9), dirt=((0.04, 0.03, 0.02), 0.85, 0.0, 0.8), dust=0.9, edge_radius=0.003, ao_dist=0.05),
    "bracket": dict(base=((0.05, 0.065, 0.05), (0.035, 0.045, 0.035)), rough=0.5, metal=0.3, chips=0.45, chip_scale=18,
                    wear=((0.22, 0.1, 0.04), 0.85, 0.2, 1.0), dirt=((0.17, 0.07, 0.03), 0.9, 0.1, 0.9), dust=0.5, edge_radius=0.002, ao_dist=0.03),
    "pages": dict(base=((0.66, 0.6, 0.46), (0.58, 0.52, 0.38)), rough=0.9, metal=0.0,
                  wear=((0.7, 0.65, 0.5), 0.9, 0.0, 0.3), dirt=((0.25, 0.18, 0.1), 0.9, 0.0, 0.7), dust=0.6, edge_radius=0.002, ao_dist=0.03),
    "book_red": _book((0.30, 0.06, 0.045)), "book_cream": _book((0.55, 0.48, 0.36)),
    "book_brown": _book((0.2, 0.115, 0.06)), "book_green": _book((0.1, 0.17, 0.11)),
    "tin": dict(base=((0.36, 0.045, 0.035), (0.27, 0.035, 0.025)), rough=0.35, metal=0.6, chips=0.3, chip_scale=12, scratch=0.15,
                wear=((0.5, 0.48, 0.45), 0.3, 1.0, 0.9), dirt=((0.18, 0.08, 0.03), 0.9, 0.1, 0.9), dust=0.6, edge_radius=0.003, ao_dist=0.04),
    "pot": dict(base=((0.42, 0.19, 0.1), (0.34, 0.15, 0.08)), rough=0.88, metal=0.0,
                wear=((0.6, 0.52, 0.42), 0.9, 0.0, 0.8), dirt=((0.06, 0.04, 0.03), 0.9, 0.0, 0.8), dust=0.4, edge_radius=0.003, ao_dist=0.04),
    "soil": dict(base=((0.05, 0.035, 0.025), (0.035, 0.025, 0.018)), rough=1.0, metal=0.0,
                 wear=((0.1, 0.08, 0.06), 1.0, 0.0, 0.1), dirt=((0.02, 0.015, 0.01), 1.0, 0.0, 0.3), dust=0.2),
    "ivy": dict(base=((0.09, 0.19, 0.05), (0.06, 0.13, 0.035)), rough=0.5, metal=0.0,
                wear=((0.36, 0.3, 0.09), 0.7, 0.0, 0.8), dirt=((0.07, 0.05, 0.02), 0.8, 0.0, 0.4), dust=0.45, edge_radius=0.002, ao_dist=0.03),
}

# ------------------------------------------------------------------ run

def build_all():
    clear_scene(); ROLES.clear()
    CFG_MUG["ceramic"]["image"] = tex_mug()
    CFG_CLIP["paper"]["image"] = tex_paper()
    CFG_SOFA["tapestry"]["image"] = tex_tapestry()
    CFG_SOFA["throw"]["image"] = tex_plaid()
    build_mug(); build_clipboard(); build_sofa(); build_shelf()
    for prop, cfg in (("Mug", CFG_MUG), ("Clipboard", CFG_CLIP), ("Sofa", CFG_SOFA), ("WallShelf", CFG_SHELF)):
        assign_materials(prop, cfg)
    apply_modifiers()
    for p in APT_PROPS:
        for o in meshes_of(p):
            if "Print" in o.data.uv_layers: o.data.uv_layers.active = o.data.uv_layers["Print"]
    return "built: " + ", ".join(f"{p}={len(meshes_of(p))}" for p in APT_PROPS)

def bake_all():
    bake_in_background([("Mug", 1024), ("Clipboard", 2048), ("Sofa", 2048), ("WallShelf", 2048)], BAKE_LOG)

def rebuild(prop, res):
    """Rebuilds one prop in the open Props_Apartment.blend (the others stay as finished), bakes and exports it."""
    for o in [bpy.data.objects[prop]] + list(bpy.data.objects[prop].children_recursive): bpy.data.objects.remove(o, do_unlink=True)
    for m in [m for m in bpy.data.materials if m.name.startswith(prop + "_")]: bpy.data.materials.remove(m)
    ROLES.clear()
    cfg, make, tex, key = {"Mug": (CFG_MUG, build_mug, tex_mug, "ceramic"), "Clipboard": (CFG_CLIP, build_clipboard, tex_paper, "paper"),
                           "Sofa": (CFG_SOFA, build_sofa, None, None), "WallShelf": (CFG_SHELF, build_shelf, None, None)}[prop]
    if tex: cfg[key]["image"] = tex()
    if prop == "Sofa": cfg["tapestry"]["image"] = tex_tapestry(); cfg["throw"]["image"] = tex_plaid()
    make(); assign_materials(prop, cfg); apply_modifiers(meshes_of(prop))
    for o in meshes_of(prop):
        if "Print" in o.data.uv_layers: o.data.uv_layers.active = o.data.uv_layers["Print"]
    r = bake_prop(prop, res)
    return r + " | " + finish_all([prop])

def finish_all(props=APT_PROPS):
    out = []
    os.makedirs(FBX_OUT, exist_ok=True)
    for p in props:
        made = finalize(p)
        rt = bpy.data.objects[p]; loc = rt.location.copy(); rt.location = (0, 0, 0)
        objs = [rt] + list(rt.children_recursive)
        with bpy.context.temp_override(**ctx(), selected_objects=objs, active_object=rt):
            bpy.ops.object.select_all(action='DESELECT')
            for o in objs: o.select_set(True)
            bpy.ops.export_scene.fbx(filepath=os.path.join(FBX_OUT, p + ".fbx"), use_selection=True,
                                     apply_scale_options='FBX_SCALE_UNITS', bake_space_transform=True,
                                     object_types={'EMPTY', 'MESH'}, use_mesh_modifiers=True, mesh_smooth_type='FACE',
                                     path_mode='STRIP', embed_textures=False, add_leaf_bones=False)
        rt.location = loc
        out.append(f"{p}: {made}")
    bpy.ops.wm.save_as_mainfile(filepath=os.path.join(PROJECT, "Blender", "Props_Apartment.blend"))
    return "; ".join(out)
