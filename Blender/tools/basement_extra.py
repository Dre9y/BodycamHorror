"""Extra basement pieces for the light fear level (run after propkit.py, basement_textures.py, basement_kit.py and
basement_props.py; see basement_kit for the workflow):

    bake_all(["extra"])

Gate_Grille: a steel grille for the level's sliding shutters, 2.0 m wide (X, centred) and 3.2 m tall (Z from 0), 8 cm
             thick (Y, centred): an angle frame, round bars, flat cross bars, a dented kick plate. The level stretches it
             to each opening's width.
Floor_Drain: a cast iron drain grate in its frame, 40 cm square, its top 6 mm above the floor (origin on the floor).
"""
import bpy, bmesh, math, random
from mathutils import Vector

def build_gate_grille():
    rt = root("Gate_Grille"); f, b, p = role("frame"), role("bars"), role("plate")
    W, H, D = 2.0, 3.2, 0.08
    # frame: a box-section rim
    for x in (-W / 2 + 0.03, W / 2 - 0.03):
        box("Grille_Stile", (0.06, D, H), (x, 0, H / 2), f, rt, bv=0.004)
    for z in (0.03, H - 0.03):
        box("Grille_Rail", (W - 0.12, D, 0.06), (0, 0, z), f, rt, bv=0.004)
    # round bars
    n = 17
    for i in range(n):
        x = -W / 2 + 0.1 + i * (W - 0.2) / (n - 1)
        o = rod("Grille_Bar", (x, 0, 0.06), (x, 0, H - 0.06), 0.013, b, rt, seg=8)
    # flat cross bars, front and back, with rivets where they cross the bars
    for z in (0.95, 1.95, 2.75):
        for y in (-0.022, 0.022):
            box("Grille_Flat", (W - 0.12, 0.012, 0.05), (0, y, z), f, rt, bv=0.002)
        for i in range(0, n, 2):
            x = -W / 2 + 0.1 + i * (W - 0.2) / (n - 1)
            sphere("Grille_Rivet", 0.009, (x, -0.03, z), b, rt, 8, 5)
    # kick plate at the foot, dented
    o = box("Grille_Plate", (W - 0.12, 0.006, 0.55), (0, -0.03, 0.06 + 0.275), p, rt, bv=0)
    subdivide(o, 10)
    rnd = random.Random(4)
    for _ in range(5):
        dent(o, (rnd.uniform(-0.8, 0.8), 0, rnd.uniform(-0.2, 0.2)), rnd.uniform(0.08, 0.16), rnd.uniform(0.004, 0.012))
    jitter(o, 0.002, 6, 2)
    return rt

def build_floor_drain():
    rt = root("Floor_Drain"); c, d = role("iron"), role("dark")
    S = 0.4
    for i in range(4):
        a = i * math.pi / 2
        o = box("Drain_Frame", (S, 0.04, 0.012), (0, 0, 0.0), c, rt, bv=0.003)
        o.location = Vector((math.sin(a) * (S / 2 - 0.02), -math.cos(a) * (S / 2 - 0.02), 0.0));
        o.rotation_euler = (0, 0, a)
    box("Drain_Pit", (S - 0.06, S - 0.06, 0.002), (0, 0, 0.001), d, rt, bv=0)   # the dark below, seen between the bars
    for k in range(7):
        y = -0.15 + k * 0.05
        box("Drain_Bar", (S - 0.08, 0.022, 0.012), (0, y, 0.0), c, rt, bv=0.003)
    for x in (-0.09, 0.09):
        box("Drain_Rib", (0.02, S - 0.08, 0.01), (x, 0, -0.008), c, rt, bv=0.002)
    return rt

C_GRILLE = CFG(frame="grey_paint", bars="rust_heavy", plate="pipe_paint")
C_DRAIN = CFG(iron="cast_iron", dark="rust_heavy")
C_DRAIN["dark"]["base"] = dict(C_DRAIN["dark"]["base"], hsv=(0.5, 0.6, 0.15))

GROUPS["extra"] = [
    ("Gate_Grille", build_gate_grille, C_GRILLE, 2048, None, None),
    ("Floor_Drain", build_floor_drain, C_DRAIN, 512, None, None),
]
