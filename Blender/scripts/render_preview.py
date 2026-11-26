"""Preview renders of RedCrewGirl framed exactly like the concept sheet, plus a
side-by-side / overlay comparison against the concept image."""
import bpy, math, os
import numpy as np

OUT = r"C:\Users\Bober\AppData\Local\Temp\claude\438c13d99dd8c879\242b5af4-07f0-4834-b252-52bc341a51aa\scratchpad"
CONCEPT = r"C:\Users\Bober\AppData\Local\Temp\claude\21eb37d5e5f14cc1\242b5af4-07f0-4834-b252-52bc341a51aa\images\1.webp"
H_PX, TOP_PX, BOT_PX = 1024, 23, 982            # concept: figure spans rows 23..982
K = 1.6 / (BOT_PX - TOP_PX)                      # metres per concept pixel
sc = bpy.context.scene

# --- concept image, top-down float array
ci = bpy.data.images.load(CONCEPT, check_existing=True)
cw, chh = ci.size
cpx = np.array(ci.pixels[:], dtype=np.float32).reshape(chh, cw, 4)[::-1]
bgc = cpx[5, 5, :3]
mask = np.abs(cpx[:, :, :3] - bgc).sum(2) > 0.08
def centre(x0, x1):
    xs = np.where(mask[500:850, x0:x1])[1]
    return (xs.min() + xs.max()) / 2 + x0
c_front, c_back = centre(0, cw // 2) , centre(cw // 2, cw) - cw // 2

for o in list(bpy.data.objects):
    if o.name.startswith("RCGPrev_"):
        bpy.data.objects.remove(o, do_unlink=True)
cam_d = bpy.data.cameras.new("RCGPrev_Cam"); cam_d.type = "ORTHO"; cam_d.ortho_scale = K * H_PX
cam = bpy.data.objects.new("RCGPrev_Cam", cam_d); sc.collection.objects.link(cam)
sun_d = bpy.data.lights.new("RCGPrev_Sun", "SUN"); sun_d.energy = 2.6
sun = bpy.data.objects.new("RCGPrev_Sun", sun_d); sc.collection.objects.link(sun)
old_cam = sc.camera; sc.camera = cam
engines = [e.identifier for e in bpy.types.RenderSettings.bl_rna.properties["engine"].enum_items]
sc.render.engine = "BLENDER_EEVEE" if "BLENDER_EEVEE" in engines else "BLENDER_EEVEE_NEXT"
sc.render.resolution_x, sc.render.resolution_y, sc.render.resolution_percentage = cw // 2, H_PX, 100
w = sc.world or bpy.data.worlds.new("World"); sc.world = w; w.use_nodes = True
bg = w.node_tree.nodes.get("Background")
bg.inputs[0].default_value = (*[c ** 2.2 for c in bgc], 1); bg.inputs[1].default_value = 2.6
sc.view_settings.view_transform = "Standard"

z_mid = (BOT_PX - H_PX / 2) * K
half = cw // 4
views = {
    "front": ((half - c_front) * K, -4, 0.0),
    "back": (-(half - c_back) * K, 4, math.pi),
    "three_q": None,
}
renders = {}
for name, v in views.items():
    if v is None:
        cam.location = (-2.8, -2.8, z_mid); cam.rotation_euler = (math.radians(90), 0, -math.pi / 4)
        sun.rotation_euler = (math.radians(18), 0, -math.pi / 4 - 0.4)
    else:
        x, y, rz = v
        cam.location = (x, y, z_mid); cam.rotation_euler = (math.radians(90), 0, rz)
        sun.rotation_euler = (math.radians(22), 0, rz - 0.3)
    path = os.path.join(OUT, "rcg_%s.png" % name)
    sc.render.filepath = path
    bpy.ops.render.render(write_still=True)
    renders[name] = path
    print("wrote", path)

# close-up of the face
cam_d.ortho_scale = 0.42
cam.location = (0, -4, 1.39); cam.rotation_euler = (math.radians(90), 0, 0)
sun.rotation_euler = (math.radians(22), 0, -0.3)
sc.render.filepath = os.path.join(OUT, "rcg_face.png")
bpy.ops.render.render(write_still=True)
cam_d.ortho_scale = K * H_PX

def load(p):
    im = bpy.data.images.load(p, check_existing=False); im.reload()
    a = np.array(im.pixels[:], dtype=np.float32).reshape(im.size[1], im.size[0], 4)[::-1]
    bpy.data.images.remove(im)
    return a

rf, rb = load(renders["front"]), load(renders["back"])
cf, cb = cpx[:, :cw // 2], cpx[:, cw // 2:]
strip = np.concatenate([cf, rf, cb, rb], axis=1)
over = cf * 0.5 + rf * 0.5
def save(a, name):
    h, wd = a.shape[:2]
    im = bpy.data.images.new(name, wd, h, alpha=True)
    im.pixels[:] = np.ascontiguousarray(a[::-1]).ravel()
    im.filepath_raw = os.path.join(OUT, name + ".png"); im.file_format = "PNG"; im.save()
    bpy.data.images.remove(im)
save(strip, "rcg_compare"); save(over, "rcg_overlay")
print("compare saved")

sc.camera = old_cam
bpy.data.objects.remove(cam, do_unlink=True); bpy.data.objects.remove(sun, do_unlink=True)
