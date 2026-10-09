# Erzeugt zwei Beispiel-Mods als GLB, um den Mod-Loader zu testen (und als Vorlage):
#   Mods/Skaters/Beispiel-Mixamo/figur.glb  - Skater mit Mixamo-Knochennamen und eigenen Farben
#   Mods/Boards/Beispiel-Longboard/board.glb - Longboard mit Textur (laengs entlang Blender-X, absichtlich gross)
# Aufruf: blender --background --factory-startup --python build_example_mods.py -- <Mods-Ordner>

import bpy
import math
import os
import runpy
import sys

argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
MODS = argv[0] if argv else os.path.join(os.path.dirname(__file__), "..", "..", "Mods")
HERE = os.path.dirname(os.path.abspath(__file__))
TMP_FBX = os.path.join(bpy.app.tempdir or HERE, "example_tmp.fbx")


def set_color(mat, rgba):
    mat.diffuse_color = rgba
    if mat.node_tree is None:
        try:
            mat.use_nodes = True
        except Exception:
            pass
    if mat.node_tree is not None:
        bsdf = next((n for n in mat.node_tree.nodes if n.type == 'BSDF_PRINCIPLED'), None)
        if bsdf is None:
            bsdf = mat.node_tree.nodes.new('ShaderNodeBsdfPrincipled')
            out = next((n for n in mat.node_tree.nodes if n.type == 'OUTPUT_MATERIAL'), None) or mat.node_tree.nodes.new('ShaderNodeOutputMaterial')
            mat.node_tree.links.new(bsdf.outputs[0], out.inputs[0])
        bsdf.inputs["Base Color"].default_value = rgba
    return mat


def export_glb(path, objects):
    os.makedirs(os.path.dirname(path), exist_ok=True)
    for o in bpy.context.scene.objects:
        o.select_set(False)
    for o in objects:
        o.select_set(True)
    bpy.context.view_layer.objects.active = objects[0]
    bpy.ops.export_scene.gltf(filepath=path, export_format='GLB', use_selection=True,
                              export_skins=True, export_animations=False, export_yup=True)
    print("DRIFTSKATE exported", path)


# ---------------------------------------------------------------- 1) Charakter mit Mixamo-Namen

sys.argv = [sys.argv[0], "--", TMP_FBX]
runpy.run_path(os.path.join(HERE, "build_skater.py"), run_name="__main__")

arm = bpy.data.objects["SkaterRig"]
rename = {"Hips": "Hips", "Spine": "Spine", "Chest": "Spine2", "Neck": "Neck", "Head": "Head"}
for side, word in (("L", "Left"), ("R", "Right")):
    rename.update({
        "UpperArm_" + side: word + "Arm", "LowerArm_" + side: word + "ForeArm", "Hand_" + side: word + "Hand",
        "UpperLeg_" + side: word + "UpLeg", "LowerLeg_" + side: word + "Leg", "Foot_" + side: word + "Foot",
    })
for old, new in rename.items():
    arm.data.bones[old].name = "mixamorig:" + new

colors = {
    "Jacket": (0.85, 0.12, 0.18, 1), "JacketTrim": (0.95, 0.95, 0.95, 1), "Pants": (0.92, 0.9, 0.85, 1),
    "Shoes": (0.1, 0.1, 0.12, 1), "Sole": (0.95, 0.95, 0.95, 1), "HeadWear": (0.95, 0.75, 0.1, 1),
    "Accent": (0.1, 0.8, 0.95, 1), "Skin": (0.62, 0.43, 0.3, 1),
}
for m in bpy.data.materials:
    if m.name in colors:
        set_color(m, colors[m.name])

keep = "Head_Beanie"
for name in ("Head_Hair", "Head_Cap", "Head_Phones"):
    if name in bpy.data.objects:
        bpy.data.objects.remove(bpy.data.objects[name], do_unlink=True)

export_glb(os.path.join(MODS, "Skaters", "Beispiel-Mixamo", "figur.glb"),
           [arm, bpy.data.objects["Body"], bpy.data.objects[keep]])
with open(os.path.join(MODS, "Skaters", "Beispiel-Mixamo", "mod.json"), "w", encoding="utf-8") as f:
    f.write('{\n  "name": "Beispiel (Mixamo-Rig)",\n  "author": "DriftSkate",\n  "height": 1.72\n}\n')

# ---------------------------------------------------------------- 2) Longboard mit Textur

for o in list(bpy.data.objects):
    bpy.data.objects.remove(o, do_unlink=True)

# Textur: Streifen und Punkte (prueft das Laden von Bildern)
img = bpy.data.images.new("LongboardArt", width=128, height=512, alpha=False)
px = []
for y in range(512):
    for x in range(128):
        stripe = (y // 32) % 2 == 0
        dot = ((x - 64) ** 2 + ((y % 128) - 64) ** 2) < 26 ** 2
        if dot:
            c = (1.0, 0.85, 0.1)
        elif stripe:
            c = (0.15, 0.75, 0.9)
        else:
            c = (0.95, 0.25, 0.55)
        px.extend((c[0], c[1], c[2], 1.0))
img.pixels = px
img.pack()

deck_mat = bpy.data.materials.new("LongboardDeck")
deck_mat.use_nodes = True if deck_mat.node_tree is None else deck_mat.use_nodes
nt = deck_mat.node_tree
bsdf = next(n for n in nt.nodes if n.type == 'BSDF_PRINCIPLED')
tex = nt.nodes.new('ShaderNodeTexImage')
tex.image = img
nt.links.new(tex.outputs["Color"], bsdf.inputs["Base Color"])
wheel_mat = set_color(bpy.data.materials.new("LongboardWheels"), (0.2, 0.95, 0.4, 1))
truck_mat = set_color(bpy.data.materials.new("LongboardTrucks"), (0.7, 0.7, 0.75, 1))

# Laengs entlang X und doppelt so gross wie noetig: der Loader soll drehen und skalieren
bpy.ops.mesh.primitive_cube_add(size=1.0, location=(0, 0, 0.1))
deck = bpy.context.active_object
deck.name = "Deck"
deck.scale = (2.0, 0.5, 0.05)
bpy.ops.object.transform_apply(location=False, rotation=True, scale=True)
bev = deck.modifiers.new("Bevel", 'BEVEL')
bev.width = 0.2
bev.segments = 6
bpy.ops.object.convert(target='MESH')
bpy.ops.object.mode_set(mode='EDIT')
bpy.ops.uv.smart_project()
bpy.ops.object.mode_set(mode='OBJECT')
deck.data.materials.append(deck_mat)
parts = [deck]
for sx in (-0.7, 0.7):
    bpy.ops.mesh.primitive_cube_add(size=1.0, location=(sx, 0, 0.0))
    t = bpy.context.active_object
    t.scale = (0.08, 0.4, 0.06)
    bpy.ops.object.transform_apply(scale=True)
    t.data.materials.append(truck_mat)
    parts.append(t)
    for sy in (-0.24, 0.24):
        bpy.ops.mesh.primitive_cylinder_add(vertices=20, radius=0.09, depth=0.08, location=(sx, sy, -0.06), rotation=(math.pi / 2, 0, 0))
        w = bpy.context.active_object
        w.data.materials.append(wheel_mat)
        parts.append(w)
for o in bpy.context.scene.objects:
    o.select_set(o in parts)
bpy.context.view_layer.objects.active = deck
bpy.ops.object.join()
board = bpy.context.active_object
board.name = "Longboard"
export_glb(os.path.join(MODS, "Boards", "Beispiel-Longboard", "board.glb"), [board])
with open(os.path.join(MODS, "Boards", "Beispiel-Longboard", "mod.json"), "w", encoding="utf-8") as f:
    f.write('{\n  "name": "Beispiel-Longboard",\n  "author": "DriftSkate",\n  "speed": 1.25,\n  "pop": 0.85,\n  "balance": 1.1\n}\n')
