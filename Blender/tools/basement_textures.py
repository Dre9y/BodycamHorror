"""Poly Haven (CC0) scans for the basement, run inside Blender over the bridge after propkit.py:

    exec(open(r"E:\\Unity\\Projects\\BodycamHorror\\Blender\\tools\\propkit.py").read())
    exec(open(r"E:\\Unity\\Projects\\BodycamHorror\\Blender\\tools\\basement_textures.py").read())
    print(fetch_all())          # downloads what is missing into Blender/tex/polyhaven/<id>/
    print(export_tiling())      # the architecture's tiling maps -> Assets/Generated/Textures/Basement

The props sample these scans in their procedural materials (basement_kit.py) and get baked to their own maps; the
walls, floor and ceiling tile them directly, so their maps are copied into the project as URP Lit sets:
<Name>_BaseColor.jpg, <Name>_Normal.jpg (OpenGL), <Name>_MetallicSmoothness.png (R metal, A 1 - roughness).
Each scan's real size (Poly Haven's "dimensions", mm) is kept in PH_SIZE so UVs in metres can be scaled to it.
"""
import bpy, os, json, shutil, urllib.request
import numpy as np

PROJECT = r"E:\Unity\Projects\BodycamHorror"
PH_DIR = os.path.join(PROJECT, "Blender", "tex", "polyhaven")
TILING_OUT = os.path.join(PROJECT, "Assets", "Generated", "Textures", "Basement")
PH_RES = "2k"
UA = {"User-Agent": "BodycamHorror-asset-fetch"}

PH_TEXTURES = [
    # architecture
    "red_brick_plaster_patch_02", "damaged_plaster", "red_brick", "concrete_floor_worn_001",
    "damaged_concrete_floor", "concrete_wall_006",
    # metals
    "rusty_metal_02", "rust_coarse_01", "rusty_painted_metal", "green_metal_rust", "metal_plate",
    "painted_metal_shutter",
    # wood
    "weathered_planks", "wood_table_worn", "old_planks_02", "raw_plank_wall",
    # misc
    "concrete", "rubble", "dirty_carpet",
]
MAPS = {"Diffuse": "diff", "nor_gl": "nor_gl", "Rough": "rough", "Displacement": "disp", "AO": "ao"}

# the tiling sets the level uses: (Unity name, Poly Haven id)
TILING = [
    ("Basement_BrickPlaster", "red_brick_plaster_patch_02"),
    ("Basement_DamagedPlaster", "damaged_plaster"),
    ("Basement_Brick", "red_brick"),
    ("Basement_FloorConcrete", "concrete_floor_worn_001"),
    ("Basement_FloorDamaged", "damaged_concrete_floor"),
    ("Basement_Ceiling", "concrete_wall_006"),
    ("Basement_MetalPaint", "rusty_metal_02"),
    ("Basement_Rust", "rust_coarse_01"),          # the floor's second layer: rust eating through the concrete
]

PH_SIZE = {}   # id -> (width m, height m)

def _get(url):
    req = urllib.request.Request(url, headers=UA)
    with urllib.request.urlopen(req, timeout=60) as r: return r.read()

def ph_path(tid, key):
    return os.path.join(PH_DIR, tid, f"{tid}_{MAPS[key]}_{PH_RES}.jpg")

def fetch(tid):
    d = os.path.join(PH_DIR, tid); os.makedirs(d, exist_ok=True)
    meta_p = os.path.join(d, "info.json")
    if not os.path.exists(meta_p):
        info = json.loads(_get(f"https://api.polyhaven.com/info/{tid}"))
        files = json.loads(_get(f"https://api.polyhaven.com/files/{tid}"))
        json.dump({"dimensions": info.get("dimensions"), "authors": info.get("authors"), "files": files}, open(meta_p, "w"))
    meta = json.load(open(meta_p))
    got = 0
    for key in MAPS:
        p = ph_path(tid, key)
        if os.path.exists(p): continue
        entry = meta["files"].get(key, {}).get(PH_RES, {}).get("jpg")
        if not entry: continue
        url = entry["url"]
        if not url.startswith("https://dl.polyhaven.org/"): continue   # only ever Poly Haven's own CDN
        open(p, "wb").write(_get(url)); got += 1
    dims = meta.get("dimensions") or [2000, 2000]
    PH_SIZE[tid] = (dims[0] / 1000.0, dims[1] / 1000.0)
    return got

def load_sizes():
    for tid in PH_TEXTURES:
        p = os.path.join(PH_DIR, tid, "info.json")
        if os.path.exists(p):
            dims = json.load(open(p)).get("dimensions") or [2000, 2000]
            PH_SIZE[tid] = (dims[0] / 1000.0, dims[1] / 1000.0)
    return PH_SIZE

def fetch_all():
    out = []
    for tid in PH_TEXTURES:
        try: out.append(f"{tid}:{fetch(tid)}")
        except Exception as e: out.append(f"{tid}:FAILED {e}")
    return ", ".join(out)

def ph_image(tid, key, non_color=True):
    p = ph_path(tid, key)
    if not os.path.exists(p): return None
    im = bpy.data.images.load(p, check_existing=True)
    if non_color: im.colorspace_settings.name = 'Non-Color'
    return im

def export_tiling():
    os.makedirs(TILING_OUT, exist_ok=True)
    out = []
    for name, tid in TILING:
        shutil.copyfile(ph_path(tid, "Diffuse"), os.path.join(TILING_OUT, f"{name}_BaseColor.jpg"))
        shutil.copyfile(ph_path(tid, "nor_gl"), os.path.join(TILING_OUT, f"{name}_Normal.jpg"))
        if os.path.exists(ph_path(tid, "Displacement")):   # for BodycamHorror/Tessellated Surface
            shutil.copyfile(ph_path(tid, "Displacement"), os.path.join(TILING_OUT, f"{name}_Height.jpg"))
        r = ph_image(tid, "Rough"); w, h = r.size
        rough = np.array(r.pixels[:], np.float32).reshape(-1, 4)[:, 0]
        ao_im = ph_image(tid, "AO")
        ao = np.array(ao_im.pixels[:], np.float32).reshape(-1, 4)[:, 0] if ao_im and ao_im.size[0] == w else np.ones_like(rough)
        nm = f"{name}_MetallicSmoothness"
        if nm in bpy.data.images: bpy.data.images.remove(bpy.data.images[nm])
        ms = bpy.data.images.new(nm, w, h, alpha=True); ms.colorspace_settings.name = 'Non-Color'
        px = np.zeros((w * h, 4), np.float32)
        px[:, 1] = ao                       # G = occlusion (URP's _OcclusionMap reads G)
        px[:, 3] = np.clip(1.0 - rough, 0, 1)
        ms.pixels.foreach_set(px.ravel())
        ms.filepath_raw = os.path.join(TILING_OUT, f"{nm}.png"); ms.file_format = 'PNG'; ms.save()
        bpy.data.images.remove(ms)
        sz = PH_SIZE.get(tid) or load_sizes().get(tid, (2, 2))
        out.append(f"{name}={sz[0]:.2f}x{sz[1]:.2f}m")
    json.dump({n: PH_SIZE.get(t) for n, t in TILING}, open(os.path.join(TILING_OUT, "sizes.json"), "w"), indent=1)
    return ", ".join(out)

def _periodic_noise(size, power, seed):
    """A seamlessly tiling random field (0..1): white noise shaped in frequency by 1/f^power, back through an FFT."""
    rng = np.random.default_rng(seed)
    fy = np.fft.fftfreq(size)[:, None]; fx = np.fft.rfftfreq(size)[None, :]
    f = np.sqrt(fx * fx + fy * fy); f[0, 0] = 1.0
    spec = (rng.normal(size=f.shape) + 1j * rng.normal(size=f.shape)) / f ** power
    spec[0, 0] = 0
    n = np.fft.irfft2(spec, s=(size, size))
    return (n - n.min()) / (n.max() - n.min())

def _smooth(x, a, b):
    t = np.clip((x - a) / (b - a), 0, 1); return t * t * (3 - 2 * t)

def floor_blend_map(size=1024):
    """Basement_FloorBlend.png for the floor's Tessellated Surface (it tiles; the level spreads it over ~12 m):
    R where rust eats through the concrete, G grime and mud, B where water stands (a few sparse low spots)."""
    rust = _smooth(_periodic_noise(size, 1.6, 11), 0.5, 0.72)
    rust = np.maximum(rust, _smooth(_periodic_noise(size, 1.1, 12), 0.68, 0.8) * 0.8)   # small flecks as well
    grime = _smooth(_periodic_noise(size, 1.4, 21), 0.3, 0.75)
    flood = _smooth(_periodic_noise(size, 2.6, 31), 0.66, 0.86)
    px = np.ones((size, size, 4), np.float32)
    px[..., 0], px[..., 1], px[..., 2] = rust, grime, flood
    nm = "Basement_FloorBlend"
    if nm in bpy.data.images: bpy.data.images.remove(bpy.data.images[nm])
    im = bpy.data.images.new(nm, size, size, alpha=True); im.colorspace_settings.name = 'Non-Color'
    im.pixels.foreach_set(px[::-1].ravel())
    im.filepath_raw = os.path.join(TILING_OUT, nm + ".png"); im.file_format = 'PNG'; im.save()
    bpy.data.images.remove(im)
    return f"{nm}: rust {rust.mean():.2f}, grime {grime.mean():.2f}, flooded {np.mean(flood > 0.3):.2f}"
