"""Decor props for BodycamHorror: books (seven different ones, one lying open, a stack, a row to fill a shelf), a tall
lacquered bookcase filled with them, a grey metal storage rack, and a photo poster taped to the wall.
Run inside Blender over the bridge, after propkit.py and apartment_props.py (it reuses their helpers), once the 2D
textures exist (python Blender/tools/decor_textures.py):

    exec(open(r"D:\\BodycamHorror\\Blender\\tools\\propkit.py").read())
    exec(open(r"D:\\BodycamHorror\\Blender\\tools\\apartment_props.py").read())
    exec(open(r"D:\\BodycamHorror\\Blender\\tools\\decor_props.py").read())
    build_decor(); bake_decor()      # bakes in the background (Blender/bake_log.txt)
    finish_decor()                   # join, baked material, FBX export, save Props_Decor.blend

Books.fbx holds one mesh per book (see BOOK_SPOTS), each with its origin at its own foot, like CardboardBoxes.
The poster is not baked: its print is drawn straight onto its UV by decor_textures.py.
Metres, Z up, fronts towards -Y (+Z in Unity). Wall props (the poster) have their origin on the wall plane.
"""
import bpy, bmesh, math, os, random
from mathutils import Vector

DECOR_PROPS = ["Books", "Bookcase", "MetalRack"]
BOOK_KEYS = ["Encyclopedia", "Chemistry", "Poems", "Collected", "Anatomy", "Diary", "Atlas"]
BOOK_DIMS = {   # thickness, depth (cover width), height: as decor_textures.BOOKS
    "Encyclopedia": (0.05, 0.175, 0.26), "Chemistry": (0.022, 0.135, 0.205), "Poems": (0.026, 0.125, 0.195),
    "Collected": (0.035, 0.14, 0.215), "Anatomy": (0.04, 0.17, 0.245), "Diary": (0.016, 0.105, 0.15),
    "Atlas": (0.014, 0.21, 0.29)}
SOFT = {"Chemistry", "Diary"}   # paperbacks: thin covers
BOOK_SPOTS = {f"Book_{k}": i * 0.4 for i, k in enumerate(BOOK_KEYS)}
BOOK_SPOTS.update({"Book_Open": 3.0, "Book_Stack": 3.6, "Book_Row": 4.3})

# ------------------------------------------------------------------ books

def jacket_uv(o, t, d, h):
    """'Print' UV for a book's cover parts, onto its jacket image laid out back cover | spine | front cover.
    The book stands with its spine towards -Y, the front cover on +X; o's coordinates are in the book's pivot space."""
    me = o.data; uv = me.uv_layers.get("Print") or me.uv_layers.new(name="Print"); M = o.matrix_basis
    W = 2 * d + t
    for p in me.polygons:
        n = p.normal; c = M @ p.center
        if n.x > 0.7: side = 'F'
        elif n.x < -0.7: side = 'B'
        elif n.y < -0.7 or c.y < -d / 2 + 0.005: side = 'S'
        else: side = 'F' if c.x > 0 else 'B'
        for li in p.loop_indices:
            co = M @ me.vertices[me.loops[li].vertex_index].co
            if side == 'F': u = min(max(d + t + co.y + d / 2, d + t), W)
            elif side == 'B': u = min(max(d / 2 - co.y, 0), d)
            else: u = min(max(d + co.x + t / 2, d), d + t)
            v = min(max((co.z + h / 2) / h, 0.004), 0.996)
            uv.data[li].uv = (min(max(u / W, 0.002), 0.998), v)

def book(prefix, key, parent, loc, rot=(0, 0, 0), dims=None, cheap=False):
    """A hardback (or a paperback, for SOFT keys) standing on its foot with its middle at loc in parent's space."""
    t, d, h = dims or BOOK_DIMS[key]
    pv = root(prefix + "_Pivot"); pv.parent = parent; pv.location = loc; pv.rotation_euler = rot
    jk, pg = role("jk_" + key), role("pages")
    bt = 0.0012 if key in SOFT else 0.0028
    bv = 0 if cheap else 0.0009
    parts = [box(prefix + "_BoardB", (bt, d, h), (-(t / 2 - bt / 2), 0, 0), jk, pv, bv=bv),
             box(prefix + "_BoardF", (bt, d, h), (t / 2 - bt / 2, 0, 0), jk, pv, bv=bv),
             box(prefix + "_Spine", (t, 0.004 if key not in SOFT else 0.0015, h), (0, -d / 2 + 0.002, 0), jk, pv, bv=0)]
    if not cheap and key not in SOFT: bevel(parts[2], min(0.004, t * 0.3), 3)   # a rounded spine
    for o in parts: jacket_uv(o, t, d, h)
    inset = 0.0 if key in SOFT else 0.004
    pgs = box(prefix + "_Pages", (t - 2 * bt, d - 0.004 - inset, h - 2 * inset), (0, 0.0005 - inset / 2, 0), pg, pv, bv=0)
    print_uv_const(pgs)
    return pv

def open_book(prefix, key, parent, loc, W=0.15, L=0.22, yaw=0.0):
    """A book lying open face up: two covers flat on the table, the page blocks curving up from the gutter."""
    pv = root(prefix + "_Pivot"); pv.parent = parent; pv.location = loc; pv.rotation_euler = (0, 0, yaw)
    jk, pt, pg = role("jk_" + key), role("pagetext"), role("pages")
    for s in (-1, 1):
        c = box(prefix + "_Cover", (W + 0.008, L + 0.008, 0.0028), (s * (W / 2 + 0.004), 0, 0.0014), jk, pv, bv=0.0008)
        print_uv_const(c, 0.12, 0.5)
        # the page block: a profile across x, pushed along y
        th = 0.011; n = 22; prof = []
        for i in range(n + 1):
            u = i / n
            z = 0.0028 + th * (0.3 + 0.7 * min(1.0, u * 3.0) ** 0.5) + 0.006 * math.sin(math.pi * min(1.0, u * 1.1))
            prof.append((s * (0.003 + u * (W - 0.004)), z))
        prof += [(s * (W - 0.001), 0.0028), (s * 0.003, 0.0028)]
        bm = bmesh.new()
        rings = [[bm.verts.new((x, y, z)) for x, z in prof] for y in (-L / 2, L / 2)]
        m = len(prof)
        for i in range(m):
            j = (i + 1) % m
            f = bm.faces.new((rings[0][i], rings[0][j], rings[1][j], rings[1][i]))
        bm.faces.new(rings[0][::-1]); bm.faces.new(rings[1])
        bmesh.ops.recalc_face_normals(bm, faces=bm.faces[:])
        o = from_bm(prefix + "_Pages", bm, pt, pv)
        me = o.data; uv = me.uv_layers.new(name="Print")
        for p in me.polygons:
            for li in p.loop_indices:
                co = me.vertices[me.loops[li].vertex_index].co
                uv.data[li].uv = (0.5 + co.x / (2 * W), co.y / L + 0.5) if p.normal.z > 0.3 else (0.02, 0.03)
            p.use_smooth = p.normal.z > 0.3
    sp = box(prefix + "_SpineBack", (0.03, L + 0.008, 0.003), (0, 0, 0.0015), jk, pv, bv=0)
    print_uv_const(sp, 0.12, 0.5)
    return pv

def lying(prefix, key, parent, x, y, z, yaw, dims=None, cheap=False):
    """A book lying on its back cover with its bottom at height z."""
    t, d, h = dims or BOOK_DIMS[key]
    return book(prefix, key, parent, (x, y, z + t / 2), (0, -math.pi / 2, yaw), dims, cheap)

def row(prefix, parent, x0, x1, y_front, z, max_h, rng, cheap=True, lean_last=True, keys=None):
    """Books standing side by side from x0 towards x1 with their spines at y_front; the last one leaning. Returns x reached."""
    x = x0; i = 0; keys = keys or BOOK_KEYS
    while True:
        k = rng.choice(keys); t0, d0, h0 = BOOK_DIMS[k]
        h = min(max_h, h0 * rng.uniform(0.85, 1.12)); d = min(0.24, d0 * rng.uniform(0.9, 1.1)); t = t0 * rng.uniform(0.8, 1.3)
        if x + t > x1: break
        book(f"{prefix}{i}", k, parent, (x + t / 2, y_front + d / 2, z + h / 2 + 0.0005), (0, 0, rng.uniform(-0.015, 0.015)), (t, d, h), cheap)
        x += t + rng.uniform(0.0005, 0.003); i += 1
    if lean_last:
        k = rng.choice(keys); t, d, h = BOOK_DIMS[k]; h = min(max_h, h); a = math.radians(rng.uniform(14, 24))
        if x + h * math.sin(a) + t + 0.005 > x1: return x   # no room to lean: it would go through the side
        book(f"{prefix}L", k, parent, (x + h / 2 * math.sin(a) + t / 2 * math.cos(a) + 0.003, y_front + d / 2, z + h / 2 * math.cos(a) + t / 2 * math.sin(a)),
             (0, a, 0), (t, d, h), cheap)
        x += h * math.sin(a) + t
    return x

def build_books():
    rt = root("Books", (0, 0, 0)); rng = random.Random(8)
    for k in BOOK_KEYS:
        t, d, h = BOOK_DIMS[k]
        book(f"Book_{k}", k, rt, (BOOK_SPOTS[f"Book_{k}"], 0, h / 2))
    open_book("Book_Open", "Poems", rt, (BOOK_SPOTS["Book_Open"], 0, 0))
    x = BOOK_SPOTS["Book_Stack"]; z = 0
    for i, (k, yaw) in enumerate((("Atlas", 0.05), ("Anatomy", -0.12), ("Collected", 0.2), ("Diary", -0.35))):
        lying(f"Book_Stack_{i}", k, rt, x + rng.uniform(-0.01, 0.01), rng.uniform(-0.01, 0.01), z, yaw)
        z += BOOK_DIMS[k][0]
    xs = BOOK_SPOTS["Book_Row"]
    row("Book_Row_", rt, xs - 0.18, xs + 0.12, -0.09, 0.0, 0.29, rng, cheap=False)
    for o in rt.children_recursive:
        if o.type == 'MESH' and "Print" not in o.data.uv_layers: print_uv_const(o)
    return rt

# ------------------------------------------------------------------ the bookcase

def sag_box(name, size, loc, mat, parent, sag):
    """A board that bows down in the middle along X."""
    bm = bmesh.new(); bmesh.ops.create_cube(bm, size=1.0)
    bmesh.ops.subdivide_edges(bm, edges=[e for e in bm.edges if abs(e.verts[0].co.x - e.verts[1].co.x) > 0.5], cuts=10)
    for v in bm.verts:
        x = v.co.x * 2; v.co.x *= size[0]; v.co.y *= size[1]; v.co.z = v.co.z * size[2] - sag * (1 - x * x)
    o = from_bm(name, bm, mat, parent); o.location = loc; bevel(o, 0.002, 1)
    return o

def build_bookcase():
    rt = root("Bookcase", (0, 2.0, 0)); rng = random.Random(21)
    case, back = role("case"), role("backboard")
    W, D, H, T = 0.9, 0.32, 1.9, 0.022
    for s in (-1, 1): box("Case_Side", (T, D, H), (s * (W / 2 - T / 2), 0, H / 2), case, rt, bv=0.002)
    box("Case_Top", (W + 0.03, D + 0.02, 0.03), (0, -0.005, H + 0.015), case, rt, bv=0.004)
    box("Case_Crown", (W + 0.05, D + 0.035, 0.012), (0, -0.008, H + 0.036), case, rt, bv=0.003)
    box("Case_Plinth", (W - 2 * T, 0.02, 0.08), (0, -D / 2 + 0.03, 0.04), case, rt, bv=0.002)
    box("Case_Bottom", (W - 2 * T, D - 0.01, T), (0, 0.005, 0.08 + T / 2), case, rt, bv=0.0015)
    box("Case_Back", (W - 0.01, 0.005, H - 0.01), (0, D / 2 - 0.0025, H / 2), back, rt, bv=0)
    levels = [0.08 + T, 0.46, 0.84, 1.22, 1.58]   # tops of the boards the books stand on
    for i, z in enumerate(levels[1:]):
        if i == 3: sag_box("Case_Shelf", (W - 2 * T, D - 0.02, T), (0, 0.005, z - T / 2), case, rt, 0.007)
        else: box("Case_Shelf", (W - 2 * T, D - 0.02, T), (0, 0.005, z - T / 2), case, rt, bv=0.0015)
    x0, x1 = -W / 2 + T + 0.005, W / 2 - T - 0.005; yf = -D / 2 + 0.02
    clear = [levels[i + 1] - T - levels[i] - 0.02 for i in range(4)] + [H - levels[4] - 0.02]
    # bottom: two big atlases lying, then a full row
    z = levels[0]
    for i in range(3):
        lying(f"Case_Lie0_{i}", rng.choice(["Atlas", "Encyclopedia"]), rt, x0 + 0.16, yf + 0.13, z, rng.uniform(-0.06, 0.06),
              (0.03, 0.22, 0.3), cheap=True); z += 0.03
    row("Case_R0_", rt, x0 + 0.33, x1, yf, levels[0], clear[0], rng)
    # a full shelf, a few pulled forward
    row("Case_R1_", rt, x0, x1 - 0.06, yf, levels[1], clear[1], rng)
    # half a row, a gap, a small stack lying
    xr = row("Case_R2_", rt, x0, x0 + 0.42, yf, levels[2], clear[2], rng)
    z = levels[2]
    for i, k in enumerate(("Anatomy", "Collected", "Poems")):
        lying(f"Case_Lie2_{i}", k, rt, x1 - 0.13, yf + 0.09, z, rng.uniform(-0.2, 0.2), cheap=True); z += BOOK_DIMS[k][0]
    # a few books, one fallen flat across the front, then empty
    row("Case_R3_", rt, x0 + 0.02, x0 + 0.2, yf, levels[3], clear[3], rng, lean_last=True)
    lying("Case_Fallen", "Chemistry", rt, x0 + 0.42, yf + 0.08, levels[3] - 0.004, 1.35, cheap=True)
    # the top: an encyclopedia set, then the open diary left there
    row("Case_R4_", rt, x1 - 0.32, x1, yf, levels[4], clear[4], rng, lean_last=False, keys=["Encyclopedia"])
    lying("Case_Lie4", "Diary", rt, x0 + 0.15, yf + 0.1, levels[4], 0.4, cheap=True)
    for o in rt.children_recursive:
        if o.type == 'MESH' and "Print" not in o.data.uv_layers: print_uv_const(o)
    return rt

# ------------------------------------------------------------------ the metal rack

def build_rack():
    rt = root("MetalRack", (0, 4.0, 0)); rng = random.Random(4)
    paint, bolt, can = role("rackpaint"), role("bolt"), role("can")
    W, D, H = 1.0, 0.4, 1.8
    for sx in (-1, 1):
        for sy in (-1, 1):
            x, y = sx * (W / 2 - 0.02), sy * (D / 2 - 0.02)
            box("Rack_PostA", (0.04, 0.002, H), (x, y + sy * 0.019, H / 2), paint, rt, bv=0.0006)
            box("Rack_PostB", (0.002, 0.04, H), (x + sx * 0.019, y, H / 2), paint, rt, bv=0.0006)
            box("Rack_Foot", (0.05, 0.05, 0.006), (x + sx * 0.005, y + sy * 0.005, 0.003), bolt, rt, bv=0.001)
    levels = [0.1, 0.6, 1.1, 1.6]
    for i, z in enumerate(levels):
        dz = -0.004 * (i == 2)
        box("Rack_Shelf", (W - 0.002, D - 0.002, 0.0015), (0, 0, z + dz), paint, rt, bv=0)
        for sy in (-1, 1): box("Rack_Lip", (W - 0.002, 0.0015, 0.035), (0, sy * (D / 2 - 0.001), z - 0.0175 + dz), paint, rt, bv=0)
        for sx in (-1, 1): box("Rack_Lip", (0.0015, D - 0.002, 0.035), (sx * (W / 2 - 0.001), 0, z - 0.0175 + dz), paint, rt, bv=0)
        for sx in (-1, 1):
            for sy in (-1, 1):
                cyl("Rack_Bolt", 0.006, 0.006, 0.005, (sx * (W / 2 + 0.002), sy * (D / 2 - 0.02), z - 0.015 + dz), bolt, rt, seg=8, axis=(sx, 0, 0))
    # flat straps crossed on the back and the sides between the lower shelves
    for z0, z1 in ((levels[0], levels[1]), (levels[2], levels[3])):
        for a, b in (((-W / 2 + 0.03, z0), (W / 2 - 0.03, z1)), ((-W / 2 + 0.03, z1), (W / 2 - 0.03, z0))):
            beam("Rack_Strap", (a[0], D / 2 + 0.004, a[1]), (b[0], D / 2 + 0.004, b[1]), 0.025, 0.002, paint, rt, bv=0)
    # two paint cans, one without its lid, and a tin left on its side
    for i, (x, y, z, r, hgt, lid) in enumerate(((-0.3, 0.02, levels[0], 0.085, 0.19, True), (-0.08, 0.05, levels[0], 0.075, 0.16, False))):
        cyl(f"Rack_Can{i}", r, r, hgt, (x, y, z + hgt / 2), can, rt, seg=24)
        if lid: cyl(f"Rack_Lid{i}", r + 0.004, r + 0.004, 0.008, (x, y, z + hgt + 0.003), bolt, rt, seg=24)
        else: tube(f"Rack_Rim{i}", [(x + (r - 0.003) * math.cos(a), y + (r - 0.003) * math.sin(a), z + hgt) for a in [k * math.tau / 24 for k in range(24)]], 0.003, bolt, rt, cyclic=True)
        tube(f"Rack_Bail{i}", [(x + r * math.cos(a), y, z + hgt + 0.06 * math.sin(a)) for a in [k * math.pi / 12 for k in range(13)]], 0.0018, bolt, rt)
    cyl("Rack_Tin", 0.05, 0.05, 0.12, (0.3, -0.05, levels[2] + 0.05), can, rt, seg=20, axis=(1, 0.3, 0))
    # a box of old papers and the odd book
    lying("Rack_Book0", "Atlas", rt, 0.2, 0.0, levels[1], 0.1, cheap=True)
    lying("Rack_Book1", "Encyclopedia", rt, 0.22, -0.01, levels[1] + 0.014, -0.15, cheap=True)
    book("Rack_Book2", "Collected", rt, (-0.3, -0.08, levels[2] + 0.1075), (0, 0.25, 0.05), cheap=True)
    for o in rt.children_recursive:
        if o.type == 'MESH' and "Print" not in o.data.uv_layers: print_uv_const(o)
    return rt

# ------------------------------------------------------------------ the poster

def build_poster():
    """A sheet 0.5 x 0.72 m on the wall (origin at its middle, on the wall plane), a little wavy, its bottom right
    corner come unstuck and curling off the wall. Front UV is the print; the back shows a bit of the white margin."""
    rt = root("Poster", (0, 6.0, 0))
    w, h, nx, nz = 0.5, 0.72, 24, 34; R = 0.045; c0 = 0.13
    u = Vector((1, -1)).normalized()
    bm = bmesh.new(); uvl = bm.loops.layers.uv.new("UVMap")
    front, backv, uvs = [], [], []
    for j in range(nz + 1):
        for i in range(nx + 1):
            x = (i / nx - 0.5) * w; z = (j / nz - 0.5) * h
            y = -0.0012 - 0.0015 * (math.sin(x * 23 + z * 7) * 0.5 + 0.5) * (1 - (2 * z / h) ** 2)
            p = Vector((x, z)); dd = (p - Vector((w / 2, -h / 2))).dot(u) + c0
            if dd > 0:
                am = math.pi * 0.85; a = dd / R
                if a < am: along, lift = R * math.sin(a), R * (1 - math.cos(a))
                else:
                    r = dd - am * R; along = R * math.sin(am) + r * math.cos(am); lift = R * (1 - math.cos(am)) + r * math.sin(am)
                p = p - u * dd + u * along; y -= lift
            front.append(bm.verts.new((p.x, y, p.y))); backv.append(bm.verts.new((p.x, y + 0.0004, p.y)))
            uvs.append((i / nx, j / nz))
    for j in range(nz):
        for i in range(nx):
            a, b, c, d = j * (nx + 1) + i, j * (nx + 1) + i + 1, (j + 1) * (nx + 1) + i + 1, (j + 1) * (nx + 1) + i
            f = bm.faces.new((front[a], front[b], front[c], front[d]))
            for lp, k in zip(f.loops, (a, b, c, d)): lp[uvl].uv = uvs[k]
            g = bm.faces.new((backv[d], backv[c], backv[b], backv[a]))
            for lp in g.loops: lp[uvl].uv = (0.03, 0.3)
    me = bpy.data.meshes.new("Poster_Mesh"); bm.to_mesh(me); bm.free()
    for f in me.polygons: f.use_smooth = True
    o = link_obj("Poster_Mesh", me, [], rt)
    # front faces must face -Y (away from the wall)
    if sum(p.normal.y for p in list(me.polygons)[0::2]) > 0:
        for p in me.polygons: p.flip()
    return rt

# ------------------------------------------------------------------ materials

def _jacket(key):
    return dict(base=((1.0, 1.0, 1.0), (0.94, 0.93, 0.9)), rough=0.72, metal=0.0, image=os.path.join(SRC_TEX, f"Book_{key}.png"),
                wear=((0.55, 0.5, 0.42), 0.9, 0.0, 0.7), dirt=((0.06, 0.045, 0.03), 0.9, 0.0, 0.6), dust=0.6,
                edge_radius=0.002, ao_dist=0.03)

CFG_PAPERS = {"pages": dict(base=((0.7, 0.64, 0.5), (0.6, 0.54, 0.4)), rough=0.9, metal=0.0,
                            wear=((0.75, 0.7, 0.56), 0.9, 0.0, 0.3), dirt=((0.25, 0.18, 0.1), 0.9, 0.0, 0.7), dust=0.6, edge_radius=0.002, ao_dist=0.03),
              "pagetext": dict(base=((1.0, 1.0, 1.0), (0.96, 0.95, 0.92)), rough=0.9, metal=0.0, image=os.path.join(SRC_TEX, "Book_PageText.png"),
                               wear=((0.75, 0.7, 0.56), 0.9, 0.0, 0.2), dirt=((0.25, 0.18, 0.1), 0.9, 0.0, 0.5), dust=0.4, edge_radius=0.002, ao_dist=0.04)}
CFG_PAPERS.update({"jk_" + k: _jacket(k) for k in BOOK_KEYS})

CFG_CASE = dict(CFG_PAPERS)
CFG_CASE.update({
    "case": dict(base=((0.13, 0.065, 0.035), (0.075, 0.036, 0.018)), rough=0.32, metal=0.0, wood=True, scratch=0.6, chips=0.15, chip_scale=14,
                 wear=((0.33, 0.21, 0.12), 0.75, 0.0, 1.0), dirt=((0.035, 0.025, 0.015), 0.85, 0.0, 0.8), dust=1.0,
                 bottom=0.5, edge_radius=0.003, ao_dist=0.12),
    "backboard": dict(base=((0.38, 0.28, 0.18), (0.32, 0.23, 0.15)), rough=0.85, metal=0.0,
                      wear=((0.45, 0.36, 0.26), 0.9, 0.0, 0.5), dirt=((0.06, 0.045, 0.03), 0.9, 0.0, 1.0), dust=0.4, ao_dist=0.2),
})

CFG_RACK = dict(CFG_PAPERS)
CFG_RACK.update({
    "rackpaint": dict(base=((0.3, 0.33, 0.3), (0.25, 0.28, 0.25)), rough=0.5, metal=0.2, chips=0.5, chip_scale=10, scratch=0.4,
                      wear=((0.24, 0.11, 0.04), 0.85, 0.2, 1.0), dirt=((0.13, 0.07, 0.03), 0.9, 0.1, 1.0), dust=0.8,
                      bottom=0.8, wet=0.25, edge_radius=0.002, ao_dist=0.06),
    "bolt": dict(base=((0.45, 0.45, 0.44), (0.35, 0.35, 0.34)), rough=0.4, metal=1.0, chips=0.6, chip_scale=30,
                 wear=((0.22, 0.1, 0.04), 0.85, 0.2, 1.0), dirt=((0.15, 0.07, 0.03), 0.9, 0.1, 1.0), dust=0.5, edge_radius=0.001, ao_dist=0.02),
    "can": dict(base=((0.55, 0.22, 0.06), (0.45, 0.17, 0.05)), rough=0.45, metal=0.3, chips=0.4, chip_scale=12, scratch=0.3,
                wear=((0.2, 0.09, 0.04), 0.85, 0.3, 1.0), dirt=((0.12, 0.07, 0.03), 0.9, 0.1, 0.9), dust=0.7, edge_radius=0.003, ao_dist=0.05),
})

# ------------------------------------------------------------------ run

def build_decor():
    clear_scene(); ROLES.clear()
    build_books(); build_bookcase(); build_rack(); build_poster()
    assign_materials("Books", CFG_PAPERS); assign_materials("Bookcase", CFG_CASE); assign_materials("MetalRack", CFG_RACK)
    apply_modifiers()
    for p in DECOR_PROPS:
        for o in meshes_of(p):
            if "Print" in o.data.uv_layers: o.data.uv_layers.active = o.data.uv_layers["Print"]
    return "built: " + ", ".join(f"{p}={len(meshes_of(p))}" for p in DECOR_PROPS)

def bake_decor():
    bake_in_background([("Books", 2048), ("Bookcase", 2048), ("MetalRack", 2048)], BAKE_LOG)

def _export(p):
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

def finish_decor():
    out = []
    os.makedirs(FBX_OUT, exist_ok=True)
    groups = {"Books": {n: [o.name for o in meshes_of("Books") if o.name.startswith(n + "_")] for n in BOOK_SPOTS},
              "Bookcase": None, "MetalRack": None}
    origins = {"Books": {n: (x, 0, 0) for n, x in BOOK_SPOTS.items()}, "Bookcase": None, "MetalRack": None}
    for p in DECOR_PROPS:
        made = finalize(p, groups[p], origins[p]); _export(p); out.append(f"{p}: {made}")
    # the poster: its own maps, no bake
    o = bpy.data.objects["Poster_Mesh"]; o.data.materials.clear(); o.data.materials.append(baked_material("Poster"))
    _export("Poster"); out.append("Poster")
    bpy.ops.wm.save_as_mainfile(filepath=os.path.join(PROJECT, "Blender", "Props_Decor.blend"))
    return "; ".join(out)
