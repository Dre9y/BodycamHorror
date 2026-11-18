"""Storage props for BodycamHorror: an old Soviet-style enamel fridge (rounded body, a full-height door with a chrome
lever handle and trim strip, a vent grille at the foot, rust coming through at the bottom) and cardboard boxes
(closed and taped, open with its flaps out, a small one, one trodden flat), the boxes sharing one texture.
Run inside Blender over the bridge, after propkit.py and apartment_props.py (it reuses their helpers):

    exec(open(r"D:\\BodycamHorror\\Blender\\tools\\propkit.py").read())
    exec(open(r"D:\\BodycamHorror\\Blender\\tools\\apartment_props.py").read())
    exec(open(r"D:\\BodycamHorror\\Blender\\tools\\storage_props.py").read())
    build_storage(); bake_storage()      # bakes in the background (Blender/bake_log.txt)
    finish_storage()                     # join, baked material, FBX export, save Props_Storage.blend

Origins on the floor under each prop's middle; fronts towards -Y (+Z in Unity). CardboardBoxes.fbx holds one
mesh per box (Box_Closed, Box_Open, Box_Small, Box_Crushed), each with its origin at its own foot.
"""
import bpy, bmesh, math, os, random
import numpy as np
from mathutils import Vector

STORAGE_PROPS = ["Fridge", "CardboardBoxes"]
BOX_SPOTS = {"Box_Closed": 0.0, "Box_Open": 1.0, "Box_Small": 2.0, "Box_Crushed": 3.0}   # x of each, inside the prop

# ------------------------------------------------------------------ the box print

def tex_box_print():
    """Ink on kraft: 'this way up' arrows, a stencilled block of figures, a round stamp, a long ink line.
    Each face of a box gets the whole square (see print_uv_faces); the corner at (0.98, 0.02) stays clean."""
    s = 1024; img = np.ones((s, s, 3)); rng = np.random.default_rng(13)
    ink = (0.12, 0.1, 0.09)
    yy, xx = np.mgrid[0:s, 0:s].astype(float)
    def arrow(cx, cy, sz):
        shaft = (np.abs(xx - cx) < sz * 0.12) & (yy > cy - sz * 0.2) & (yy < cy + sz * 0.6)
        head = (yy > cy - sz * 0.75) & (yy < cy - sz * 0.15) & (np.abs(xx - cx) < (yy - (cy - sz * 0.75)) * 0.55)
        return (shaft | head).astype(float)
    m = arrow(150, 190, 110) + arrow(260, 190, 110)
    m += ((np.abs(yy - 300) < 5) & (xx > 80) & (xx < 330)).astype(float)
    # stencilled figures: blocks with gaps, as a box's spec print
    for row in range(3):
        x = 560
        while x < 900:
            w = int(rng.integers(18, 34)); h = 46 if row == 0 else 30
            y0 = 120 + row * 60
            blk = (xx >= x) & (xx < x + w) & (yy >= y0) & (yy < y0 + h)
            gap = (np.abs(yy - (y0 + h / 2)) < 3) & (np.abs(xx - (x + w / 2)) < 2)
            m += (blk & ~gap).astype(float) * (rng.random() > 0.15)
            x += w + int(rng.integers(8, 16))
    rr = np.hypot(yy - 720, xx - 300)
    m += ((np.abs(rr - 120) < 6) | (np.abs(rr - 95) < 3)).astype(float) * 0.8
    m += ((np.abs(yy - 720) < 14) & (np.abs(xx - 300) < 70)).astype(float) * 0.7
    m += ((np.abs(yy - 880) < 3) & (xx > 520) & (xx < 940)).astype(float)
    m = np.clip(m, 0, 1) * (0.55 + 0.45 * _fbm(s, s, 20, 3, 3))     # worn, patchy ink
    m[s - 60:, s - 60:] = 0
    _blend(img, m, ink)
    return _save("Box_Print", img)

def print_uv_faces(o, dims):
    """A 'Print' UV that lays the whole print square on every face, by the face's axis and the box's size."""
    me = o.data; uv = me.uv_layers.get("Print") or me.uv_layers.new(name="Print")
    for p in me.polygons:
        n = p.normal; ax = max(range(3), key=lambda i: abs(n[i]))
        a, b = [(1, 2), (0, 2), (0, 1)][ax]
        for li in p.loop_indices:
            co = me.vertices[me.loops[li].vertex_index].co
            uv.data[li].uv = (co[a] / dims[a] + 0.5, co[b] / dims[b] + 0.5)

def flap(name, w, h, t, hinge, axis, angle, mat, parent):
    """A box flap w wide (along the hinge) and h long, hinged at its edge at `hinge`, turned `angle` degrees
    about the hinge. axis: 'x' for a hinge along X (flap on a long side), 'y' for one along Y."""
    bm = bmesh.new(); bmesh.ops.create_cube(bm, size=1.0)
    for v in bm.verts:
        if axis == 'x': v.co.x *= w; v.co.y *= t; v.co.z = (v.co.z + 0.5) * h
        else: v.co.y *= w; v.co.x *= t; v.co.z = (v.co.z + 0.5) * h
    o = from_bm(name, bm, mat, parent); o.location = hinge
    o.rotation_euler = (math.radians(angle), 0, 0) if axis == 'x' else (0, math.radians(angle), 0)
    print_uv_faces(o, (w if axis == 'x' else t * 40, w if axis == 'y' else t * 40, h * 2))
    return o

def build_boxes():
    rt = root("CardboardBoxes", (0, 3.0, 0))
    card, tape = role("cardboard"), role("tape")
    rng = random.Random(5)
    # closed and taped
    x0 = BOX_SPOTS["Box_Closed"]; W, D, H = 0.6, 0.5, 0.45
    b = pillow("Box_Closed_Body", (W, D, H), (x0, 0, H / 2), card, rt, puff=0.01, cuts=6, sag=0.012)
    print_uv_faces(b, (W, D, H))
    for nm, size, loc in (("Box_Closed_Tape", (W + 0.004, 0.05, 0.002), (x0, 0, H + 0.006)),
                          ("Box_Closed_TapeL", (0.002, 0.05, 0.09), (x0 - W / 2 - 0.006, 0, H - 0.045)),
                          ("Box_Closed_TapeR", (0.002, 0.05, 0.09), (x0 + W / 2 + 0.006, 0, H - 0.045))):
        print_uv_const(box(nm, size, loc, tape, rt, bv=0), 0.98, 0.02)
    # open: four walls, a floor, the flaps out
    x0 = BOX_SPOTS["Box_Open"]; W, D, H, T = 0.55, 0.5, 0.5, 0.005
    for nm, size, loc in (("Box_Open_Front", (W, T, H), (x0, -D / 2, H / 2)), ("Box_Open_Back", (W, T, H), (x0, D / 2, H / 2)),
                          ("Box_Open_Left", (T, D, H), (x0 - W / 2, 0, H / 2)), ("Box_Open_Right", (T, D, H), (x0 + W / 2, 0, H / 2)),
                          ("Box_Open_Floor", (W, D, T), (x0, 0, T / 2))):
        print_uv_faces(box(nm, size, loc, card, rt, bv=0.0015), (W, D, H))
    flap("Box_Open_FlapF", W, D / 2, T, (x0, -D / 2, H), "x", 125, card, rt)
    flap("Box_Open_FlapB", W, D / 2, T, (x0, D / 2, H), "x", -140, card, rt)
    flap("Box_Open_FlapL", D, D / 2, T, (x0 - W / 2, 0, H), 'y', -105, card, rt)
    flap("Box_Open_FlapR", D, D / 2, T, (x0 + W / 2, 0, H), 'y', 160, card, rt)
    # small, closed, a strip of tape torn
    x0 = BOX_SPOTS["Box_Small"]; W, D, H = 0.5, 0.4, 0.35
    b = pillow("Box_Small_Body", (W, D, H), (x0, 0, H / 2), card, rt, puff=0.008, cuts=6, sag=0.006)
    print_uv_faces(b, (W, D, H))
    print_uv_const(box("Box_Small_Tape", (W * 0.7, 0.05, 0.002), (x0 - W * 0.15, 0, H + 0.005), tape, rt, bv=0), 0.98, 0.02)
    # trodden flat: a closed box squashed, its sides bulging out and folding
    x0 = BOX_SPOTS["Box_Crushed"]; W, D, H = 0.5, 0.4, 0.4
    bm = bmesh.new(); bmesh.ops.create_cube(bm, size=1.0)
    bmesh.ops.subdivide_edges(bm, edges=bm.edges[:], cuts=8, use_grid_fill=True)
    for v in bm.verts:
        x, y, z = v.co.x * 2, v.co.y * 2, v.co.z * 2
        side = (1 - z * z)
        v.co.x = x * W / 2 * (1 + 0.12 * side) + 0.01 * math.sin(y * 7)
        v.co.y = y * D / 2 * (1 + 0.15 * side) + 0.01 * math.sin(x * 5)
        v.co.z = (z + 1) * 0.055 + 0.02 * math.sin(x * 4 + y * 3) * (z > 0) + 0.012 * (1 - x * x) * (1 - y * y) * (z > 0)
    o = from_bm("Box_Crushed_Body", bm, card, rt); o.location = (x0, 0, 0)
    for p in o.data.polygons: p.use_smooth = True
    print_uv_faces(o, (W, D, 0.12))
    return rt

CFG_BOXES = {
    "cardboard": dict(base=((0.34, 0.2, 0.095), (0.29, 0.17, 0.08)), rough=0.85, metal=0.0, image=None,
                      wear=((0.5, 0.36, 0.2), 0.9, 0.0, 0.9), dirt=((0.08, 0.05, 0.025), 0.9, 0.0, 0.9), dust=0.6,
                      bottom=0.7, wet=0.45, wrinkle=0.15, bump=0.3, edge_radius=0.006, ao_dist=0.08),
    "tape": dict(base=((0.45, 0.33, 0.16), (0.4, 0.29, 0.14)), rough=0.35, metal=0.0,
                 wear=((0.55, 0.45, 0.3), 0.6, 0.0, 0.5), dirt=((0.1, 0.07, 0.04), 0.8, 0.0, 0.8), dust=0.5,
                 wrinkle=0.2, edge_radius=0.002, ao_dist=0.03),
}

# ------------------------------------------------------------------ the fridge

def build_fridge():
    rt = root("Fridge", (0, 0, 0))
    enamel, chrome, grille, rubber = role("enamel"), role("chrome"), role("grille"), role("gasket")
    W, D, H = 0.63, 0.6, 1.45
    body = box("Fridge_Body", (W, D - 0.03, H - 0.09), (0, 0.015, 0.09 + (H - 0.09) / 2), enamel, rt, bv=0)
    bevel(body, 0.05, 6)
    # the plinth: set back, a slotted vent across the front
    box("Fridge_Plinth", (W - 0.04, D - 0.08, 0.1), (0, 0.03, 0.05), grille, rt, bv=0.004)
    for i in range(6):
        box("Fridge_Slot", (W - 0.12, 0.006, 0.006), (0, -D / 2 + 0.035, 0.025 + i * 0.011), chrome, rt, bv=0.0015)
    # the door: a full-height slab with rounded edges, the rubber seal behind it
    door = box("Fridge_Door", (W - 0.01, 0.045, H - 0.14), (0, -D / 2 + 0.02, 0.1 + (H - 0.14) / 2 + 0.005), enamel, rt, bv=0)
    bevel(door, 0.035, 6)
    box("Fridge_Gasket", (W - 0.03, 0.012, H - 0.16), (0, -D / 2 + 0.045, 0.1 + (H - 0.14) / 2 + 0.005), rubber, rt, bv=0.004)
    # chrome: a trim strip across the door's top, a lever handle on its right edge on two brackets
    box("Fridge_Trim", (W - 0.12, 0.006, 0.035), (0, -D / 2 - 0.004, H - 0.16), chrome, rt, bv=0.002)
    hx = W / 2 - 0.05
    for z in (0.92, 1.18):
        box("Fridge_HandleFoot", (0.03, 0.035, 0.03), (hx, -D / 2 - 0.012, z), chrome, rt, bv=0.006)
    pts = smooth_path([(hx, -D / 2 - 0.02, 0.9), (hx, -D / 2 - 0.045, 0.95), (hx + 0.004, -D / 2 - 0.05, 1.05),
                       (hx, -D / 2 - 0.045, 1.15), (hx, -D / 2 - 0.02, 1.2)], 20)
    hd = tube("Fridge_Handle", [tuple(p) for p in pts], 0.011, chrome, rt)
    for v in hd.data.vertices: v.co.x = hx + (v.co.x - hx) * 1.6   # a flat, wide lever
    # a small chrome badge plate up on the door (no lettering), and the hinges on the left
    box("Fridge_Badge", (0.14, 0.006, 0.03), (-0.1, -D / 2 - 0.004, H - 0.3), chrome, rt, bv=0.003)
    for z in (0.12, H - 0.04):
        box("Fridge_Hinge", (0.04, 0.05, 0.02), (-W / 2 + 0.03, -D / 2 + 0.01, z), chrome, rt, bv=0.004)
    # the condenser grid on the back, against the wall
    for i in range(9):
        z = 0.2 + i * 0.13
        rod("Fridge_Coil", (-W / 2 + 0.06, D / 2 + 0.01, z), (W / 2 - 0.06, D / 2 + 0.01, z), 0.004, grille, rt, seg=8)
    for x in (-W / 2 + 0.07, W / 2 - 0.07):
        rod("Fridge_CoilRail", (x, D / 2 + 0.015, 0.15), (x, D / 2 + 0.015, 1.3), 0.003, grille, rt, seg=8)
    return rt

CFG_FRIDGE = {
    "enamel": dict(base=((0.72, 0.71, 0.64), (0.64, 0.63, 0.55)), rough=0.3, metal=0.0, chips=0.45, chip_scale=8,
                   wear=((0.18, 0.08, 0.03), 0.85, 0.2, 1.0), dirt=((0.16, 0.12, 0.07), 0.85, 0.0, 1.0), dust=0.7,
                   bottom=1.0, wet=0.3, scratch=0.4, edge_radius=0.01, ao_dist=0.12),
    "chrome": dict(base=((0.72, 0.72, 0.72), (0.62, 0.62, 0.62)), rough=0.22, metal=1.0, chips=0.5, chip_scale=30,
                   wear=((0.22, 0.11, 0.04), 0.8, 0.2, 0.8), dirt=((0.12, 0.08, 0.04), 0.85, 0.1, 0.9), dust=0.5,
                   edge_radius=0.003, ao_dist=0.04),
    "grille": dict(base=((0.035, 0.035, 0.035), (0.025, 0.025, 0.025)), rough=0.6, metal=0.4,
                   wear=((0.2, 0.09, 0.035), 0.85, 0.2, 1.0), dirt=((0.1, 0.06, 0.03), 0.9, 0.0, 0.9), dust=0.6,
                   edge_radius=0.003, ao_dist=0.05),
    "gasket": dict(base=((0.05, 0.05, 0.05), (0.04, 0.04, 0.04)), rough=0.85, metal=0.0,
                   wear=((0.12, 0.12, 0.11), 0.9, 0.0, 0.4), dirt=((0.1, 0.08, 0.05), 0.9, 0.0, 0.8), dust=0.5,
                   edge_radius=0.003, ao_dist=0.03),
}

# ------------------------------------------------------------------ run

def build_storage():
    clear_scene(); ROLES.clear()
    CFG_BOXES["cardboard"]["image"] = tex_box_print()
    build_fridge(); build_boxes()
    for o in meshes_of("Fridge"):
        if "Print" not in o.data.uv_layers: print_uv_const(o)
    assign_materials("Fridge", CFG_FRIDGE); assign_materials("CardboardBoxes", CFG_BOXES)
    apply_modifiers()
    for p in STORAGE_PROPS:
        for o in meshes_of(p):
            if "Print" in o.data.uv_layers: o.data.uv_layers.active = o.data.uv_layers["Print"]
    return "built: " + ", ".join(f"{p}={len(meshes_of(p))}" for p in STORAGE_PROPS)

def bake_storage():
    bake_in_background([("Fridge", 2048), ("CardboardBoxes", 2048)], BAKE_LOG)

def finish_storage():
    out = []
    groups = {"Fridge": None,
              "CardboardBoxes": {n: [o.name for o in meshes_of("CardboardBoxes") if o.name.startswith(n + "_")] for n in BOX_SPOTS}}
    origins = {"Fridge": None, "CardboardBoxes": {n: (x, 0, 0) for n, x in BOX_SPOTS.items()}}
    for p in STORAGE_PROPS:
        made = finalize(p, groups[p], origins[p])
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
    bpy.ops.wm.save_as_mainfile(filepath=os.path.join(PROJECT, "Blender", "Props_Storage.blend"))
    return "; ".join(out)
