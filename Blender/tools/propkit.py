"""Prop toolkit for BodycamHorror, run inside Blender (exec'd over the BlenderMCP bridge).

Workflow for a prop:
  1. build meshes under an empty named after the prop (root(...)), using role materials (role(...))
  2. assign_materials(prop, CFG): replace roles with procedural worn materials (build(...))
  3. bake_prop(prop, res): UV-unwrap into a "Bake" UV layer and bake BaseColor / Normal / MetallicSmoothness PNGs
     into Assets/Generated/Textures/Props (URP Lit layout: R = metallic, A = smoothness)
  4. finalize(prop, groups, origins): one baked material, meshes joined per group, ready for FBX export
Meshes are built in metres, Z up, the prop's front facing -Y (it ends up facing +Z in Unity).
"""
import bpy, bmesh, math, os, time
import numpy as np
from mathutils import Vector, Quaternion, Matrix, Euler

PROJECT = r"E:\Unity\Projects\BodycamHorror"
TEX_OUT = os.path.join(PROJECT, "Assets", "Generated", "Textures", "Props")
SRC_TEX = os.path.join(PROJECT, "Blender", "tex")
FONTS = r"C:\Windows\Fonts"

# ------------------------------------------------------------------ context / scene

def ctx():
    win = bpy.context.window_manager.windows[0]
    area = next(a for a in win.screen.areas if a.type == 'VIEW_3D')
    region = next(r for r in area.regions if r.type == 'WINDOW')
    return dict(window=win, area=area, region=region)

def clear_scene():
    for o in list(bpy.data.objects): bpy.data.objects.remove(o, do_unlink=True)
    for coll in (bpy.data.meshes, bpy.data.curves, bpy.data.materials, bpy.data.textures, bpy.data.cameras, bpy.data.lights):
        for d in list(coll): coll.remove(d)
    for im in list(bpy.data.images):
        if im.type == 'IMAGE': bpy.data.images.remove(im)
    for s in list(bpy.data.scenes):
        if s != bpy.context.scene: bpy.data.scenes.remove(s)

def view(loc, rx, rz, dist, shading='MATERIAL'):
    for win in bpy.context.window_manager.windows:
        for area in win.screen.areas:
            if area.type == 'VIEW_3D':
                sp = area.spaces.active; sp.shading.type = shading; sp.overlay.show_overlays = False
                r3 = sp.region_3d; r3.view_perspective = 'PERSP'
                r3.view_location = Vector(loc); r3.view_distance = dist
                r3.view_rotation = Euler((math.radians(rx), 0, math.radians(rz)), 'XYZ').to_quaternion()

# ------------------------------------------------------------------ meshes

ROLES = {}
def role(name, col=(0.5, 0.5, 0.5)):
    if name in ROLES and ROLES[name].name in bpy.data.materials: return ROLES[name]
    m = bpy.data.materials.new(name); m.use_nodes = True
    m.node_tree.nodes["Principled BSDF"].inputs["Base Color"].default_value = (*col, 1)
    ROLES[name] = m; return m

def root(name, loc=(0, 0, 0)):
    e = bpy.data.objects.new(name, None); e.empty_display_type = 'PLAIN_AXES'
    bpy.context.collection.objects.link(e); e.location = loc; return e

def link_obj(name, me, mats, parent):
    o = bpy.data.objects.new(name, me); bpy.context.collection.objects.link(o)
    o.parent = parent
    for m in (mats if isinstance(mats, (list, tuple)) else [mats]): me.materials.append(m)
    return o

def from_bm(name, bm, mats, parent):
    me = bpy.data.meshes.new(name); bm.to_mesh(me); bm.free()
    return link_obj(name, me, mats, parent)

def bevel(o, w, seg=2):
    m = o.modifiers.new("Bevel", "BEVEL"); m.width = w; m.segments = seg; m.limit_method = 'ANGLE'
    return o

def box(name, size, loc, mat, parent, bv=0.003, rot=None, taper=None):
    bm = bmesh.new(); bmesh.ops.create_cube(bm, size=1.0)
    for v in bm.verts:
        v.co.x *= size[0]; v.co.y *= size[1]; v.co.z *= size[2]
        if taper and v.co.z < 0: v.co.x *= taper; v.co.y *= taper
    o = from_bm(name, bm, mat, parent); o.location = loc
    if rot is not None: o.rotation_mode = 'QUATERNION'; o.rotation_quaternion = rot
    if bv: bevel(o, bv)
    return o

def beam(name, p1, p2, w, d, mat, parent, bv=0.003, taper=None):
    p1 = Vector(p1); p2 = Vector(p2); v = p2 - p1
    return box(name, (w, d, v.length), (p1 + p2) / 2, mat, parent, bv, v.to_track_quat('Z', 'Y'), taper)

def cyl(name, r1, r2, depth, loc, mat, parent, seg=24, caps=True, axis=None, bv=0):
    bm = bmesh.new()
    bmesh.ops.create_cone(bm, cap_ends=caps, segments=seg, radius1=r1, radius2=r2, depth=depth)
    o = from_bm(name, bm, mat, parent); o.location = loc
    for p in o.data.polygons: p.use_smooth = abs(p.normal.z) < 0.9
    if axis is not None:
        o.rotation_mode = 'QUATERNION'; o.rotation_quaternion = Vector(axis).normalized().to_track_quat('Z', 'Y')
    if bv: bevel(o, bv)
    return o

def rod(name, a, b, r, mat, parent, seg=12):
    a = Vector(a); b = Vector(b); v = b - a
    return cyl(name, r, r, v.length, (a + b) / 2, mat, parent, seg=seg, axis=v)

def sphere(name, r, loc, mat, parent, u=20, v=12):
    bm = bmesh.new(); bmesh.ops.create_uvsphere(bm, u_segments=u, v_segments=v, radius=r)
    o = from_bm(name, bm, mat, parent); o.location = loc
    for p in o.data.polygons: p.use_smooth = True
    return o

def tube(name, pts, r, mat, parent, cyclic=False):
    cu = bpy.data.curves.new(name + "_c", 'CURVE'); cu.dimensions = '3D'
    cu.bevel_depth = r; cu.bevel_resolution = 2; cu.use_fill_caps = True
    sp = cu.splines.new('POLY'); sp.points.add(len(pts) - 1)
    for p, co in zip(sp.points, pts): p.co = (co[0], co[1], co[2], 1)
    sp.use_cyclic_u = cyclic
    return curve_to_mesh(name, cu, mat, parent)

def curve_to_mesh(name, cu, mat, parent, smooth=True):
    tmp = bpy.data.objects.new(name + "_tmp", cu); bpy.context.collection.objects.link(tmp)
    dg = bpy.context.evaluated_depsgraph_get()
    me = bpy.data.meshes.new_from_object(tmp.evaluated_get(dg)); me.name = name
    bpy.data.objects.remove(tmp); bpy.data.curves.remove(cu)
    me.materials.clear()
    o = link_obj(name, me, mat, parent)
    for p in me.polygons: p.use_smooth = smooth
    return o

def font(file):
    return bpy.data.fonts.load(os.path.join(FONTS, file), check_existing=True)

def text_mesh(name, body, font_file, size, loc, mat, parent, rot=(0, 0, 0), extrude=0.0, bevel_depth=0.0, align='CENTER'):
    cu = bpy.data.curves.new(name + "_t", 'FONT'); cu.body = body; cu.size = size
    cu.extrude = extrude; cu.bevel_depth = bevel_depth; cu.align_x = align; cu.align_y = 'CENTER'
    if font_file: cu.font = font(font_file)
    o = curve_to_mesh(name, cu, mat, parent, smooth=False)
    o.location = loc; o.rotation_euler = rot
    return o

def apply_modifiers(objs=None):
    dg = bpy.context.evaluated_depsgraph_get()
    for o in (objs or [o for o in bpy.data.objects if o.type == 'MESH']):
        if o.type != 'MESH' or not o.modifiers: continue
        me = bpy.data.meshes.new_from_object(o.evaluated_get(dg)); old = o.data
        o.modifiers.clear(); o.data = me
        if old.users == 0: bpy.data.meshes.remove(old)

def meshes_of(prop):
    return [o for o in bpy.data.objects[prop].children_recursive if o.type == 'MESH' and not o.get("no_bake")]

# ------------------------------------------------------------------ procedural worn materials

class NB:
    def __init__(s, nt): s.nt = nt
    def n(s, t, **kw):
        node = s.nt.nodes.new(t)
        for k, v in kw.items(): setattr(node, k, v)
        return node
    def L(s, a, b): s.nt.links.new(a, b)
    def _set(s, sock, x):
        if isinstance(x, (int, float)): sock.default_value = x
        elif isinstance(x, tuple): sock.default_value = (*x, 1) if (len(x) == 3 and sock.type == 'RGBA') else x
        else: s.L(x, sock)
    def m(s, op, a, b=None, clamp=False):
        node = s.n("ShaderNodeMath", operation=op, use_clamp=clamp)
        s._set(node.inputs[0], a)
        if b is not None: s._set(node.inputs[1], b)
        return node.outputs[0]
    def mr(s, x, a, b, c=0.0, d=1.0):
        node = s.n("ShaderNodeMapRange", clamp=True)
        s._set(node.inputs["Value"], x); node.inputs["From Min"].default_value = a; node.inputs["From Max"].default_value = b
        node.inputs["To Min"].default_value = c; node.inputs["To Max"].default_value = d
        return node.outputs["Result"]
    def mix(s, fac, a, b, kind='RGBA', blend='MIX'):
        node = s.n("ShaderNodeMix"); node.data_type = kind; node.clamp_factor = True
        if kind == 'RGBA': node.blend_type = blend
        st = 'RGBA' if kind == 'RGBA' else 'VALUE'
        f = [i for i in node.inputs if i.name == "Factor" and i.type == 'VALUE'][0]
        A = [i for i in node.inputs if i.name == "A" and i.type == st][0]
        B = [i for i in node.inputs if i.name == "B" and i.type == st][0]
        s._set(f, fac); s._set(A, a); s._set(B, b)
        return [o for o in node.outputs if o.type == st][0]
    def noise(s, vec, scale, detail=4.0, rough=0.5, dist=0.0):
        t = s.n("ShaderNodeTexNoise")
        t.inputs["Scale"].default_value = scale; t.inputs["Detail"].default_value = detail
        t.inputs["Roughness"].default_value = rough; t.inputs["Distortion"].default_value = dist
        if vec is not None: s.L(vec, t.inputs["Vector"])
        return t.outputs["Fac"]
    def vmath(s, op, a, b):
        node = s.n("ShaderNodeVectorMath", operation=op); s._set(node.inputs[0], a); s._set(node.inputs[1], b)
        return node.outputs[0] if op != 'DOT_PRODUCT' else node.outputs["Value"]
    def oattr(s, name):
        a = s.n("ShaderNodeAttribute"); a.attribute_type = 'OBJECT'; a.attribute_name = name
        return a.outputs["Fac"]

def build(name, c):
    """c keys: base=(colA, colB), rough, metal, wear=(col, rough, metal, amount), dirt=(col, rough, metal, amount), dust,
    optional: wood, scratch, chips (non-edge peeling), chip_scale, bottom (dirt rising from the floor), wet,
    image (print texture path, read through the 'Print' UV), bump, wrinkle (coarse fold bump), edge_radius"""
    mat = bpy.data.materials.new(name); mat.use_nodes = True
    nt = mat.node_tree; nt.nodes.clear(); s = NB(nt)
    out = s.n("ShaderNodeOutputMaterial"); bsdf = s.n("ShaderNodeBsdfPrincipled"); s.L(bsdf.outputs[0], out.inputs["Surface"])
    tc = s.n("ShaderNodeTexCoord"); oi = s.n("ShaderNodeObjectInfo"); geo = s.n("ShaderNodeNewGeometry")
    coord = s.vmath('ADD', tc.outputs["Object"], s.m('MULTIPLY', oi.outputs["Random"], 13.0))
    bev = s.n("ShaderNodeBevel"); bev.samples = 8; bev.inputs["Radius"].default_value = c.get("edge_radius", 0.005)
    edge = s.mr(s.vmath('DOT_PRODUCT', bev.outputs["Normal"], geo.outputs["Normal"]), 0.995, 0.9)
    ao = s.n("ShaderNodeAmbientOcclusion"); ao.samples = 16; ao.inputs["Distance"].default_value = c.get("ao_dist", 0.08)
    cav = s.m('SUBTRACT', 1.0, ao.outputs["AO"])
    blotch = s.mr(s.noise(coord, 3.5, 6, 0.6), 0.5, 0.72)
    wear_n = s.mr(s.noise(coord, 45, 6, 0.65), 0.42, 0.62)
    wear = s.m('MULTIPLY', s.m('MULTIPLY', edge, wear_n), c["wear"][3], clamp=True)
    if c.get("chips"):
        sc = c.get("chip_scale", 9.0)
        chip = s.mr(s.noise(coord, sc, 12, 0.72, 0.3), 0.6, 0.625)
        chip = s.m('MULTIPLY', chip, s.mr(s.noise(coord, 1.6, 2), 0.42, 0.62))
        wear = s.m('MAXIMUM', wear, s.m('MULTIPLY', chip, c["chips"], clamp=True))
    pos = s.n("ShaderNodeSeparateXYZ"); s.L(geo.outputs["Position"], pos.inputs[0])
    dirt = s.m('ADD', s.m('MULTIPLY', cav, 1.6), s.m('MULTIPLY', blotch, 0.35))
    if c.get("bottom"):
        dirt = s.m('ADD', dirt, s.m('MULTIPLY', s.m('MULTIPLY', s.mr(pos.outputs["Z"], 0.45, 0.0), s.mr(s.noise(coord, 7, 6, 0.6), 0.3, 0.6)), c["bottom"]))
    dirt = s.m('MULTIPLY', dirt, c["dirt"][3], clamp=True)
    nz = s.n("ShaderNodeSeparateXYZ"); s.L(geo.outputs["Normal"], nz.inputs[0])
    dust = s.m('MULTIPLY', s.m('MULTIPLY', s.mr(nz.outputs["Z"], 0.55, 0.95), s.mr(s.noise(coord, 9, 5, 0.6), 0.35, 0.7)), c["dust"] * 0.55, clamp=True)
    var = s.noise(coord, 6, 4, 0.5)
    height = s.m('MULTIPLY', s.noise(coord, 180, 4, 0.5), 0.15)
    if c.get("wood"):
        st = s.n("ShaderNodeCombineXYZ")
        for i, ax in enumerate("xyz"): s.L(s.m('SUBTRACT', 1.0, s.m('MULTIPLY', s.oattr("g" + ax), 0.93)), st.inputs[i])
        scv = s.vmath('MULTIPLY', coord, st.outputs[0])
        g = s.noise(scv, 7, 10, 0.68, 1.2); streak = s.noise(scv, 70, 6, 0.6)
        base = s.mix(s.mr(g, 0.3, 0.7), c["base"][0], c["base"][1])
        base = s.mix(s.m('MULTIPLY', s.mr(streak, 0.45, 0.7), 0.6), base, c["base"][0])
        height = s.m('ADD', height, s.m('MULTIPLY', streak, 0.5))
    else:
        base = s.mix(var, c["base"][0], c["base"][1])
    rough = s.m('ADD', c["rough"], s.m('MULTIPLY', s.m('SUBTRACT', var, 0.5), 0.2))
    if c.get("scratch"):
        vor = s.n("ShaderNodeTexVoronoi"); vor.feature = 'DISTANCE_TO_EDGE'; vor.inputs["Scale"].default_value = 22
        s.L(s.vmath('MULTIPLY', coord, (1.0, 3.0, 1.0)), vor.inputs["Vector"])
        scr = s.m('MULTIPLY', s.mr(vor.outputs["Distance"], 0.012, 0.0), s.m('MULTIPLY', s.mr(s.noise(coord, 4, 2), 0.45, 0.6), c["scratch"]), clamp=True)
        base = s.mix(s.m('MULTIPLY', scr, 0.7), base, c["wear"][0]); rough = s.mix(scr, rough, 0.7, 'FLOAT')
        height = s.m('ADD', height, s.m('MULTIPLY', scr, -0.6))
    if c.get("image"):
        im = s.n("ShaderNodeTexImage"); im.image = bpy.data.images.load(c["image"], check_existing=True)
        im.interpolation = 'Cubic'
        uvn = s.n("ShaderNodeUVMap"); uvn.uv_map = "Print"; s.L(uvn.outputs["UV"], im.inputs["Vector"])
        base = s.mix(1.0, base, im.outputs["Color"], blend='MULTIPLY')
    if c.get("wrinkle"):
        w1 = s.noise(s.vmath('MULTIPLY', coord, (1.0, 1.0, 1.0)), 9, 3, 0.5, 2.5)
        vor2 = s.n("ShaderNodeTexVoronoi"); vor2.feature = 'DISTANCE_TO_EDGE'; vor2.inputs["Scale"].default_value = 14
        s.L(coord, vor2.inputs["Vector"])
        height = s.m('ADD', height, s.m('MULTIPLY', s.m('ADD', s.m('MULTIPLY', w1, 0.6), s.mr(vor2.outputs["Distance"], 0.0, 0.05)), c["wrinkle"]))
    metal = s.mix(0.0, c["metal"], 0.0, 'FLOAT')
    layers = [(wear, c["wear"][:3]), (dirt, c["dirt"][:3]), (dust, ((0.12, 0.11, 0.095), 0.95, 0.0))]
    for mask, (col, r, mt) in layers:
        base = s.mix(mask, base, col); rough = s.mix(mask, rough, r, 'FLOAT'); metal = s.mix(mask, metal, mt, 'FLOAT')
    if c.get("wet"):
        wet = s.m('MULTIPLY', s.mr(s.noise(coord, 2.2, 4, 0.55), 0.55, 0.64), c["wet"], clamp=True)
        base = s.mix(wet, base, s.mix(1.0, base, (0.45, 0.42, 0.36), blend='MULTIPLY')); rough = s.mix(wet, rough, 0.22, 'FLOAT')
    height = s.m('ADD', height, s.m('MULTIPLY', wear, -0.4))
    bump = s.n("ShaderNodeBump"); bump.inputs["Strength"].default_value = c.get("bump", 0.25); bump.inputs["Distance"].default_value = 0.002
    s.L(height, bump.inputs["Height"])
    s.L(base, bsdf.inputs["Base Color"]); s.L(rough, bsdf.inputs["Roughness"]); s.L(metal, bsdf.inputs["Metallic"])
    s.L(bump.outputs["Normal"], bsdf.inputs["Normal"])
    mat["metal_src"] = metal.node.name
    return mat

def assign_materials(prop, cfg):
    """Swap role placeholder materials on the prop's meshes for per-prop procedural ones built from cfg[role]."""
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
            if r not in cache: cache[r] = build(f"{prop}_{r}", cfg[r])
            o.data.materials[idx] = cache[r]
    return list(cache)

# ------------------------------------------------------------------ bake

def _use_gpu(sc):
    try:
        prefs = bpy.context.preferences.addons['cycles'].preferences
        for t in ('OPTIX', 'CUDA', 'HIP', 'ONEAPI'):
            try:
                prefs.compute_device_type = t; prefs.get_devices()
                if any(d.type == t for d in prefs.devices):
                    for d in prefs.devices: d.use = d.type == t
                    sc.cycles.device = 'GPU'; return t
            except Exception: pass
    except Exception: pass
    sc.cycles.device = 'CPU'; return 'CPU'

def bake_prop(prop, res, res_y=None, samples=24, margin=8, angle=60.0, island=0.004):
    t0 = time.time(); res_y = res_y or res
    sc = bpy.context.scene; sc.render.engine = 'CYCLES'; sc.cycles.samples = samples; _use_gpu(sc)
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
    mats = {s.material for o in objs for s in o.material_slots if s.material}
    imgs = {}
    for key, nc in (("BaseColor", False), ("Rough", True), ("Normal", True), ("Metal", True)):
        nm = f"{prop}_{key}"
        if nm in bpy.data.images: bpy.data.images.remove(bpy.data.images[nm])
        im = bpy.data.images.new(nm, res, res_y, alpha=False)
        if nc: im.colorspace_settings.name = 'Non-Color'
        imgs[key] = im
    nodes = {}
    for m in mats:
        n = m.node_tree.nodes.new("ShaderNodeTexImage"); n.name = "BAKE_TARGET"; nodes[m] = n
    def run(key, **kw):
        for m, n in nodes.items(): n.image = imgs[key]; m.node_tree.nodes.active = n
        with bpy.context.temp_override(**ctx(), selected_objects=objs, selected_editable_objects=objs, active_object=objs[0]):
            bpy.ops.object.bake(margin=margin, use_clear=True, **kw)
    run("BaseColor", type='DIFFUSE', pass_filter={'COLOR'})
    run("Rough", type='ROUGHNESS')
    run("Normal", type='NORMAL', normal_space='TANGENT')
    saved = []
    for m in mats:
        nt = m.node_tree; src = nt.nodes[m["metal_src"]]
        sock = [o for o in src.outputs if o.type == 'VALUE' and o.enabled and o.is_linked][0]
        em = nt.nodes.new("ShaderNodeEmission"); nt.links.new(sock, em.inputs["Color"])
        outn = [n for n in nt.nodes if n.type == 'OUTPUT_MATERIAL'][0]
        prev = outn.inputs["Surface"].links[0].from_socket
        nt.links.new(em.outputs[0], outn.inputs["Surface"]); saved.append((nt, em, prev, outn))
    run("Metal", type='EMIT')
    for nt, em, prev, outn in saved: nt.links.new(prev, outn.inputs["Surface"]); nt.nodes.remove(em)
    for m, n in nodes.items(): m.node_tree.nodes.remove(n)
    os.makedirs(TEX_OUT, exist_ok=True)
    for key in ("BaseColor", "Normal"):
        im = imgs[key]; im.filepath_raw = os.path.join(TEX_OUT, f"{prop}_{key}.png"); im.file_format = 'PNG'; im.save()
    rough = np.array(imgs["Rough"].pixels[:]).reshape(-1, 4); metal = np.array(imgs["Metal"].pixels[:]).reshape(-1, 4)
    nm = f"{prop}_MetallicSmoothness"
    if nm in bpy.data.images: bpy.data.images.remove(bpy.data.images[nm])
    ms = bpy.data.images.new(nm, res, res_y, alpha=True); ms.colorspace_settings.name = 'Non-Color'
    px = np.zeros_like(rough); px[:, 0] = px[:, 1] = px[:, 2] = metal[:, 0]; px[:, 3] = 1.0 - rough[:, 0]
    ms.pixels.foreach_set(px.ravel().astype(np.float32))
    ms.filepath_raw = os.path.join(TEX_OUT, f"{nm}.png"); ms.file_format = 'PNG'; ms.save()
    return f"{prop}: {len(objs)} objs, {len(mats)} mats, {time.time() - t0:.1f}s"

def bake_in_background(jobs, log_path):
    """jobs: list of (prop, res) or (prop, res, res_y). Runs on a timer so the bridge call returns at once."""
    if os.path.exists(log_path): os.remove(log_path)
    def job():
        lines = []
        try:
            for j in jobs: lines.append(bake_prop(*j))
            open(log_path, "w").write("DONE\n" + "\n".join(lines) + "\n")
        except Exception:
            import traceback; open(log_path, "w").write("FAILED\n" + "\n".join(lines) + "\n" + traceback.format_exc())
        return None
    bpy.app.timers.register(job, first_interval=0.3)

# ------------------------------------------------------------------ finalize

def baked_material(prop):
    def img(key, nc):
        im = bpy.data.images.load(os.path.join(TEX_OUT, f"{prop}_{key}.png"), check_existing=True)
        if nc: im.colorspace_settings.name = 'Non-Color'
        return im
    m = bpy.data.materials.new(prop + "_Mat"); m.use_nodes = True; nt = m.node_tree
    bsdf = nt.nodes["Principled BSDF"]
    bc = nt.nodes.new("ShaderNodeTexImage"); bc.image = img("BaseColor", False)
    ms = nt.nodes.new("ShaderNodeTexImage"); ms.image = img("MetallicSmoothness", True)
    nm = nt.nodes.new("ShaderNodeTexImage"); nm.image = img("Normal", True)
    nmap = nt.nodes.new("ShaderNodeNormalMap"); inv = nt.nodes.new("ShaderNodeMath"); inv.operation = 'SUBTRACT'; inv.inputs[0].default_value = 1
    nt.links.new(bc.outputs["Color"], bsdf.inputs["Base Color"])
    nt.links.new(ms.outputs["Color"], bsdf.inputs["Metallic"])
    nt.links.new(ms.outputs["Alpha"], inv.inputs[1]); nt.links.new(inv.outputs[0], bsdf.inputs["Roughness"])
    nt.links.new(nm.outputs["Color"], nmap.inputs["Color"]); nt.links.new(nmap.outputs["Normal"], bsdf.inputs["Normal"])
    return m

def finalize(prop, groups=None, origins=None):
    """Join the prop's baked meshes. groups: {mesh name: [object names] | None (= everything else)};
    origins: {mesh name: point in the prop's local space} for pivots such as a door's hinge."""
    bpy.context.view_layer.update()
    rt = bpy.data.objects[prop]; objs = meshes_of(prop)
    m = bpy.data.materials.get(prop + "_Mat") or baked_material(prop)
    inv = rt.matrix_world.inverted()
    for o in objs:
        local = inv @ o.matrix_world
        o.data.transform(local)
        o.parent = rt; o.matrix_parent_inverse = Matrix.Identity(4); o.matrix_basis = Matrix.Identity(4)
        if "Bake" in o.data.uv_layers:   # (already done when finalize is re-run)
            for uv in list(o.data.uv_layers):
                if uv.name != "Bake": o.data.uv_layers.remove(uv)
            o.data.uv_layers["Bake"].name = "UVMap"
        o.data.materials.clear(); o.data.materials.append(m)
        for k in ("gx", "gy", "gz"):
            if k in o: del o[k]
    groups = groups or {prop + "_Mesh": None}
    claimed = set(n for v in groups.values() if v for n in v)
    names = [o.name for o in objs]   # joins delete objects: resolve by name per group
    made = []
    for gname, members in groups.items():
        g = [bpy.data.objects[n] for n in names if n in bpy.data.objects and (n in members if members else n not in claimed)]
        if not g: continue
        if len(g) > 1:
            with bpy.context.temp_override(**ctx(), active_object=g[0], selected_objects=g, selected_editable_objects=g):
                bpy.ops.object.join()
        j = g[0]; j.name = gname; j.data.name = gname
        if origins and gname in origins:
            p = Vector(origins[gname]); j.data.transform(Matrix.Translation(-p)); j.location = p
        made.append(j.name)
    for o in list(rt.children_recursive):
        if o.type == 'EMPTY': bpy.data.objects.remove(o, do_unlink=True)
    for mm in [mm for mm in bpy.data.materials if mm.users == 0]: bpy.data.materials.remove(mm)
    return made
