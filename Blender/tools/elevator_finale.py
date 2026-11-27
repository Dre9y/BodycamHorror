"""Final cutscene: Soviet passenger lift + landing.

Run inside Blender (Elevator_Finale.blend):  exec(open(r"D:/BodycamHorror/Blender/tools/elevator_finale.py").read())
Builds collection "Lift" (the moving cab, parented to empty "Cab") and "Landing" (static floor
outside, shaft wall and shaft doors). Blender units are metres, Z up; the player stands at the
origin looking down -Y at the doors.
"""
import bpy, bmesh, math, os
import numpy as np
from mathutils import Vector

ROOT = "D:/BodycamHorror"
ENV = ROOT + "/Assets/Textures/Environment/"
TEX = ROOT + "/Blender/tex/"

# ---------------------------------------------------------------- cab dimensions
CAB_X = 0.55          # half width inside
CAB_Y0, CAB_Y1 = -0.60, 0.62   # front (door) / back wall
CAB_H = 2.15
DOOR_HALF = 0.40      # half width of the opening
DOOR_H = 2.00
LEAF_W = 0.42
CAB_DOOR_Y = -0.645
SHAFT_DOOR_Y = -0.705
SHAFT_WALL_Y0, SHAFT_WALL_Y1 = -0.725, -0.84


# ---------------------------------------------------------------- generated textures
def _noise(n, cells, seed):
    rng = np.random.default_rng(seed)
    g = rng.random((cells + 1, cells + 1))
    t = np.linspace(0, cells, n, endpoint=False)
    i = t.astype(int); f = t - i
    f = f * f * (3 - 2 * f)
    a = g[i][:, i]; b = g[i + 1][:, i]; c = g[i][:, i + 1]; d = g[i + 1][:, i + 1]
    fy = f[:, None]; fx = f[None, :]
    return (a * (1 - fy) + b * fy) * (1 - fx) + (c * (1 - fy) + d * fy) * fx


def _fbm(n, seed, octaves=5, base=4):
    out = np.zeros((n, n)); amp = 1.0; tot = 0
    for o in range(octaves):
        out += _noise(n, base * 2 ** o, seed + o) * amp
        tot += amp; amp *= 0.5
    return out / tot


def _save(name, rgb):
    path = TEX + name + ".png"
    n = rgb.shape[0]
    img = bpy.data.images.get(name) or bpy.data.images.new(name, n, n, alpha=False)
    if img.size[0] != n:
        img.scale(n, n)
    rgba = np.ones((n, n, 4), dtype=np.float32)
    rgba[..., :3] = np.clip(rgb, 0, 1)
    img.pixels.foreach_set(rgba.ravel())
    img.filepath_raw = path
    img.file_format = "PNG"
    img.save()
    return path


def make_textures():
    n = 1024
    if not os.path.exists(TEX + "Lift_Laminate.png"):
        # walnut-ish laminate, grain runs vertically (v), slightly wavy
        warp = _fbm(n, 11, 4, 3)
        x = np.linspace(0, 1, n)[None, :].repeat(n, 0)
        rings = np.sin((x * 70 + warp * 5.0) * math.tau) * 0.5 + 0.5
        fine = _fbm(n, 21, 6, 32)
        pores = _noise(n, 512, 23)
        tone = 0.35 * rings ** 2 + 0.45 * fine + 0.2 * pores
        dark = np.array([0.17, 0.085, 0.04]); light = np.array([0.36, 0.19, 0.09])
        rgb = dark + (light - dark) * tone[..., None]
        # wear: lighter scuffed band low down and a few scratches
        scratches = (_noise(n, 300, 5) > 0.985).astype(float)
        rgb = rgb * (1 - 0.15 * _fbm(n, 7, 3, 2)[..., None]) + scratches[..., None] * 0.12
        _save("Lift_Laminate", rgb)
    if not os.path.exists(TEX + "Lift_Linoleum.png"):
        base = np.array([0.20, 0.11, 0.07])
        speck = (_noise(n, 400, 3) > 0.8).astype(float) * 0.05
        dirt = _fbm(n, 33, 5, 4)
        rgb = base[None, None, :] * (0.65 + 0.6 * dirt[..., None]) + speck[..., None]
        # worn path in the middle
        y = np.linspace(-1, 1, n)[:, None]; xx = np.linspace(-1, 1, n)[None, :]
        wear = np.exp(-(xx ** 2 + y ** 2) * 2.5)
        rgb = rgb * (1 - 0.25 * wear[..., None]) + 0.05 * wear[..., None]
        _save("Lift_Linoleum", rgb)
    if not os.path.exists(TEX + "Lift_DoorPaint.png"):
        # warm grey oil paint over steel: mottled, grimy low down, vertical streaks, chips to metal
        base = np.array([0.33, 0.31, 0.27])
        mott = _fbm(n, 41, 5, 6)
        y = np.linspace(0, 1, n)[:, None].repeat(n, 1)          # row 0 = bottom after Blender flips
        grime = np.clip(1.0 - y * 3.0, 0, 1) * _fbm(n, 43, 4, 8)
        streak = _noise(n, 60, 47)[:, :1].repeat(n, 1).T * _fbm(n, 49, 3, 3)
        rgb = base * (0.8 + 0.35 * mott[..., None])
        rgb = rgb * (1 - 0.55 * grime[..., None]) * (1 - 0.25 * streak[..., None])
        chips = (_fbm(n, 51, 5, 10) > 0.72).astype(float)[..., None]
        rgb = rgb * (1 - chips) + chips * np.array([0.12, 0.10, 0.09])
        scratches = (_noise(n, 500, 53) > 0.99).astype(float)[..., None]
        rgb = rgb + scratches * 0.15
        _save("Lift_DoorPaint", rgb)


# ---------------------------------------------------------------- materials
def mat_pbr(name, color_path=None, normal_path=None, tint=(1, 1, 1), rough=0.6, metal=0.0, scale=1.0, emit=None):
    m = bpy.data.materials.get(name) or bpy.data.materials.new(name)
    m.use_nodes = True
    nt = m.node_tree; nt.nodes.clear()
    out = nt.nodes.new("ShaderNodeOutputMaterial")
    bs = nt.nodes.new("ShaderNodeBsdfPrincipled")
    nt.links.new(bs.outputs[0], out.inputs[0])
    bs.inputs["Roughness"].default_value = rough
    bs.inputs["Metallic"].default_value = metal
    bs.inputs["Base Color"].default_value = (*tint, 1)
    if color_path:
        t = nt.nodes.new("ShaderNodeTexImage")
        t.image = bpy.data.images.load(color_path, check_existing=True)
        if tint != (1, 1, 1):
            mix = nt.nodes.new("ShaderNodeMix"); mix.data_type = "RGBA"; mix.blend_type = "MULTIPLY"
            mix.inputs["Factor"].default_value = 1.0
            nt.links.new(t.outputs[0], mix.inputs["A"])
            mix.inputs["B"].default_value = (*tint, 1)
            nt.links.new(mix.outputs["Result"], bs.inputs["Base Color"])
        else:
            nt.links.new(t.outputs[0], bs.inputs["Base Color"])
    if normal_path:
        t = nt.nodes.new("ShaderNodeTexImage")
        t.image = bpy.data.images.load(normal_path, check_existing=True)
        t.image.colorspace_settings.name = "Non-Color"
        nm = nt.nodes.new("ShaderNodeNormalMap")
        nt.links.new(t.outputs[0], nm.inputs["Color"])
        nt.links.new(nm.outputs[0], bs.inputs["Normal"])
    if emit:
        bs.inputs["Emission Color"].default_value = (*emit[0], 1)
        bs.inputs["Emission Strength"].default_value = emit[1]
    return m


def materials():
    make_textures()
    M = {}
    M["laminate"] = mat_pbr("Lift_Laminate", TEX + "Lift_Laminate.png", rough=0.45)
    M["lino"] = mat_pbr("Lift_Linoleum", TEX + "Lift_Linoleum.png", rough=0.7)
    M["door"] = mat_pbr("Lift_DoorPaint", TEX + "Lift_DoorPaint.png",
                         ENV + "Metal021/Metal021_NormalGL.jpg", rough=0.5, metal=0.1)
    M["alu"] = mat_pbr("Lift_Aluminium", ENV + "Metal021/Metal021_Color.jpg", ENV + "Metal021/Metal021_NormalGL.jpg",
                        tint=(0.7, 0.7, 0.68), rough=0.35, metal=1.0)
    M["rubber"] = mat_pbr("Lift_Rubber", tint=(0.02, 0.02, 0.02), rough=0.9)
    M["panel"] = mat_pbr("Lift_PanelPlate", ENV + "Metal021/Metal021_Color.jpg", tint=(0.35, 0.35, 0.33), rough=0.4, metal=1.0)
    M["button"] = mat_pbr("Lift_Button", tint=(0.03, 0.03, 0.03), rough=0.3)
    M["button_red"] = mat_pbr("Lift_ButtonRed", tint=(0.6, 0.02, 0.01), rough=0.3)
    M["label"] = mat_pbr("Lift_Label", tint=(0.85, 0.85, 0.8), rough=0.5)
    M["lamp"] = mat_pbr("Lift_Plafond", tint=(0.9, 0.95, 0.9), rough=0.2, emit=((0.85, 1.0, 0.9), 6.0))
    M["lamp_ind"] = mat_pbr("Lift_Indicator", tint=(1.0, 0.3, 0.05), emit=((1.0, 0.25, 0.02), 8.0))
    M["green_wall"] = mat_pbr("Landing_GreenPaint", ENV + "PaintedPlaster011/PaintedPlaster011_Color.jpg",
                              ENV + "PaintedPlaster011/PaintedPlaster011_NormalGL.jpg", tint=(0.22, 0.42, 0.30), rough=0.5)
    M["white_wall"] = mat_pbr("Landing_Whitewash", ENV + "Plaster007/Plaster007_Color.jpg",
                              ENV + "Plaster007/Plaster007_NormalGL.jpg", tint=(0.75, 0.73, 0.66), rough=0.9)
    M["tiles"] = mat_pbr("Landing_Tiles", ENV + "Tiles110/Tiles110_Color.jpg", ENV + "Tiles110/Tiles110_NormalGL.jpg",
                         tint=(0.75, 0.6, 0.5), rough=0.6)
    M["concrete"] = mat_pbr("Shaft_Concrete", ENV + "PaintedPlaster015/PaintedPlaster015_Color.jpg",
                            ENV + "PaintedPlaster015/PaintedPlaster015_NormalGL.jpg", tint=(0.25, 0.25, 0.25), rough=0.95)
    M["rust"] = mat_pbr("Shaft_Rust", ENV + "Rust002/Rust002_Color.jpg", ENV + "Rust002/Rust002_NormalGL.jpg", rough=0.8, metal=0.4)
    M["void"] = mat_pbr("Stair_Dark", tint=(0.01, 0.01, 0.01), rough=1.0)
    M["leak"] = mat_pbr("Shaft_LightLeak", tint=(0.0, 0.0, 0.0), emit=((1.0, 0.75, 0.45), 25.0))
    return M


# ---------------------------------------------------------------- geometry helpers
def _col(name, parent=None):
    c = bpy.data.collections.get(name)
    if not c:
        c = bpy.data.collections.new(name)
        (parent or bpy.context.scene.collection).children.link(c)
    return c


def _box_uv(bm, tile):
    uv = bm.loops.layers.uv.verify()
    for f in bm.faces:
        n = f.normal
        ax = max(range(3), key=lambda i: abs(n[i]))
        for l in f.loops:
            co = l.vert.co
            if ax == 0:   u, v = co.y, co.z
            elif ax == 1: u, v = co.x, co.z
            else:         u, v = co.x, co.y
            l[uv].uv = (u / tile, v / tile)


def box(name, center, size, mat, col, parent=None, tile=1.0, bevel=0.0):
    me = bpy.data.meshes.new(name)
    bm = bmesh.new()
    bmesh.ops.create_cube(bm, size=1.0)
    for v in bm.verts:
        v.co = Vector((v.co.x * size[0], v.co.y * size[1], v.co.z * size[2])) + Vector(center)
    if bevel:
        bmesh.ops.bevel(bm, geom=list(bm.edges), offset=bevel, segments=2, affect="EDGES")
    bm.normal_update()
    _box_uv(bm, tile)
    bm.to_mesh(me); bm.free()
    ob = bpy.data.objects.new(name, me)
    me.materials.append(mat)
    col.objects.link(ob)
    if parent:
        ob.parent = parent
    return ob


def cyl(name, center, radius, depth, mat, col, parent=None, axis="X", verts=24):
    me = bpy.data.meshes.new(name)
    bm = bmesh.new()
    bmesh.ops.create_cone(bm, cap_ends=True, segments=verts, radius1=radius, radius2=radius, depth=depth)
    rot = {"X": (0, math.pi / 2, 0), "Y": (math.pi / 2, 0, 0), "Z": (0, 0, 0)}[axis]
    from mathutils import Euler
    bmesh.ops.rotate(bm, verts=bm.verts, matrix=Euler(rot).to_matrix())
    bmesh.ops.translate(bm, verts=bm.verts, vec=Vector(center))
    _box_uv(bm, 0.1)
    bm.to_mesh(me); bm.free()
    ob = bpy.data.objects.new(name, me)
    me.materials.append(mat)
    col.objects.link(ob)
    if parent:
        ob.parent = parent
    return ob


def text(name, body, loc, rot, size, mat, col, parent=None, extrude=0.0008):
    cu = bpy.data.curves.new(name, "FONT")
    cu.body = body; cu.size = size; cu.extrude = extrude
    cu.align_x = "CENTER"; cu.align_y = "CENTER"
    for f in ("C:/Windows/Fonts/arialbd.ttf", "C:/Windows/Fonts/arial.ttf"):
        if os.path.exists(f):
            cu.font = bpy.data.fonts.load(f, check_existing=True); break
    tmp = bpy.data.objects.new(name + "_tmp", cu)
    col.objects.link(tmp)
    dg = bpy.context.evaluated_depsgraph_get()
    me = bpy.data.meshes.new_from_object(tmp.evaluated_get(dg))
    bpy.data.objects.remove(tmp); bpy.data.curves.remove(cu)
    ob = bpy.data.objects.new(name, me)
    me.materials.clear(); me.materials.append(mat)
    ob.location = loc; ob.rotation_euler = rot
    col.objects.link(ob)
    if parent:
        ob.parent = parent
    return ob


# ---------------------------------------------------------------- build
def clear(names=("Lift", "Landing")):
    for n in names:
        c = bpy.data.collections.get(n)
        if c:
            for ob in list(c.all_objects):
                bpy.data.objects.remove(ob, do_unlink=True)
            for ch in list(c.children):
                bpy.data.collections.remove(ch)
            bpy.data.collections.remove(c)
    for blk in (bpy.data.meshes, bpy.data.lights):
        for d in list(blk):
            if d.users == 0:
                blk.remove(d)


def build_cab(M):
    col = _col("Lift")
    cab = bpy.data.objects.new("Cab", None)
    cab.empty_display_type = "CUBE"; cab.empty_display_size = 0.1
    col.objects.link(cab)
    t = 0.03
    xm, y0, y1, h = CAB_X, CAB_Y0, CAB_Y1, CAB_H
    depth = y1 - y0
    # shell
    box("Cab_Floor", (0, (y0 + y1) / 2, -0.04), (2 * xm + 2 * t, depth + 0.1, 0.08), M["lino"], col, cab, tile=1.0)
    box("Cab_Ceiling", (0, (y0 + y1) / 2, h + 0.02), (2 * xm + 2 * t, depth + 0.1, 0.04), M["laminate"], col, cab, tile=1.2)
    box("Cab_Wall_Back", (0, y1 + t / 2, h / 2), (2 * xm + 2 * t, t, h), M["laminate"], col, cab, tile=1.4)
    box("Cab_Wall_L", (-xm - t / 2, (y0 + y1) / 2, h / 2), (t, depth, h), M["laminate"], col, cab, tile=1.4)
    box("Cab_Wall_R", (xm + t / 2, (y0 + y1) / 2, h / 2), (t, depth, h), M["laminate"], col, cab, tile=1.4)
    # front wall: two jambs + lintel, extended outward as the door pocket the leaves slide behind
    jw = 0.9 - DOOR_HALF
    for s in (-1, 1):
        box("Cab_Front_" + ("L" if s < 0 else "R"), (s * (DOOR_HALF + jw / 2), y0 - t / 2, h / 2), (jw, t, h), M["door"], col, cab, tile=0.8)
    box("Cab_Front_Lintel", (0, y0 - t / 2, (DOOR_H + h) / 2), (2 * DOOR_HALF, t, h - DOOR_H), M["door"], col, cab, tile=0.8)
    # door threshold (aluminium sill with groove look)
    box("Cab_Sill", (0, y0 - 0.03, 0.005), (2 * DOOR_HALF + 0.1, 0.09, 0.012), M["alu"], col, cab, tile=0.3)
    # aluminium trim strips: corners and a horizontal rail band on side/back walls
    for s in (-1, 1):
        box("Trim_CornerBack_%d" % s, (s * (xm - 0.008), y1 - 0.008, h / 2), (0.016, 0.016, h), M["alu"], col, cab)
        box("Trim_CornerFront_%d" % s, (s * (xm - 0.008), y0 + 0.008, h / 2), (0.016, 0.016, h), M["alu"], col, cab)
        box("Trim_Band_%d" % s, (s * (xm - 0.004), (y0 + y1) / 2, 0.95), (0.008, depth, 0.03), M["alu"], col, cab)
        box("Trim_Kick_%d" % s, (s * (xm - 0.006), (y0 + y1) / 2, 0.06), (0.012, depth, 0.12), M["alu"], col, cab, tile=0.3)
    box("Trim_Band_Back", (0, y1 - 0.004, 0.95), (2 * xm, 0.008, 0.03), M["alu"], col, cab)
    box("Trim_Kick_Back", (0, y1 - 0.006, 0.06), (2 * xm, 0.012, 0.12), M["alu"], col, cab, tile=0.3)
    # ceiling plafond: frosted round lamp in a ring
    cyl("Plafond_Ring", (0, 0.02, h - 0.012), 0.17, 0.025, M["alu"], col, cab, axis="Z", verts=40)
    cyl("Plafond_Glass", (0, 0.02, h - 0.03), 0.15, 0.02, M["lamp"], col, cab, axis="Z", verts=40)
    # ventilation slots on the ceiling
    for i in range(6):
        box("Vent_%d" % i, (0, y1 - 0.12 - i * 0.03, h - 0.002), (0.3, 0.012, 0.004), M["button"], col, cab)

    # control panel on the right wall near the door
    px = xm - 0.006
    pz, py = 1.18, y0 + 0.17
    box("Panel_Plate", (px, py, pz), (0.012, 0.15, 0.48), M["panel"], col, cab, tile=0.2, bevel=0.003)
    rot = (math.pi / 2, 0, -math.pi / 2)    # text facing -X (into the cab)
    for i in range(9):
        floor = i + 1
        colm, row = i % 2, i // 2
        y = py - 0.035 + colm * 0.07
        z = pz + 0.17 - row * 0.065
        cyl("Btn_%d" % floor, (px - 0.01, y, z), 0.016, 0.014, M["button"], col, cab, axis="X")
        text("BtnTxt_%d" % floor, str(floor), (px - 0.018, y, z), rot, 0.02, M["label"], col, cab)
    cyl("Btn_Stop", (px - 0.01, py, pz - 0.17), 0.019, 0.016, M["button_red"], col, cab, axis="X")
    text("BtnTxt_Stop", "СТОП", (px - 0.007, py, pz - 0.205), rot, 0.014, M["label"], col, cab)
    cyl("Btn_Call", (px - 0.01, py + 0.05, pz - 0.17), 0.012, 0.014, M["button"], col, cab, axis="X")
    cyl("Indicator", (px - 0.008, py - 0.05, pz - 0.17), 0.006, 0.01, M["lamp_ind"], col, cab, axis="X")
    # capacity plate above the panel
    box("Plate_Capacity", (px, py, pz + 0.37), (0.004, 0.16, 0.09), M["label"], col, cab)
    text("Plate_Text", "ГРУЗОПОДЪЁМНОСТЬ\n320 кг\n4 человека", (px - 0.0035, py, pz + 0.37), rot, 0.014, M["button"], col, cab)

    # cab door leaves (keyed in animate.py), each with a rubber leading edge
    for s, nm in ((-1, "L"), (1, "R")):
        leaf = box("CabDoor_" + nm, (0, 0, 0), (LEAF_W, 0.02, DOOR_H), M["door"], col, cab, tile=0.8)
        leaf.data.transform(__import__("mathutils").Matrix.Translation((s * LEAF_W / 2, 0, DOOR_H / 2)))
        leaf.location = (0, CAB_DOOR_Y, 0)
        edge = box("CabDoor_%s_Rubber" % nm, (s * 0.006, 0, DOOR_H / 2), (0.012, 0.03, DOOR_H), M["rubber"], col, leaf)
    # lights
    ld = bpy.data.lights.new("Cab_Light", "AREA"); ld.shape = "DISK"; ld.size = 0.3
    ld.energy = 45; ld.color = (0.85, 1.0, 0.88)
    lo = bpy.data.objects.new("Cab_Light", ld); lo.location = (0, 0.02, h - 0.05); lo.parent = cab
    col.objects.link(lo)
    return cab


def build_landing(M):
    col = _col("Landing")
    # shaft front wall with the opening
    wy = (SHAFT_WALL_Y0 + SHAFT_WALL_Y1) / 2; wt = SHAFT_WALL_Y0 - SHAFT_WALL_Y1
    W, H = 1.6, 2.7
    for s in (-1, 1):
        cx = s * (DOOR_HALF + (W - DOOR_HALF) / 2); sx = W - DOOR_HALF
        box("ShaftWall_Lower_%d" % s, (cx, wy, 0.75), (sx, wt, 1.5), M["green_wall"], col, tile=1.5)
        box("ShaftWall_Upper_%d" % s, (cx, wy, 1.5 + (H - 1.5) / 2), (sx, wt, H - 1.5), M["white_wall"], col, tile=1.5)
    box("ShaftWall_Lintel", (0, wy, (DOOR_H + H) / 2), (2 * DOOR_HALF, wt, H - DOOR_H), M["white_wall"], col, tile=1.5)
    # steel door frame of the shaft door
    for s in (-1, 1):
        box("ShaftFrame_%d" % s, (s * (DOOR_HALF + 0.03), SHAFT_WALL_Y1 - 0.01, DOOR_H / 2), (0.06, 0.03, DOOR_H + 0.06), M["door"], col)
    box("ShaftFrame_Top", (0, SHAFT_WALL_Y1 - 0.01, DOOR_H + 0.03), (2 * DOOR_HALF + 0.12, 0.03, 0.06), M["door"], col)
    box("Landing_Sill", (0, SHAFT_WALL_Y0 - 0.06, 0.005), (2 * DOOR_HALF + 0.1, 0.12, 0.012), M["alu"], col, tile=0.3)
    # call button + floor number plate beside the door (outside)
    box("Call_Plate", (DOOR_HALF + 0.22, SHAFT_WALL_Y1 - 0.004, 1.1), (0.08, 0.006, 0.14), M["panel"], col)
    cyl("Call_Button", (DOOR_HALF + 0.22, SHAFT_WALL_Y1 - 0.012, 1.1), 0.015, 0.012, M["button"], col, axis="Y")
    text("Floor_Number", "7", (DOOR_HALF + 0.22, SHAFT_WALL_Y1 - 0.004, 2.25), (math.pi / 2, 0, math.pi), 0.22, M["white_wall"], col, extrude=0.004).data.materials[0] = M["button_red"]
    # shaft doors (move with the cab doors while the cab is here)
    for s, nm in ((-1, "L"), (1, "R")):
        leaf = box("ShaftDoor_" + nm, (0, 0, 0), (LEAF_W, 0.02, DOOR_H), M["door"], col, tile=0.8)
        leaf.data.transform(__import__("mathutils").Matrix.Translation((s * LEAF_W / 2, 0, DOOR_H / 2)))
        leaf.location = (0, SHAFT_DOOR_Y, 0)
    # the shaft itself around the cab: concrete, tall, with rails
    sx, sy0, sy1 = 0.80, SHAFT_WALL_Y0, 0.85
    z0, z1 = -1.0, 14.0
    for s in (-1, 1):
        box("Shaft_Side_%d" % s, (s * (sx + 0.05), (sy0 + sy1) / 2, (z0 + z1) / 2), (0.1, sy1 - sy0, z1 - z0), M["concrete"], col, tile=2.0)
        box("Shaft_Rail_%d" % s, (s * (CAB_X + 0.12), 0.0, (z0 + z1) / 2), (0.03, 0.05, z1 - z0), M["rust"], col, tile=0.5)
    box("Shaft_Back", (0, sy1 + 0.05, (z0 + z1) / 2), (2 * sx + 0.2, 0.1, z1 - z0), M["concrete"], col, tile=2.0)
    # shaft front above / below this floor (concrete behind the landing wall)
    box("Shaft_Front_Above", (0, SHAFT_WALL_Y0 + 0.01, (H + z1) / 2), (2 * sx, 0.02, z1 - H), M["concrete"], col, tile=2.0)
    box("Shaft_Front_Below", (0, SHAFT_WALL_Y0 + 0.01, (z0 - 0.1) / 2), (2 * sx, 0.02, -z0 - 0.1), M["concrete"], col, tile=2.0)
    for fl in range(1, 4):   # landing door outlines of the floors above, seen sliding past the gap
        z = fl * 2.8
        box("Shaft_UpperDoor_%d" % fl, (0, SHAFT_WALL_Y0 + 0.025, z + DOOR_H / 2), (2 * DOOR_HALF + 0.04, 0.02, DOOR_H), M["door"], col, tile=0.8)
        box("Shaft_UpperSill_%d" % fl, (0, SHAFT_WALL_Y0 + 0.03, z), (2 * DOOR_HALF + 0.1, 0.06, 0.03), M["alu"], col)
        # landing light leaking through that floor's door seam and sill
        box("Shaft_UpperLeak_%d" % fl, (0, SHAFT_WALL_Y0 + 0.037, z + DOOR_H / 2), (0.01, 0.004, DOOR_H), M["leak"], col)
        box("Shaft_UpperLeakSill_%d" % fl, (0, SHAFT_WALL_Y0 + 0.037, z + 0.02), (2 * DOOR_HALF, 0.004, 0.012), M["leak"], col)
    # landing room in front of the lift
    LX0, LX1, LY = -2.2, 1.6, -3.4
    box("Landing_Floor", ((LX0 + LX1) / 2, (SHAFT_WALL_Y1 + LY) / 2, -0.05), (LX1 - LX0, SHAFT_WALL_Y1 - LY, 0.1), M["tiles"], col, tile=0.6)
    box("Landing_Ceiling", ((LX0 + LX1) / 2, (SHAFT_WALL_Y1 + LY) / 2, H + 0.05), (LX1 - LX0, SHAFT_WALL_Y1 - LY, 0.1), M["white_wall"], col, tile=2)
    for nm, c, sz in (("Landing_Wall_Far", ((LX0 + LX1) / 2, LY - 0.05, 0), (LX1 - LX0, 0.1, 0)),
                      ("Landing_Wall_R", (LX1 + 0.05, (SHAFT_WALL_Y1 + LY) / 2, 0), (0.1, SHAFT_WALL_Y1 - LY, 0))):
        box(nm + "_Low", (c[0], c[1], 0.75), (sz[0], sz[1], 1.5), M["green_wall"], col, tile=1.5)
        box(nm + "_High", (c[0], c[1], 1.5 + (H - 1.5) / 2), (sz[0], sz[1], H - 1.5), M["white_wall"], col, tile=1.5)
    # left side opens to the stairwell (the infected comes from there); dark stair void
    box("Landing_Wall_L_Pier", (LX0 - 0.05, SHAFT_WALL_Y1 - 0.25, H / 2), (0.1, 0.5, H), M["green_wall"], col, tile=1.5)
    box("Landing_Wall_L_Far", (LX0 - 0.05, LY + 0.3, H / 2), (0.1, 0.6, H), M["green_wall"], col, tile=1.5)
    box("Stair_Void", (LX0 - 1.2, (SHAFT_WALL_Y1 + LY) / 2, H / 2), (2.0, SHAFT_WALL_Y1 - LY + 1, H + 2), M["void"], col)
    for i in range(6):   # a few steps going down into the dark
        box("Stair_Step_%d" % i, (LX0 - 0.15 - i * 0.28, -2.1, -0.075 - i * 0.15), (0.3, 1.4, 0.15), M["concrete"], col, tile=1)
    # wall bench light: bare bulb
    cyl("Bulb_Socket", (-0.3, -2.1, H - 0.05), 0.025, 0.08, M["button"], col, axis="Z")
    ico = bpy.data.meshes.new("Bulb_Glass"); bm = bmesh.new(); bmesh.ops.create_uvsphere(bm, u_segments=16, v_segments=10, radius=0.035); bm.to_mesh(ico); bm.free()
    bulb = bpy.data.objects.new("Bulb_Glass", ico); ico.materials.append(mat_pbr("Landing_Bulb", tint=(1, 0.8, 0.5), emit=((1, 0.7, 0.35), 20)))
    bulb.location = (-0.3, -2.1, H - 0.12); col.objects.link(bulb)
    ld = bpy.data.lights.new("Landing_Light", "POINT"); ld.energy = 40; ld.color = (1, 0.72, 0.42); ld.shadow_soft_size = 0.05
    lo = bpy.data.objects.new("Landing_Light", ld); lo.location = (-0.3, -2.1, H - 0.2); col.objects.link(lo)
    return col


def build():
    clear()
    M = materials()
    cab = build_cab(M)
    build_landing(M)
    sc = bpy.context.scene
    sc.render.fps = 30
    sc.unit_settings.system = "METRIC"
    return cab


if __name__ == "__main__" or True:
    build()
    print("lift built")
