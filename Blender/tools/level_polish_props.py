"""Props that replace the block stand-ins in the Level scene's flat (the rooms before the padlocked door): crates, a
bucket and a waste bin, the bulb's ceiling rose and cord, pipe clamps and couplings, floor mattresses and a cushion,
the window's wooden frame, and the rubbish on the floors (paper, plaster, bottles, a can, a rag, a mixed pile).
Run inside Blender over the bridge, after propkit.py, apartment_props.py and khrushchev_props.py (it reuses them):

    for f in ("propkit", "apartment_props", "khrushchev_props", "level_polish_props"):
        exec(open(rf"E:\\Unity\\Projects\\BodycamHorror\\Blender\\tools\\{f}.py").read())
    build_lp(); bake_lp()     # bakes in the background (Blender/bake_log.txt)
    finish_lp()               # join, baked material, FBX export, save Level_Polish.blend

One FBX per set, one mesh per piece with its own origin (metres, Z up, front towards -Y = +Z in Unity):
  Crates      Crate_Slat (0.7 x 0.65 x 0.6 open slatted), Crate_Box (0.6 x 0.5 x 0.5 nailed shut, stencilled),
              Crate_Broken (0.55 x 0.55 x 0.45, slats missing)                         origin floor middle
  Fixtures    Bucket (galvanised, 0.3 high), TrashBin (enamel bin, 0.44 high)        origin floor middle
              LampCord (ceiling rose, twisted cord, socket; 0.3 long)               origin at the ceiling
              PipeClamp, PipeCollar (for a 0.05 m-radius pipe along Z; the clamp's stem points -Y to the wall,
              0.12 m from the pipe's axis)                                          origin on the pipe's axis
  SoftGoods   Mattress_Single (0.9 x 1.85), Mattress_Double (1.4 x 1.9), Cushion       origin floor middle
  WindowFrame a double-sashed wooden window for a 1.64 x 1.14 opening in a 0.3 m wall, with an inside sill
              (on the -Y side) and a tin sill outside; no glass (the scene keeps its own pane)  origin opening centre
  Debris      Paper_Sheet, Paper_Ball, Plaster_A, Plaster_B, Plaster_Slab, Bottle (lying), BrokenBottle, Can (lying),
              Rag, Junk_Pile                                                        origin on the floor
"""
import bpy, bmesh, math, os, random
import numpy as np
from mathutils import Vector, Matrix, noise

LP_SETS = {
    "Crates": ["Crate_Slat", "Crate_Box", "Crate_Broken"],
    "Fixtures": ["Bucket", "TrashBin", "LampCord", "PipeClamp", "PipeCollar"],
    "SoftGoods": ["Mattress_Single", "Mattress_Double", "Cushion"],
    "Debris": ["Paper_Sheet", "Paper_Ball", "Plaster_A", "Plaster_B", "Plaster_Slab", "Bottle", "BrokenBottle", "Can",
               "Rag", "Junk_Pile"],
}
LP_SINGLE = ["WindowFrame"]
LP_PROPS = list(LP_SETS) + LP_SINGLE
LP_GAP = {"Crates": 1.0, "Fixtures": 0.6, "SoftGoods": 1.8, "Debris": 0.9}
LP_RES = {"Crates": 2048, "Fixtures": 2048, "SoftGoods": 2048, "Debris": 2048, "WindowFrame": 2048}

def _spot(prop, piece): return LP_SETS[prop].index(piece) * LP_GAP[prop]

# ------------------------------------------------------------------ print textures

def tex_ticking():
    """Mattress ticking: grey-blue stripes on unbleached cotton, piping lines, and the stains of years on it."""
    H, W = 2048, 1024
    yy, xx = _grid(H, W)
    img = np.zeros((H, W, 3)); img[:] = (0.78, 0.74, 0.64)
    u = (xx % 64) / 64
    _bl(img, (u < 0.18) * 0.85, (0.3, 0.36, 0.44)); _bl(img, (np.abs(u - 0.32) < 0.025) * 0.7, (0.3, 0.36, 0.44))
    _bl(img, (np.abs(u - 0.62) < 0.06) * 0.35, (0.5, 0.52, 0.55))
    weave = ((xx % 3 == 0) | (yy % 3 == 0)) * 0.06
    img *= (1 - weave)[..., None]
    f = _fbm(H, W, 4, 3); f2 = _fbm(H, W, 9, 7)
    for cx, cy, r, col, a in ((0.4, 0.45, 0.22, (0.55, 0.4, 0.18), 0.6), (0.65, 0.7, 0.13, (0.45, 0.3, 0.14), 0.5),
                             (0.3, 0.2, 0.1, (0.35, 0.22, 0.12), 0.55), (0.7, 0.3, 0.16, (0.5, 0.45, 0.35), 0.4)):
        d = np.hypot((xx / W - cx) * 1.0, (yy / H - cy) * 2.0) / r + (f2 - 0.5) * 0.8
        m = np.clip(1 - d, 0, 1) ** 0.6
        _bl(img, m * a, col)
        _bl(img, (np.abs(d - 0.95) < 0.04) * a, np.array(col) * 0.6)   # the tide mark
    img *= (0.85 + 0.25 * f)[..., None]
    return _save("Level_Ticking", img)

# ------------------------------------------------------------------ crates

def _slat_crate(pre, x, W, D, H, wood, rt, rng, broken=0):
    t, post, n = 0.014, 0.042, 4
    sh = (H - 0.02) / n * 0.62
    missing = set(rng.sample(range(4 * n), broken)) if broken else set()
    for sx in (-1, 1):
        for sy in (-1, 1):
            box(f"{pre}_Post", (post, post, H - 0.004), (x + sx * (W / 2 - t - post / 2), sy * (D / 2 - t - post / 2), (H - 0.004) / 2), wood, rt, bv=0.004)
    idx = 0
    for side in range(4):
        along_x = side < 2; s = 1 if side % 2 == 0 else -1
        L = W if along_x else D - 2 * t
        for k in range(n):
            z = 0.012 + (k + 0.5) * (H - 0.02) / n
            if idx in missing:
                idx += 1; continue
            idx += 1
            jitter = rng.uniform(-0.004, 0.004)
            if along_x: o = box(f"{pre}_Slat", (L, t, sh), (x + jitter, s * (D / 2 - t / 2), z), wood, rt, bv=0.003)
            else: o = box(f"{pre}_Slat", (t, L, sh), (x + s * (W / 2 - t / 2), jitter, z), wood, rt, bv=0.003)
            o.rotation_euler = (rng.uniform(-0.01, 0.01), rng.uniform(-0.01, 0.01), 0)
    for k in range(5):
        box(f"{pre}_Bottom", (W - 2 * t, (D - 2 * t) / 5 * 0.8, t), (x, -D / 2 + t + (k + 0.5) * (D - 2 * t) / 5, t / 2 + 0.002), wood, rt, bv=0.002)

def build_crates(x0):
    rt = root("Crates", (x0, 0, 0)); rng = random.Random(4)
    wood, grey, stencil, nail = role("pine"), role("pinegrey"), role("stencil"), role("nail")
    _slat_crate("Crate_Slat", _spot("Crates", "Crate_Slat"), 0.7, 0.65, 0.6, wood, rt, rng)
    # a closed box of planks, battens on the faces, a lid nailed on, stencilled
    x = _spot("Crates", "Crate_Box"); W, D, H = 0.6, 0.5, 0.5; t = 0.016
    for side in range(4):
        along_x = side < 2; s = 1 if side % 2 == 0 else -1
        L = W if along_x else D - 2 * t
        for k in range(3):
            z = 0.02 + (k + 0.5) * (H - 0.04) / 3; h = (H - 0.04) / 3 - 0.004
            if along_x: box("Crate_Box_Plank", (L, t, h), (x, s * (D / 2 - t / 2), z), grey, rt, bv=0.002)
            else: box("Crate_Box_Plank", (t, L, h), (x + s * (W / 2 - t / 2), 0, z), grey, rt, bv=0.002)
        for e in (-1, 1):
            if along_x: box("Crate_Box_Batten", (0.05, 0.018, H - 0.02), (x + e * (W / 2 - 0.03), s * (D / 2 + 0.009), H / 2), grey, rt, bv=0.003)
            else: box("Crate_Box_Batten", (0.018, 0.05, H - 0.02), (x + s * (W / 2 + 0.009), e * (D / 2 - 0.03), H / 2), grey, rt, bv=0.003)
    for k in range(3):
        box("Crate_Box_Lid", (W + 0.01, D / 3 - 0.004, t), (x, -D / 3 + k * D / 3, H - t / 2 + 0.004), grey, rt, bv=0.002)
        box("Crate_Box_Floor", (W - 0.02, D / 3 - 0.004, t), (x, -D / 3 + k * D / 3, 0.012), grey, rt, bv=0.002)
    for e in (-1, 1):
        box("Crate_Box_LidBatten", (0.05, D, 0.018), (x + e * (W / 2 - 0.06), 0, H + 0.013), grey, rt, bv=0.003)
    for k, (txt, z, sz) in enumerate((("НЕ КАНТОВАТЬ", 0.33, 0.045), ("№ 117", 0.2, 0.07))):
        tm = text_mesh(f"Crate_Box_Stencil{k}", txt, "arialbd.ttf", sz, (x, -D / 2 - 0.0185, z), stencil, rt, rot=(math.pi / 2, 0, 0))
    for e in (-1, 1):
        for zz in (0.05, H - 0.05):
            cyl("Crate_Box_Nail", 0.004, 0.004, 0.004, (x + e * (W / 2 - 0.03), -D / 2 - 0.019, zz), nail, rt, seg=8, axis=(0, 1, 0))
    _slat_crate("Crate_Broken", _spot("Crates", "Crate_Broken"), 0.55, 0.55, 0.45, wood, rt, rng, broken=4)
    x = _spot("Crates", "Crate_Broken")
    o = box("Crate_Broken_Snapped", (0.3, 0.014, 0.065), (x - 0.12, -0.29, 0.25), wood, rt, bv=0.003); o.rotation_euler = (0.1, 0.25, 0.35)
    o = box("Crate_Broken_Loose", (0.55, 0.014, 0.065), (x + 0.1, -0.42, 0.012), wood, rt, bv=0.003); o.rotation_euler = (math.pi / 2, 0, 0.3)
    return rt

# ------------------------------------------------------------------ fixtures

def _ring(name, r, z, tube_r, mat, parent, x=0.0, seg=40):
    pts = [(x + r * math.cos(a), r * math.sin(a), z) for a in (i / seg * math.tau for i in range(seg))]
    return tube(name, pts, tube_r, mat, parent, cyclic=True)

def build_fixtures(x0):
    rt = root("Fixtures", (x0, 0, 0)); rng = random.Random(8)
    zinc, sludge, binp, bake, wire, pipe, paper, brass = (role("zinc"), role("sludge"), role("binpaint"), role("bakelite"),
                                                          role("wire"), role("radpipe"), role("paper"), role("brass"))
    # Bucket
    x = _spot("Fixtures", "Bucket")
    prof = [(0.0, 0.004), (0.118, 0.004), (0.121, 0.0), (0.124, 0.007), (0.149, 0.29), (0.153, 0.296), (0.151, 0.303),
            (0.145, 0.297), (0.143, 0.29), (0.117, 0.011), (0.0, 0.011)]
    revolve("Bucket_Body", prof, 40, zinc, rt, loc=(x, 0, 0))
    for z in (0.1, 0.2):
        _ring("Bucket_Rib", 0.124 + (0.149 - 0.124) * z / 0.29 + 0.002, z, 0.0025, zinc, rt, x=x)
    cyl("Bucket_Sludge", 0.12, 0.125, 0.03, (x, 0, 0.026), sludge, rt, seg=32)
    for s in (-1, 1): box("Bucket_Ear", (0.012, 0.03, 0.035), (x + s * 0.153, 0, 0.272), zinc, rt, bv=0.002)
    arc = [(x + 0.157 * math.cos(a), 0.157 * math.sin(a) * math.cos(1.1) , 0.272 + 0.157 * math.sin(a) * math.sin(1.1) * 0.15) for a in np.linspace(0, math.pi, 20)]
    tube("Bucket_Handle", [(px, py, pz + 0.0) for px, py, pz in arc], 0.0035, zinc, rt)
    # TrashBin: an enamelled bin with a dent and papers sticking out
    x = _spot("Fixtures", "TrashBin")
    prof = [(0.0, 0.006), (0.15, 0.006), (0.155, 0.0), (0.16, 0.01), (0.176, 0.425), (0.182, 0.432), (0.18, 0.44),
            (0.172, 0.433), (0.17, 0.425), (0.152, 0.014), (0.0, 0.014)]
    b = revolve("TrashBin_Body", prof, 44, binp, rt, loc=(x, 0, 0))
    for v in b.data.vertices:   # a dent on the front
        d = (Vector((v.co.x, v.co.y, v.co.z)) - Vector((0, -0.18, 0.26))).length
        if d < 0.09: v.co.y += (0.09 - d) * 0.35
    for k in range(3):
        a = k * 2.1 + 0.4
        bm = bmesh.new(); bmesh.ops.create_grid(bm, x_segments=8, y_segments=10, size=0.5)
        for v in bm.verts:
            v.co.x *= 0.21; v.co.y *= 0.29
            v.co.z = 0.012 * noise.noise(Vector((v.co.x * 30, v.co.y * 30, k))) + 0.04 * (v.co.x * 4) ** 2
        o = from_bm("TrashBin_Paper", bm, paper, rt)
        o.location = (x + 0.06 * math.cos(a), 0.06 * math.sin(a), 0.4)
        o.rotation_euler = (math.radians(70) * math.cos(a), math.radians(70) * math.sin(a), a)
        for p in o.data.polygons: p.use_smooth = True
    # LampCord: rose on the ceiling, a twisted pair, a bakelite socket
    x = _spot("Fixtures", "LampCord")
    revolve("LampCord_Rose", [(0.0, -0.024), (0.03, -0.024), (0.045, -0.018), (0.055, -0.006), (0.056, 0.0), (0.0, 0.0)], 32, bake, rt, loc=(x, 0, 0))
    L = 0.25
    for ph in (0, math.pi):
        pts = [(x + 0.0024 * math.cos(z / 0.012 * math.tau + ph), 0.0024 * math.sin(z / 0.012 * math.tau + ph), -0.022 - z) for z in np.linspace(0, L - 0.02, 90)]
        tube("LampCord_Wire", pts, 0.0021, wire, rt)
    revolve("LampCord_Socket", [(0.0, -L - 0.058), (0.019, -L - 0.058), (0.021, -L - 0.05), (0.021, -L - 0.02), (0.016, -L - 0.006),
                                (0.008, -L + 0.002), (0.0, -L + 0.002)], 24, bake, rt, loc=(x, 0, 0))
    # PipeClamp: a strap round the pipe, a threaded stem to the wall plug, a dowel plate
    x = _spot("Fixtures", "PipeClamp")
    for dz in (-0.012, 0.012):
        _ring("PipeClamp_Strap", 0.053, dz, 0.004, pipe, rt, x=x)
    for s in (-1, 1): box("PipeClamp_Ear", (0.012, 0.03, 0.03), (x + s * 0.012, -0.065, 0), pipe, rt, bv=0.002)
    cyl("PipeClamp_Bolt", 0.004, 0.004, 0.045, (x, -0.065, 0), pipe, rt, seg=8, axis=(1, 0, 0))
    rod("PipeClamp_Stem", (x, -0.08, 0), (x, -0.118, 0), 0.006, pipe, rt)
    cyl("PipeClamp_Plate", 0.022, 0.022, 0.004, (x, -0.118, 0), pipe, rt, seg=16, axis=(0, 1, 0))
    # PipeCollar: a threaded coupling with a hex lock nut
    x = _spot("Fixtures", "PipeCollar")
    revolve("PipeCollar_Sleeve", [(0.05, -0.04), (0.058, -0.038), (0.06, -0.03), (0.06, 0.03), (0.058, 0.038), (0.05, 0.04)], 32, pipe, rt, loc=(x, 0, 0))
    cyl("PipeCollar_Nut", 0.066, 0.066, 0.018, (x, 0, -0.045), pipe, rt, seg=6)
    return rt

# ------------------------------------------------------------------ soft goods

def _mattress(name, x, W, L, T, mat, rt, seed):
    mt = pillow(name + "_Pad", (W, L, T), (x, 0, T / 2), mat, rt, puff=0.022, cuts=14, sag=0.035)
    rnd = random.Random(seed)
    for v in mt.data.vertices:   # lumps where the wadding gathered, a hollow where someone lay
        p = Vector((v.co.x * 3, v.co.y * 3, seed))
        if v.co.z > -T / 4: v.co.z += 0.012 * noise.noise(p) - 0.02 * math.exp(-((v.co.x / (W * 0.3)) ** 2 + ((v.co.y + 0.1) / (L * 0.3)) ** 2))
        if v.co.z < -T / 2 + 0.01: v.co.z = -T / 2 + 0.002
    print_uv_planar(mt, 0, 1, ((-W / 2 - 0.05, W / 2 + 0.05), (-L / 2 - 0.05, L / 2 + 0.05)))
    for k in range(6):   # the tufting buttons
        bx = (k % 2 - 0.5) * W * 0.5; by = (k // 2 - 1) * L * 0.3
        cyl(name + "_Tuft", 0.012, 0.012, 0.008, (x + bx, by, T - 0.012), mat, rt, seg=10)

def build_softgoods(x0):
    rt = root("SoftGoods", (x0, 0, 0))
    tick, tap = role("mattress"), role("tapestry")
    _mattress("Mattress_Single", _spot("SoftGoods", "Mattress_Single"), 0.9, 1.85, 0.14, tick, rt, 3)
    _mattress("Mattress_Double", _spot("SoftGoods", "Mattress_Double"), 1.4, 1.9, 0.16, tick, rt, 9)
    c = pillow("Cushion_Pad", (0.5, 0.5, 0.11), (_spot("SoftGoods", "Cushion"), 0, 0.055), tap, rt, puff=0.04, sag=0.03, rot=(0.05, -0.04, 0.3))
    print_uv_cube(c, 0.5)
    return rt

# ------------------------------------------------------------------ window

def build_window(x0):
    rt = root("WindowFrame", (x0, 0, 0))
    paint, putty, tape, chrome, zinc = role("winpaint"), role("putty"), role("tape"), role("chrome"), role("zinc")
    W, H, D = 1.64, 1.14, 0.14; f = 0.06
    box("WF_Top", (W + 2 * f, D, f), (0, 0, H / 2 + f / 2), paint, rt, bv=0.004)
    box("WF_Bottom", (W + 2 * f, D, f), (0, 0, -H / 2 - f / 2), paint, rt, bv=0.004)
    for s in (-1, 1): box("WF_Side", (f, D, H), (s * (W / 2 + f / 2), 0, 0), paint, rt, bv=0.004)
    box("WF_Mullion", (0.07, D, H), (0, 0, 0), paint, rt, bv=0.004)
    zt = 0.25
    box("WF_Transom", (W, D, 0.06), (0, 0, zt), paint, rt, bv=0.004)
    sm = 0.045
    def sash(pre, x0_, x1_, z0_, z1_, y):
        w = x1_ - x0_; h = z1_ - z0_; cx = (x0_ + x1_) / 2; cz = (z0_ + z1_) / 2
        box(pre + "_T", (w, 0.04, sm), (cx, y, z1_ - sm / 2), paint, rt, bv=0.004)
        box(pre + "_B", (w, 0.04, sm * 1.3), (cx, y, z0_ + sm * 0.65), paint, rt, bv=0.004)
        for s in (-1, 1): box(pre + "_S", (sm, 0.04, h), (cx + s * (w / 2 - sm / 2), y, cz), paint, rt, bv=0.004)
        for s in (-1, 1):   # putty beads along the glass
            box(pre + "_PuttyH", (w - 2 * sm, 0.012, 0.012), (cx, y + (0.02 if y > 0 else -0.02), (z1_ - sm if s > 0 else z0_ + sm * 1.3) - s * 0.006), putty, rt, bv=0.003)
            box(pre + "_PuttyV", (0.012, 0.012, h - sm * 2.3), (cx + s * (w / 2 - sm - 0.006), y + (0.02 if y > 0 else -0.02), cz + 0.01), putty, rt, bv=0.003)
    xm = 0.035; zb = -H / 2; ztop = H / 2
    for y in (-0.045, 0.045):
        sash(f"WF_SashL{y > 0}", -W / 2, -xm, zb, zt - 0.03, y)
        sash(f"WF_SashR{y > 0}", xm, W / 2, zb, zt - 0.03, y)
        sash(f"WF_Fort{y > 0}", -W / 2, -xm, zt + 0.03, ztop, y)
        sash(f"WF_Fixed{y > 0}", xm, W / 2, zt + 0.03, ztop, y)
    for x_, z_ in ((-xm - 0.03, -0.05), (xm + 0.03, -0.05), (-xm - 0.03, zt + 0.15)):
        box("WF_HandleBase", (0.018, 0.008, 0.05), (x_, -0.07, z_), chrome, rt, bv=0.002)
        o = box("WF_Handle", (0.012, 0.012, 0.075), (x_, -0.08, z_ - 0.03), chrome, rt, bv=0.003); o.rotation_euler = (0.2, 0, 0)
    rng = random.Random(2)
    for (x_, z_, w_, h_) in ((0, -0.1, 0.05, H * 0.9), (-W / 4, zt, W / 2 - 0.05, 0.05), (W / 4, zt, W / 2 - 0.05, 0.05)):   # winter tape
        o = box("WF_Tape", (w_, 0.002, h_), (x_, -0.071, z_), tape, rt, bv=0); o.rotation_euler = (0, rng.uniform(-0.02, 0.02), 0)
    sill = slab("WF_Sill", rounded_rect(W + 0.2, 0.24, 0.015), -H / 2 - f - 0.03, -H / 2 - f, paint, rt)
    sill.location = (0, -D / 2 - 0.1, 0)
    o = box("WF_SillApron", (W + 0.16, 0.02, 0.06), (0, -D / 2 - 0.02, -H / 2 - f - 0.06), paint, rt, bv=0.003)
    tin = box("WF_TinSill", (W + 0.14, 0.17, 0.006), (0, D / 2 + 0.07, -H / 2 - f + 0.005), zinc, rt, bv=0.002); tin.rotation_euler = (-0.12, 0, 0)
    box("WF_TinLip", (W + 0.14, 0.006, 0.03), (0, D / 2 + 0.155, -H / 2 - f - 0.02), zinc, rt, bv=0.001)
    return rt

# ------------------------------------------------------------------ debris

def _crumple(o, amp, freq, seed):
    for v in o.data.vertices:
        n = noise.noise_vector(Vector((v.co.x * freq + seed, v.co.y * freq, v.co.z * freq)))
        v.co += n * amp
    for p in o.data.polygons: p.use_smooth = False

def _paper_sheet(name, loc, rot, mat, rt, seed, w=0.21, l=0.297):
    rnd = random.Random(seed); ph = [rnd.uniform(0, 6) for _ in range(4)]
    def fn(u, v):
        x = (u - 0.5) * w; y = (v - 0.5) * l
        z = 0.002 + 0.008 * max(0, math.sin(u * 3 + ph[0])) * v + 0.012 * max(0.0, v - 0.8) / 0.2 * (0.5 + 0.5 * math.sin(ph[1]))
        z += 0.003 * math.sin(u * 17 + v * 11 + ph[2])
        return (x, y, z)
    o = sheet(name, fn, 10, 14, mat, rt, thick=0.0008, uvscale=(1, 1))
    o.location = loc; o.rotation_euler = rot
    return o

def _paper_ball(name, loc, r, mat, rt, seed):
    bm = bmesh.new(); bmesh.ops.create_icosphere(bm, subdivisions=2, radius=r)
    o = from_bm(name, bm, mat, rt); o.location = loc
    _crumple(o, r * 0.35, 18 / r * 0.05, seed)
    return o

def _chunk(name, loc, size, mat, rt, seed, rot=0.0):
    bm = bmesh.new(); bmesh.ops.create_cube(bm, size=1.0)
    bmesh.ops.subdivide_edges(bm, edges=bm.edges[:], cuts=2, use_grid_fill=True)
    for v in bm.verts:
        v.co.x *= size[0]; v.co.y *= size[1]; v.co.z *= size[2]
    o = from_bm(name, bm, mat, rt)
    _crumple(o, min(size) * 0.35, 8, seed)
    for v in o.data.vertices:
        v.co.z = max(v.co.z, -size[2] / 2)
    o.location = (loc[0], loc[1], loc[2] + size[2] / 2); o.rotation_euler = (0, 0, rot)
    return o

def _jagged(n, rx, ry, rnd, rough=0.3):
    return [((rx * (1 + rnd.uniform(-rough, rough * 0.5))) * math.cos(a), (ry * (1 + rnd.uniform(-rough, rough * 0.5))) * math.sin(a))
            for a in (i / n * math.tau for i in range(n))]

def _plaster_flake(name, loc, rx, ry, t, mat, paintm, rt, seed, rot=0.0):
    rnd = random.Random(seed); out = _jagged(14, rx, ry, rnd)
    o = slab(name, out, 0.0, t, mat, rt); o.location = loc; o.rotation_euler = (rnd.uniform(-0.05, 0.05), rnd.uniform(-0.05, 0.05), rot)
    p = slab(name + "_Paint", [(x * 0.97, y * 0.97) for x, y in out], t, t + 0.0015, paintm, rt); p.location = loc; p.rotation_euler = o.rotation_euler
    return o

def _bottle(name, loc, rot, mat, labelm, rt, broken=False, seed=0):
    prof = [(0.0, 0.002), (0.028, 0.0), (0.033, 0.006), (0.034, 0.17), (0.03, 0.2), (0.016, 0.235), (0.0135, 0.265),
            (0.015, 0.268), (0.015, 0.276), (0.011, 0.278), (0.0105, 0.27), (0.012, 0.24), (0.028, 0.19), (0.0305, 0.17),
            (0.0305, 0.008), (0.0, 0.01)]
    if broken: prof = [p for p in prof if p[1] < 0.12 or p[0] == 0.0][:6] + [(0.034, 0.12), (0.0305, 0.12), (0.0305, 0.008), (0.0, 0.01)]
    o = revolve(name + "_Glass", prof, 28, mat, rt)
    if broken:
        rnd = random.Random(seed)
        for v in o.data.vertices:
            if v.co.z > 0.11: v.co.z += rnd.uniform(-0.06, 0.0) * (0.5 + 0.5 * math.sin(math.atan2(v.co.y, v.co.x) * 3))
    lab = cyl(name + "_Label", 0.0345, 0.0345, 0.07 if not broken else 0.05, (0, 0, 0.08 if not broken else 0.06), labelm, rt, seg=28, caps=False)
    for ob in (o, lab):
        ob.data.transform(Matrix.Rotation(rot[0], 4, 'X') @ Matrix.Rotation(rot[2], 4, 'Z'))
        ob.location = loc
    return o

def build_debris(x0):
    rt = root("Debris", (x0, 0, 0))
    paper, plaster, whitewash, glass, label, tin, rag, card = (role("paper"), role("plaster"), role("whitewash"), role("bottleglass"),
                                                               role("label"), role("tin"), role("rag"), role("cardboard"))
    S = lambda n: _spot("Debris", n)
    _paper_sheet("Paper_Sheet_A", (S("Paper_Sheet"), 0, 0), (0, 0, 0), paper, rt, 1)
    _paper_ball("Paper_Ball_A", (S("Paper_Ball"), 0, 0.03), 0.035, paper, rt, 2)
    _chunk("Plaster_A_Lump", (S("Plaster_A"), 0, 0), (0.14, 0.1, 0.05), plaster, rt, 3)
    _plaster_flake("Plaster_B_Flake", (S("Plaster_B"), 0, 0), 0.12, 0.08, 0.022, plaster, whitewash, rt, 4)
    x = S("Plaster_Slab")
    _plaster_flake("Plaster_Slab_Main", (x, 0, 0), 0.34, 0.24, 0.045, plaster, whitewash, rt, 5, rot=0.2)
    rnd = random.Random(6)
    for k in range(7):
        a = rnd.uniform(0, 6.28); r = rnd.uniform(0.3, 0.45)
        _chunk("Plaster_Slab_Bit", (x + math.cos(a) * r, math.sin(a) * r * 0.8, 0), (rnd.uniform(0.03, 0.08), rnd.uniform(0.03, 0.06), rnd.uniform(0.015, 0.03)), plaster, rt, 10 + k, rnd.uniform(0, 3))
    _bottle("Bottle_A", (S("Bottle"), -0.14, 0.034), (-math.pi / 2, 0, 0), glass, label, rt)
    x = S("BrokenBottle")
    _bottle("BrokenBottle_A", (x, 0, 0), (0, 0, 0), glass, label, rt, broken=True, seed=7)
    for k in range(4):
        bm = bmesh.new(); a = rnd.uniform(0, 6.28); rr = rnd.uniform(0.07, 0.15)
        v1 = bm.verts.new((0, 0, 0)); v2 = bm.verts.new((rnd.uniform(0.02, 0.05), rnd.uniform(-0.01, 0.01), 0)); v3 = bm.verts.new((rnd.uniform(0, 0.03), rnd.uniform(0.015, 0.04), 0))
        bm.faces.new((v1, v2, v3)); o = from_bm("BrokenBottle_Shard", bm, glass, rt)
        m = o.modifiers.new("S", "SOLIDIFY"); m.thickness = 0.003
        o.location = (x + math.cos(a) * rr, math.sin(a) * rr, 0.001); o.rotation_euler = (0, 0, rnd.uniform(0, 6.28))
    x = S("Can")
    c = revolve("Can_Body", [(0.0, 0.0), (0.034, 0.0), (0.037, 0.003), (0.037, 0.107), (0.034, 0.11), (0.03, 0.108), (0.0, 0.108)], 28, tin, rt)
    for v in c.data.vertices:
        d = (v.co - Vector((0.03, 0.0, 0.05))).length
        if d < 0.04: v.co.x -= (0.04 - d) * 0.5
    c.data.transform(Matrix.Rotation(-math.pi / 2, 4, 'X')); c.location = (x, -0.055, 0.037)
    def ragfn(u, v):
        xx = (u - 0.5) * 0.42; yy = (v - 0.5) * 0.36
        z = 0.004 + 0.025 * max(0, noise.noise(Vector((u * 3, v * 3, 1.5)))) + 0.012 * abs(math.sin(u * 9 + v * 4))
        return (xx + 0.03 * noise.noise(Vector((u * 2, v * 2, 7))), yy + 0.03 * noise.noise(Vector((u * 2, v * 2, 9))), z)
    o = sheet("Rag_Cloth", ragfn, 26, 22, rag, rt, thick=0.003); o.location = (S("Rag"), 0, 0)
    # a mixed pile: cardboard, paper, plaster, a ball, a can
    x = S("Junk_Pile"); rnd = random.Random(12)
    for k in range(4):
        o = box("Junk_Pile_Card", (rnd.uniform(0.15, 0.3), rnd.uniform(0.12, 0.25), 0.004), (x + rnd.uniform(-0.2, 0.2), rnd.uniform(-0.2, 0.2), 0.006 + k * 0.012), card, rt, bv=0)
        o.rotation_euler = (rnd.uniform(-0.12, 0.12), rnd.uniform(-0.12, 0.12), rnd.uniform(0, 3))
        bev = o.modifiers.new("Bend", "SIMPLE_DEFORM"); bev.deform_method = 'BEND'; bev.angle = rnd.uniform(-0.5, 0.5); bev.deform_axis = 'Y'
    for k in range(3):
        _paper_sheet("Junk_Pile_Sheet", (x + rnd.uniform(-0.2, 0.2), rnd.uniform(-0.2, 0.2), 0.05 + k * 0.004), (0, 0, rnd.uniform(0, 6)), paper, rt, 20 + k)
    for k in range(2): _paper_ball("Junk_Pile_Ball", (x + rnd.uniform(-0.2, 0.2), rnd.uniform(-0.2, 0.2), 0.05), rnd.uniform(0.025, 0.04), paper, rt, 30 + k)
    for k in range(5):
        _chunk("Junk_Pile_Plaster", (x + rnd.uniform(-0.25, 0.25), rnd.uniform(-0.25, 0.25), 0.0), (rnd.uniform(0.03, 0.08), rnd.uniform(0.03, 0.06), rnd.uniform(0.015, 0.03)), plaster, rt, 40 + k, rnd.uniform(0, 3))
    return rt

# ------------------------------------------------------------------ materials

def _lp_cfg():
    c = {k: dict(v) for k, v in CFG_COMMON.items()}
    c.update({
        "pine": _wood((0.4, 0.29, 0.16), (0.28, 0.19, 0.1), 0.8, chips=0.05, scratch=0.3, wet=0.25),
        "pinegrey": _wood((0.33, 0.29, 0.23), (0.22, 0.19, 0.15), 0.85, chips=0.05, scratch=0.4, wet=0.3),
        "stencil": _plain((0.035, 0.035, 0.035), 0.9, wear_col=(0.3, 0.26, 0.2), amount=1.0, chips=0.7, chip_scale=40, dust=0.6),
        "zinc": dict(base=((0.52, 0.53, 0.52), (0.42, 0.43, 0.42)), rough=0.42, metal=1.0, chips=0.55, chip_scale=9,
                     wear=((0.3, 0.17, 0.08), 0.9, 0.3, 1.0), dirt=((0.11, 0.09, 0.06), 0.9, 0.2, 1.0), dust=0.7, bottom=0.6,
                     edge_radius=0.003, ao_dist=0.05),
        "sludge": _plain((0.05, 0.04, 0.03), 0.12, amount=0.1, dust=0.0),
        "binpaint": _plain((0.12, 0.19, 0.14), 0.45, wear_col=(0.22, 0.1, 0.04), amount=1.0, chips=0.7, chip_scale=10, dust=0.7, bottom=0.6),
        "bakelite": _plain((0.08, 0.045, 0.025), 0.3, amount=0.4, dust=1.0),
        "wire": _plain((0.18, 0.14, 0.09), 0.6, dust=0.9),
        "mattress": _print(0.95, 0.5, 0.35, bottom=0.6, wet=0.3, bump=0.45),
        "tapestry": _print(0.95, 0.6, 0.25),
        "winpaint": dict(base=((0.76, 0.74, 0.68), (0.68, 0.66, 0.6)), rough=0.5, metal=0.0, chips=0.85, chip_scale=7,
                         wear=((0.32, 0.29, 0.24), 0.9, 0.0, 1.0), dirt=((0.18, 0.15, 0.1), 0.9, 0.0, 1.0), dust=1.0,
                         edge_radius=0.004, ao_dist=0.06, bump=0.4),
        "putty": _plain((0.5, 0.46, 0.38), 0.9, amount=0.8, chips=0.5, chip_scale=20),
        "tape": _plain((0.66, 0.58, 0.4), 0.9, wrinkle=0.4, amount=0.6),
        "plaster": _plain((0.62, 0.6, 0.56), 0.95, wear_col=(0.45, 0.43, 0.4), amount=0.6, chips=0.4, chip_scale=6, dust=0.9, bump=0.6),
        "whitewash": _plain((0.8, 0.78, 0.72), 0.95, wear_col=(0.55, 0.52, 0.48), amount=1.0, chips=0.6, chip_scale=9, dust=0.8),
        "bottleglass": dict(base=((0.035, 0.11, 0.045), (0.025, 0.08, 0.035)), rough=0.08, metal=0.0, wear=((0.2, 0.25, 0.2), 0.4, 0.0, 0.3),
                            dirt=((0.12, 0.1, 0.07), 0.8, 0.0, 0.8), dust=1.0, ao_dist=0.03),
        "label": _plain((0.62, 0.55, 0.36), 0.9, wear_col=(0.5, 0.45, 0.35), amount=0.9, chips=0.7, chip_scale=12, wrinkle=0.2),
        "tin": dict(base=((0.45, 0.43, 0.4), (0.35, 0.33, 0.3)), rough=0.4, metal=1.0, chips=0.9, chip_scale=7,
                    wear=((0.3, 0.13, 0.05), 0.9, 0.2, 1.0), dirt=((0.2, 0.09, 0.03), 0.9, 0.1, 1.0), dust=0.5, edge_radius=0.002, ao_dist=0.03),
        "rag": _plain((0.28, 0.26, 0.22), 1.0, wear_col=(0.2, 0.15, 0.1), wrinkle=0.6, bump=0.5, wet=0.45, dust=0.3),
        "cardboard": _plain((0.45, 0.34, 0.2), 0.95, wear_col=(0.3, 0.22, 0.12), wrinkle=0.3, wet=0.35, dust=0.6),
    })
    return c

LP_BUILDERS = {"Crates": build_crates, "Fixtures": build_fixtures, "SoftGoods": build_softgoods, "Debris": build_debris,
               "WindowFrame": build_window}

# ------------------------------------------------------------------ run

def build_lp(props=None):
    clear_scene(); ROLES.clear()
    props = props or LP_PROPS
    cfg = _lp_cfg()
    cfg["mattress"]["image"] = tex_ticking()
    cfg["paper"]["image"] = os.path.join(SRC_TEX, "Book_PageText.png")
    cfg["tapestry"]["image"] = os.path.join(SRC_TEX, "Sofa_Tapestry.png")
    x = 0.0
    for p in props:
        LP_BUILDERS[p](x); x += 14.0
    for p in props:
        for o in meshes_of(p):
            if "Print" not in o.data.uv_layers: print_uv_const(o)
        assign_materials(p, cfg)
    apply_modifiers()
    for p in props:
        for o in meshes_of(p):
            if "Print" in o.data.uv_layers: o.data.uv_layers.active = o.data.uv_layers["Print"]
    return "built: " + ", ".join(f"{p}={len(meshes_of(p))}" for p in props)

def bake_lp(props=None):
    bake_in_background([(p, LP_RES[p]) for p in (props or LP_PROPS)], BAKE_LOG)

def finish_lp(props=None):
    out = []; os.makedirs(FBX_OUT, exist_ok=True)
    for p in (props or LP_PROPS):
        groups = origins = None
        if p in LP_SETS:
            names = LP_SETS[p]
            groups = {n: [o.name for o in meshes_of(p) if o.name.startswith(n + "_")] for n in names}
            origins = {n: (_spot(p, n), 0, 0) for n in names}
        made = finalize(p, groups, origins); _export_kh(p); out.append(f"{p}: {made}")
    bpy.ops.wm.save_as_mainfile(filepath=os.path.join(PROJECT, "Blender", "Level_Polish.blend"))
    return "; ".join(out)
