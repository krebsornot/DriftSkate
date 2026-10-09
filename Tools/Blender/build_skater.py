# Baut die Spielfigur (stilisierter Skater im Jet-Set-Radio-Look) mit Skelett und exportiert sie als FBX.
# Aufruf:  blender --background --factory-startup --python build_skater.py -- <ausgabe.fbx> [vorschau.png]
#
# Figur schaut in Blender nach -Y (Unity: +Z). Materialnamen werden in Unity auf Outfit-Farben gemappt:
# Skin, Jacket, JacketTrim, Accent, Pants, Shoes, Sole, HeadWear, HairDark, Eyes, EyeWhite

import bpy
import bmesh
import math
import sys
from mathutils import Vector, Matrix

argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
OUT_FBX = argv[0] if len(argv) > 0 else "Skater.fbx"
OUT_PNG = argv[1] if len(argv) > 1 else None

# ---------------------------------------------------------------- Szene leeren

for o in list(bpy.data.objects):
    bpy.data.objects.remove(o, do_unlink=True)
for m in list(bpy.data.meshes):
    bpy.data.meshes.remove(m)

scene = bpy.context.scene
coll = scene.collection
view_layer = bpy.context.view_layer

MATS = {}
COLORS = {
    "Skin": (0.91, 0.70, 0.56, 1), "Jacket": (0.2, 0.35, 1.0, 1), "JacketTrim": (0.95, 0.95, 0.95, 1),
    "Pants": (0.18, 0.24, 0.43, 1), "Shoes": (0.96, 0.95, 0.92, 1), "Sole": (0.98, 0.98, 0.98, 1),
    "HeadWear": (0.16, 0.12, 0.1, 1), "HairDark": (0.12, 0.09, 0.08, 1), "Eyes": (0.06, 0.05, 0.08, 1),
    "EyeWhite": (1, 1, 1, 1), "Accent": (1.0, 0.24, 0.55, 1),
}


def mat(name):
    if name not in MATS:
        m = bpy.data.materials.new(name)
        m.diffuse_color = COLORS.get(name, (1, 1, 1, 1))
        MATS[name] = m
    return MATS[name]


def select_only(*objs, active=None):
    for o in scene.objects:
        if o is not None:
            o.select_set(False)
    for o in objs:
        o.select_set(True)
    bpy.context.view_layer.objects.active = active or objs[0]


def smooth(o):
    n = len(o.data.polygons)
    o.data.polygons.foreach_set("use_smooth", [True] * n)


def set_material(o, name):
    o.data.materials.clear()
    o.data.materials.append(mat(name))


def apply_transform(o):
    select_only(o)
    bpy.ops.object.transform_apply(location=True, rotation=True, scale=True)


def finalize(o, material, smooth_shade=True):
    apply_transform(o)
    set_material(o, material)
    if smooth_shade:
        smooth(o)
    return o


def sphere(name, loc, scale, material, segs=24, rings=14, rot=(0, 0, 0)):
    bpy.ops.mesh.primitive_uv_sphere_add(segments=segs, ring_count=rings, radius=1.0, location=loc, rotation=rot)
    o = bpy.context.active_object
    o.name = name
    o.scale = scale
    return finalize(o, material)


def box(name, loc, size, material, bevel=0.0, rot=(0, 0, 0), subsurf=0):
    bpy.ops.mesh.primitive_cube_add(size=1.0, location=loc, rotation=rot)
    o = bpy.context.active_object
    o.name = name
    o.scale = size
    apply_transform(o)
    if bevel > 0:
        b = o.modifiers.new("Bevel", 'BEVEL')
        b.width = bevel
        b.segments = 3
    if subsurf > 0:
        s = o.modifiers.new("Sub", 'SUBSURF')
        s.levels = subsurf
        s.render_levels = subsurf
    if bevel > 0 or subsurf > 0:
        select_only(o)
        bpy.ops.object.convert(target='MESH')
    set_material(o, material)
    smooth(o)
    return o


def torus(name, loc, major, minor, material, rot=(0, 0, 0), scale=(1, 1, 1)):
    bpy.ops.mesh.primitive_torus_add(major_radius=major, minor_radius=minor, major_segments=28, minor_segments=10,
                                     location=loc, rotation=rot)
    o = bpy.context.active_object
    o.name = name
    o.scale = scale
    return finalize(o, material)


def cone(name, loc, r, depth, material, rot):
    bpy.ops.mesh.primitive_cone_add(vertices=10, radius1=r, radius2=0.0, depth=depth, location=loc, rotation=rot)
    o = bpy.context.active_object
    o.name = name
    return finalize(o, material)


def cylinder(name, loc, r, depth, material, rot=(0, 0, 0), scale=(1, 1, 1)):
    bpy.ops.mesh.primitive_cylinder_add(vertices=24, radius=r, depth=depth, location=loc, rotation=rot)
    o = bpy.context.active_object
    o.name = name
    o.scale = scale
    return finalize(o, material)


def delete_verts(o, predicate):
    bm = bmesh.new()
    bm.from_mesh(o.data)
    doomed = [v for v in bm.verts if predicate(v.co)]
    bmesh.ops.delete(bm, geom=doomed, context='VERTS')
    bm.to_mesh(o.data)
    bm.free()


def rot_to(direction):
    """Euler-Rotation, die die lokale Z-Achse in 'direction' dreht."""
    d = Vector(direction).normalized()
    return Vector((0, 0, 1)).rotation_difference(d).to_euler()


# ---------------------------------------------------------------- Skelett-Punkte (Meter, Z oben, Blick nach -Y)

J = {
    "pelvis": (0, 0, 0.93), "waist": (0, 0, 1.03), "belly": (0, 0, 1.18), "chest": (0, 0, 1.32),
    "upperchest": (0, 0, 1.42), "neck": (0, 0, 1.52), "necktop": (0, 0, 1.575),
}
for s, side in ((1, "L"), (-1, "R")):
    J["shoulder_" + side] = (0.20 * s, 0.0, 1.415)
    J["elbow_" + side] = (0.29 * s, 0.015, 1.15)
    J["wrist_" + side] = (0.355 * s, -0.01, 0.935)
    J["handtip_" + side] = (0.378 * s, -0.025, 0.83)
    J["hip_" + side] = (0.105 * s, 0.0, 0.88)
    J["knee_" + side] = (0.115 * s, -0.015, 0.50)
    J["ankle_" + side] = (0.115 * s, 0.01, 0.115)
    J["toe_" + side] = (0.115 * s, -0.17, 0.03)


def V(name):
    return Vector(J[name])


# ---------------------------------------------------------------- Koerper (Skin-Modifier = weiche, organische Form)

skin_nodes = [
    ("pelvis", 0.16), ("waist", 0.158), ("belly", 0.155), ("chest", 0.17), ("upperchest", 0.15),
    ("neck", 0.052), ("necktop", 0.048),
    ("shoulder_L", 0.078), ("elbow_L", 0.07), ("wrist_L", 0.054),
    ("shoulder_R", 0.078), ("elbow_R", 0.07), ("wrist_R", 0.054),
    ("hip_L", 0.122), ("knee_L", 0.106), ("ankle_L", 0.112),
    ("hip_R", 0.122), ("knee_R", 0.106), ("ankle_R", 0.112),
]
names = [n for n, _ in skin_nodes]
edges_by_name = [
    ("pelvis", "waist"), ("waist", "belly"), ("belly", "chest"), ("chest", "upperchest"), ("upperchest", "neck"),
    ("neck", "necktop"),
    ("upperchest", "shoulder_L"), ("shoulder_L", "elbow_L"), ("elbow_L", "wrist_L"),
    ("upperchest", "shoulder_R"), ("shoulder_R", "elbow_R"), ("elbow_R", "wrist_R"),
    ("pelvis", "hip_L"), ("hip_L", "knee_L"), ("knee_L", "ankle_L"),
    ("pelvis", "hip_R"), ("hip_R", "knee_R"), ("knee_R", "ankle_R"),
]
verts = [J[n] for n in names]
edges = [(names.index(a), names.index(b)) for a, b in edges_by_name]

me = bpy.data.meshes.new("BodyMesh")
me.from_pydata(verts, edges, [])
body = bpy.data.objects.new("Body", me)
coll.objects.link(body)
skin = body.modifiers.new("Skin", 'SKIN')
skin.branch_smoothing = 0.6
skin.use_smooth_shade = True
for i, (_, r) in enumerate(skin_nodes):
    body.data.skin_vertices[0].data[i].radius = (r, r)
body.data.skin_vertices[0].data[0].use_root = True
sub = body.modifiers.new("Sub", 'SUBSURF')
sub.levels = 2
sub.render_levels = 2
select_only(body)
bpy.ops.object.convert(target='MESH')

# Rumpf flacher machen (Tiefe < Breite), Becken etwas flacher
for v in body.data.vertices:
    x, y, z = v.co
    if abs(x) < 0.24 and 0.97 < z < 1.5:
        v.co.y = y * 0.72
    elif abs(x) < 0.27 and 0.8 < z <= 0.97:
        v.co.y = y * 0.86
smooth(body)


def seg_dist(p, a, b):
    ab = b - a
    t = max(0.0, min(1.0, (p - a).dot(ab) / max(1e-9, ab.length_squared)))
    return (a + ab * t - p).length, t


# Materialzonen ueber das naechste Skelett-Segment
zones = [
    ("pelvis", "waist", "Pants"), ("waist", "belly", "Jacket"), ("belly", "chest", "Jacket"),
    ("chest", "upperchest", "Jacket"), ("upperchest", "neck", "Jacket"), ("neck", "necktop", "Skin"),
]
for side in ("L", "R"):
    zones += [("upperchest", "shoulder_" + side, "Jacket"), ("shoulder_" + side, "elbow_" + side, "Jacket"),
              ("elbow_" + side, "wrist_" + side, "Jacket"),
              ("pelvis", "hip_" + side, "Pants"), ("hip_" + side, "knee_" + side, "Pants"), ("knee_" + side, "ankle_" + side, "Pants")]
body.data.materials.clear()
for m in ("Jacket", "Pants", "Skin"):
    body.data.materials.append(mat(m))
slot = {"Jacket": 0, "Pants": 1, "Skin": 2}
for poly in body.data.polygons:
    c = poly.center
    best, best_mat = 1e9, "Jacket"
    for a, b, m in zones:
        d, t = seg_dist(c, V(a), V(b))
        if d < best:
            best, best_mat = d, m
    if c.z > 1.49 and abs(c.x) < 0.12:
        best_mat = "Skin"
    elif abs(c.x) < 0.235 and 0.75 < c.z < 1.25:
        # Rumpf: gerade Kante zwischen Hoodie und Hose
        best_mat = "Pants" if c.z < 0.975 else "Jacket"
    poly.material_index = slot[best_mat]

# ---------------------------------------------------------------- Skelett (Armature)

arm_data = bpy.data.armatures.new("SkaterRig")
arm = bpy.data.objects.new("SkaterRig", arm_data)
coll.objects.link(arm)
select_only(arm)
bpy.ops.object.mode_set(mode='EDIT')
eb = arm_data.edit_bones


def bone(name, head, tail, parent=None, connect=False):
    b = eb.new(name)
    b.head = Vector(head)
    b.tail = Vector(tail)
    if parent:
        b.parent = eb[parent]
        b.use_connect = connect
    return b


bone("Hips", J["pelvis"], J["waist"])
bone("Spine", J["waist"], (0, 0, 1.22), "Hips", True)
bone("Chest", (0, 0, 1.22), (0, 0, 1.44), "Spine", True)
bone("Neck", (0, 0, 1.44), (0, 0, 1.56), "Chest", True)
bone("Head", (0, 0, 1.56), (0, 0, 1.82), "Neck", True)
for side in ("L", "R"):
    bone("UpperArm_" + side, J["shoulder_" + side], J["elbow_" + side], "Chest")
    bone("LowerArm_" + side, J["elbow_" + side], J["wrist_" + side], "UpperArm_" + side, True)
    bone("Hand_" + side, J["wrist_" + side], J["handtip_" + side], "LowerArm_" + side, True)
    bone("UpperLeg_" + side, J["hip_" + side], J["knee_" + side], "Hips")
    bone("LowerLeg_" + side, J["knee_" + side], J["ankle_" + side], "UpperLeg_" + side, True)
    bone("Foot_" + side, J["ankle_" + side], J["toe_" + side], "LowerLeg_" + side, True)
bpy.ops.object.mode_set(mode='OBJECT')

# Koerper automatisch gewichten
select_only(body, arm, active=arm)
bpy.ops.object.parent_set(type='ARMATURE_AUTO')
unweighted = sum(1 for v in body.data.vertices if len(v.groups) == 0)
print("DRIFTSKATE unweighted body verts:", unweighted)

# ---------------------------------------------------------------- Kleidung, Kopf, Haende, Schuhe (starr an Knochen)

rigid_parts = []


def rigid(o, bone_name):
    g = o.vertex_groups.new(name=bone_name)
    g.add(list(range(len(o.data.vertices))), 1.0, 'REPLACE')
    rigid_parts.append(o)
    return o


# Kopf: Ei-Form mit schmalem Kinn
head_c = Vector((0, 0.0, 1.665))
bpy.ops.mesh.primitive_uv_sphere_add(segments=32, ring_count=18, radius=1.0, location=head_c)
head = bpy.context.active_object
head.name = "HeadMesh"
head.scale = (0.112, 0.124, 0.14)
apply_transform(head)
for v in head.data.vertices:
    t = max(0.0, (head_c.z - v.co.z) / 0.14)
    v.co.x *= 1.0 - 0.38 * t
    if v.co.y < 0:
        v.co.y *= 1.0 - 0.1 * t
set_material(head, "Skin")
smooth(head)
rigid(head, "Head")

for s in (1, -1):
    rigid(sphere("Ear", (0.11 * s, 0.01, 1.66), (0.022, 0.035, 0.045), "Skin"), "Head")
    rigid(sphere("Eye", (0.046 * s, -0.112, 1.685), (0.024, 0.012, 0.036), "Eyes"), "Head")
    rigid(sphere("EyeShine", (0.040 * s, -0.123, 1.698), (0.007, 0.004, 0.009), "EyeWhite", 10, 6), "Head")
    rigid(box("Brow", (0.048 * s, -0.115, 1.737), (0.042, 0.01, 0.011), "HairDark", rot=(0, -0.18 * s, 0)), "Head")
rigid(sphere("Nose", (0, -0.124, 1.648), (0.015, 0.022, 0.02), "Skin", 12, 8), "Head")
rigid(box("Mouth", (0, -0.112, 1.6), (0.032, 0.006, 0.006), "Eyes", rot=(0.15, 0, 0)), "Head")

# Hoodie-Details: Bund, Kragen, Kapuze, Reissverschluss, Aermelbuendchen
rigid(torus("Collar", (0, 0.004, 1.468), 0.068, 0.018, "JacketTrim", scale=(1, 0.9, 1)), "Chest")
hood = sphere("Hood", (0, 0.085, 1.47), (0.135, 0.075, 0.085), "Jacket")
rigid(hood, "Chest")
rigid(box("Zipper", (0, -0.118, 1.23), (0.012, 0.01, 0.42), "Accent"), "Chest")
for s, side in ((1, "L"), (-1, "R")):
    w, e = V("wrist_" + side), V("elbow_" + side)
    rigid(torus("Cuff", w + (e - w).normalized() * 0.02, 0.047, 0.013, "JacketTrim", rot=rot_to(e - w)), "LowerArm_" + side)
    # Hand als Faeustling mit Daumen
    ht = V("handtip_" + side)
    hand = sphere("Hand", w + (ht - w) * 0.6, (0.043, 0.032, 0.066), "Skin", 16, 10, rot=rot_to(ht - w))
    rigid(hand, "Hand_" + side)
    rigid(sphere("Thumb", w + (ht - w) * 0.4 + Vector((-0.014 * s, -0.03, 0)), (0.018, 0.018, 0.034), "Skin", 10, 6,
                 rot=rot_to(ht - w)), "Hand_" + side)
    # Hosenbein-Umschlag
    a, k = V("ankle_" + side), V("knee_" + side)
    # Dicke Sneaker: Obermaterial, Sohle, Schnuersenkel, Seitenstreifen
    sx = 0.115 * s
    rigid(box("Shoe", (sx, -0.045, 0.068), (0.118, 0.29, 0.1), "Shoes", bevel=0.035, subsurf=1), "Foot_" + side)
    rigid(box("Sole", (sx, -0.045, 0.02), (0.13, 0.305, 0.04), "Sole", bevel=0.015), "Foot_" + side)
    for i in range(3):
        rigid(box("Lace", (sx, -0.095 - i * 0.035, 0.118 - i * 0.006), (0.07, 0.012, 0.008), "Sole"), "Foot_" + side)
    rigid(box("Stripe", (sx + 0.06 * s, -0.04, 0.075), (0.006, 0.14, 0.028), "Accent", rot=(0.25, 0, 0)), "Foot_" + side)

# In den Koerper einfuegen (ein Mesh = ein SkinnedMeshRenderer)
select_only(body, *rigid_parts, active=body)
bpy.ops.object.join()
body.name = "Body"

# ---------------------------------------------------------------- Kopf-Varianten (eigene Meshes, ein-/ausblendbar)

head_variants = []


def variant(name, parts):
    select_only(*parts, active=parts[0])
    if len(parts) > 1:
        bpy.ops.object.join()
    o = bpy.context.active_object
    o.name = name
    g = o.vertex_groups.new(name="Head")
    g.add(list(range(len(o.data.vertices))), 1.0, 'REPLACE')
    o.parent = arm
    o.matrix_parent_inverse = arm.matrix_world.inverted()
    mod = o.modifiers.new("Armature", 'ARMATURE')
    mod.object = arm
    head_variants.append(o)
    return o


def hair_cap(name, material, center_z=1.695, cut_front=True, scale=(0.121, 0.133, 0.115), cut_below=1.6):
    o = sphere(name, (0, 0.012, center_z), scale, material, 32, 18)
    if cut_front:
        delete_verts(o, lambda c: (c.y < -0.05 and c.z < 1.735) or c.z < cut_below)
    return o


# 0: Struwwelhaare
parts = [hair_cap("HairCap", "HeadWear")]
spikes = [((0.0, 0.03, 1.80), (-0.5, 0, 0)), ((0.06, 0.02, 1.79), (-0.4, 0.4, 0)), ((-0.06, 0.02, 1.79), (-0.4, -0.4, 0)),
          ((0.03, 0.09, 1.76), (-1.1, 0.2, 0)), ((-0.03, 0.09, 1.76), (-1.1, -0.2, 0)), ((0.0, -0.06, 1.79), (0.5, 0, 0)),
          ((0.09, 0.05, 1.74), (-0.8, 0.9, 0)), ((-0.09, 0.05, 1.74), (-0.8, -0.9, 0))]
for i, (loc, r) in enumerate(spikes):
    parts.append(cone("Spike", loc, 0.042, 0.13, "HeadWear", r))
variant("Head_Hair", parts)

# 1: Cap mit Schirm
cap = hair_cap("Cap", "HeadWear", 1.705, cut_front=False, scale=(0.126, 0.136, 0.105))
delete_verts(cap, lambda c: c.z < 1.70)
brim = cylinder("Brim", (0, -0.13, 1.708), 0.085, 0.014, "HeadWear", rot=(0.12, 0, 0), scale=(1, 1.15, 1))
button = sphere("Button", (0, 0.01, 1.812), (0.016, 0.016, 0.01), "HeadWear", 10, 6)
hair_under = hair_cap("HairUnder", "HairDark", 1.68, scale=(0.118, 0.13, 0.1), cut_below=1.62)
variant("Head_Cap", [cap, brim, button, hair_under])

# 2: Beanie mit Bommel
beanie = sphere("Beanie", (0, 0.006, 1.70), (0.127, 0.137, 0.135), "HeadWear", 32, 18)
delete_verts(beanie, lambda c: c.z < 1.69)
fold = torus("Fold", (0, 0.006, 1.705), 0.124, 0.02, "HeadWear", scale=(1, 1.08, 1))
pom = sphere("Pom", (0, 0.01, 1.85), (0.037, 0.037, 0.037), "JacketTrim", 14, 8)
variant("Head_Beanie", [beanie, fold, pom])

# 3: Kopfhoerer ueber kurzen Haaren
band = torus("Band", (0, 0, 1.665), 0.135, 0.012, "HeadWear", rot=(0, math.pi / 2, 0))
delete_verts(band, lambda c: c.z < 1.67)
cups = [cylinder("Cup", (0.122 * s, 0.0, 1.655), 0.048, 0.04, "HeadWear", rot=(0, math.pi / 2, 0)) for s in (1, -1)]
pads = [cylinder("Pad", (0.142 * s, 0.0, 1.655), 0.034, 0.012, "Eyes", rot=(0, math.pi / 2, 0)) for s in (1, -1)]
short_hair = hair_cap("ShortHair", "HairDark", 1.69, scale=(0.12, 0.132, 0.112))
extra = [cone("Spike", (0.0, 0.04, 1.79), 0.04, 0.1, "HairDark", (-0.6, 0, 0)),
         cone("Spike", (0.0, -0.04, 1.80), 0.04, 0.1, "HairDark", (0.4, 0, 0))]
variant("Head_Phones", [band] + cups + pads + [short_hair] + extra)

print("DRIFTSKATE body verts:", len(body.data.vertices), "variants:", [v.name for v in head_variants])

# ---------------------------------------------------------------- Export

select_only(arm, body, *head_variants, active=arm)
bpy.ops.export_scene.fbx(
    filepath=OUT_FBX, use_selection=True, object_types={'ARMATURE', 'MESH'},
    apply_scale_options='FBX_SCALE_ALL', axis_forward='-Z', axis_up='Y', bake_space_transform=False,
    add_leaf_bones=False, bake_anim=False, mesh_smooth_type='FACE', use_mesh_modifiers=True,
    primary_bone_axis='Y', secondary_bone_axis='X', armature_nodetype='NULL', use_armature_deform_only=False)
print("DRIFTSKATE exported", OUT_FBX)

# ---------------------------------------------------------------- Vorschau-Bild (optional)

if OUT_PNG:
    for v in head_variants:
        v.hide_render = v.name != "Head_Cap"
    cam_data = bpy.data.cameras.new("Cam")
    cam = bpy.data.objects.new("Cam", cam_data)
    coll.objects.link(cam)
    cam.location = (1.35, -2.35, 1.35)
    cam.rotation_euler = (math.radians(82), 0, math.radians(30))
    cam_data.lens = 50
    scene.camera = cam
    light_data = bpy.data.lights.new("Sun", 'SUN')
    light_data.energy = 3.0
    light = bpy.data.objects.new("Sun", light_data)
    light.rotation_euler = (math.radians(50), math.radians(10), math.radians(30))
    coll.objects.link(light)
    scene.render.engine = 'BLENDER_WORKBENCH'
    scene.display.shading.light = 'STUDIO'
    scene.display.shading.color_type = 'MATERIAL'
    scene.display.shading.show_object_outline = True
    scene.render.resolution_x = 900
    scene.render.resolution_y = 1100
    scene.render.filepath = OUT_PNG
    bpy.ops.render.render(write_still=True)
    print("DRIFTSKATE preview", OUT_PNG)
