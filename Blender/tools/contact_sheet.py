"""Render a grid of frames from the scene camera into one PNG.
    FRAMES = [...]; OUT = "..."; exec(open(r"D:/BodycamHorror/Blender/tools/contact_sheet.py").read())
"""
import bpy, os, numpy as np

sc = bpy.context.scene
FRAMES = globals().get("FRAMES", [0, 40, 60, 100, 140, 176, 197, 215, 245, 330])
OUT = globals().get("OUT", "D:/BodycamHorror/Captures/finale_sheet.png")
COLS = globals().get("COLS", 4)
W, H = 480, 270
tmp = os.path.dirname(OUT) + "/_frames/"
os.makedirs(tmp, exist_ok=True)
old = (sc.render.resolution_x, sc.render.resolution_y, sc.render.resolution_percentage, sc.render.filepath)
sc.render.resolution_x, sc.render.resolution_y, sc.render.resolution_percentage = W, H, 100
tiles = []
for f in FRAMES:
    sc.frame_set(f)
    sc.render.filepath = tmp + "f%04d.png" % f
    bpy.ops.render.render(write_still=True)
    img = bpy.data.images.load(sc.render.filepath)
    px = np.array(img.pixels[:], dtype=np.float32).reshape(H, W, 4)
    bpy.data.images.remove(img)
    tiles.append(px)
rows = (len(tiles) + COLS - 1) // COLS
sheet = np.zeros((rows * H, COLS * W, 4), dtype=np.float32); sheet[..., 3] = 1
for i, t in enumerate(tiles):
    r, c = divmod(i, COLS)
    sheet[(rows - 1 - r) * H:(rows - r) * H, c * W:(c + 1) * W] = t
out = bpy.data.images.new("_sheet", COLS * W, rows * H)
out.pixels.foreach_set(sheet.ravel())
out.filepath_raw = OUT; out.file_format = "PNG"; out.save()
bpy.data.images.remove(out)
sc.render.resolution_x, sc.render.resolution_y, sc.render.resolution_percentage, sc.render.filepath = old
print("sheet", OUT, FRAMES)
