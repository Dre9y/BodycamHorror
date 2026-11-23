"""Basement kit for BodycamHorror (after the concept: a brick cellar with rusty pipes, a bunker door with a wheel,
a caged lamp, a barred window, barrels, racks, crates and rubbish). Run inside Blender over the bridge:

    exec(open(r"E:\\Unity\\Projects\\BodycamHorror\\Blender\\tools\\propkit.py").read())
    exec(open(r"E:\\Unity\\Projects\\BodycamHorror\\Blender\\tools\\basement_textures.py").read())
    exec(open(r"E:\\Unity\\Projects\\BodycamHorror\\Blender\\tools\\basement_kit.py").read())
    exec(open(r"E:\\Unity\\Projects\\BodycamHorror\\Blender\\tools\\basement_props.py").read())
    build_architecture()          # tiling walls / floor / ceiling -> FBX at once (no bake)
    build_group("pipes")          # or "props_a", "props_b", "trash": meshes + scan-based materials, bakes in background
    finish_group("pipes")         # once Blender/bake_log.txt says DONE: join, baked material, FBX export, .blend

Surfaces sample the Poly Haven scans (basement_textures.py) through a triplanar projection in object space, then
propkit's wear / cavity dirt / dust / floor grime / wet layers go over them, and bake_prop() bakes each prop to its
own URP maps. Metres, Z up, fronts towards -Y (+Z in Unity), origins on the floor under the prop's middle unless said.
"""
import bpy, bmesh, math, os, random, json
import numpy as np
from mathutils import Vector, Matrix, Euler, noise

TEX_OUT = os.path.join(PROJECT, "Assets", "Generated", "Textures", "Basement", "Props")
FBX_OUT = os.path.join(PROJECT, "Assets", "Generated", "Imported", "Basement")
BAKE_LOG = os.path.join(PROJECT, "Blender", "bake_log.txt")
load_sizes()

# ------------------------------------------------------------------ extra mesh helpers

def lathe(name, prof, mat, parent, loc=(0, 0, 0), seg=32, cyclic=False, smooth=True):
    """Revolve a (radius, z) profile about Z. A radius of 0 at either end closes it with a fan."""
    bm = bmesh.new(); rings = []
    for r, z in prof:
        if r < 1e-6: rings.append([bm.verts.new((0, 0, z))])
        else: rings.append([bm.verts.new((r * math.cos(2 * math.pi * i / seg), r * math.sin(2 * math.pi * i / seg), z)) for i in range(seg)])
    pairs = list(zip(rings, rings[1:])) + ([(rings[-1], rings[0])] if cyclic else [])
    for a, b in pairs:
        if len(a) == 1 and len(b) == 1: continue
        for i in range(seg):
            j = (i + 1) % seg
            if len(a) == 1: bm.faces.new((a[0], b[i], b[j])[::-1])
            elif len(b) == 1: bm.faces.new((a[i], a[j], b[0])[::-1])
            else: bm.faces.new((a[i], a[j], b[j], b[i])[::-1])
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    o = from_bm(name, bm, mat, parent); o.location = loc
    for p in o.data.polygons: p.use_smooth = smooth
    return o

def torus(name, R, r, loc, mat, parent, seg=32, rseg=10, axis=None):
    prof = [(R + r * math.cos(2 * math.pi * k / rseg), r * math.sin(2 * math.pi * k / rseg)) for k in range(rseg)]
    o = lathe(name, prof, mat, parent, loc, seg, cyclic=True)
    if axis is not None:
        o.rotation_mode = 'QUATERNION'; o.rotation_quaternion = Vector(axis).normalized().to_track_quat('Z', 'Y')
    return o

def arc(c, r, a0, a1, steps, plane='XZ'):
    """Points on a circle about c from angle a0 to a1 (degrees) in the given plane."""
    c = Vector(c); out = []
    for k in range(steps + 1):
        a = math.radians(a0 + (a1 - a0) * k / steps)
        d = {'XZ': Vector((math.cos(a), 0, math.sin(a))), 'XY': Vector((math.cos(a), math.sin(a), 0)),
             'YZ': Vector((0, math.cos(a), math.sin(a)))}[plane]
        out.append(c + d * r)
    return out

def sweep(name, pts, r, mat, parent, seg=20, caps=True, smooth=True):
    """A round tube along a polyline with parallel-transported rings (no twisting through bends)."""
    pts = [Vector(p) for p in pts]; n = len(pts)
    tans = []
    for i in range(n):
        a = pts[max(i - 1, 0)]; b = pts[min(i + 1, n - 1)]; tans.append((b - a).normalized())
    ref = Vector((0, 0, 1)) if abs(tans[0].z) < 0.9 else Vector((1, 0, 0))
    nrm = (ref - tans[0] * tans[0].dot(ref)).normalized()
    bm = bmesh.new(); rings = []
    for i in range(n):
        t = tans[i]
        nrm = (nrm - t * t.dot(nrm)).normalized(); bi = t.cross(nrm)
        rings.append([bm.verts.new(pts[i] + (nrm * math.cos(2 * math.pi * k / seg) + bi * math.sin(2 * math.pi * k / seg)) * r) for k in range(seg)])
    for a, b in zip(rings, rings[1:]):
        for k in range(seg):
            j = (k + 1) % seg; bm.faces.new((a[k], a[j], b[j], b[k]))
    if caps:
        bm.faces.new(rings[0][::-1]); bm.faces.new(rings[-1])
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    o = from_bm(name, bm, mat, parent)
    for p in o.data.polygons: p.use_smooth = smooth and len(p.vertices) == 4
    return o

def jitter(o, amt, scale=3.0, seed=0.0):
    """Push vertices along their normals by low-frequency noise: dents, sag, hand-made unevenness."""
    me = o.data; me.update()
    off = Vector((seed * 7.1, seed * 3.3, seed * 5.9))
    for v in me.vertices:
        v.co += v.normal * noise.noise(v.co * scale + off) * amt
    return o

def dent(o, centre, radius, depth):
    """A knock: vertices within radius of centre (object space) pushed in along -normal, smoothly."""
    c = Vector(centre); me = o.data; me.update()
    for v in me.vertices:
        d = (v.co - c).length
        if d < radius: v.co -= v.normal * depth * (0.5 + 0.5 * math.cos(math.pi * d / radius))
    return o

def subdivide(o, cuts=1):
    bm = bmesh.new(); bm.from_mesh(o.data)
    bmesh.ops.subdivide_edges(bm, edges=bm.edges, cuts=cuts, use_grid_fill=True)
    bm.to_mesh(o.data); bm.free(); return o

def plank(name, size, loc, mat, parent, rot=None, bv=0.004, warp=0.0, seed=0.0):
    """A board: a bevelled box, cut along its length so it can warp a little."""
    o = box(name, size, loc, mat, parent, bv=0, rot=rot)
    L = max(size); ax = list(size).index(L)
    bm = bmesh.new(); bm.from_mesh(o.data)
    cuts = max(1, int(L / 0.15))
    edges = [e for e in bm.edges if abs((e.verts[0].co - e.verts[1].co)[ax]) > 0.5]
    bmesh.ops.subdivide_edges(bm, edges=edges, cuts=cuts)
    bm.to_mesh(o.data); bm.free()
    if bv: bevel(o, bv, 1)
    if warp:
        for v in o.data.vertices:
            t = v.co[ax] / L
            v.co.z += warp * (noise.noise(Vector((t * 2.0 + seed, seed, 0))) )
    return o

# ------------------------------------------------------------------ scan-based materials

def _tri_weights(s, geo):
    vt = s.n("ShaderNodeVectorTransform"); vt.vector_type = 'NORMAL'; vt.convert_from = 'WORLD'; vt.convert_to = 'OBJECT'
    s.L(geo.outputs["Normal"], vt.inputs["Vector"])
    sep = s.n("ShaderNodeSeparateXYZ"); s.L(s.vmath('ABSOLUTE', vt.outputs["Vector"], (0, 0, 0)), sep.inputs[0])
    p = [s.m('POWER', sep.outputs[i], 6.0) for i in range(3)]
    tot = s.m('ADD', s.m('ADD', p[0], p[1]), p[2])
    return [s.m('DIVIDE', p[i], tot) for i in range(3)]

def _tri_uvs(s, coord, scale, grain):
    sv = s.vmath('DIVIDE', coord, (scale, scale, scale))
    sep = s.n("ShaderNodeSeparateXYZ"); s.L(sv, sep.inputs[0]); x, y, z = (sep.outputs[i] for i in range(3))
    def comb(u, v):
        c = s.n("ShaderNodeCombineXYZ"); s._set(c.inputs[0], u); s._set(c.inputs[1], v); return c.outputs[0]
    if grain:   # wood: the image's U (along the planks) follows the object's longest axis
        gy, gz = s.oattr("gy"), s.oattr("gz")
        fm = lambda f, a, b: s.mix(f, a, b, 'FLOAT')
        return [comb(fm(gz, y, z), fm(gz, z, y)), comb(fm(gz, x, z), fm(gz, z, x)), comb(fm(gy, x, y), fm(gy, y, x))]
    return [comb(y, z), comb(x, z), comb(x, y)]

def _tri_sample(s, tid, key, uvs, w, color):
    im = ph_image(tid, key, non_color=not color)
    if im is None: return None
    outs = []
    for uv in uvs:
        t = s.n("ShaderNodeTexImage"); t.image = im; t.interpolation = 'Linear'; s.L(uv, t.inputs["Vector"])
        outs.append(t.outputs["Color"])
    if color:
        a = s.vmath('MULTIPLY', outs[0], s._vec(w[0])); b = s.vmath('MULTIPLY', outs[1], s._vec(w[1])); c = s.vmath('MULTIPLY', outs[2], s._vec(w[2]))
        return s.vmath('ADD', s.vmath('ADD', a, b), c)
    vals = []
    for o_, wi in zip(outs, w):
        sep = s.n("ShaderNodeSeparateColor"); s.L(o_, sep.inputs[0]); vals.append(s.m('MULTIPLY', sep.outputs[0], wi))
    return s.m('ADD', s.m('ADD', vals[0], vals[1]), vals[2])

def _vec(s, f):
    c = s.n("ShaderNodeCombineXYZ")
    for i in range(3): s._set(c.inputs[i], f)
    return c.outputs[0]
NB._vec = _vec

def _scan(s, coord, w, d):
    """d: tex, scale (m per tile; default the scan's real size), grain, hsv, tint -> (col, rough, disp)"""
    tid = d["tex"]; scale = d.get("scale", PH_SIZE.get(tid, (2, 2))[0])
    uvs = _tri_uvs(s, coord, scale, d.get("grain", False))
    col = _tri_sample(s, tid, "Diffuse", uvs, w, True)
    rough = _tri_sample(s, tid, "Rough", uvs, w, False) or 0.7
    disp = _tri_sample(s, tid, "Displacement", uvs, w, False) or 0.5
    if "hsv" in d:
        hs = s.n("ShaderNodeHueSaturation"); h, sa, v = d["hsv"]
        hs.inputs["Hue"].default_value = h; hs.inputs["Saturation"].default_value = sa; hs.inputs["Value"].default_value = v
        s.L(col, hs.inputs["Color"]); col = hs.outputs["Color"]
    if "repaint" in d:   # recolour the scan's pale paint, keep its rust: paint = scan value x colour
        hv = s.n("ShaderNodeSeparateColor"); hv.mode = 'HSV'; s.L(col, hv.inputs[0])
        sat0 = d.get("repaint_sat", 0.62)   # linear-space saturation: the pale paint sits near 0.3, rust near 0.9
        pm = s.mr(hv.outputs[1], sat0, sat0 - 0.2)
        val = s.m('MULTIPLY', hv.outputs[2], 1.25)
        cv = s.n("ShaderNodeCombineColor")
        for i in range(3): s.L(val, cv.inputs[i])
        newc = s.mix(1.0, cv.outputs[0], d["repaint"], blend='MULTIPLY')
        col = s.mix(pm, col, newc)
        rough = s.mix(pm, rough, d.get("paint_rough", 0.5), 'FLOAT')
        if "rust_hsv" in d:
            hs2 = s.n("ShaderNodeHueSaturation"); h, sa, v = d["rust_hsv"]
            hs2.inputs["Hue"].default_value = h; hs2.inputs["Saturation"].default_value = sa; hs2.inputs["Value"].default_value = v
            s.L(col, hs2.inputs["Color"]); col = s.mix(pm, hs2.outputs["Color"], col)
    if "tint" in d:
        col = s.mix(d.get("tint_amt", 1.0), col, d["tint"], blend='MULTIPLY')
    if "paint" in d:   # flat paint over the scan, broken where the scan is dark (pits, rust) and on noise patches
        lum = s.n("ShaderNodeRGBToBW"); s.L(col, lum.inputs[0])
        keep = s.mr(lum.outputs[0], d.get("paint_cut", 0.18), d.get("paint_cut", 0.18) + 0.08)
        pc = d["paint"]; pcol = s.mix(0.25, pc, col, blend='OVERLAY')
        col = s.mix(s.m('MULTIPLY', keep, d.get("paint_amt", 1.0)), col, pcol)
        rough = s.mix(keep, rough, d.get("paint_rough", 0.55), 'FLOAT')
    if "rough_mul" in d: rough = s.m('MULTIPLY', rough, d["rough_mul"])
    if "rough_add" in d: rough = s.m('ADD', rough, d["rough_add"])
    return col, rough, disp

def phmat(name, c):
    """c: base={tex,...} (see _scan), metal (bare metal where the scan is unrusted), wear={tex|col, amount, metal,
    chips, chip_scale} revealed at edges and chips, streaks (rust runs), dirt, dust, bottom, wet, bump, edge_radius,
    ao_dist, emission. Leaves mat["metal_src"] for bake_prop."""
    mat = bpy.data.materials.new(name); mat.use_nodes = True
    nt = mat.node_tree; nt.nodes.clear(); s = NB(nt)
    out = s.n("ShaderNodeOutputMaterial"); bsdf = s.n("ShaderNodeBsdfPrincipled"); s.L(bsdf.outputs[0], out.inputs["Surface"])
    tc = s.n("ShaderNodeTexCoord"); oi = s.n("ShaderNodeObjectInfo"); geo = s.n("ShaderNodeNewGeometry")
    coord = s.vmath('ADD', tc.outputs["Object"], s.m('MULTIPLY', oi.outputs["Random"], 11.0))
    w = _tri_weights(s, geo)
    col, rough, disp = _scan(s, coord, w, c["base"])
    height = s.m('MULTIPLY', disp, c.get("bump", 1.0))
    # metal only where the scan is not rusty (saturated orange-brown) and not dark
    hsvn = s.n("ShaderNodeSeparateColor"); hsvn.mode = 'HSV'; s.L(col, hsvn.inputs[0])
    metal = s.m('MULTIPLY', c.get("metal", 0.0), s.mr(hsvn.outputs[1], 0.45, 0.2))
    # edges / chips revealing the wear layer
    bev = s.n("ShaderNodeBevel"); bev.samples = 8; bev.inputs["Radius"].default_value = c.get("edge_radius", 0.006)
    edge = s.mr(s.vmath('DOT_PRODUCT', bev.outputs["Normal"], geo.outputs["Normal"]), 0.995, 0.9)
    pos = s.n("ShaderNodeSeparateXYZ"); s.L(geo.outputs["Position"], pos.inputs[0])
    nz = s.n("ShaderNodeSeparateXYZ"); s.L(geo.outputs["Normal"], nz.inputs[0])
    if "wear" in c:
        wd = c["wear"]
        wn = s.mr(s.noise(coord, 30, 6, 0.65), 0.4, 0.62)
        mask = s.m('MULTIPLY', s.m('MULTIPLY', edge, wn), wd.get("amount", 1.0), clamp=True)
        if wd.get("chips"):
            chip = s.mr(s.noise(coord, wd.get("chip_scale", 7.0), 12, 0.72, 0.3), 0.6, 0.63)
            chip = s.m('MULTIPLY', chip, s.mr(s.noise(coord, 1.4, 2), 0.4, 0.62))
            mask = s.m('MAXIMUM', mask, s.m('MULTIPLY', chip, wd["chips"], clamp=True))
        if "tex" in wd:
            wcol, wrough, wdisp = _scan(s, coord, w, wd)
        else:
            wcol, wrough, wdisp = wd["col"], wd.get("rough", 0.8), 0.3
        col = s.mix(mask, col, wcol); rough = s.mix(mask, rough, wrough, 'FLOAT')
        metal = s.mix(mask, metal, wd.get("metal", 0.0), 'FLOAT')
        height = s.mix(mask, height, s.m('SUBTRACT', s.m('MULTIPLY', wdisp, c.get("bump", 1.0)), 0.25), 'FLOAT')
    if c.get("streaks"):   # rust / damp running down: noise stretched along Z, fading upwards from where it starts
        sc = s.vmath('MULTIPLY', coord, (26.0, 26.0, 1.3))
        st = s.mr(s.noise(sc, 1.0, 4, 0.55), 0.55, 0.7)
        st = s.m('MULTIPLY', st, s.mr(s.noise(coord, 2.0, 2), 0.45, 0.65))
        st = s.m('MULTIPLY', st, c["streaks"], clamp=True)
        scol = c.get("streak_col", (0.16, 0.07, 0.025))
        col = s.mix(s.m('MULTIPLY', st, 0.8), col, s.mix(1.0, col, scol, blend='MULTIPLY'))
        col = s.mix(s.m('MULTIPLY', st, 0.35), col, scol)
        rough = s.mix(st, rough, 0.85, 'FLOAT'); metal = s.mix(st, metal, 0.0, 'FLOAT')
    ao = s.n("ShaderNodeAmbientOcclusion"); ao.samples = 16; ao.inputs["Distance"].default_value = c.get("ao_dist", 0.1)
    cav = s.m('SUBTRACT', 1.0, ao.outputs["AO"])
    blotch = s.mr(s.noise(coord, 3.0, 6, 0.6), 0.5, 0.75)
    dirt = s.m('ADD', s.m('MULTIPLY', cav, 1.5), s.m('MULTIPLY', blotch, 0.4))
    if c.get("bottom"):
        dirt = s.m('ADD', dirt, s.m('MULTIPLY', s.m('MULTIPLY', s.mr(pos.outputs["Z"], c.get("bottom_h", 0.35), 0.0),
                                                     s.mr(s.noise(coord, 6, 6, 0.6), 0.3, 0.6)), c["bottom"]))
    dirt = s.m('MULTIPLY', dirt, c.get("dirt", 0.8), clamp=True)
    dcol = c.get("dirt_col", (0.07, 0.055, 0.04))
    col = s.mix(dirt, col, s.mix(0.6, col, dcol, blend='MULTIPLY')); rough = s.mix(dirt, rough, 0.9, 'FLOAT')
    metal = s.mix(dirt, metal, 0.0, 'FLOAT')
    # broad grime: years of damp and soot, darkening whole patches
    grime = s.m('MULTIPLY', s.mr(s.noise(coord, 1.3, 4, 0.6), 0.3, 0.7), c.get("grime", 0.55), clamp=True)
    col = s.mix(grime, col, s.mix(1.0, col, (0.35, 0.32, 0.28), blend='MULTIPLY'))
    dust = s.m('MULTIPLY', s.m('MULTIPLY', s.mr(nz.outputs["Z"], 0.55, 0.95), s.mr(s.noise(coord, 8, 5, 0.6), 0.35, 0.7)),
               c.get("dust", 0.5) * 0.6, clamp=True)
    col = s.mix(dust, col, (0.13, 0.12, 0.105)); rough = s.mix(dust, rough, 0.95, 'FLOAT'); metal = s.mix(dust, metal, 0.0, 'FLOAT')
    if c.get("wet"):
        wet = s.m('MULTIPLY', s.mr(s.noise(coord, 2.0, 4, 0.55), 0.55, 0.63), c["wet"], clamp=True)
        col = s.mix(wet, col, s.mix(1.0, col, (0.5, 0.47, 0.42), blend='MULTIPLY')); rough = s.mix(wet, rough, 0.18, 'FLOAT')
    bump = s.n("ShaderNodeBump"); bump.inputs["Strength"].default_value = c.get("bump_str", 0.6)
    bump.inputs["Distance"].default_value = c.get("bump_dist", 0.004)
    s.L(height, bump.inputs["Height"])
    metal = s.m('ADD', metal, 0.0, clamp=True)
    s.L(col, bsdf.inputs["Base Color"]); s.L(s.m('ADD', rough, 0.0, clamp=True), bsdf.inputs["Roughness"])
    s.L(metal, bsdf.inputs["Metallic"]); s.L(bump.outputs["Normal"], bsdf.inputs["Normal"])
    mat["metal_src"] = metal.node.name
    return mat

def assign(prop, cfg):
    """Like propkit.assign_materials, but a cfg entry with 'base' is a scan material (phmat), else propkit.build."""
    cache = {}
    for o in meshes_of(prop):
        xs = [v.co for v in o.data.vertices]
        if xs:
            dims = [max(v[i] for v in xs) - min(v[i] for v in xs) for i in range(3)]
            k = dims.index(max(dims))
            for i, ax in enumerate("xyz"): o["g" + ax] = 1.0 if i == k else 0.0
        for idx, slot in enumerate(o.data.materials):
            if slot is None: continue
            r = slot.name.split(".")[0]
            if r not in cfg: continue
            if r not in cache: cache[r] = (phmat if isinstance(cfg[r].get("base"), dict) else build)(f"{prop}_{r}", cfg[r])
            o.data.materials[idx] = cache[r]
    return list(cache)

# shared looks -----------------------------------------------------------------------------------------------
RUST = dict(tex="rust_coarse_01", scale=0.8, hsv=(0.5, 0.75, 0.42))
LOOK = {
    "rust_heavy": dict(base=dict(tex="rust_coarse_01", scale=0.7, hsv=(0.5, 0.8, 0.55)), metal=0.15, streaks=0.4, dirt=0.9,
                       dust=0.5, bottom=0.8, bump=1.0, bump_str=0.7),
    "pipe_paint": dict(base=dict(tex="rusty_metal_02", scale=0.9, repaint=(0.2, 0.16, 0.13), rust_hsv=(0.5, 0.7, 0.32), repaint_sat=0.75),
                       metal=0.2, streaks=0.7, dirt=0.9, dust=0.8, wear=dict(RUST, amount=1.0, chips=0.5, chip_scale=5),
                       bump_str=0.6, wet=0.2),
    "drum": dict(base=dict(tex="rusty_metal_02", scale=0.8, repaint=(0.09, 0.11, 0.13), rust_hsv=(0.5, 0.7, 0.28), repaint_sat=0.75),
                 metal=0.2, streaks=0.9, dirt=0.9, dust=0.9, bottom=1.0, wear=dict(RUST, amount=1.0, chips=0.55, chip_scale=4),
                 bump_str=0.6, wet=0.25),
    "green_paint": dict(base=dict(tex="green_metal_rust", scale=1.0, hsv=(0.5, 0.8, 0.42)), metal=0.2, streaks=0.8, dirt=0.9,
                        dust=0.8, bottom=1.0, wear=dict(RUST, amount=1.0, chips=0.5, chip_scale=5), bump_str=0.5, grime=0.7),
    "grey_paint": dict(base=dict(tex="rusty_metal_02", scale=1.0, repaint=(0.12, 0.125, 0.12), rust_hsv=(0.5, 0.7, 0.32), repaint_sat=0.72), metal=0.2, streaks=0.6,
                       dirt=0.8, dust=0.8, bottom=0.8, wear=dict(RUST, amount=1.0, chips=0.4, chip_scale=6), bump_str=0.5),
    "steel": dict(base=dict(tex="rusty_metal_02", scale=0.6, repaint=(0.3, 0.3, 0.29), rust_hsv=(0.5, 0.7, 0.3), repaint_sat=0.7, paint_rough=0.4), metal=0.75, streaks=0.3, dirt=0.9, dust=0.5,
                  wear=dict(RUST, amount=0.5), bump_str=0.3),
    "cast_iron": dict(base=dict(tex="rust_coarse_01", scale=0.6, hsv=(0.5, 0.6, 0.5)), metal=0.2, dirt=1.0, dust=0.9,
                      streaks=0.4, bump_str=0.8, edge_radius=0.004),
    "wood_old": dict(base=dict(tex="weathered_planks", scale=1.4, grain=True, hsv=(0.5, 0.75, 0.42)), dirt=0.9, dust=0.9,
                     bottom=0.7, wear=dict(col=(0.32, 0.27, 0.2), rough=0.9, amount=0.4), bump_str=0.8, edge_radius=0.004),
    "wood_worn": dict(base=dict(tex="wood_table_worn", scale=1.2, grain=True, hsv=(0.5, 0.75, 0.4)), dirt=0.9, dust=0.8,
                      bottom=0.6, wear=dict(col=(0.3, 0.24, 0.17), rough=0.9, amount=0.5), bump_str=0.6, edge_radius=0.004),
    "wood_raw": dict(base=dict(tex="raw_plank_wall", scale=1.6, grain=True, hsv=(0.5, 0.75, 0.4)), dirt=0.9, dust=0.8,
                     bottom=0.9, bump_str=0.7, edge_radius=0.004, wet=0.2),
    "wood_dark": dict(base=dict(tex="old_planks_02", scale=1.4, grain=True, hsv=(0.5, 0.7, 0.32)), dirt=1.0, dust=0.8,
                      bottom=0.8, bump_str=0.7, edge_radius=0.004),
    "concrete": dict(base=dict(tex="concrete", scale=1.2, hsv=(0.5, 0.6, 0.6)), dirt=1.0, dust=0.8, bump_str=0.8),
    "brick": dict(base=dict(tex="concrete", scale=0.6, tint=(0.62, 0.28, 0.2), hsv=(0.5, 0.8, 0.8)), dirt=1.0, dust=0.9,
                  bump_str=1.0, edge_radius=0.01, wear=dict(col=(0.5, 0.42, 0.36), rough=0.95, amount=0.5)),
    "rubble": dict(base=dict(tex="rubble", scale=1.0, hsv=(0.5, 0.6, 0.42)), dirt=0.8, dust=0.7, bump_str=1.0),
    "fabric": dict(base=dict(tex="dirty_carpet", scale=0.8, hsv=(0.5, 0.35, 0.8), tint=(0.62, 0.55, 0.42)), dirt=1.0,
                   dust=0.6, bottom=0.5, bump_str=0.5, edge_radius=0.01),
    "rubber": dict(base=dict(tex="concrete", scale=0.5, hsv=(0.5, 0.0, 0.07)), dirt=0.6, dust=0.5, bump_str=0.5,
                   edge_radius=0.004),
}
# procedural (propkit.build) looks
PLOOK = {
    "paper": dict(base=((0.34, 0.28, 0.2), (0.28, 0.23, 0.16)), rough=0.88, metal=0.0, wear=((0.32, 0.27, 0.2), 0.9, 0.0, 0.6),
                  dirt=((0.2, 0.15, 0.09), 0.9, 0.0, 1.0), dust=0.8, bottom=1.0, wrinkle=0.35, bump=0.35, edge_radius=0.01),
    "ink": dict(base=((0.05, 0.045, 0.04), (0.07, 0.06, 0.05)), rough=0.8, metal=0.0, wear=((0.4, 0.35, 0.28), 0.9, 0.0, 0.6),
                dirt=((0.1, 0.08, 0.05), 0.9, 0.0, 0.5), dust=0.5),
    "white_paint": dict(base=((0.75, 0.73, 0.66), (0.66, 0.64, 0.57)), rough=0.6, metal=0.0, wear=((0.2, 0.1, 0.05), 0.85, 0.0, 0.9),
                        dirt=((0.15, 0.11, 0.07), 0.9, 0.0, 0.9), dust=0.6, chips=0.4, chip_scale=14),
    "yellow_paint": dict(base=((0.8, 0.6, 0.06), (0.7, 0.5, 0.05)), rough=0.5, metal=0.0, wear=((0.3, 0.25, 0.2), 0.85, 0.0, 0.7),
                         dirt=((0.15, 0.11, 0.07), 0.9, 0.0, 0.9), dust=0.5),
    "red_paint": dict(base=((0.24, 0.03, 0.018), (0.18, 0.025, 0.015)), rough=0.5, metal=0.0, chips=0.5, chip_scale=8,
                      wear=((0.16, 0.08, 0.04), 0.85, 0.3, 1.0), dirt=((0.12, 0.08, 0.05), 0.9, 0.0, 0.9), dust=0.8,
                      bottom=0.8, scratch=0.4),
    "glass_green": dict(base=((0.04, 0.09, 0.05), (0.03, 0.07, 0.04)), rough=0.12, metal=0.0, wear=((0.1, 0.12, 0.1), 0.4, 0.0, 0.3),
                        dirt=((0.12, 0.1, 0.07), 0.8, 0.0, 0.8), dust=0.9),
    "glass_brown": dict(base=((0.1, 0.045, 0.015), (0.08, 0.035, 0.01)), rough=0.12, metal=0.0, wear=((0.12, 0.1, 0.08), 0.4, 0.0, 0.3),
                        dirt=((0.12, 0.1, 0.07), 0.8, 0.0, 0.8), dust=0.9),
    "cable": dict(base=((0.03, 0.03, 0.03), (0.04, 0.04, 0.04)), rough=0.6, metal=0.0, wear=((0.1, 0.1, 0.1), 0.8, 0.0, 0.3),
                  dirt=((0.1, 0.08, 0.05), 0.9, 0.0, 0.6), dust=0.8),
    "brass": dict(base=((0.55, 0.4, 0.17), (0.45, 0.32, 0.12)), rough=0.4, metal=1.0, wear=((0.2, 0.25, 0.15), 0.8, 0.2, 0.8),
                  dirt=((0.1, 0.08, 0.04), 0.9, 0.0, 1.0), dust=0.6),
    "porcelain": dict(base=((0.7, 0.68, 0.62), (0.62, 0.6, 0.55)), rough=0.3, metal=0.0, wear=((0.3, 0.25, 0.2), 0.8, 0.0, 0.5),
                      dirt=((0.15, 0.11, 0.07), 0.9, 0.0, 1.0), dust=0.8),
    "rag": dict(base=((0.3, 0.27, 0.22), (0.22, 0.2, 0.17)), rough=0.95, metal=0.0, wear=((0.15, 0.12, 0.09), 0.95, 0.0, 0.5),
                dirt=((0.08, 0.06, 0.04), 0.95, 0.0, 1.0), dust=0.5, wrinkle=0.6, bump=0.5, wet=0.4),
}
def CFG(**roles):
    """CFG(metal="drum", wood="wood_old", ...): roles -> looks (scan LOOK first, then procedural PLOOK)."""
    out = {}
    for r, look in roles.items():
        out[r] = dict(LOOK[look]) if look in LOOK else dict(PLOOK[look])
    return out

def glass_mat(name="Glass"):
    m = bpy.data.materials.get(name) or bpy.data.materials.new(name)
    m.use_nodes = True; b = m.node_tree.nodes["Principled BSDF"]
    b.inputs["Base Color"].default_value = (0.5, 0.55, 0.6, 1); b.inputs["Roughness"].default_value = 0.25
    b.inputs["Alpha"].default_value = 0.35
    return m

def bulb_mat(name="Bulb"):
    m = bpy.data.materials.get(name) or bpy.data.materials.new(name)
    m.use_nodes = True; b = m.node_tree.nodes["Principled BSDF"]
    b.inputs["Base Color"].default_value = (1.0, 0.8, 0.5, 1); b.inputs["Emission Color"].default_value = (1.0, 0.75, 0.4, 1)
    b.inputs["Emission Strength"].default_value = 8.0
    return m

def no_bake(o):
    o["no_bake"] = 1; return o

# ------------------------------------------------------------------ architecture (tiling, UVs in scan tiles)

WALL_H = 2.7
WALL_T = 0.3
ARCH = ["Wall_1m", "Wall_2m", "Wall_4m", "Wall_Door_2m", "Wall_Window_2m", "Wall_2m_Plaster", "Wall_4m_Plaster",
        "Floor_4x4", "Floor_4x4_Damaged", "Ceiling_4x4", "Ceiling_Beam_4m", "Column", "Wall_Corner"]

def arch_mat(name):
    m = bpy.data.materials.get(name) or bpy.data.materials.new(name)
    m.use_nodes = True; nt = m.node_tree
    unity = dict(TILING)
    tid = unity.get(name)
    if tid and not any(n.type == 'TEX_IMAGE' for n in nt.nodes):
        t = nt.nodes.new("ShaderNodeTexImage"); t.image = ph_image(tid, "Diffuse", False)
        nt.links.new(t.outputs["Color"], nt.nodes["Principled BSDF"].inputs["Base Color"])
    return m

def box_uv(o, tile):
    """UVs by face axis in world metres / tile size, so the scan keeps its real scale on any module."""
    me = o.data; uv = me.uv_layers.get("UVMap") or me.uv_layers.new(name="UVMap")
    mw = o.matrix_world
    for p in me.polygons:
        n = (mw.to_3x3() @ p.normal); ax = max(range(3), key=lambda i: abs(n[i]))
        for li in p.loop_indices:
            co = mw @ me.vertices[me.loops[li].vertex_index].co
            if ax == 0: u, v = co.y * (1 if n.x < 0 else -1), co.z
            elif ax == 1: u, v = co.x * (1 if n.y > 0 else -1) * -1, co.z
            else: u, v = co.x, co.y * (1 if n.z > 0 else -1)
            uv.data[li].uv = (u / tile[0], v / tile[1])

def slab(name, x0, x1, y0, y1, z0, z1, mat, cuts=(1, 1, 1)):
    bm = bmesh.new(); bmesh.ops.create_cube(bm, size=1.0)
    for v in bm.verts:
        v.co.x = x0 if v.co.x < 0 else x1; v.co.y = y0 if v.co.y < 0 else y1; v.co.z = z0 if v.co.z < 0 else z1
    me = bpy.data.meshes.new(name); bm.to_mesh(me); bm.free()
    o = bpy.data.objects.new(name, me); bpy.context.collection.objects.link(o); me.materials.append(mat)
    return o

def wall_with_holes(name, W, holes, mat):
    """A wall W long (x centred), WALL_T thick (y centred), WALL_H tall, with rectangular holes (x0, x1, z0, z1),
    made of slabs so the reveals get their own faces."""
    parts = []; t = WALL_T / 2
    xs = sorted({-W / 2, W / 2} | {h[0] for h in holes} | {h[1] for h in holes})
    for a, b in zip(xs, xs[1:]):
        cut = [h for h in holes if h[0] <= a and h[1] >= b]
        if not cut: parts.append(slab(name, a, b, -t, t, 0, WALL_H, mat)); continue
        h = cut[0]
        if h[2] > 0: parts.append(slab(name, a, b, -t, t, 0, h[2], mat))
        if h[3] < WALL_H: parts.append(slab(name, a, b, -t, t, h[3], WALL_H, mat))
    for p in parts[1:]: pass
    with bpy.context.temp_override(**ctx(), active_object=parts[0], selected_objects=parts, selected_editable_objects=parts):
        if len(parts) > 1: bpy.ops.object.join()
    o = parts[0]; o.name = name; o.data.name = name
    bm = bmesh.new(); bm.from_mesh(o.data); bmesh.ops.remove_doubles(bm, verts=bm.verts, dist=1e-4); bm.to_mesh(o.data); bm.free()
    return o

def build_architecture(export=True):
    clear_scene(); load_sizes()
    sizes = {n: PH_SIZE.get(t, (2, 2)) for n, t in TILING}
    brick, plaster, rb = arch_mat("Basement_BrickPlaster"), arch_mat("Basement_DamagedPlaster"), arch_mat("Basement_Brick")
    floor, floord, ceil = arch_mat("Basement_FloorConcrete"), arch_mat("Basement_FloorDamaged"), arch_mat("Basement_Ceiling")
    made = {}
    made["Wall_1m"] = (wall_with_holes("Wall_1m", 1, [], brick), "Basement_BrickPlaster")
    made["Wall_2m"] = (wall_with_holes("Wall_2m", 2, [], brick), "Basement_BrickPlaster")
    made["Wall_4m"] = (wall_with_holes("Wall_4m", 4, [], brick), "Basement_BrickPlaster")
    made["Wall_2m_Plaster"] = (wall_with_holes("Wall_2m_Plaster", 2, [], plaster), "Basement_DamagedPlaster")
    made["Wall_4m_Plaster"] = (wall_with_holes("Wall_4m_Plaster", 4, [], plaster), "Basement_DamagedPlaster")
    made["Wall_Door_2m"] = (wall_with_holes("Wall_Door_2m", 2, [(-0.5, 0.5, 0.0, 2.1)], brick), "Basement_BrickPlaster")
    made["Wall_Window_2m"] = (wall_with_holes("Wall_Window_2m", 2, [(-0.55, 0.55, 1.85, 2.4)], brick), "Basement_BrickPlaster")
    made["Floor_4x4"] = (slab("Floor_4x4", -2, 2, -2, 2, -0.1, 0, floor), "Basement_FloorConcrete")
    made["Floor_4x4_Damaged"] = (slab("Floor_4x4_Damaged", -2, 2, -2, 2, -0.1, 0, floord), "Basement_FloorDamaged")
    made["Ceiling_4x4"] = (slab("Ceiling_4x4", -2, 2, -2, 2, 0, 0.2, ceil), "Basement_Ceiling")
    made["Ceiling_Beam_4m"] = (slab("Ceiling_Beam_4m", -2, 2, -0.15, 0.15, -0.35, 0, ceil), "Basement_Ceiling")
    made["Column"] = (slab("Column", -0.22, 0.22, -0.22, 0.22, 0, WALL_H, rb), "Basement_Brick")
    made["Wall_Corner"] = (slab("Wall_Corner", -WALL_T / 2, WALL_T / 2, -WALL_T / 2, WALL_T / 2, 0, WALL_H, brick), "Basement_BrickPlaster")
    for i, (n, (o, mname)) in enumerate(made.items()):
        o.location = ((i % 5) * 5.0, (i // 5) * 5.0, 0); bpy.context.view_layer.update()
        o.location = (0, 0, 0); bpy.context.view_layer.update()
        box_uv(o, sizes[mname])
        for p in o.data.polygons: p.use_smooth = False
        o.location = ((i % 5) * 5.0, (i // 5) * 5.0, 0)
    if export:
        os.makedirs(FBX_OUT, exist_ok=True)
        for n, (o, _) in made.items():
            loc = o.location.copy(); o.location = (0, 0, 0)
            with bpy.context.temp_override(**ctx(), selected_objects=[o], active_object=o):
                bpy.ops.object.select_all(action='DESELECT'); o.select_set(True)
                bpy.ops.export_scene.fbx(filepath=os.path.join(FBX_OUT, n + ".fbx"), use_selection=True,
                                         apply_scale_options='FBX_SCALE_UNITS', bake_space_transform=True,
                                         object_types={'MESH'}, mesh_smooth_type='FACE', path_mode='STRIP',
                                         embed_textures=False, add_leaf_bones=False)
            o.location = loc
        bpy.ops.wm.save_as_mainfile(filepath=os.path.join(PROJECT, "Blender", "Basement_Architecture.blend"))
    return "architecture: " + ", ".join(made)

# ------------------------------------------------------------------ pipes

PR = 0.11     # large pipe radius
SR = 0.035    # small pipe radius

def flange(name, p, axis, r, mat, bolt_mat, parent, bolts=8):
    a = Vector(axis).normalized()
    f = cyl(name, r + 0.065, r + 0.065, 0.028, p, mat, parent, seg=32, axis=a, bv=0.004)
    q = a.to_track_quat('Z', 'Y')
    for k in range(bolts):
        ang = 2 * math.pi * (k + 0.5) / bolts
        off = q @ Vector((math.cos(ang) * (r + 0.042), math.sin(ang) * (r + 0.042), 0))
        cyl(name + "_Bolt", 0.011, 0.011, 0.05, Vector(p) + off, bolt_mat, parent, seg=6, axis=a)
    return f

def coupling(name, p, axis, r, mat, parent):
    cyl(name, r * 1.28, r * 1.28, 0.05, p, mat, parent, seg=6, axis=axis, bv=0.003)

def handwheel(name, centre, axis, R, mat, parent, spokes=4):
    a = Vector(axis).normalized(); q = a.to_track_quat('Z', 'Y'); c = Vector(centre)
    torus(name + "_Rim", R, R * 0.09, c, mat, parent, seg=40, rseg=8, axis=a)
    cyl(name + "_Hub", R * 0.18, R * 0.18, R * 0.25, c, mat, parent, seg=16, axis=a, bv=0.003)
    for k in range(spokes):
        ang = 2 * math.pi * k / spokes + 0.3
        d = q @ Vector((math.cos(ang), math.sin(ang), 0))
        rod(name + "_Spoke", c + d * R * 0.15 + a * R * 0.04, c + d * R * 0.95, R * 0.05, mat, parent, seg=8)
    # a few knobs on the rim, as on old cast wheels
    for k in range(spokes * 2):
        ang = 2 * math.pi * (k + 0.5) / (spokes * 2) + 0.3
        sphere(name + "_Knob", R * 0.11, c + (q @ Vector((math.cos(ang), math.sin(ang), 0))) * R, mat, parent, 10, 6)

def build_pipe_large_straight():
    rt = root("Pipe_Large_2m", (0, 0, 0)); m, b = role("pipe"), role("bolt")
    o = sweep("PipeL_Body", [(-0.975 + 0.0975 * i, 0, 0) for i in range(21)], PR, m, rt, seg=24, caps=False)
    jitter(o, 0.002, 4.0, 1)
    flange("PipeL_Flange", (-0.986, 0, 0), (1, 0, 0), PR, m, b, rt)
    flange("PipeL_Flange", (0.986, 0, 0), (1, 0, 0), PR, m, b, rt)
    # a welded seam ring and a patch clamp where it once leaked
    torus("PipeL_Seam", PR, 0.006, (0.15, 0, 0), m, rt, seg=24, rseg=6, axis=(1, 0, 0))
    o = cyl("PipeL_Patch", PR + 0.008, PR + 0.008, 0.12, (-0.45, 0, 0), m, rt, seg=24, axis=(1, 0, 0), bv=0.003)
    box("PipeL_PatchLug", (0.1, 0.03, 0.04), (-0.45, 0, PR + 0.025), m, rt, bv=0.004)
    cyl("PipeL_PatchBolt", 0.008, 0.008, 0.07, (-0.45, 0, PR + 0.025), b, rt, seg=6, axis=(0, 1, 0))
    return rt

def build_pipe_large_elbow():
    rt = root("Pipe_Large_Elbow", (3, 0, 0)); m, b = role("pipe"), role("bolt")
    R = 0.28; L = 0.6
    pts = [(L, 0, 0), (R + 0.15, 0, 0)] + [(R - R * math.sin(math.radians(a)), 0, R - R * math.cos(math.radians(a))) for a in range(0, 91, 6)] + [(0, 0, R + 0.15), (0, 0, L)]
    sweep("PipeLE_Body", pts, PR, m, rt, seg=24, caps=False)
    flange("PipeLE_Flange", (L - 0.014, 0, 0), (1, 0, 0), PR, m, b, rt)
    flange("PipeLE_Flange", (0, 0, L - 0.014), (0, 0, 1), PR, m, b, rt)
    return rt

def build_pipe_large_valve():
    """An inline gate valve, 0.8 m flange to flange along X, the bonnet and handwheel towards the front (-Y)."""
    rt = root("Pipe_Large_Valve", (6, 0, 0)); m, b, w = role("pipe"), role("bolt"), role("wheel")
    sweep("PipeLV_Body", [(-0.4 + 0.05 * i, 0, 0) for i in range(17)], PR, m, rt, seg=24, caps=False)
    flange("PipeLV_Flange", (-0.386, 0, 0), (1, 0, 0), PR, m, b, rt)
    flange("PipeLV_Flange", (0.386, 0, 0), (1, 0, 0), PR, m, b, rt)
    # valve body: a fat barrel with its own flanges
    o = lathe("PipeLV_Valve", [(0, -0.2), (PR + 0.03, -0.2), (PR + 0.07, -0.13), (PR + 0.08, 0), (PR + 0.07, 0.13), (PR + 0.03, 0.2), (0, 0.2)], m, rt, seg=28)
    o.rotation_euler = (0, math.radians(90), 0)
    flange("PipeLV_VFlange", (-0.19, 0, 0), (1, 0, 0), PR, m, b, rt, bolts=6)
    flange("PipeLV_VFlange", (0.19, 0, 0), (1, 0, 0), PR, m, b, rt, bolts=6)
    # bonnet towards -Y
    o = lathe("PipeLV_Bonnet", [(0.0, 0.0), (0.09, 0.0), (0.085, 0.18), (0.06, 0.24), (0.06, 0.3), (0.0, 0.3)], m, rt, seg=20)
    o.rotation_euler = (math.radians(90), 0, 0)
    flange("PipeLV_BFlange", (0, -0.14, 0), (0, 1, 0), 0.03, m, b, rt, bolts=6)
    rod("PipeLV_Stem", (0, -0.28, 0), (0, -0.46, 0), 0.016, b, rt, seg=10)
    handwheel("PipeLV_Wheel", (0, -0.42, 0), (0, 1, 0), 0.17, w, rt, spokes=4)
    return rt

def build_pipe_small_straight():
    rt = root("Pipe_Small_2m", (0, 2, 0)); m = role("pipe")
    sweep("PipeS_Body", [(-1 + 0.1 * i, 0, 0) for i in range(21)], SR, m, rt, seg=14, caps=True)
    coupling("PipeS_Coupling", (-0.975, 0, 0), (1, 0, 0), SR, m, rt)
    coupling("PipeS_Coupling", (0.975, 0, 0), (1, 0, 0), SR, m, rt)
    return rt

def build_pipe_small_elbow():
    rt = root("Pipe_Small_Elbow", (3, 2, 0)); m = role("pipe")
    R = 0.08; L = 0.3
    pts = [(L, 0, 0)] + [(R - R * math.sin(math.radians(a)), 0, R - R * math.cos(math.radians(a))) for a in range(0, 91, 10)] + [(0, 0, L)]
    sweep("PipeSE_Body", pts, SR, m, rt, seg=14, caps=True)
    coupling("PipeSE_Coupling", (L - 0.025, 0, 0), (1, 0, 0), SR, m, rt)
    coupling("PipeSE_Coupling", (0, 0, L - 0.025), (0, 0, 1), SR, m, rt)
    return rt

def build_pipe_small_valve():
    rt = root("Pipe_Small_Valve", (6, 2, 0)); m, w = role("pipe"), role("wheel")
    sweep("PipeSV_Body", [(-0.25 + 0.05 * i, 0, 0) for i in range(11)], SR, m, rt, seg=14, caps=True)
    o = lathe("PipeSV_Valve", [(0, -0.07), (SR * 1.35, -0.07), (SR * 1.7, -0.03), (SR * 1.8, 0), (SR * 1.7, 0.03), (SR * 1.35, 0.07), (0, 0.07)], m, rt, seg=16)
    o.rotation_euler = (0, math.radians(90), 0)
    for x in (-0.075, 0.075): coupling("PipeSV_Nut", (x, 0, 0), (1, 0, 0), SR * 1.05, m, rt)
    rod("PipeSV_Stem", (0, 0, 0.04), (0, 0, 0.13), 0.008, m, rt, seg=8)
    cyl("PipeSV_Bonnet", 0.022, 0.018, 0.05, (0, 0, 0.075), m, rt, seg=6, bv=0.002)
    handwheel("PipeSV_Wheel", (0, 0, 0.13), (0, 0, 1), 0.055, w, rt, spokes=3)
    return rt

def build_pipe_brackets():
    """Pipe_Bracket_Large: a wall arm (into the wall at y=+0.25) with a strap around a large pipe whose axis is at the
    origin along X; Pipe_Clamp_Small: a two-hole saddle for a small pipe 6 cm off the wall. One FBX, two meshes."""
    rt = root("Pipe_Brackets", (9, 0, 0)); m, b = role("bracket"), role("bolt")
    # large: angle arm from the wall, strap ring
    box("Large_Arm", (0.05, 0.25, 0.006), (0, 0.24, -PR - 0.012), m, rt, bv=0.002)
    box("Large_ArmWeb", (0.006, 0.25, 0.05), (0, 0.24, -PR - 0.035), m, rt, bv=0.002)
    box("Large_Plate", (0.14, 0.008, 0.14), (0, 0.366, -PR - 0.03), m, rt, bv=0.003)
    for x in (-0.05, 0.05):
        for z in (-PR + 0.02, -PR - 0.08):
            cyl("Large_Bolt", 0.009, 0.009, 0.02, (x, 0.358, z), b, rt, seg=6, axis=(0, 1, 0))
    o = lathe("Large_Strap", [(PR + 0.004, -0.02), (PR + 0.01, -0.02), (PR + 0.01, 0.02), (PR + 0.004, 0.02)], m, rt, seg=32, cyclic=True)
    o.rotation_euler = (0, math.radians(90), 0)
    for s_ in (-1, 1):
        box("Large_Ear", (0.04, 0.008, 0.035), (0, s_ * (PR + 0.02), 0), m, rt, bv=0.002)
    # small clamp: around a pipe at (0, 0, 0) local of its own spot, wall at y=+0.06
    c = Vector((0, 0, 0.6))
    o = lathe("Small_Saddle", [(SR + 0.002, -0.012), (SR + 0.006, -0.012), (SR + 0.006, 0.012), (SR + 0.002, 0.012)], m, rt, seg=20, cyclic=True)
    o.location = c; o.rotation_euler = (0, math.radians(90), 0)
    box("Small_Stem", (0.024, 0.03, 0.006), c + Vector((0, SR + 0.012, 0)), m, rt, bv=0.002)
    box("Small_Foot", (0.07, 0.006, 0.03), c + Vector((0, 0.06, 0)), m, rt, bv=0.002)
    for x in (-0.025, 0.025): cyl("Small_Screw", 0.006, 0.006, 0.008, c + Vector((x, 0.056, 0)), b, rt, seg=6, axis=(0, 1, 0))
    return rt

PIPE_CFG = CFG(pipe="pipe_paint", bolt="rust_heavy", wheel="cast_iron", bracket="rust_heavy")

# ------------------------------------------------------------------ preview renders

def preview(path, target, dist, rx=68, rz=25, res=(960, 540), samples=24, lens=40, light=600.0):
    """Cycles still of the scene from a camera orbiting target: one warm key, a cold fill, a dim world."""
    sc = bpy.context.scene; sc.render.engine = 'CYCLES'; sc.cycles.samples = samples; _use_gpu(sc)
    sc.cycles.use_denoising = True
    sc.render.resolution_x, sc.render.resolution_y = res; sc.render.resolution_percentage = 100
    for n in ("PV_Cam", "PV_Key", "PV_Fill"):
        if n in bpy.data.objects: bpy.data.objects.remove(bpy.data.objects[n])
    cam = bpy.data.objects.new("PV_Cam", bpy.data.cameras.new("PV_Cam")); sc.collection.objects.link(cam)
    cam.data.lens = lens
    t = Vector(target); e = Euler((math.radians(rx), 0, math.radians(rz)), 'XYZ')
    cam.location = t + e.to_matrix() @ Vector((0, 0, dist)); cam.rotation_euler = e; sc.camera = cam
    key = bpy.data.objects.new("PV_Key", bpy.data.lights.new("PV_Key", 'AREA')); sc.collection.objects.link(key)
    key.data.energy = light; key.data.size = 1.0; key.data.color = (1.0, 0.78, 0.5)
    key.location = t + Vector((-2.0, -2.5, 3.0)); key.rotation_euler = (t - key.location).to_track_quat('-Z', 'Y').to_euler()
    fill = bpy.data.objects.new("PV_Fill", bpy.data.lights.new("PV_Fill", 'AREA')); sc.collection.objects.link(fill)
    fill.data.energy = light * 0.35; fill.data.size = 3.0; fill.data.color = (0.55, 0.65, 1.0)
    fill.location = t + Vector((3.0, -1.0, 1.5)); fill.rotation_euler = (t - fill.location).to_track_quat('-Z', 'Y').to_euler()
    w = sc.world or bpy.data.worlds.new("World"); sc.world = w; w.use_nodes = True
    w.node_tree.nodes["Background"].inputs["Color"].default_value = (0.02, 0.022, 0.025, 1)
    sc.render.filepath = path; sc.render.image_settings.file_format = 'PNG'
    bpy.ops.render.render(write_still=True)
    return path

# ------------------------------------------------------------------ groups: build / bake / finish

def unwrap_only(prop, angle=60.0, island=0.004):
    """The same Bake UV layout bake_prop makes (smart_project is deterministic), without baking: for a prop whose maps
    are already on disk, e.g. when a queue is resumed after Blender restarted."""
    objs = meshes_of(prop)
    for o in objs:
        uv = o.data.uv_layers.get("Bake") or o.data.uv_layers.new(name="Bake")
        o.data.uv_layers.active = uv; uv.active_render = True
    with bpy.context.temp_override(**ctx()):
        if bpy.context.object and bpy.context.object.mode != 'OBJECT': bpy.ops.object.mode_set(mode='OBJECT')
        bpy.ops.object.select_all(action='DESELECT')
        for o in objs: o.select_set(True)
        bpy.context.view_layer.objects.active = objs[0]
        bpy.ops.object.mode_set(mode='EDIT'); bpy.ops.mesh.select_all(action='SELECT')
        bpy.ops.uv.smart_project(angle_limit=math.radians(angle), island_margin=island, scale_to_bounds=False)
        bpy.ops.object.mode_set(mode='OBJECT')
    return f"{prop}: unwrapped only (maps on disk)"

def _baked(prop):
    return all(os.path.exists(os.path.join(TEX_OUT, f"{prop}_{k}.png")) for k in ("BaseColor", "Normal", "MetallicSmoothness"))

def bake_all(groups, log_path=None, resume=False):
    """Build, bake and finish each group in turn on Blender timers (the bridge call returns at once).
    Progress and errors go to Blender/bake_log.txt; the last line reads ALL DONE or FAILED.
    resume=True skips the bake (UVs only) for props whose maps are already on disk."""
    log_path = log_path or BAKE_LOG
    open(log_path, "a" if resume else "w").write("RESUME\n" if resume else "START\n")
    steps = []
    for g in groups:
        steps.append(("build", g, None))
        for it in GROUPS[g]: steps.append(("bake", g, it))
        steps.append(("finish", g, None))
    state = {"i": 0}
    def log(t): open(log_path, "a").write(t + "\n")
    def tick():
        if state["i"] >= len(steps): log("ALL DONE"); return None
        kind, g, it = steps[state["i"]]
        try:
            if kind == "build": log(build_group(g, bake=False))
            elif kind == "bake": log(unwrap_only(it[0]) if resume and _baked(it[0]) else bake_prop(it[0], it[3]))
            else: log(finish_group(g))
        except Exception:
            import traceback; log("FAILED at %s %s %s\n%s" % (kind, g, it[0] if it else "", traceback.format_exc())); return None
        state["i"] += 1
        return 0.2
    bpy.app.timers.register(tick, first_interval=0.5)
    return f"{len(steps)} steps queued"

GROUPS = {}     # name -> list of (prop, builder, cfg, res, split groups or None, origins or None)

GROUPS["pipes"] = [
    ("Pipe_Large_2m", build_pipe_large_straight, PIPE_CFG, 2048, None, None),
    ("Pipe_Large_Elbow", build_pipe_large_elbow, PIPE_CFG, 1024, None, None),
    ("Pipe_Large_Valve", build_pipe_large_valve, PIPE_CFG, 2048, None, None),
    ("Pipe_Small_2m", build_pipe_small_straight, PIPE_CFG, 1024, None, None),
    ("Pipe_Small_Elbow", build_pipe_small_elbow, PIPE_CFG, 512, None, None),
    ("Pipe_Small_Valve", build_pipe_small_valve, PIPE_CFG, 1024, None, None),
    ("Pipe_Brackets", build_pipe_brackets, PIPE_CFG, 1024, "prefix:Large,Small", {"Large": (0, 0, 0), "Small": (0, 0, 0.6)}),
]

def _groups_for(prop, split):
    if not split: return None
    if isinstance(split, dict): return split
    names = split.split(":", 1)[1].split(",")
    return {n: [o.name for o in meshes_of(prop) if o.name == n or o.name.startswith((n + "_", n + "."))] for n in names}

def build_group(gname, bake=True, only=None):
    clear_scene(); ROLES.clear()
    items = [it for it in GROUPS[gname] if not only or it[0] in only]
    for prop, fn, cfg, res, split, org in items:
        fn()
        assign(prop, cfg)
    apply_modifiers()
    # each prop at its own spot so the bakes do not see each other's AO
    for i, it in enumerate(items):
        bpy.data.objects[it[0]].location = ((i % 6) * 4.0, (i // 6) * 4.0, 0)
    if bake: bake_in_background([(it[0], it[3]) for it in items], BAKE_LOG)
    return f"{gname}: " + ", ".join(f"{it[0]}={len(meshes_of(it[0]))}" for it in items)

def export_prop(prop):
    rt = bpy.data.objects[prop]; loc = rt.location.copy(); rt.location = (0, 0, 0)
    objs = [rt] + list(rt.children_recursive)
    with bpy.context.temp_override(**ctx(), selected_objects=objs, active_object=rt):
        bpy.ops.object.select_all(action='DESELECT')
        for o in objs: o.select_set(True)
        bpy.ops.export_scene.fbx(filepath=os.path.join(FBX_OUT, prop + ".fbx"), use_selection=True,
                                 apply_scale_options='FBX_SCALE_UNITS', bake_space_transform=True,
                                 object_types={'EMPTY', 'MESH'}, use_mesh_modifiers=True, mesh_smooth_type='FACE',
                                 path_mode='STRIP', embed_textures=False, add_leaf_bones=False)
    rt.location = loc

def finish_group(gname, only=None):
    os.makedirs(FBX_OUT, exist_ok=True); out = []
    for prop, fn, cfg, res, split, org in GROUPS[gname]:
        if only and prop not in only: continue
        made = finalize(prop, _groups_for(prop, split), org)
        export_prop(prop); out.append(f"{prop}: {made}")
    bpy.ops.wm.save_as_mainfile(filepath=os.path.join(PROJECT, "Blender", f"Basement_{gname}.blend"))
    return "; ".join(out)
