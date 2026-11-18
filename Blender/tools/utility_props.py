"""Utility props for BodycamHorror: an oval bulkhead wall lamp (frosted glass under a two-bar grille) and an open
electrical panel (a painted steel box without its door: breakers in two columns, a main switch, a junction box,
a tangle of wires, two cables going up the wall). Run inside Blender over the bridge, after propkit.py and
apartment_props.py (it reuses their helpers):

    exec(open(r"D:\\BodycamHorror\\Blender\\tools\\propkit.py").read())
    exec(open(r"D:\\BodycamHorror\\Blender\\tools\\apartment_props.py").read())
    exec(open(r"D:\\BodycamHorror\\Blender\\tools\\utility_props.py").read())
    build_utility(); bake_utility()      # bakes in the background (Blender/bake_log.txt)
    finish_utility()                     # join, baked material, FBX export, save Props_Utility.blend

Both hang on a wall: the origin is on the wall plane (y = 0), the prop comes out towards -Y (+Z in Unity).
The lamp's glass is kept as its own mesh (WallLamp_Glass) so Unity can make it glow.
"""
import bpy, bmesh, math, os, random
from mathutils import Vector

UTIL_PROPS = ["WallLamp", "ElectricPanel"]

def stadium(w, h, n=10):
    """An oval with straight sides: w across, h tall (h >= w), in x, y."""
    r = w / 2; c = h / 2 - r; pts = []
    for i in range(n + 1):
        a = math.pi * i / n; pts.append((r * math.cos(a), c + r * math.sin(a)))
    for i in range(n + 1):
        a = math.pi + math.pi * i / n; pts.append((r * math.cos(a), -c + r * math.sin(a)))
    return pts

def on_wall(o, y0=0.0):
    """Turns a part built lying in x, y (thickness along +z) up onto the wall: z goes to -y, y goes to z."""
    o.rotation_euler = (math.radians(90), 0, 0)
    o.location.y -= y0
    return o

# ------------------------------------------------------------------ the lamp

def build_lamp():
    rt = root("WallLamp", (0, 0, 0))
    body, glass = role("lampbody"), role("lampglass")
    W, H = 0.16, 0.28
    base = on_wall(slab("Lamp_Base", stadium(W, H), 0.0, 0.034, body, rt)); bevel(base, 0.006, 3)
    for o in (base,): pass
    # the glass dome: a thick stadium with a big rounded edge, standing out of the base
    g = on_wall(slab("Lamp_Glass", stadium(0.124, 0.244, 12), 0.026, 0.058, glass, rt)); bevel(g, 0.014, 5)
    # the grille: a ring round the glass and two bars over it
    ring = [(x, -0.06, y) for x, y in stadium(0.13, 0.25, 12)]
    ring = [(x, z, y) for x, z, y in ring]
    tube("Lamp_GrilleRing", [(x, -0.052, y) for (x, _, y) in ring], 0.0035, body, rt, cyclic=True)
    for zb in (-0.045, 0.045):
        pts = []
        for i in range(13):
            t = i / 12; x = -0.064 + 0.128 * t
            pts.append((x, -0.052 - 0.011 * math.sin(math.pi * t), zb))
        tube("Lamp_Bar", pts, 0.0028, body, rt)
    for zs in (-0.115, 0.115):
        cyl("Lamp_Screw", 0.005, 0.0045, 0.004, (0, -0.035, zs), body, rt, seg=12, axis=(0, -1, 0))
    return rt

CFG_LAMP = {
    "lampbody": dict(base=((0.62, 0.62, 0.56), (0.52, 0.52, 0.46)), rough=0.55, metal=0.0, chips=0.3, chip_scale=18,
                     wear=((0.3, 0.27, 0.22), 0.8, 0.0, 0.8), dirt=((0.12, 0.1, 0.06), 0.9, 0.0, 1.0), dust=0.6, bottom=0.0,
                     edge_radius=0.003, ao_dist=0.03),
    "lampglass": dict(base=((0.93, 0.95, 0.92), (0.86, 0.88, 0.84)), rough=0.45, metal=0.0,
                      wear=((0.8, 0.8, 0.75), 0.5, 0.0, 0.2), dirt=((0.42, 0.38, 0.28), 0.8, 0.0, 0.8), dust=0.5,
                      edge_radius=0.004, ao_dist=0.02, bump=0.05),
}

# ------------------------------------------------------------------ the panel

def build_panel():
    rt = root("ElectricPanel", (1.0, 0, 0))
    out, inn, brk, metal, junc = role("panel_out"), role("panel_in"), role("breaker"), role("busmetal"), role("junction")
    wires = {"grey": role("wire_grey"), "black": role("wire_black"), "red": role("wire_red"),
             "yellow": role("wire_yellow"), "white": role("wire_white")}
    W, H, D, T = 0.36, 0.56, 0.13, 0.003
    # the outer shell, painted dark: back, four sides
    box("Panel_Back", (W, T, H), (0, -T / 2, 0), out, rt, bv=0)
    for sx in (-1, 1): box("Panel_Side", (T, D, H), (sx * (W / 2 - T / 2), -D / 2, 0), out, rt, bv=0.001)
    for sz in (-1, 1): box("Panel_TopBot", (W, D, T), (0, -D / 2, sz * (H / 2 - T / 2)), out, rt, bv=0.001)
    # the white-painted inside: a liner a hair inside the shell, and the flanged rim round the opening
    L = 0.0015
    box("Panel_InBack", (W - 2 * T, L, H - 2 * T), (0, -T - L / 2, 0), inn, rt, bv=0)
    for sx in (-1, 1): box("Panel_InSide", (L, D - T - 0.002, H - 2 * T), (sx * (W / 2 - T - L / 2), -T - (D - T) / 2, 0), inn, rt, bv=0)
    for sz in (-1, 1): box("Panel_InTopBot", (W - 2 * T, D - T - 0.002, L), (0, -T - (D - T) / 2, sz * (H / 2 - T - L / 2)), inn, rt, bv=0)
    F = 0.016
    for sx in (-1, 1): box("Panel_Flange", (F, 0.004, H), (sx * (W / 2 - F / 2), -D + 0.002, 0), inn, rt, bv=0.0012)
    for sz in (-1, 1): box("Panel_Flange", (W, 0.004, F), (0, -D + 0.002, sz * (H / 2 - F / 2)), inn, rt, bv=0.0012)
    # a mounting plate, the busbar between the two columns of breakers, the breakers
    box("Panel_Plate", (0.2, 0.004, 0.38), (0.01, -0.012, -0.03), inn, rt, bv=0.001)
    box("Panel_Busbar", (0.012, 0.012, 0.3), (0.01, -0.02, -0.06), metal, rt, bv=0.001)
    rng = random.Random(9)
    terminals = []   # where wires end: (x, y, z, side)
    for col, cx in ((-1, -0.035), (1, 0.055)):
        for i in range(13):
            z = 0.075 - i * 0.021
            box("Panel_Breaker", (0.07, 0.042, 0.019), (cx, -0.035, z), brk, rt, bv=0.0015)
            lever = box("Panel_Lever", (0.012, 0.012, 0.008), (cx + col * 0.008, -0.058, z + rng.choice((-0.003, 0.003))), brk, rt, bv=0.001)
            terminals.append((cx - col * 0.04, -0.04, z, col))
    # the main switch at the top, a junction box at the bottom left
    box("Panel_Main", (0.13, 0.06, 0.075), (0.01, -0.04, 0.155), brk, rt, bv=0.004)
    box("Panel_MainHandle", (0.03, 0.03, 0.04), (0.04, -0.08, 0.16), brk, rt, bv=0.003).rotation_euler = (math.radians(20), 0, 0)
    for x in (-0.025, 0.0, 0.025, 0.045):
        cyl("Panel_MainScrew", 0.004, 0.004, 0.004, (x, -0.071, 0.135), metal, rt, seg=10, axis=(0, -1, 0))
    box("Panel_Junction", (0.05, 0.035, 0.06), (-0.12, -0.115, -0.215), junc, rt, bv=0.005).rotation_euler = (0, math.radians(-12), math.radians(8))
    # wires: a bundle in from the top left, down the left side and across to the left column's terminals,
    # a few down the right side to the right column, an earth along the bottom, loops hanging out
    kinds = ["grey", "grey", "black", "grey", "white", "grey", "black", "grey", "red", "grey", "grey", "black", "white"]
    for i, (tx, ty, tz, col) in enumerate(terminals):
        if rng.random() < 0.18: continue
        kind = kinds[i % len(kinds)] if rng.random() < 0.85 else rng.choice(list(wires))
        side = -0.155 + rng.uniform(0, 0.03) if col < 0 else 0.15 - rng.uniform(0, 0.03)
        ex = (-0.13 if col < 0 else 0.13) + rng.uniform(-0.02, 0.02)
        depth = -0.02 - rng.uniform(0, 0.06)
        pts = [(ex, -0.03 - rng.uniform(0, 0.03), H / 2 + 0.02), (ex + rng.uniform(-0.01, 0.01), depth, H / 2 - 0.05),
               (side, depth - rng.uniform(0, 0.02), 0.12), (side + rng.uniform(-0.01, 0.01), depth, tz + 0.03),
               ((side + tx) * 0.5, -0.05 - rng.uniform(0, 0.025), tz + rng.uniform(-0.01, 0.01)), (tx, ty, tz)]
        tube("Panel_Wire", [tuple(p) for p in smooth_path(pts, 22)], 0.0022, wires[kind], rt)
    for k in range(4):   # slack hanging below the breakers
        x0 = -0.14 + k * 0.025
        pts = [(x0, -0.05, -0.12), (x0 + 0.01, -0.07 - k * 0.01, -0.2), (x0 + 0.05, -0.06, -0.24), (x0 + 0.12, -0.04, -0.2)]
        tube("Panel_Slack", [tuple(p) for p in smooth_path(pts, 16)], 0.0022, wires[["red", "grey", "yellow", "black"][k]], rt)
    pts = [(-0.165, -0.01, -0.255), (0.0, -0.012, -0.258), (0.12, -0.02, -0.25), (0.15, -0.04, -0.15), (0.13, -0.045, -0.1)]
    tube("Panel_Earth", [tuple(p) for p in smooth_path(pts, 20)], 0.0025, wires["yellow"], rt)
    pts = [(0.15, -0.03, -0.2), (0.14, -0.05, -0.24), (0.05, -0.04, -0.26), (-0.06, -0.03, -0.25), (-0.12, -0.08, -0.2)]
    tube("Panel_Red", [tuple(p) for p in smooth_path(pts, 20)], 0.0022, wires["red"], rt)
    # two cables out of the top, up the wall, and one out of the bottom down it
    for x, top in ((-0.13, 0.9), (-0.09, 0.85)):
        pts = [(x, -0.03, H / 2 - 0.01), (x, -0.025, H / 2 + 0.04), (x + 0.01, -0.008, H / 2 + 0.12), (x + 0.02, -0.005, top)]
        tube("Panel_Cable", [tuple(p) for p in smooth_path(pts, 18)], 0.0045, wires["black"], rt)
    pts = [(0.12, -0.03, -H / 2 + 0.01), (0.12, -0.02, -H / 2 - 0.05), (0.125, -0.006, -H / 2 - 0.15), (0.13, -0.005, -0.9)]
    tube("Panel_CableDown", [tuple(p) for p in smooth_path(pts, 18)], 0.0045, wires["black"], rt)
    return rt

def _wire(col):
    return dict(base=(col, tuple(c * 0.8 for c in col)), rough=0.45, metal=0.0,
                wear=(tuple(min(1, c * 1.2 + 0.05) for c in col), 0.5, 0.0, 0.2), dirt=((0.08, 0.07, 0.05), 0.9, 0.0, 0.8),
                dust=0.5, edge_radius=0.001, ao_dist=0.02)

CFG_PANEL = {
    "panel_out": dict(base=((0.03, 0.033, 0.033), (0.022, 0.024, 0.024)), rough=0.55, metal=0.4, chips=0.35, chip_scale=14, scratch=0.5,
                      wear=((0.25, 0.12, 0.05), 0.8, 0.3, 1.0), dirt=((0.12, 0.06, 0.03), 0.9, 0.2, 0.9), dust=0.5,
                      edge_radius=0.002, ao_dist=0.04),
    "panel_in": dict(base=((0.75, 0.75, 0.72), (0.66, 0.66, 0.62)), rough=0.6, metal=0.0, chips=0.25, chip_scale=16,
                     wear=((0.35, 0.3, 0.25), 0.8, 0.0, 0.8), dirt=((0.2, 0.17, 0.12), 0.9, 0.0, 1.0), dust=0.7, bottom=0.0,
                     edge_radius=0.002, ao_dist=0.06),
    "breaker": dict(base=((0.6, 0.6, 0.58), (0.52, 0.52, 0.5)), rough=0.4, metal=0.0,
                    wear=((0.7, 0.68, 0.62), 0.5, 0.0, 0.3), dirt=((0.18, 0.15, 0.1), 0.85, 0.0, 1.0), dust=0.6,
                    edge_radius=0.0015, ao_dist=0.025),
    "busmetal": dict(base=((0.55, 0.5, 0.42), (0.45, 0.4, 0.33)), rough=0.4, metal=1.0,
                     wear=((0.7, 0.66, 0.58), 0.3, 1.0, 0.4), dirt=((0.15, 0.1, 0.05), 0.9, 0.0, 0.9), dust=0.4, edge_radius=0.001, ao_dist=0.02),
    "junction": dict(base=((0.08, 0.08, 0.08), (0.06, 0.06, 0.06)), rough=0.5, metal=0.0,
                     wear=((0.2, 0.2, 0.19), 0.6, 0.0, 0.5), dirt=((0.1, 0.08, 0.05), 0.9, 0.0, 0.7), dust=0.6, edge_radius=0.002, ao_dist=0.03),
    "wire_grey": _wire((0.4, 0.42, 0.42)), "wire_black": _wire((0.025, 0.025, 0.025)), "wire_red": _wire((0.45, 0.04, 0.03)),
    "wire_yellow": _wire((0.5, 0.42, 0.06)), "wire_white": _wire((0.72, 0.72, 0.68)),
}

# ------------------------------------------------------------------ run

def build_utility():
    clear_scene(); ROLES.clear()
    build_lamp(); build_panel()
    assign_materials("WallLamp", CFG_LAMP); assign_materials("ElectricPanel", CFG_PANEL)
    apply_modifiers()
    return "built: " + ", ".join(f"{p}={len(meshes_of(p))}" for p in UTIL_PROPS)

def bake_utility():
    bake_in_background([("WallLamp", 1024), ("ElectricPanel", 1024)], BAKE_LOG)

def finish_utility():
    out = []
    groups = {"WallLamp": {"WallLamp_Body": None, "WallLamp_Glass": ["Lamp_Glass"]}, "ElectricPanel": None}
    for p in UTIL_PROPS:
        made = finalize(p, groups[p])
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
    bpy.ops.wm.save_as_mainfile(filepath=os.path.join(PROJECT, "Blender", "Props_Utility.blend"))
    return "; ".join(out)
