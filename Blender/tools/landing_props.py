"""Props for the PvE landing: flat keys on a ring with a numbered plastic tag (1..4), a big old iron key, and a small
wooden key cabinet that hangs on a wall (its door open, two rows of numbered hooks, a couple of spare keys).
Run inside Blender over the bridge, after propkit.py and apartment_props.py (it reuses their helpers), and after
Blender/tools/landing_textures.py has written the tag prints (Assets/Generated/Textures/Props/KeyTag_<N>.png):

    exec(open(r"D:\\BodycamHorror\\Blender\\tools\\propkit.py").read())
    exec(open(r"D:\\BodycamHorror\\Blender\\tools\\apartment_props.py").read())
    exec(open(r"D:\\BodycamHorror\\Blender\\tools\\landing_props.py").read())
    build_landing(); bake_landing()      # bakes in the background (Blender/bake_log.txt)
    finish_landing()                     # join, baked material, FBX export, save Props_Landing.blend

Metres, Z up, front facing -Y (+Z in Unity). The keys hang: their origin is the top of the split ring, the key and
its tag hang below it, flat to the front. The iron key hangs the same way, origin at the top of its bow.
The cabinet's origin is on the wall plane (y = 0) in the middle of its back; hook 3 is at HOOK3 (where a key's
origin goes to hang it there).
"""
import bpy, bmesh, math, os
from mathutils import Vector

LANDING_PROPS = ["FlatKeys", "IronKey", "KeyCabinet"]
HOOK_X = (-0.105, -0.035, 0.035, 0.105)
HOOK_ROWS = (0.075, -0.085)
HOOK_Y = -0.036            # the hook's tip, out from the back board
HOOK3 = (HOOK_X[2], -0.03, HOOK_ROWS[0] + 0.0018)

# ------------------------------------------------------------------ flat plates with holes

def plate(name, outer, holes, t, mat, parent, y=0.0):
    """A flat plate in the x-z plane, `t` thick along y: outline `outer` [(x, z)], cut through by `holes`."""
    bm = bmesh.new(); edges = []
    for loop in [outer] + list(holes):
        vs = [bm.verts.new((x, y - t / 2, z)) for x, z in loop]
        edges += [bm.edges.new((vs[i], vs[(i + 1) % len(vs)])) for i in range(len(vs))]
    bmesh.ops.triangle_fill(bm, use_beauty=True, use_dissolve=True, edges=edges)
    ext = bmesh.ops.extrude_face_region(bm, geom=bm.faces[:])
    for v in [e for e in ext["geom"] if isinstance(e, bmesh.types.BMVert)]: v.co.y += t
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces[:])
    return from_bm(name, bm, mat, parent)

def circle(cx, cz, r, n=20, a0=0.0, a1=2 * math.pi, closed=True):
    m = n if closed else n + 1
    return [(cx + r * math.cos(a0 + (a1 - a0) * i / n), cz + r * math.sin(a0 + (a1 - a0) * i / n)) for i in range(m)]

def print_uv_two_sided(o, box):
    """'Print' UV from x, z fitted to box; the back (+y) faces read the other way round, so the print is not mirrored."""
    me = o.data; uv = me.uv_layers.get("Print") or me.uv_layers.new(name="Print")
    (u0, u1), (v0, v1) = box
    for p in me.polygons:
        back = p.normal.y > 0.5
        for li in p.loop_indices:
            co = me.vertices[me.loops[li].vertex_index].co
            u = (co.x - u0) / (u1 - u0)
            uv.data[li].uv = (1 - u if back else u, (co.z - v0) / (v1 - v0))

# ------------------------------------------------------------------ a flat key on a split ring, with a tag

RING_R = 0.011

def key_outline(hc, ro=0.0105):
    """A flat cylinder-lock key: round head (centre z = hc) and a blade with cuts down its right edge."""
    s = hc - ro * math.sin(math.radians(60))          # where the blade meets the head
    pts = circle(0, hc, ro, 22, math.radians(-60), math.radians(240), closed=False)
    sh = s - 0.004; tip = sh - 0.031
    pts += [(-0.00525, sh), (-0.0035, sh - 0.0012), (-0.0035, tip + 0.002), (-0.0013, tip), (0.0016, tip), (0.0035, tip + 0.0025)]
    z = tip + 0.0025
    for i, (dz, x) in enumerate(((0.004, 0.0019), (0.003, 0.0035), (0.004, 0.0022), (0.003, 0.0035), (0.005, 0.0017), (0.003, 0.0035))):
        z += dz; pts.append((x, z))
    pts += [(0.0035, sh - 0.0012), (0.00525, sh)]
    return pts

def flat_key(prefix, ox, mats, parent, tag_mat=None, y=0.0):
    """The ring (in the y-z plane, its top at z = 0), the key through it and, when given, the tag beside it."""
    ring, metal = mats
    tube(prefix + "Ring", [(ox, y + RING_R * math.cos(a), -RING_R + RING_R * math.sin(a))
                           for a in [2 * math.pi * i / 28 for i in range(28)]], 0.0009, ring, parent, cyclic=True)
    wire_top = -2 * RING_R + 0.0009
    ri = 0.0032; hc = wire_top - ri
    k = plate(prefix + "Key", [(x + ox - 0.0035, z) for x, z in key_outline(hc)], [circle(ox - 0.0035, hc, ri, 16)],
              0.0022, metal, parent, y=y - 0.0012)
    bevel(k, 0.0004, 1)
    if tag_mat is not None:
        W, H, rh = 0.03, 0.046, 0.0026
        hcz = wire_top - rh; top = hcz + 0.125 * H; tx = ox + 0.006
        outer = []
        for cx, cz, r, a0 in ((tx + W / 2 - 0.009, top - 0.009, 0.009, 0), (tx - W / 2 + 0.009, top - 0.009, 0.009, 90),
                              (tx - W / 2 + 0.004, top - H + 0.004, 0.004, 180), (tx + W / 2 - 0.004, top - H + 0.004, 0.004, 270)):
            outer += circle(cx, cz, r, 6, math.radians(a0), math.radians(a0 + 90), closed=False)
        tag = plate(prefix + "Tag", outer, [circle(tx, hcz, rh, 14)], 0.003, tag_mat, parent, y=y + 0.0022)
        print_uv_two_sided(tag, ((tx - W / 2, tx + W / 2), (top - H, top)))
        bevel(tag, 0.0006, 2)

def build_flat_keys():
    rt = root("FlatKeys", (0, 0.3, 0))
    ring, metal = role("ring"), role("keymetal")
    for n in (1, 2, 3, 4):
        flat_key(f"K{n}_", (n - 1) * 0.08, (ring, metal), rt, role(f"tag{n}"))
    return rt

def _tag(n):
    return dict(base=((1.0, 1.0, 1.0), (0.94, 0.94, 0.92)), rough=0.42, metal=0.0, image=os.path.join(TEX_OUT, f"KeyTag_{n}.png"),
                wear=((0.85, 0.82, 0.75), 0.5, 0.0, 0.35), dirt=((0.2, 0.17, 0.12), 0.85, 0.0, 0.6), dust=0.2,
                edge_radius=0.0008, ao_dist=0.006, bump=0.05)

CFG_KEYS = {
    "ring": dict(base=((0.62, 0.62, 0.6), (0.5, 0.5, 0.48)), rough=0.35, metal=1.0, scratch=0.4,
                 wear=((0.75, 0.74, 0.7), 0.25, 1.0, 0.4), dirt=((0.12, 0.1, 0.07), 0.8, 0.0, 0.7), dust=0.2,
                 edge_radius=0.0005, ao_dist=0.006),
    "keymetal": dict(base=((0.66, 0.55, 0.32), (0.55, 0.45, 0.25)), rough=0.38, metal=1.0, scratch=0.5,
                     wear=((0.8, 0.7, 0.45), 0.25, 1.0, 0.5), dirt=((0.14, 0.1, 0.05), 0.85, 0.0, 0.8), dust=0.2,
                     edge_radius=0.0006, ao_dist=0.006),
    "tag1": _tag(1), "tag2": _tag(2), "tag3": _tag(3), "tag4": _tag(4),
}

# ------------------------------------------------------------------ the big iron key

def build_iron_key():
    rt = root("IronKey", (0.5, 0.3, 0))
    iron = role("iron")
    br, bt = 0.026, 0.0062                       # bow radius (to the middle of the bar), bar radius
    bc = -(br + bt)
    tube("Iron_Bow", [(br * math.cos(a), 0, bc + br * math.sin(a)) for a in [2 * math.pi * i / 40 for i in range(40)]], bt, iron, rt, cyclic=True)
    # a cross inside the bow, as on old church and barn keys
    rod("Iron_BowBar", (-br, 0, bc), (br, 0, bc), 0.0035, iron, rt)
    rod("Iron_BowBar", (0, 0, bc + br), (0, 0, bc - br), 0.0035, iron, rt)
    neck = bc - br - bt
    cyl("Iron_Collar", 0.0095, 0.0095, 0.012, (0, 0, neck - 0.004), iron, rt, seg=20, bv=0.0015)
    cyl("Iron_Collar2", 0.0085, 0.0085, 0.006, (0, 0, neck - 0.026), iron, rt, seg=20, bv=0.0012)
    tip = neck - 0.165
    rod("Iron_Shaft", (0, 0, neck - 0.008), (0, 0, tip), 0.0058, iron, rt, seg=16)
    sphere("Iron_Tip", 0.0068, (0, 0, tip), iron, rt, u=16, v=10)
    # the bit: a plate off one side near the end, with wards cut into it
    z0, z1, x0, x1 = tip + 0.004, tip + 0.036, 0.004, 0.034
    out = [(x0, z1), (x0, z0), (x1, z0), (x1, z0 + 0.008), (x1 - 0.008, z0 + 0.008), (x1 - 0.008, z0 + 0.014), (x1, z0 + 0.014),
           (x1, z1 - 0.006), (x1 - 0.012, z1 - 0.006), (x1 - 0.012, z1)]
    b = plate("Iron_Bit", out, [], 0.0055, iron, rt); bevel(b, 0.0008, 1)
    return rt

CFG_IRON = {
    "iron": dict(base=((0.075, 0.07, 0.065), (0.05, 0.047, 0.043)), rough=0.62, metal=0.85, chips=0.45, chip_scale=40, scratch=0.3,
                 wear=((0.34, 0.15, 0.05), 0.9, 0.0, 0.9), dirt=((0.16, 0.08, 0.03), 0.9, 0.0, 0.9), dust=0.3,
                 edge_radius=0.0015, ao_dist=0.015, bump=0.4),
}

# ------------------------------------------------------------------ the key cabinet

def build_cabinet():
    rt = root("KeyCabinet", (-0.5, 0.3, 0))
    wood, inner, paper, ink, hook = role("cabwood"), role("cabinner"), role("cabpaper"), role("cabink"), role("cabhook")
    W, H, D, T, B = 0.32, 0.42, 0.085, 0.014, 0.008
    box("Cab_Back", (W, B, H), (0, -B / 2, 0), inner, rt, bv=0.001)
    for sx in (-1, 1): box("Cab_Side", (T, D - B, H), (sx * (W / 2 - T / 2), -B - (D - B) / 2, 0), wood, rt, bv=0.002)
    for sz in (-1, 1): box("Cab_TopBot", (W, D - B, T), (0, -B - (D - B) / 2, sz * (H / 2 - T / 2)), wood, rt, bv=0.002)
    # the door, hung on the left, swung open past square
    hinge = Vector((-W / 2, -D, 0)); ang = math.radians(-112)
    rot = Vector((math.cos(ang), math.sin(ang), 0)); nrm = Vector((-math.sin(ang), math.cos(ang), 0))   # along the door, and its back
    def on_door(u, v, z):   # u along the door from the hinge, v out of its front
        return hinge + rot * u - nrm * v + Vector((0, 0, z))
    q = Vector((1, 0, 0)).rotation_difference(rot)
    box("Cab_Door", (W, 0.012, H), on_door(W / 2, 0.006, 0), wood, rt, bv=0.002, rot=q)
    # a raised frame on its front, the plain carpentry of the time
    box("Cab_DoorFrameV", (0.018, 0.006, H - 0.04), on_door(0.03, 0.014, 0), wood, rt, bv=0.0015, rot=q)
    box("Cab_DoorFrameV", (0.018, 0.006, H - 0.04), on_door(W - 0.03, 0.014, 0), wood, rt, bv=0.0015, rot=q)
    box("Cab_DoorFrameH", (W - 0.06, 0.006, 0.018), on_door(W / 2, 0.014, H / 2 - 0.03), wood, rt, bv=0.0015, rot=q)
    box("Cab_DoorFrameH", (W - 0.06, 0.006, 0.018), on_door(W / 2, 0.014, -H / 2 + 0.03), wood, rt, bv=0.0015, rot=q)
    sphere("Cab_Knob", 0.009, on_door(W - 0.03, 0.026, 0.0), hook, rt, u=14, v=8)
    for zz in (H / 2 - 0.05, -H / 2 + 0.05):
        cyl("Cab_Hinge", 0.004, 0.004, 0.04, hinge + Vector((0, 0.002, zz)), hook, rt, seg=10)
    # hooks in two rows, a paper strip over each row with the numbers in pen
    n = 0
    for row, z in enumerate(HOOK_ROWS):
        box("Cab_Strip", (W - 2 * T - 0.02, 0.0012, 0.02), (0, -B - 0.0006, z + 0.03), paper, rt, bv=0)
        for x in HOOK_X:
            n += 1
            rod("Cab_Hook", (x, -B, z), (x, HOOK_Y, z), 0.0018, hook, rt, seg=10)
            rod("Cab_HookTip", (x, HOOK_Y, z), (x, HOOK_Y, z + 0.009), 0.0018, hook, rt, seg=10)
            cyl("Cab_HookPlate", 0.005, 0.005, 0.002, (x, -B - 0.001, z), hook, rt, seg=12, axis=(0, -1, 0))
            text_mesh("Cab_Num", str(n), "segoeprb.ttf", 0.017, (x, -B - 0.0014, z + 0.03), ink, rt,
                      rot=(math.radians(90), 0, 0), extrude=0.0002)
    # spare keys left on hooks 5 and 8 (no tags)
    for i, x in ((5, HOOK_X[0]), (8, HOOK_X[3])):
        sub = root(f"Cab_Spare{i}", (0, 0, 0)); sub.parent = rt
        flat_key(f"Cab_S{i}_", 0.0, (hook, role("cabkey")), sub, None)
        sub.location = (x, -0.03, HOOK_ROWS[1] + 0.0018)
    return rt

CFG_CABINET = {
    "cabwood": dict(base=((0.36, 0.2, 0.1), (0.26, 0.13, 0.06)), rough=0.5, metal=0.0, wood=True, chips=0.3, chip_scale=16,
                    wear=((0.55, 0.42, 0.28), 0.75, 0.0, 0.7), dirt=((0.1, 0.07, 0.04), 0.9, 0.0, 0.9), dust=0.6,
                    edge_radius=0.0025, ao_dist=0.03),
    "cabinner": dict(base=((0.5, 0.36, 0.22), (0.42, 0.3, 0.18)), rough=0.7, metal=0.0, wood=True,
                     wear=((0.55, 0.45, 0.32), 0.8, 0.0, 0.3), dirt=((0.12, 0.09, 0.05), 0.9, 0.0, 1.0), dust=0.5,
                     edge_radius=0.002, ao_dist=0.04),
    "cabpaper": dict(base=((0.82, 0.78, 0.66), (0.72, 0.67, 0.54)), rough=0.85, metal=0.0,
                     wear=((0.6, 0.55, 0.42), 0.9, 0.0, 0.3), dirt=((0.35, 0.28, 0.16), 0.9, 0.0, 0.8), dust=0.3,
                     edge_radius=0.0005, ao_dist=0.01, bump=0.05),
    "cabink": dict(base=((0.05, 0.06, 0.12), (0.04, 0.05, 0.1)), rough=0.7, metal=0.0,
                   wear=((0.1, 0.1, 0.15), 0.7, 0.0, 0.0), dirt=((0.1, 0.1, 0.1), 0.8, 0.0, 0.0), dust=0.0,
                   edge_radius=0.0002, ao_dist=0.002),
    "cabhook": dict(base=((0.42, 0.4, 0.36), (0.32, 0.3, 0.27)), rough=0.45, metal=1.0,
                    wear=((0.3, 0.16, 0.07), 0.8, 0.0, 0.6), dirt=((0.12, 0.08, 0.04), 0.9, 0.0, 0.8), dust=0.3,
                    edge_radius=0.0008, ao_dist=0.01),
    "cabkey": dict(CFG_KEYS["keymetal"]),
}

# ------------------------------------------------------------------ run

def build_landing():
    clear_scene(); ROLES.clear()
    build_flat_keys(); build_iron_key(); build_cabinet()
    assign_materials("FlatKeys", CFG_KEYS); assign_materials("IronKey", CFG_IRON); assign_materials("KeyCabinet", CFG_CABINET)
    apply_modifiers()
    return "built: " + ", ".join(f"{p}={len(meshes_of(p))}" for p in LANDING_PROPS)

def bake_landing():
    bake_in_background([("FlatKeys", 1024), ("IronKey", 1024), ("KeyCabinet", 1024)], BAKE_LOG)

def finish_landing():
    os.makedirs(FBX_OUT, exist_ok=True)
    out = []
    names = {p: [o.name for o in meshes_of(p)] for p in LANDING_PROPS}
    groups = {"FlatKeys": {f"Key_{n}": [o for o in names["FlatKeys"] if o.startswith(f"K{n}_")] for n in (1, 2, 3, 4)},
              "IronKey": None, "KeyCabinet": None}
    origins = {"FlatKeys": {f"Key_{n}": ((n - 1) * 0.08, 0, 0) for n in (1, 2, 3, 4)}}
    for p in LANDING_PROPS:
        made = finalize(p, groups[p], origins.get(p))
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
    bpy.ops.wm.save_as_mainfile(filepath=os.path.join(PROJECT, "Blender", "Props_Landing.blend"))
    return "; ".join(out)
