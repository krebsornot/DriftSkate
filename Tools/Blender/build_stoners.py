# Baut die Stoner-NPCs (Jojo, Kalle, Luna) mit Sofa und die Gassen-Crew (Nix, Moe) mit Bierkasten fuer die Stadt.
# Jede Figur hat ein humanoides Skelett in T-Pose (Unity "Humanoid", passt auch fuer Mixamo-Animationen):
#   Hips, Spine, Chest, Neck, Head, Jaw, Eye_L/R, Lid_L/R (Oberlider), Shoulder_L/R, UpperArm, LowerArm, Hand,
#   Thumb1-3, Index1-3, Middle1-3, Ring1-3, Pinky1-3, UpperLeg, LowerLeg, Foot, Toes
# und eigene Animationen (NLA-Spuren -> FBX-Takes): Idle, Laugh und eigene (Smoke, Snack, Vibe, Lookout, Deal, Count).
#
# Aufruf:  blender --background --factory-startup --python build_stoners.py -- <ausgabe-ordner> [vorschau-ordner] [nur-figur]
#
# Koordinaten wie bei build_skater.py: Meter, Z oben, Figur schaut nach -Y (Unity: +Z), +X = linke Koerperseite.
# Materialnamen: <Figur>_<Rolle>__<RRGGBB>[_flat|_glow]; Unity liest daraus Farbe, "_flat" = ohne Outline, "_glow" = leuchtet.

import bpy
import bmesh
import math
import random
import sys
from mathutils import Vector, Matrix, Quaternion, Euler, noise
from mathutils.bvhtree import BVHTree

argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
OUT_DIR = argv[0] if len(argv) > 0 else "."
PREVIEW_DIR = argv[1] if len(argv) > 1 else None
ONLY = argv[2].lower() if len(argv) > 2 else None
FPS = 30

scene = bpy.context.scene
scene.render.fps = FPS


def log(*a):
    print("STONERS", *a)


# ================================================================ Szene, Materialien, Grundformen

for o in list(bpy.data.objects):
    bpy.data.objects.remove(o, do_unlink=True)
for coll in (bpy.data.meshes, bpy.data.materials, bpy.data.armatures, bpy.data.actions, bpy.data.curves):
    for d in list(coll):
        coll.remove(d)

FLAT = {"EyeWhite", "EyeRed", "Iris", "Pupil", "Shine", "Teeth", "MouthIn", "Tongue", "Lens", "Lash", "Lid", "Brows", "Nails",
        "PatchInk", "Gold", "Strings", "Laces", "Stubble", "Goatee", "Button"}
GLOW = {"Ember"}
MATS = {}


class Char:
    name = ""
    pal = {}


CUR = Char()


def hex_rgb(h):
    return tuple(int(h[i:i + 2], 16) / 255.0 for i in (0, 2, 4))


def M(role):
    suffix = "_flat" if role in FLAT else "_glow" if role in GLOW else ""
    hexcol = CUR.pal.get(role, "FF00FF").upper()
    key = f"{CUR.name}_{role}__{hexcol}{suffix}"
    if key not in MATS:
        m = bpy.data.materials.new(key)
        m.diffuse_color = (*hex_rgb(CUR.pal.get(role, "FF00FF")), 1.0)
        MATS[key] = m
    return MATS[key]


def link(o):
    scene.collection.objects.link(o)
    return o


def from_bm(name, bm):
    me = bpy.data.meshes.new(name)
    bm.to_mesh(me)
    bm.free()
    return link(bpy.data.objects.new(name, me))


def select_only(*objs, active=None):
    for o in scene.objects:
        o.select_set(False)
    for o in objs:
        o.select_set(True)
    bpy.context.view_layer.objects.active = active or objs[0]


def smooth(o):
    n = len(o.data.polygons)
    o.data.polygons.foreach_set("use_smooth", [True] * n)
    o.data.update()


def set_mat(o, role):
    o.data.materials.clear()
    o.data.materials.append(M(role))
    for p in o.data.polygons:
        p.material_index = 0
    return o


def apply_mods(o):
    select_only(o)
    bpy.ops.object.convert(target='MESH')
    return o


def join(objs, name=None):
    objs = [o for o in objs if o is not None]
    if len(objs) > 1:
        select_only(*objs, active=objs[0])
        bpy.ops.object.join()
    o = objs[0]
    if name:
        o.name = name
    return o


def delete(o):
    bpy.data.objects.remove(o, do_unlink=True)


def transform_bm(bm, loc, size, rot=(0, 0, 0)):
    m = Matrix.Translation(Vector(loc)) @ Euler(rot).to_matrix().to_4x4() @ Matrix.Diagonal((size[0], size[1], size[2], 1.0))
    bmesh.ops.transform(bm, matrix=m, verts=bm.verts)


def ellipsoid(name, loc, size, role="Skin", segs=32, rings=20, rot=(0, 0, 0)):
    bm = bmesh.new()
    bmesh.ops.create_uvsphere(bm, u_segments=segs, v_segments=rings, radius=1.0)
    transform_bm(bm, loc, size, rot)
    o = from_bm(name, bm)
    set_mat(o, role)
    smooth(o)
    return o


def box(name, loc, size, role, rot=(0, 0, 0), bevel=0.0, subsurf=0):
    bm = bmesh.new()
    bmesh.ops.create_cube(bm, size=1.0)
    transform_bm(bm, loc, size, rot)
    o = from_bm(name, bm)
    if bevel > 0:
        b = o.modifiers.new("Bevel", 'BEVEL')
        b.width = bevel
        b.segments = 3
    if subsurf > 0:
        s = o.modifiers.new("Sub", 'SUBSURF')
        s.levels = subsurf
        s.render_levels = subsurf
    if bevel > 0 or subsurf > 0:
        apply_mods(o)
    set_mat(o, role)
    smooth(o)
    return o


def cylinder(name, loc, r1, r2, depth, role, rot=(0, 0, 0), segs=24, scale=(1, 1, 1), caps=True):
    bm = bmesh.new()
    bmesh.ops.create_cone(bm, cap_ends=caps, cap_tris=False, segments=segs, radius1=r1, radius2=r2, depth=depth)
    transform_bm(bm, loc, scale, rot)
    o = from_bm(name, bm)
    set_mat(o, role)
    smooth(o)
    return o


def torus(name, loc, major, minor, role, rot=(0, 0, 0), scale=(1, 1, 1), arc=(0.0, 2 * math.pi), seg=36, mseg=10):
    """Ring um die lokale Z-Achse; arc schneidet einen Bogen aus (Winkel ab +X)."""
    full = abs(arc[1] - arc[0] - 2 * math.pi) < 1e-4
    n = seg if full else seg + 1
    bm = bmesh.new()
    rings = []
    for i in range(n):
        a = arc[0] + (arc[1] - arc[0]) * i / (seg if full else seg)
        c = Vector((math.cos(a), math.sin(a), 0.0))
        ring = []
        for j in range(mseg):
            b = 2 * math.pi * j / mseg
            p = c * (major + minor * math.cos(b)) + Vector((0, 0, minor * math.sin(b)))
            ring.append(bm.verts.new(p))
        rings.append(ring)
    for i in range(n if full else n - 1):
        r0, r1 = rings[i], rings[(i + 1) % n]
        for j in range(mseg):
            bm.faces.new((r0[j], r0[(j + 1) % mseg], r1[(j + 1) % mseg], r1[j]))
    if not full:
        for ring in (rings[0], rings[-1]):
            bm.faces.new(ring if ring is rings[-1] else list(reversed(ring)))
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    transform_bm(bm, loc, scale, rot)
    o = from_bm(name, bm)
    set_mat(o, role)
    smooth(o)
    return o


def rot_to(direction):
    """Euler, die die lokale Z-Achse in 'direction' dreht."""
    return Vector((0, 0, 1)).rotation_difference(Vector(direction).normalized()).to_euler()


def capsule(name, a, b, r, role, segs=16):
    a, b = Vector(a), Vector(b)
    d = b - a
    parts = [cylinder(name, (a + b) / 2, r, r, d.length, role, rot=rot_to(d), segs=segs, caps=False),
             ellipsoid(name, a, (r, r, r), role, segs, 10), ellipsoid(name, b, (r, r, r), role, segs, 10)]
    return join(parts, name)


def skin_tubes(name, chains, role, subsurf=2, branch=0.5):
    """Weiche Roehren entlang von Punktketten [(punkt, radius), ...] (Skin-Modifier)."""
    verts, edges, radii = [], [], []
    for chain in chains:
        base = len(verts)
        for i, (p, r) in enumerate(chain):
            verts.append(tuple(p))
            radii.append(r)
            if i > 0:
                edges.append((base + i - 1, base + i))
    me = bpy.data.meshes.new(name)
    me.from_pydata(verts, edges, [])
    o = link(bpy.data.objects.new(name, me))
    mod = o.modifiers.new("Skin", 'SKIN')
    mod.branch_smoothing = branch
    mod.use_smooth_shade = True
    sv = o.data.skin_vertices[0].data
    for i, r in enumerate(radii):
        sv[i].radius = (r, r) if not isinstance(r, tuple) else r
    base = 0
    for chain in chains:
        sv[base].use_root = True
        base += len(chain)
    if subsurf:
        s = o.modifiers.new("Sub", 'SUBSURF')
        s.levels = subsurf
        s.render_levels = subsurf
    apply_mods(o)
    set_mat(o, role)
    smooth(o)
    return o


def remesh(o, voxel, smooth_factor=0.5, smooth_iter=0):
    r = o.modifiers.new("Remesh", 'REMESH')
    r.mode = 'VOXEL'
    r.voxel_size = voxel
    r.adaptivity = 0.0
    r.use_smooth_shade = True
    if smooth_iter > 0:
        s = o.modifiers.new("Smooth", 'SMOOTH')
        s.factor = smooth_factor
        s.iterations = smooth_iter
    apply_mods(o)
    smooth(o)
    return o


def boolean_diff(o, cutter):
    b = o.modifiers.new("Bool", 'BOOLEAN')
    b.operation = 'DIFFERENCE'
    b.object = cutter
    b.solver = 'EXACT'
    apply_mods(o)
    delete(cutter)
    return o


def solidify(o, thickness, offset=-1.0):
    s = o.modifiers.new("Solid", 'SOLIDIFY')
    s.thickness = thickness
    s.offset = offset
    s.use_even_offset = False
    apply_mods(o)
    return o


def subdivide(o, levels):
    s = o.modifiers.new("Sub", 'SUBSURF')
    s.levels = levels
    s.render_levels = levels
    apply_mods(o)
    return o


def shell(src, name, pred, offset, thickness, role, amp=0.0, freq=60.0, seed=0.0):
    """Teil der Oberflaeche von 'src' (Flaechen mit pred(mitte, normale)) als abstehende Schicht: Taschen, Baerte, Haare."""
    bm = bmesh.new()
    bm.from_mesh(src.data)
    bm.normal_update()
    doomed = [f for f in bm.faces if not pred(f.calc_center_median(), f.normal)]
    bmesh.ops.delete(bm, geom=doomed, context='FACES')
    loose = [v for v in bm.verts if not v.link_faces]
    bmesh.ops.delete(bm, geom=loose, context='VERTS')
    if not bm.faces:
        bm.free()
        return None
    bm.normal_update()
    for v in bm.verts:
        d = offset
        if amp:
            d += amp * noise.noise(v.co * freq + Vector((seed, seed * 0.7, 0)))
        v.co += v.normal * d
    o = from_bm(name, bm)
    if thickness > 0:
        solidify(o, thickness)
    set_mat(o, role)
    smooth(o)
    return o


_BVH = {}


def bvh_of(o):
    vs = o.data.vertices
    key = (o.name, len(vs), round(sum(vs[i].co.x + vs[i].co.z for i in range(0, len(vs), 97)), 5))
    if key not in _BVH:
        _BVH[key] = BVHTree.FromPolygons([v.co for v in o.data.vertices], [p.vertices[:] for p in o.data.polygons])
    return _BVH[key]


def surf(o, x, z, offset=0.0, side=-1):
    """Punkt auf der Oberflaeche von o: Strahl von vorn (side=-1) oder hinten (side=1) bei (x, z)."""
    loc, nor, idx, dist = bvh_of(o).ray_cast(Vector((x, side * 2.0, z)), Vector((0, -side, 0)))
    if loc is None:
        return Vector((x, 0, z))
    return loc + nor * offset


def band(body, center, axis, ref, height, offset, thickness, role, n=56, rows=4, ripple=0.0, ripple_n=0, name="Band"):
    """Ring um einen Koerperteil (Bund, Buendchen, Streifen): Strahlen von innen nach aussen treffen die Oberflaeche."""
    axis = Vector(axis).normalized()
    ref = Vector(ref)
    u = (ref - axis * axis.dot(ref)).normalized()
    v = axis.cross(u)
    bvh = bvh_of(body)
    bm = bmesh.new()
    grid = []
    for r in range(rows):
        h = -height / 2 + height * r / (rows - 1)
        c = Vector(center) + axis * h
        row = []
        for i in range(n):
            a = 2 * math.pi * i / n
            d = u * math.cos(a) + v * math.sin(a)
            hit = bvh.ray_cast(c, d, 0.5)
            p = hit[0] if hit[0] is not None else c + d * 0.05
            extra = ripple * math.sin(a * ripple_n) if ripple_n else 0.0
            row.append(bm.verts.new(p + d * (offset + extra)))
        grid.append(row)
    for r in range(rows - 1):
        for i in range(n):
            bm.faces.new((grid[r][i], grid[r][(i + 1) % n], grid[r + 1][(i + 1) % n], grid[r + 1][i]))
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    o = from_bm(name, bm)
    # Normalen nach aussen
    me = o.data
    c0 = Vector(center)
    if sum((p.center - c0).dot(p.normal) for p in me.polygons) < 0:
        for p in me.polygons:
            p.flip()
    if thickness > 0:
        solidify(o, thickness, offset=-1.0)
    set_mat(o, role)
    smooth(o)
    return o


def rounded_rect(w, h, r, taper=0.0, per_corner=6):
    """Umriss (gegen den Uhrzeigersinn) eines Rechtecks mit runden Ecken; taper verbreitert unten."""
    pts = []
    corners = [(w / 2 - r, h / 2 - r, 0), (-w / 2 + r, h / 2 - r, 90), (-w / 2 + r, -h / 2 + r, 180), (w / 2 - r, -h / 2 + r, 270)]
    for cx, cy, a0 in corners:
        for i in range(per_corner + 1):
            a = math.radians(a0 + 90 * i / per_corner)
            x, y = cx + r * math.cos(a), cy + r * math.sin(a)
            x *= 1 + taper * (0.5 - y / h)
            pts.append((x, y))
    return pts


def ellipse_outline(w, h, n=32):
    return [(w / 2 * math.cos(2 * math.pi * i / n), h / 2 * math.sin(2 * math.pi * i / n)) for i in range(n)]


def patch(body, center, u, v, outline, ray, offset, thickness, role, rings=5, name="Patch", puff=0.0):
    """Aufgesetztes Stueck (Tasche, Klappe, Aufnaeher) mit glattem Umriss, auf die Oberflaeche projiziert."""
    u, v, ray = Vector(u).normalized(), Vector(v).normalized(), Vector(ray).normalized()
    bvh = bvh_of(body)
    bm = bmesh.new()

    def place(a, b, t):
        w = Vector(center) + u * a + v * b
        hit = bvh.ray_cast(w - ray * 0.3, ray, 0.6)
        if hit[0] is None:
            return w
        return hit[0] + hit[1] * (offset + puff * (1 - t * t))

    cv = bm.verts.new(place(0, 0, 0))
    ringsv = []
    for k in range(1, rings + 1):
        t = k / rings
        ringsv.append([bm.verts.new(place(a * t, b * t, t)) for a, b in outline])
    n = len(outline)
    for i in range(n):
        bm.faces.new((cv, ringsv[0][i], ringsv[0][(i + 1) % n]))
    for k in range(rings - 1):
        for i in range(n):
            bm.faces.new((ringsv[k][i], ringsv[k + 1][i], ringsv[k + 1][(i + 1) % n], ringsv[k][(i + 1) % n]))
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    o = from_bm(name, bm)
    if sum(p.normal.dot(-ray) for p in o.data.polygons) < 0:
        for p in o.data.polygons:
            p.flip()
    if thickness > 0:
        solidify(o, thickness, offset=-1.0)
    set_mat(o, role)
    smooth(o)
    return o


def decimate(o, ratio):
    if ratio >= 1.0:
        return o
    d = o.modifiers.new("Dec", 'DECIMATE')
    d.decimate_type = 'COLLAPSE'
    d.ratio = ratio
    apply_mods(o)
    smooth(o)
    return o


def cut_ellipsoid(name, loc, size, role, keep, segs=64, rings=40, rot=(0, 0, 0)):
    """Ellipsoid, von dem nur die Punkte mit keep(punkt) bleiben (Muetzen, Haar-Kappen)."""
    o = ellipsoid(name, loc, size, role, segs, rings, rot)
    delete_verts_fn(o, lambda c: not keep(c))
    return o


def hair_cap(sp, J, name, role, front_line, back_line, puff, thick, grooves=0.0, ears=True):
    """Haar-Kappe aus einem Ellipsoid ueber dem Schaedel, unten am Haaransatz abgeschnitten, Kanten rund."""
    k, p, sz, local = head_frame(sp, J)
    f = sp["face"]
    pred = scalp_pred(sp, J, front_line, back_line, ears)
    o = cut_ellipsoid(name, p(0, 0.012, 0.024), sz(0.090 * f.get("cw", 1) + puff, 0.104 + puff, 0.108 + puff), role,
                      lambda c: pred(c, None), 72, 44)
    solidify(o, thick * k, offset=-1.0)
    remesh(o, 0.0026 * k, 0.5, 3)
    decimate(o, 0.3)
    if grooves:
        cen = p(0, 0.02, 0.11)
        displace(o, lambda c, n: grooves * k * math.sin(math.atan2(c.x - cen.x, c.y - cen.y) * 26 + 3 * noise.noise(c * (40 / k))))
    set_mat(o, role)
    return o


def crescent(r_out, r_in, a0, a1, n=16, dy=0.0):
    pts = [(r_out * math.cos(math.radians(a0 + (a1 - a0) * i / n)), r_out * math.sin(math.radians(a0 + (a1 - a0) * i / n)) + dy) for i in range(n + 1)]
    pts += [(r_in * math.cos(math.radians(a1 - (a1 - a0) * i / n)), r_in * math.sin(math.radians(a1 - (a1 - a0) * i / n)) + dy) for i in range(n + 1)]
    return pts


def assign_regions(o, fn):
    """Materialzonen: fn(mitte, normale) -> Rolle."""
    roles = []
    idx = []
    for p in o.data.polygons:
        r = fn(p.center, p.normal)
        if r not in roles:
            roles.append(r)
        idx.append(roles.index(r))
    o.data.materials.clear()
    for r in roles:
        o.data.materials.append(M(r))
    o.data.polygons.foreach_set("material_index", idx)
    o.data.update()


def displace(o, fn):
    """Verschiebt jeden Punkt entlang seiner Normalen um fn(punkt, normale)."""
    me = o.data
    me.update()
    for v in me.vertices:
        d = fn(v.co, v.normal)
        if d:
            v.co += v.normal * d
    me.update()


def smoothstep(a, b, x):
    if a == b:
        return 1.0 if x >= b else 0.0
    t = max(0.0, min(1.0, (x - a) / (b - a)))
    return t * t * (3 - 2 * t)


def gauss(d, w):
    return math.exp(-(d * d) / (w * w))


def seg_param(p, a, b):
    ab = b - a
    t = max(0.0, min(1.0, (p - a).dot(ab) / max(1e-9, ab.length_squared)))
    return (a + ab * t - p).length, t


# ================================================================ Gewichte

def weight_rigid(o, bone):
    g = o.vertex_groups.new(name=bone)
    g.add(list(range(len(o.data.vertices))), 1.0, 'REPLACE')
    return o


def weight_fn(o, fn):
    """fn(punkt) -> {knochen: gewicht}"""
    groups = {}
    for v in o.data.vertices:
        ws = fn(v.co)
        tot = sum(ws.values()) or 1.0
        for b, w in ws.items():
            if w <= 0:
                continue
            if b not in groups:
                groups[b] = o.vertex_groups.new(name=b)
            groups[b].add([v.index], w / tot, 'REPLACE')
    return o


def weights_from(o, src):
    """Gewichte von der naechsten Stelle der Oberflaeche von src uebernehmen (Kleidung auf dem Koerper)."""
    sme = src.data
    names = {g.index: g.name for g in src.vertex_groups}
    vw = [{names[g.group]: g.weight for g in v.groups if g.weight > 0.001} for v in sme.vertices]
    bvh = BVHTree.FromPolygons([v.co for v in sme.vertices], [p.vertices[:] for p in sme.polygons])
    groups = {}
    for v in o.data.vertices:
        loc, nor, fi, dist = bvh.find_nearest(v.co)
        if fi is None:
            continue
        acc, tot = {}, 0.0
        for vi in sme.polygons[fi].vertices:
            w = 1.0 / ((sme.vertices[vi].co - loc).length + 1e-4)
            tot += w
            for b, gw in vw[vi].items():
                acc[b] = acc.get(b, 0.0) + gw * w
        s = sum(acc.values())
        if s <= 0:
            continue
        for b, val in acc.items():
            if val / s < 0.01:
                continue
            if b not in groups:
                groups[b] = o.vertex_groups.get(b) or o.vertex_groups.new(name=b)
            groups[b].add([v.index], val / s, 'REPLACE')
    return o


def nearest_bone_weights(o, arm, allowed):
    """Fuer Punkte ohne Gewicht: naechster erlaubter Knochen."""
    segs = [(b.name, b.head_local, b.tail_local) for b in arm.data.bones if b.name in allowed]
    fixed = 0
    for v in o.data.vertices:
        if any(g.weight > 0.001 for g in v.groups):
            continue
        best, bn = 1e9, None
        for n, h, t in segs:
            d, _ = seg_param(v.co, h, t)
            if d < best:
                best, bn = d, n
        g = o.vertex_groups.get(bn) or o.vertex_groups.new(name=bn)
        g.add([v.index], 1.0, 'REPLACE')
        fixed += 1
    return fixed


# ================================================================ Proportionen

FINGERS = [  # name, y am Knoechel, x am Knoechel, Gliedlaengen, Radius
    ("Index", -0.031, 0.795, (0.042, 0.026, 0.021), 0.0093),
    ("Middle", -0.010, 0.800, (0.046, 0.029, 0.023), 0.0096),
    ("Ring", 0.011, 0.796, (0.043, 0.027, 0.022), 0.0090),
    ("Pinky", 0.030, 0.786, (0.034, 0.021, 0.019), 0.0080),
]


def make_joints(sp):
    s, sw, hw = sp["s"], sp["sw"], sp["hw"]
    J = {}

    def P(x, y, z):
        return Vector((x, y, z)) * s

    nd = sp.get("neck_drop", 0.012)
    J.update(pelvis=P(0, 0, 0.93), waist=P(0, 0, 1.03), belly=P(0, -0.01, 1.16), chest=P(0, 0, 1.30),
             upperchest=P(0, 0.005, 1.40), neck=P(0, 0.012, 1.50 - nd * 0.4), necktop=P(0, 0.014, 1.575 - nd),
             head=P(0, 0.005, 1.67 - nd))
    for sg, side in ((1, "L"), (-1, "R")):
        shx = 0.185 * sw

        def A(x, y, z):
            return P(sg * (shx + (x - 0.185)), y, z)

        J["clav_" + side] = P(sg * 0.035, -0.005, 1.43)
        J["clavmid_" + side] = P(sg * 0.10 * sw, 0.0, 1.435)
        J["shoulder_" + side] = A(0.185, 0.01, 1.435)
        J["bicep_" + side] = A(0.32, 0.015, 1.432)
        J["elbow_" + side] = A(0.455, 0.02, 1.43)
        J["forearm_" + side] = A(0.58, 0.01, 1.428)
        J["wrist_" + side] = A(0.70, 0.0, 1.425)
        for fname, ky, kx, lens, _ in FINGERS:
            x = kx
            J[f"{fname}0_{side}"] = A(x, ky, 1.418)
            for i, ln in enumerate(lens):
                x += ln
                J[f"{fname}{i + 1}_{side}"] = A(x, ky, 1.418)
        J["Thumb0_" + side] = A(0.722, -0.022, 1.410)
        J["Thumb1_" + side] = A(0.752, -0.048, 1.402)
        J["Thumb2_" + side] = A(0.778, -0.064, 1.398)
        J["Thumb3_" + side] = A(0.800, -0.076, 1.396)
        J["hip_" + side] = P(sg * 0.10 * hw, 0, 0.88)
        J["thigh_" + side] = P(sg * 0.105 * hw, -0.01, 0.70)
        J["knee_" + side] = P(sg * 0.11 * hw, -0.012, 0.50)
        J["calf_" + side] = P(sg * 0.11 * hw, 0.005, 0.32)
        J["ankle_" + side] = P(sg * 0.112 * hw, 0.02, 0.10)
        J["ball_" + side] = P(sg * 0.112 * hw, -0.10, 0.025)
        J["toe_" + side] = P(sg * 0.112 * hw, -0.165, 0.025)
    return J


def head_frame(sp, J):
    """Kopf-Hilfen: Punkt im Kopfraum (Meter fuer 1,80 m) -> Welt."""
    k = sp["s"] * sp.get("head", 1.0)
    H = J["head"]

    def p(x, y, z):
        return H + Vector((x, y, z)) * k

    def sz(x, y, z):
        return Vector((x, y, z)) * k

    def local(co):
        return (Vector(co) - H) / k

    return k, p, sz, local


# ================================================================ Koerper

def build_body(sp, J):
    tr = sp["r"]
    nodes = [("pelvis", 0.150 * tr["pelvis"]), ("waist", 0.135 * tr["waist"]), ("belly", 0.140 * tr["belly"]),
             ("chest", 0.160 * tr["chest"]), ("upperchest", 0.145 * tr["chest"]),
             ("neck", 0.052 * tr["neck"]), ("necktop", 0.049 * tr["neck"])]
    edges = [("pelvis", "waist"), ("waist", "belly"), ("belly", "chest"), ("chest", "upperchest"),
             ("upperchest", "neck"), ("neck", "necktop")]
    for side in ("L", "R"):
        nodes += [("clavmid_" + side, 0.066 * tr["chest"]), ("shoulder_" + side, 0.064 * tr["arm"]),
                  ("bicep_" + side, 0.056 * tr["arm"]), ("elbow_" + side, 0.050 * tr["arm"]),
                  ("forearm_" + side, 0.050 * tr["fore"]), ("wrist_" + side, 0.044 * tr["fore"]),
                  ("hip_" + side, 0.118 * tr["leg"]), ("thigh_" + side, 0.104 * tr["leg"]),
                  ("knee_" + side, 0.088 * tr["knee"]), ("calf_" + side, 0.085 * tr["shin"]),
                  ("ankle_" + side, 0.078 * tr["ankle"])]
        edges += [("upperchest", "clavmid_" + side), ("clavmid_" + side, "shoulder_" + side),
                  ("shoulder_" + side, "bicep_" + side), ("bicep_" + side, "elbow_" + side),
                  ("elbow_" + side, "forearm_" + side), ("forearm_" + side, "wrist_" + side),
                  ("pelvis", "hip_" + side), ("hip_" + side, "thigh_" + side), ("thigh_" + side, "knee_" + side),
                  ("knee_" + side, "calf_" + side), ("calf_" + side, "ankle_" + side)]
    s = sp["s"]
    names = [n for n, _ in nodes]
    me = bpy.data.meshes.new("BodyMesh")
    me.from_pydata([tuple(J[n]) for n in names], [(names.index(a), names.index(b)) for a, b in edges], [])
    body = link(bpy.data.objects.new(CUR.name + "_Body", me))
    mod = body.modifiers.new("Skin", 'SKIN')
    mod.branch_smoothing = 0.75
    mod.use_smooth_shade = True
    for i, (_, r) in enumerate(nodes):
        body.data.skin_vertices[0].data[i].radius = (r * s, r * s)
    body.data.skin_vertices[0].data[0].use_root = True
    sub = body.modifiers.new("Sub", 'SUBSURF')
    sub.levels = 3
    sub.render_levels = 3
    apply_mods(body)

    # Rumpf: flacher als breit, dazu Bauch, Brust, Po je nach Figur
    shape = sp.get("shape", {})
    soft = sp.get("soft_flat", False)  # weicher Uebergang zu den Armen (sonst Kante an der Schulter bei schmalen Figuren)
    for v in body.data.vertices:
        x, y, z = v.co
        zn = z / s
        if soft:
            arm_t = smoothstep(0.17 * s * sp["sw"], 0.3 * s * sp["sw"], abs(x))
            f_chest = 0.74 + 0.26 * arm_t
            f_hip = 0.86 + 0.14 * smoothstep(0.2 * s * sp["hw"], 0.3 * s * sp["hw"], abs(x))
            t_z = smoothstep(0.93, 0.99, zn) * (1 - smoothstep(1.47, 1.53, zn))
            f = f_hip + (f_chest - f_hip) * t_z if zn > 0.78 else 1.0
            v.co.y = y * (f if zn > 0.78 and zn < 1.53 else 1.0)
        elif abs(x) < 0.25 * s * sp["sw"] and 0.96 < zn < 1.5:
            v.co.y = y * 0.74
        elif abs(x) < 0.27 * s * sp["hw"] and 0.78 < zn <= 0.96:
            v.co.y = y * 0.86
        x, y, z = v.co
        zn = z / s
        if y < 0 and abs(x) < 0.2 * s:
            v.co.y -= shape.get("belly", 0.0) * s * gauss(zn - 1.11, 0.12) * gauss(x / s, 0.13)
            for sg in (1, -1):
                v.co.y -= shape.get("bust", 0.0) * s * gauss(zn - 1.31, 0.055) * gauss(x / s - sg * 0.072, 0.05)
        if y > 0 and abs(x) < 0.2 * s:
            for sg in (1, -1):
                v.co.y += shape.get("butt", 0.0) * s * gauss(zn - 0.87, 0.07) * gauss(x / s - sg * 0.07, 0.07)
        if 0.95 < zn < 1.12 and abs(x) > 0.08 * s:
            v.co.x *= 1.0 - shape.get("waist_in", 0.0) * gauss(zn - 1.04, 0.06)
        # Handgelenk/Unterarm-Ende flacher (die Hand sitzt darin)
        if abs(v.co.x) > 0.64 * s * sp["sw"]:
            wz = J["wrist_L"].z
            v.co.z = wz + (v.co.z - wz) * 0.78
    body.data.update()
    smooth(body)
    return body


def build_hand(sp, J, side):
    s = sp["s"]
    sg = 1 if side == "L" else -1
    fr = sp["r"].get("finger", 1.0)
    w = J["wrist_" + side]
    k_mid = J["Middle0_" + side]
    palm_c = (w + k_mid) * 0.5 + Vector((0, 0.002, 0.0)) * s
    palm = box("Palm", palm_c, Vector((0.085, 0.082, 0.028)) * s * Vector((1, fr, fr)), "Skin", bevel=0.012 * s, subsurf=2)
    # Handballen zum Daumen, schmaler zum Handgelenk
    for v in palm.data.vertices:
        t = (v.co.x - w.x) * sg / (0.1 * s)
        if t < 0.35:
            v.co.y = palm_c.y + (v.co.y - palm_c.y) * (0.82 + 0.5 * max(0.0, t))
    pad = ellipsoid("ThumbPad", J["Thumb0_" + side] + Vector((sg * 0.006, 0.006, -0.004)) * s, Vector((0.022, 0.016, 0.012)) * s * fr, "Skin")
    chains = []
    for fname, _, _, lens, r in FINGERS:
        pts = [J[f"{fname}{i}_{side}"] for i in range(4)]
        rr = r * s * fr
        chains.append([(pts[0] - Vector((sg * 0.012 * s, 0, 0)), rr * 1.05), (pts[0], rr * 1.05), (pts[1], rr * 0.98),
                       (pts[2], rr * 0.9), (pts[3] - Vector((sg * 0.004 * s, 0, 0)), rr * 0.82)])
    tp = [J[f"Thumb{i}_{side}"] for i in range(4)]
    tr_ = 0.0115 * s * fr
    chains.append([(tp[0], tr_ * 1.3), (tp[1], tr_ * 1.1), (tp[2], tr_), (tp[3] - (tp[3] - tp[2]).normalized() * 0.004 * s, tr_ * 0.9)])
    wrist_stub = [(w - Vector((sg * 0.03 * s, 0, 0)), 0.026 * s * fr), (w + Vector((sg * 0.02 * s, 0, 0)), 0.028 * s * fr)]
    chains.append(wrist_stub)
    fingers = skin_tubes("Fingers", chains, "Skin", subsurf=1)
    hand = join([palm, pad, fingers], CUR.name + "_Hand" + side)
    remesh(hand, 0.0021 * s, 0.5, 3)
    set_mat(hand, "Skin")
    # Knoechel etwas betonen
    for fname, _, _, _, r in FINGERS:
        kn = J[f"{fname}0_{side}"]
        for v in hand.data.vertices:
            d = (v.co - kn).length
            if d < 0.016 * s and v.co.z > kn.z:
                v.co.z += 0.0025 * s * (1 - d / (0.016 * s))
    hand.data.update()
    decimate(hand, 0.4)
    # Fingernaegel
    nails = []
    for fname, _, _, lens, r in FINGERS:
        t3, t2 = J[f"{fname}3_{side}"], J[f"{fname}2_{side}"]
        d = (t3 - t2).normalized()
        c = t3 - d * 0.009 * s + Vector((0, 0, r * s * fr * 0.78))
        nails.append(ellipsoid("Nail", c, Vector((0.0075, 0.0062 * fr, 0.0018)) * s, "Nails", 16, 8))
    d = (tp[3] - tp[2]).normalized()
    c = tp[3] - d * 0.009 * s + Vector((0, 0, tr_ * 0.75))
    nails.append(ellipsoid("Nail", c, Vector((0.008, 0.0068, 0.0018)) * s, "Nails", 16, 8, rot=(0, 0, math.atan2(d.y, d.x))))
    return hand, nails


# ================================================================ Kopf

def build_head(sp, J):
    f = sp["face"]
    k, p, sz, local = head_frame(sp, J)
    parts = [
        ellipsoid("Cranium", p(0, 0.012, 0.022), sz(0.090 * f.get("cw", 1), 0.104, 0.108), "Skin", 48, 32),
        ellipsoid("Face", p(0, -0.022, -0.03), sz(0.074 * f.get("jw", 1), 0.078, 0.088 * f.get("fl", 1)), "Skin", 48, 32),
        ellipsoid("Chin", p(0, -0.062 + f.get("chin_y", 0.0), -0.094 * f.get("fl", 1)), sz(0.025 * f.get("chin", 1), 0.026, 0.024), "Skin"),
        ellipsoid("Brow", p(0, -0.07, 0.034), sz(0.062, 0.02, 0.014), "Skin"),
        ellipsoid("JawBar", p(0, -0.018, -0.066 * f.get("fl", 1)), sz(0.07 * f.get("jw", 1), 0.052, 0.034), "Skin", 48, 24),
        ellipsoid("Neck", p(0, 0.012, -0.10), sz(0.048 * f.get("nw", 1), 0.05, 0.06), "Skin"),
    ]
    for sg in (1, -1):
        parts += [
            ellipsoid("Cheek", p(sg * 0.042, -0.052, -0.018), sz(0.026, 0.019, 0.021) * f.get("cheek", 1), "Skin"),
            ellipsoid("Bag", p(sg * 0.033, -0.077, -0.0135), sz(0.0145, 0.0075, 0.0055) * f.get("bags", 1), "Skin"),
            ellipsoid("Ear", p(sg * 0.088, 0.008, -0.002), sz(0.011, 0.024, 0.033) * f.get("ear", 1), "Skin", rot=(0.15, 0, sg * 0.25)),
        ]
    if f.get("double_chin"):
        parts.append(ellipsoid("Chin2", p(0, -0.035, -0.105), sz(0.058, 0.05, 0.03), "Skin"))
    nose = f.get("nose", 1.0)
    nl = f.get("nose_len", 1.0)
    parts += [
        ellipsoid("NoseBridge", p(0, -0.089, 0.006), sz(0.0105, 0.016 * nose, 0.03 * nl), "Skin", rot=(-0.35, 0, 0)),
        ellipsoid("NoseTip", p(0, -0.106 - 0.004 * (nose - 1), -0.024 - 0.006 * (nl - 1) + f.get("nose_up", 0)), sz(0.016, 0.015, 0.015) * nose, "Skin"),
    ]
    for sg in (1, -1):
        parts.append(ellipsoid("NoseWing", p(sg * 0.0155 * nose, -0.095, -0.031 - 0.006 * (nl - 1)), sz(0.011, 0.011, 0.009) * nose, "Skin"))
    lip = f.get("lips", 1.0)
    parts += [ellipsoid("LipUp", p(0, -0.0915, -0.056), sz(0.025, 0.011, 0.0075 * lip), "Skin"),
              ellipsoid("LipLow", p(0, -0.089, -0.069), sz(0.023, 0.011 * lip, 0.0085 * lip), "Skin")]
    head = join(parts, CUR.name + "_Head")
    remesh(head, 0.0022 * k, 0.6, 7)
    decimate(head, 0.26)

    # Augenhoehlen und Mundspalte
    eyes = {}
    for sg, side in ((1, "L"), (-1, "R")):
        E = p(sg * 0.032, -0.071, 0.004)
        eyes[side] = E
        boolean_diff(head, ellipsoid("Socket", E + sz(0, -0.004, 0), sz(0.0205, 0.0205, 0.0195), "Skin", 32, 20))
    mouth_z = -0.0625
    cut = ellipsoid("MouthCut", p(0, -0.094, mouth_z), sz(0.0235 * f.get("mouth_w", 1), 0.02, 0.0013), "Skin", 32, 16)
    smile = f.get("smile", 0.003)
    for v in cut.data.vertices:
        lx = local(v.co).x
        v.co.z += smile * k * (lx / 0.024) ** 2
    boolean_diff(head, cut)
    smooth(head)

    zone_extra = f.get("zone")

    def head_role(c, n):
        lc = local(c)
        for zc, yc, sx, sy, sz_ in ((-0.056, -0.0915, 0.025, 0.012, 0.0085 * lip), (-0.069, -0.089, 0.023, 0.012, 0.0095 * lip)):
            if ((lc.x / sx) ** 2 + ((lc.y - yc) / (sy * 1.5)) ** 2 + ((lc.z - zc) / (sz_ * 1.25)) ** 2) < 1.0 and lc.y < -0.08:
                return "Lips"
        if zone_extra:
            r = zone_extra(lc)
            if r:
                return r
        return "Skin"

    assign_regions(head, head_role)

    # Kiefer: Unterlippe, Kinn und Unterseite gehen mit
    def jaw_w(co):
        lc = local(co)
        smile_z = mouth_z + smile * (lc.x / 0.024) ** 2 if abs(lc.x) < 0.03 else mouth_z
        front = smoothstep(0.03, -0.01, lc.y)
        below = smoothstep(smile_z + 0.002, smile_z - 0.008, lc.z)
        neck = smoothstep(-0.085, -0.115, lc.z) * smoothstep(-0.03, 0.0, lc.y)
        w = front * below * (1 - neck)
        return {"Jaw": w, "Head": 1 - w}

    weight_fn(head, jaw_w)

    rigid = []  # (obj, bone)
    for sg, side in ((1, "L"), (-1, "R")):
        E = eyes[side]
        ball = ellipsoid("Eye", E, sz(0.0165, 0.0165, 0.0165), "EyeWhite", 32, 20)
        # rote Aederchen: Unterseite etwas roeter
        ball.data.materials.append(M("EyeRed"))
        for poly in ball.data.polygons:
            lc = local(poly.center) - local(E)
            if lc.z < -0.006 or abs(lc.x) > 0.011:
                poly.material_index = 1
        iris = ellipsoid("Iris", E + sz(0, -0.0152, 0), sz(0.0088, 0.003, 0.0088), "Iris", 24, 12)
        pupil = ellipsoid("Pupil", E + sz(0, -0.0168, 0), sz(0.0045, 0.0018, 0.0045 * 1.0), "Pupil", 16, 8)
        shine = ellipsoid("Shine", E + sz(sg * 0.003 - 0.002, -0.0178, 0.0035), sz(0.0018, 0.001, 0.0018), "Shine", 10, 6)
        rigid.append((join([ball, iris, pupil, shine]), "Eye_" + side))
        # Oberlid (halb zu, das ist der Look): Kugelschale vor dem Auge, Kante dunkel
        lid_edge = f.get("lid", 6.0)
        lid = ellipsoid("Lid", E, sz(0.0182, 0.0182, 0.0182), "Lid", 40, 28)
        delete_verts_fn(lid, lambda c, E=E: _elev(local(c) - local(E)) < math.radians(lid_edge) or (local(c) - local(E)).y > 0.006)
        solidify(lid, 0.0016 * k, offset=1.0)
        lid.data.materials.append(M("Lash"))
        for poly in lid.data.polygons:
            if _elev(local(poly.center) - local(E)) < math.radians(lid_edge + 7):
                poly.material_index = 1
        smooth(lid)
        rigid.append((lid, "Lid_" + side))
        low = ellipsoid("LidLow", E, sz(0.0176, 0.0176, 0.0176), "Skin", 40, 28)
        delete_verts_fn(low, lambda c, E=E: _elev(local(c) - local(E)) > math.radians(-34) or (local(c) - local(E)).y > 0.004)
        solidify(low, 0.0014 * k, offset=1.0)
        rigid.append((low, "Head"))
        # Augenbrauen
        bh = f.get("brow_h", 0.0)
        brow = ellipsoid("Brow", E + sz(sg * 0.002, -0.0145, 0.025 + bh), sz(0.023, 0.0055, 0.0045) * f.get("brow_size", 1.0),
                         "Brows", 24, 12, rot=(0.2, sg * 0.05, sg * (0.12 + f.get("brow_tilt", 0.0))))
        rigid.append((brow, "Head"))
    # Mundinneres, Zaehne, Zunge
    rigid.append((ellipsoid("MouthIn", p(0, -0.072, -0.066), sz(0.024, 0.022, 0.017), "MouthIn"), "Head"))
    up_t = torus("TeethUp", p(0, -0.068, -0.0585), 0.0205 * k, 0.0045 * k, "Teeth", scale=(1, 1, 0.75),
                 arc=(math.radians(200), math.radians(340)), seg=20, mseg=8)
    rigid.append((up_t, "Head"))
    lo_t = torus("TeethLow", p(0, -0.066, -0.0685), 0.019 * k, 0.0042 * k, "Teeth", scale=(1, 1, 0.7),
                 arc=(math.radians(205), math.radians(335)), seg=20, mseg=8)
    rigid.append((lo_t, "Jaw"))
    rigid.append((ellipsoid("Tongue", p(0, -0.066, -0.071), sz(0.016, 0.02, 0.0055), "Tongue"), "Jaw"))
    return head, rigid, eyes, mouth_z


def _elev(v):
    """Hoehenwinkel eines Punkts relativ zur Augenmitte (vorn = -Y)."""
    return math.atan2(v.z, max(1e-6, math.hypot(v.x, v.y)))


def delete_verts_fn(o, pred):
    bm = bmesh.new()
    bm.from_mesh(o.data)
    doomed = [v for v in bm.verts if pred(v.co)]
    bmesh.ops.delete(bm, geom=doomed, context='VERTS')
    bm.to_mesh(o.data)
    bm.free()
    o.data.update()


def scalp_pred(sp, J, front_line=0.05, back_line=-0.075, ears=True):
    k, p, sz, local = head_frame(sp, J)

    def pred(c, n):
        lc = local(c)
        yaw = math.atan2(lc.x, -lc.y)  # 0 = vorn
        line = back_line + (front_line - back_line) * (0.5 + 0.5 * math.cos(yaw))
        if lc.z < line:
            return False
        if ears and abs(lc.x) > 0.078 and -0.04 < lc.z < 0.04 and -0.03 < lc.y < 0.045:
            return False
        if lc.y < -0.07 and lc.z < 0.07:
            return False
        return True

    return pred


def hair_curves(name, strands, role, depth, bevel_res=2, res_u=4):
    """Haarstraehnen/Dreads als Roehren: strands = [[(punkt, radius), ...], ...]."""
    cu = bpy.data.curves.new(name, 'CURVE')
    cu.dimensions = '3D'
    cu.bevel_depth = depth
    cu.bevel_resolution = bevel_res
    cu.use_fill_caps = True
    for st in strands:
        sp_ = cu.splines.new('NURBS')
        sp_.points.add(len(st) - 1)
        for i, (pt, r) in enumerate(st):
            sp_.points[i].co = (pt.x, pt.y, pt.z, 1.0)
            sp_.points[i].radius = r
        sp_.use_endpoint_u = True
        sp_.order_u = 3
        sp_.resolution_u = res_u
    o = link(bpy.data.objects.new(name, cu))
    select_only(o)
    bpy.ops.object.convert(target='MESH')
    o = bpy.context.view_layer.objects.active
    set_mat(o, role)
    smooth(o)
    return o


# ================================================================ Schuhe

def footprint(x, s, L, h, role, wavy):
    """Sohle in Fussform: rund vorn und hinten, Mitte etwas schmaler."""
    toe = cylinder("SoleToe", Vector((x, -0.13 * s * L, h / 2)), 0.053 * s, 0.053 * s, h, role, segs=32)
    heel = cylinder("SoleHeel", Vector((x, 0.045 * s, h / 2)), 0.046 * s, 0.046 * s, h, role, segs=32)
    mid = box("SoleMid", Vector((x, -0.045 * s * L, h / 2)), Vector((0.094 * s, 0.17 * s * L, h)), role)
    sole = join([toe, heel, mid], "Sole")
    remesh(sole, 0.0028 * s, 0.4, 2)
    if wavy:
        displace(sole, lambda c, n: 0.0025 * s * math.sin(c.y / s * 80) if abs(n.z) < 0.6 else 0.0)
    decimate(sole, 0.5)
    set_mat(sole, role)
    return sole


def sneaker(sp, J, side, style):
    s = sp["s"]
    sg = 1 if side == "L" else -1
    a, ball = J["ankle_" + side], J["ball_" + side]
    x = a.x
    L = style.get("len", 1.0)
    sole_h = style.get("sole", 0.028) * s
    parts = []
    if style["kind"] == "slide":
        sock = join([ellipsoid("SockToe", Vector((x, -0.105 * s * L, 0.04 * s + sole_h)), Vector((0.046, 0.07, 0.035)) * s, "Socks"),
                     ellipsoid("SockHeel", Vector((x, 0.02 * s, 0.05 * s + sole_h)), Vector((0.042, 0.055, 0.05)) * s, "Socks"),
                     cylinder("SockCuff", Vector((x, 0.02 * s, 0.13 * s)), 0.048 * s, 0.046 * s, 0.12 * s, "Socks")])
        remesh(sock, 0.0025 * s, 0.5, 3)
        set_mat(sock, "Socks")
        # Rippenbund oben
        displace(sock, lambda c, n: 0.0012 * s * math.sin(math.atan2(c.y - 0.02 * s, c.x - x) * 26) if c.z > 0.12 * s else 0.0)
        # Zehen andeuten
        for i in range(4):
            parts.append(ellipsoid("Toe", Vector((x + sg * (0.02 - i * 0.014) * s, -0.168 * s * L, 0.03 * s + sole_h)), Vector((0.009, 0.012, 0.01)) * s, "Socks"))
        decimate(sock, 0.45)
        sole = footprint(x, s, L * 1.03, sole_h, "Sole", False)
        mid = Vector((x, -0.085 * s * L, 0.035 * s + sole_h))
        strap = band(sock, mid, (0, 1, 0), (0, 0, 1), 0.075 * s, 0.003 * s, 0.006 * s, "Strap", n=40, rows=4, name="Strap")
        stripe = band(sock, mid, (0, 1, 0), (0, 0, 1), 0.016 * s, 0.0095 * s, 0.002 * s, "Accent", n=40, rows=3, name="StrapStripe")
        parts += [sock, sole, strap, stripe]
    else:
        up = join([ellipsoid("Toe", Vector((x, -0.105 * s * L, 0.028 * s + sole_h)), Vector((0.052, 0.078 * L, 0.03)) * s, "Shoes"),
                   ellipsoid("Mid", Vector((x, -0.035 * s, 0.045 * s + sole_h)), Vector((0.055, 0.08, 0.05)) * s, "Shoes"),
                   ellipsoid("Heel", Vector((x, 0.035 * s, 0.055 * s + sole_h)), Vector((0.047, 0.05, 0.058)) * s, "Shoes"),
                   cylinder("Collar", Vector((x, 0.02 * s, 0.12 * s + sole_h * 0.5)), 0.05 * s, 0.047 * s, 0.07 * s, "Shoes")])
        remesh(up, 0.0026 * s, 0.5, 3)
        decimate(up, 0.2)
        set_mat(up, "Shoes")
        top = up.data.vertices
        zmax = max(v.co.z for v in top)
        # Kragen polstern, Toe-Cap und Seitenstreifen als eigene Schicht
        cap = shell(up, "ToeCap", lambda c, n: c.y < -0.125 * s * L and c.z < 0.06 * s + sole_h, 0.0015 * s, 0.0015 * s, style.get("cap", "ShoeCap"))
        stripe = shell(up, "Stripe", lambda c, n: abs(c.x - x) > 0.035 * s and -0.06 * s < c.y < 0.02 * s and
                       abs((c.z - sole_h) / s - 0.035 - 0.25 * (c.y / s + 0.06)) < 0.012, 0.0015 * s, 0.0012 * s, "Accent")
        tongue = ellipsoid("Tongue", Vector((x, -0.035 * s, 0.11 * s + sole_h * 0.6)), Vector((0.03, 0.045, 0.012)) * s, style.get("tongue", "Shoes"), rot=(-0.9, 0, 0))
        laces = [box("Lace", Vector((x, (-0.095 + i * 0.022) * s, (0.077 + i * 0.014) * s + sole_h)), Vector((0.05, 0.007, 0.006)) * s,
                     "Laces", rot=(-0.55, 0, 0)) for i in range(4)]
        sole = footprint(x, s, L, sole_h, "Sole", style.get("wavy", False))
        parts += [up, cap, stripe, tongue, sole] + laces
    shoe = join([p_ for p_ in parts if p_], CUR.name + "_Shoe" + side)

    def w(co):
        t = smoothstep(-0.075 * s, -0.115 * s, co.y)
        return {"Foot_" + side: 1 - t, "Toes_" + side: t}

    weight_fn(shoe, w)
    return shoe


# ================================================================ Skelett

def build_armature(sp, J, eyes):
    k, p, sz, local = head_frame(sp, J)
    s = sp["s"]
    arm_data = bpy.data.armatures.new(CUR.name + "_Rig")
    arm = link(bpy.data.objects.new(CUR.name + "_Rig", arm_data))
    select_only(arm)
    bpy.ops.object.mode_set(mode='EDIT')
    eb = arm_data.edit_bones

    def bone(name, head, tail, parent=None, connect=False, deform=True):
        b = eb.new(name)
        b.head = Vector(head)
        b.tail = Vector(tail)
        b.use_deform = deform
        if parent:
            b.parent = eb[parent]
            b.use_connect = connect
        return b

    z = lambda v: Vector((0, 0, v)) * s
    bone("Hips", J["pelvis"], z(1.03), None)
    bone("Spine", z(1.03), z(1.15), "Hips", True)
    bone("Spine1", z(1.15), z(1.30), "Spine", True)
    bone(CHEST, z(1.30), z(1.45), "Spine1", True)
    bone("Neck", z(1.45), J["head"] + Vector((0, 0, -0.11)) * k, CHEST, True)
    bone("Head", J["head"] + Vector((0, 0, -0.11)) * k, J["head"] + Vector((0, 0, 0.13)) * k, "Neck", True)
    bone("Jaw", p(0, -0.004, -0.032), p(0, -0.07, -0.095), "Head")
    for side in ("L", "R"):
        E = eyes[side]
        bone("Eye_" + side, E, E + sz(0, -0.03, 0), "Head")
        bone("Lid_" + side, E, E + sz(0, -0.022, 0.012), "Head")
        bone("Shoulder_" + side, J["clav_" + side], J["shoulder_" + side], CHEST)
        bone("UpperArm_" + side, J["shoulder_" + side], J["elbow_" + side], "Shoulder_" + side)
        bone("LowerArm_" + side, J["elbow_" + side], J["wrist_" + side], "UpperArm_" + side, True)
        bone("Hand_" + side, J["wrist_" + side], J["Middle0_" + side], "LowerArm_" + side, True)
        for fname, *_ in FINGERS:
            for i in range(1, 4):
                bone(f"{fname}{i}_{side}", J[f"{fname}{i - 1}_{side}"], J[f"{fname}{i}_{side}"],
                     "Hand_" + side if i == 1 else f"{fname}{i - 1}_{side}", i > 1)
        for i in range(1, 4):
            bone(f"Thumb{i}_{side}", J[f"Thumb{i - 1}_{side}"], J[f"Thumb{i}_{side}"],
                 "Hand_" + side if i == 1 else f"Thumb{i - 1}_{side}", i > 1)
        bone("UpperLeg_" + side, J["hip_" + side], J["knee_" + side], "Hips")
        bone("LowerLeg_" + side, J["knee_" + side], J["ankle_" + side], "UpperLeg_" + side, True)
        bone("Foot_" + side, J["ankle_" + side], J["ball_" + side], "LowerLeg_" + side, True)
        bone("Toes_" + side, J["ball_" + side], J["toe_" + side], "Foot_" + side, True)
    bpy.ops.object.mode_set(mode='OBJECT')
    return arm


FACE_BONES = {"Jaw", "Eye_L", "Eye_R", "Lid_L", "Lid_R"}
# Wirbelsaeule wie bei Mixamo: Spine, Spine1 (Unity: Chest), Spine2 (UpperChest, traegt Hals und Schultern).
# Mit nur einem Brustknochen stuft Unity ihn als UpperChest ohne Chest ein und verwirft ihn samt Animation.
CHEST = "Spine2"


def skin_body(arm, body):
    """Koerper + Haende automatisch gewichten (ohne Gesichtsknochen)."""
    for b in arm.data.bones:
        if b.name in FACE_BONES or b.name.startswith("Toes"):
            b.use_deform = False
    select_only(body, arm, active=arm)
    bpy.ops.object.parent_set(type='ARMATURE_AUTO')
    for b in arm.data.bones:
        b.use_deform = True
    allowed = {b.name for b in arm.data.bones if b.name not in FACE_BONES and not b.name.startswith("Toes")}
    fixed = nearest_bone_weights(body, arm, allowed)
    log(CUR.name, "body verts", len(body.data.vertices), "ohne Auto-Gewicht:", fixed)


def attach(arm, o):
    o.parent = arm
    o.matrix_parent_inverse = arm.matrix_world.inverted()
    mod = o.modifiers.new("Armature", 'ARMATURE')
    mod.object = arm
    return o


# ================================================================ Posen und Animation
#
# Eine Pose ordnet Knochen eine Drehung in Ruhe-Achsen zu, die der Elternknochen mitnimmt
# (lokal = Ruhe^-1 * q * Ruhe). So lassen sich Drehungen anschaulich angeben ("Arm um Y senken")
# und die IK unten rechnet in denselben Achsen.

class Rig:
    def __init__(self, arm):
        self.arm = arm
        self.rest = {}
        for b in arm.data.bones:
            self.rest[b.name] = (b.head_local.copy(), b.matrix_local.to_quaternion(), b.parent.name if b.parent else None, b.tail_local.copy())

    def fk(self, pose):
        D = {}

        def get(n):
            if n in D:
                return D[n]
            h, _, par, _ = self.rest[n]
            q = pose.get(n, Quaternion())
            m = Matrix.Translation(h) @ q.to_matrix().to_4x4() @ Matrix.Translation(-h)
            if par:
                m = get(par) @ m
            elif "loc" in pose:
                m = Matrix.Translation(pose["loc"]) @ m
            D[n] = m
            return m

        for n in self.rest:
            get(n)
        return D

    def point(self, pose, bone, rest_point):
        return self.fk(pose)[bone] @ Vector(rest_point)


def rq(*rots):
    q = Quaternion()
    for ax, deg in rots:
        q = Quaternion(Vector(ax).normalized(), math.radians(deg)) @ q
    return q


def mirror_rots(rots):
    return [((ax[0], -ax[1], -ax[2]), deg) for ax, deg in rots]


X, Y, Z = (1, 0, 0), (0, 1, 0), (0, 0, 1)


def pose(spec, base=None):
    """spec: {"Knochen": [(achse, grad), ...], "Knochen_*": ... (links, rechts gespiegelt), "loc": (x,y,z)}"""
    out = {} if base is None else dict(base)
    for key, val in spec.items():
        if key == "loc":
            out["loc"] = (out.get("loc", Vector()) + Vector(val))
            continue
        if key == "Chest":
            key = CHEST
        targets = [(key[:-1] + "L", val), (key[:-1] + "R", mirror_rots(val))] if key.endswith("*") else [(key, val)]
        for name, rots in targets:
            q = rots if isinstance(rots, Quaternion) else rq(*rots)
            out[name] = q @ out.get(name, Quaternion())
    return out


def frame_rot(a0, b0, a1, b1):
    def basis(a, b):
        a = Vector(a).normalized()
        b = (Vector(b) - a * a.dot(Vector(b))).normalized()
        return Matrix((a, b, a.cross(b))).transposed()

    return (basis(a1, b1) @ basis(a0, b0).transposed()).to_quaternion()


def limb_ik(rig, P, upper, lower, end, target, pole, hinge_rest, hinge_sign, end_frame=None, end_rest_frame=None):
    """Zwei-Glieder-IK in Ruhe-Achsen. end_frame=(richtung, normale) dreht das Endglied (Hand/Fuss) in der Welt."""
    D = rig.fk(P)
    par = rig.rest[upper][2]
    Rpar = D[par].to_quaternion()
    hU, _, _, _ = rig.rest[upper]
    hL, _, _, _ = rig.rest[lower]
    hE, _, _, _ = rig.rest[end]
    s_pos = D[par] @ hU
    L1, L2 = (hL - hU).length, (hE - hL).length
    t = Vector(target)
    dv = t - s_pos
    dist = max(1e-4, min(dv.length, (L1 + L2) * 0.9995))
    dirn = dv.normalized()
    cosA = max(-1.0, min(1.0, (L1 * L1 + dist * dist - L2 * L2) / (2 * L1 * dist)))
    A = math.acos(cosA)
    pole = Vector(pole)
    perp = pole - dirn * pole.dot(dirn)
    perp = perp.normalized() if perp.length > 1e-6 else Vector((0, -1, 0))
    e = s_pos + dirn * L1 * math.cos(A) + perp * L1 * math.sin(A)
    w = s_pos + dirn * dist
    inv = Rpar.inverted()
    a_l = (inv @ (e - s_pos)).normalized()
    f_l = (inv @ (w - e)).normalized()
    u = f_l - a_l * f_l.dot(a_l)
    u = u.normalized() if u.length > 1e-6 else (inv @ perp).normalized()
    n_l = a_l.cross(u) * hinge_sign
    a_rest = (hL - hU).normalized()
    b_rest = Vector(hinge_rest)
    b_rest = (b_rest - a_rest * a_rest.dot(b_rest)).normalized()
    P = dict(P)
    P[upper] = frame_rot(a_rest, b_rest, a_l, n_l)
    theta = math.acos(max(-1.0, min(1.0, a_l.dot(f_l))))
    # Unterarm/-schenkel: Ruherichtung kann leicht vom Oberglied abweichen -> echte Ruhe-Richtung nachfuehren
    a2_rest = (hE - hL).normalized()
    q_fix = a_rest.rotation_difference(a2_rest)
    P[lower] = Quaternion(b_rest, hinge_sign * theta) @ q_fix.inverted()
    if end_frame is not None:
        D2 = rig.fk(P)
        R_lo = D2[lower].to_quaternion()
        Qd = frame_rot(end_rest_frame[0], end_rest_frame[1], end_frame[0], end_frame[1])
        P[end] = R_lo.inverted() @ Qd
    return P


def arm_ik(rig, P, side, wrist, pole, fingers=None, palm=None):
    sg = 1 if side == "L" else -1
    hinge = (0, 0, 1) if side == "L" else (0, 0, -1)
    ef = (Vector(fingers), Vector(palm)) if fingers is not None else None
    return limb_ik(rig, P, "UpperArm_" + side, "LowerArm_" + side, "Hand_" + side, wrist, pole, hinge, -1,
                   ef, ((sg, 0, 0), (0, 0, -1)))


def leg_ik(rig, P, side, ankle, pole, toe_dir=(0, -1, 0)):
    return limb_ik(rig, P, "UpperLeg_" + side, "LowerLeg_" + side, "Foot_" + side, ankle, pole, (1, 0, 0), 1,
                   (Vector(toe_dir), Vector((0, 0, -1))), ((0, -1, 0), (0, 0, -1)))


def curl(amount=1.0, spread=0.0, thumb=1.0, per=None):
    """Finger einrollen (Ruheachsen: um Y fuer links)."""
    spec = {}
    base = {"Index": (14, 20, 12), "Middle": (18, 24, 14), "Ring": (22, 28, 16), "Pinky": (26, 30, 18)}
    for i, (fname, angles) in enumerate(base.items()):
        a = amount * (per.get(fname, 1.0) if per else 1.0)
        for j in range(3):
            rots = [(Y, angles[j] * a)]
            if j == 0 and spread:
                rots.append((Z, spread * (i - 1.5) * -4))
            spec[f"{fname}{j + 1}_*"] = rots
    for j, ang in enumerate((8, 16, 14)):
        spec[f"Thumb{j + 1}_*"] = [((0.8, 0.6, 0.0), ang * thumb)]
    return spec


class Anim:
    def __init__(self, rig, name, length, loop=True):
        self.rig, self.name, self.length, self.loop = rig, name, length, loop
        self.keys = []

    def key(self, frame, P):
        self.keys.append((frame, P))

    def write(self):
        arm = self.rig.arm
        ad = arm.animation_data_create()
        act = bpy.data.actions.new(f"{CUR.name}_{self.name}")
        ad.action = act
        prev = {}
        keys = sorted(self.keys, key=lambda kv: kv[0])
        if self.loop and keys[-1][0] != self.length:
            keys.append((self.length, keys[0][1]))
        for frame, P in keys:
            for pb in arm.pose.bones:
                pb.rotation_mode = 'QUATERNION'
                _, qr, _, _ = self.rig.rest[pb.name]
                q = P.get(pb.name, Quaternion())
                ql = qr.inverted() @ q @ qr
                if pb.name in prev and prev[pb.name].dot(ql) < 0:
                    ql.negate()
                prev[pb.name] = ql.copy()
                pb.rotation_quaternion = ql
                pb.keyframe_insert("rotation_quaternion", frame=frame)
                if pb.name == "Hips":
                    pb.location = qr.inverted() @ Vector(P.get("loc", Vector()))
                    pb.keyframe_insert("location", frame=frame)
        ad.action = None
        track = ad.nla_tracks.new()
        track.name = self.name
        strip = track.strips.new(self.name, 0, act)
        strip.name = self.name
        strip.extrapolation = 'NOTHING'  # ausserhalb der Spur: Ruhepose (T-Pose fuer den Export)
        if hasattr(strip, "action_slot") and len(act.slots) > 0:
            strip.action_slot = act.slots[0]
        return act


def blend(a, b, t):
    """Pose zwischen a und b (fuer Zwischenschritte)."""
    out = {}
    for n in set(a) | set(b):
        if n == "loc":
            out["loc"] = Vector(a.get("loc", Vector())).lerp(Vector(b.get("loc", Vector())), t)
        else:
            out[n] = a.get(n, Quaternion()).slerp(b.get(n, Quaternion()), t)
    return out


# ================================================================ Export und Vorschau

def export_fbx(arm, meshes, path):
    select_only(arm, *meshes, active=arm)
    arm.animation_data_create().action = None
    # Grundhaltung in der Datei = T-Pose (Unity baut daraus den Humanoid-Avatar); vor Bild 0 ist keine Spur aktiv
    scene.frame_set(-20)
    bpy.ops.export_scene.fbx(
        filepath=path, use_selection=True, object_types={'ARMATURE', 'MESH'},
        apply_scale_options='FBX_SCALE_ALL', axis_forward='-Z', axis_up='Y', bake_space_transform=False,
        add_leaf_bones=False, mesh_smooth_type='FACE', use_mesh_modifiers=True,
        primary_bone_axis='Y', secondary_bone_axis='X', armature_nodetype='NULL', use_armature_deform_only=False,
        bake_anim=True, bake_anim_use_all_bones=True, bake_anim_use_nla_strips=True, bake_anim_use_all_actions=False,
        bake_anim_force_startend_keying=True, bake_anim_step=1.0, bake_anim_simplify_factor=0.0)
    log("exportiert", path)


def export_static(objs, path):
    select_only(*objs)
    bpy.ops.export_scene.fbx(
        filepath=path, use_selection=True, object_types={'MESH'}, apply_scale_options='FBX_SCALE_ALL',
        axis_forward='-Z', axis_up='Y', bake_space_transform=False, mesh_smooth_type='FACE', use_mesh_modifiers=True,
        bake_anim=False)
    log("exportiert", path)


def set_frame_pose(arm, action_name, frame):
    """Armature auf ein Bild einer Animation stellen (fuer Vorschau-Bilder)."""
    ad = arm.animation_data
    for t in ad.nla_tracks:
        t.mute = t.name != action_name
    scene.frame_set(int(frame))


# ================================================================ Figuren

def finish_character(sp, J, body, hands, nails, head, head_rigid, extras, arm):
    """Alles zu einem Mesh zusammenfuegen und gewichten."""
    # Koerper + Haende: Auto-Gewichte
    body = join([body] + hands, CUR.name + "_Body")
    skin_body(arm, body)
    parts = [body]
    for n in nails:
        weights_from(n, body)
        parts.append(n)
    parts.append(head)
    for o, b in head_rigid:
        weight_rigid(o, b)
        parts.append(o)
    for o, mode in extras:
        if o is None:
            continue
        if mode == "body":
            weights_from(o, body)
        elif mode == "head":
            weights_from(o, head)
        elif mode == "custom":
            pass
        else:
            weight_rigid(o, mode)
        parts.append(o)
    caps = {"Head": 13000, "Body": 1e9}
    for o in parts:
        base = o.name.split(".")[0].split("_")[-1]
        cap = caps.get(base, 4200)
        if len(o.data.vertices) > cap:
            decimate(o, cap / len(o.data.vertices))
    counts = {}
    for o in parts:
        key = o.name.split(".")[0]
        counts[key] = counts.get(key, 0) + len(o.data.vertices)
    log(CUR.name, "teile:", ", ".join(f"{n}={c}" for n, c in sorted(counts.items(), key=lambda kv: -kv[1])[:14]))
    full = join(parts, CUR.name + "_Body")
    for m in list(full.modifiers):
        full.modifiers.remove(m)
    full.parent = None
    attach(arm, full)
    # leere Gruppen raus, Gewichte pro Punkt normieren
    select_only(full)
    bpy.ops.object.vertex_group_normalize_all(lock_active=False)
    unweighted = sum(1 for v in full.data.vertices if not any(g.weight > 0.001 for g in v.groups))
    log(CUR.name, "verts gesamt", len(full.data.vertices), "ohne Gewicht", unweighted, "materialien", len(full.data.materials))
    return full


# ---------------------------------------------------------------- gemeinsame Kleidungs-Helfer

def sleeve_folds(sp, J, amp, sides=("L", "R"), ankle_amp=0.0, fabric=0.0015, bunch_wrist=1.0):
    s = sp["s"]

    def fn(co, n):
        d = fabric * s * noise.noise(co * (38 / s))
        for side in sides:
            for a, b, env in ((J["shoulder_" + side], J["elbow_" + side], lambda t: 0.6 * smoothstep(0.65, 1.0, t)),
                              (J["elbow_" + side], J["wrist_" + side], lambda t: 0.6 * (1 - smoothstep(0.0, 0.3, t)) + bunch_wrist * smoothstep(0.6, 1.0, t))):
                dist, t = seg_param(co, a, b)
                if dist < 0.09 * s:
                    L = (b - a).length
                    ph = (t * L) / s * 2 * math.pi / 0.032 + 2.0 * noise.noise(co * (20 / s))
                    d += amp * s * env(t) * math.sin(ph)
            if ankle_amp:
                dist, t = seg_param(co, J["knee_" + side], J["ankle_" + side])
                if dist < 0.13 * s:
                    L = (J["knee_" + side] - J["ankle_" + side]).length
                    ph = (t * L) / s * 2 * math.pi / 0.045 + 2.5 * noise.noise(co * (16 / s))
                    d += ankle_amp * s * smoothstep(0.55, 1.0, t) * math.sin(ph)
                    d += 0.4 * ankle_amp * s * (1 - smoothstep(0.0, 0.2, t)) * math.sin(ph * 0.8)
        return d

    return fn


def hoodie_details(sp, J, body, role, trim, pocket=True, strings=True, hood=True, hem_z=0.985, pocket_z=1.1, pocket_puff=0.0015, neck_rib=True):
    s = sp["s"]
    out = []
    if pocket:
        c = surf(body, 0, pocket_z * s)
        out.append((patch(body, Vector((0, c.y, pocket_z * s)), (1, 0, 0), (0, 0, 1), rounded_rect(0.2 * s, 0.15 * s, 0.025 * s, taper=0.25),
                          (0, 1, 0), 0.0045 * s, 0.003 * s, role, rings=8, name="Pocket", puff=pocket_puff * s), "body"))
    # Bund und Buendchen (Rippen)
    out.append((band(body, Vector((0, 0, hem_z * s)), (0, 0, 1), (1, 0, 0), 0.05 * s, 0.006 * s, 0.004 * s, trim,
                     ripple=0.0012 * s, ripple_n=90, name="Hem"), "body"))
    for side in ("L", "R"):
        w, e = J["wrist_" + side], J["elbow_" + side]
        ax = (w - e).normalized()
        out.append((band(body, w - ax * 0.025 * s, ax, (0, 0, 1), 0.045 * s, 0.004 * s, 0.003 * s, trim,
                         ripple=0.001 * s, ripple_n=40, name="Cuff"), "body"))
    if neck_rib:  # (bei dicken Armen laufen die Strahlen in die Aermel; dann die Kapuze/den Kragen nehmen)
        out.append((band(body, Vector((0, 0.012 * s, 1.475 * s)), (0, 0, 1), (1, 0, 0), 0.035 * s, 0.004 * s, 0.003 * s, trim,
                         ripple=0.0008 * s, ripple_n=40, name="NeckRib"), "body"))
    if hood:
        hood_o = join([ellipsoid("Hood", J["neck"] + Vector((0, 0.075, -0.015)) * s, Vector((0.135, 0.07, 0.085)) * s, role, 40, 24),
                       torus("HoodRim", J["neck"] + Vector((0, 0.018, -0.005)) * s, 0.076 * s, 0.017 * s, role, rot=(0.3, 0, 0), scale=(1.15, 1, 1))])
        remesh(hood_o, 0.003 * s, 0.5, 3)
        decimate(hood_o, 0.12)
        displace(hood_o, lambda c, n: 0.004 * s * noise.noise(c * (25 / s)))
        set_mat(hood_o, role)
        out.append((hood_o, "body"))
    if strings:
        for sg in (1, -1):
            a = surf(body, sg * 0.035 * s, J["upperchest"].z + 0.06 * s, 0.004 * s)
            pts = [(a, 1.0), (a + Vector((sg * 0.004, -0.008, -0.06)) * s, 1.0), (a + Vector((sg * 0.006, -0.012, -0.13)) * s, 1.0)]
            out.append((hair_curves("String", [pts], "Strings", 0.0035 * s), "body"))
            out.append((cylinder("Aglet", pts[-1][0] - Vector((0, 0, 0.012)) * s, 0.0045 * s, 0.0045 * s, 0.022 * s, "Accent"), "body"))
    return out


def joint_prop(sp, J, side="R"):
    """Selbstgedrehte zwischen Zeige- und Mittelfinger; Achse = Handflaechen-Normale (Filter zur Handflaeche)."""
    s = sp["s"]
    sg = 1 if side == "L" else -1
    c = (J[f"Index0_{side}"] + J[f"Middle0_{side}"]) * 0.5 + Vector((sg * 0.026, 0, 0)) * s
    filt = c + Vector((0, 0, -0.022)) * s
    tip = c + Vector((0, 0, 0.055)) * s
    paper = cylinder("Joint", (filt + tip) * 0.5, 0.0038 * s, 0.0052 * s, (tip - filt).length, "Paper", segs=12)
    twist = cylinder("Twist", tip + Vector((0, 0, 0.004)) * s, 0.0052 * s, 0.001 * s, 0.008 * s, "Paper", segs=12)
    ember = ellipsoid("Ember", tip + Vector((0, 0, 0.001)) * s, Vector((0.0053, 0.0053, 0.003)) * s, "Ember", 12, 6)
    crutch = cylinder("Crutch", filt + Vector((0, 0, 0.005)) * s, 0.004 * s, 0.004 * s, 0.01 * s, "Crutch", segs=12)
    return join([paper, twist, ember, crutch], CUR.name + "_Joint"), tip


# ---------------------------------------------------------------- JOJO: langer Hippie mit Dreads, Baja-Hoodie, Cargo

def build_jojo():
    CUR.name = "Jojo"
    CUR.pal = {
        "Skin": "E9B996", "Lips": "C98B78", "Nails": "F2CFB4", "EyeWhite": "FBEAE6", "EyeRed": "F3B8B4", "Iris": "6E9E4F",
        "Pupil": "1A1414", "Shine": "FFFFFF", "Lid": "E2AE8A", "Lash": "3A2A22", "Brows": "8A6A3E", "MouthIn": "4A1E26",
        "Teeth": "F4EFE2", "Tongue": "D9707A",
        "Hoodie": "EFE4CC", "Stripe1": "B8483A", "Stripe2": "2F4E7A", "Stripe3": "E0A23A", "HoodieTrim": "D9CBAE",
        "Strings": "3A3226", "Accent": "B8483A", "Pants": "6F734A", "PantsDark": "585B3A",
        "Shoes": "5E4636", "ShoeCap": "4A372A", "Sole": "EDE6D3", "Laces": "EDE6D3",
        "Beanie": "D9A441", "BeanieRib": "C48F33", "Dreads": "B48A55", "DreadsDark": "8E6A3F", "Goatee": "9A7446",
        "Hair": "A07C4C", "Paper": "F4F1E8", "Ember": "FF6A1F", "Crutch": "E8D9B0", "Beads": "7A4A2A", "Beads2": "3E7A6A",
    }
    sp = dict(s=1.045, sw=0.98, hw=0.95, head=0.98,
              r=dict(pelvis=1.0, waist=1.0, belly=0.98, chest=0.98, neck=0.95, arm=1.12, fore=1.08, finger=0.95,
                     leg=1.15, knee=1.15, shin=1.2, ankle=1.3),
              shape=dict(belly=0.0, waist_in=0.0),
              face=dict(cw=0.97, jw=0.92, fl=1.1, chin=1.15, cheek=0.85, nose=1.05, nose_len=1.2, lips=0.95, ear=1.05,
                        smile=0.004, lid=5.0, bags=1.2, brow_h=0.002, brow_tilt=-0.05))
    J = make_joints(sp)
    s = sp["s"]
    k, p, sz, local = head_frame(sp, J)
    body = build_body(sp, J)
    displace(body, sleeve_folds(sp, J, 0.0035, ankle_amp=0.007, bunch_wrist=1.3))
    decimate(body, 0.42)

    def zone(c, n):
        zn = c.z / s
        ax = abs(c.x) / s
        if zn > 1.47 and ax < 0.09:
            return "Skin"
        if zn < 0.99 and ax < 0.3:
            return "Pants"
        return "Pants" if zn < 0.95 else "Hoodie"

    assign_regions(body, zone)
    hands, nails = [], []
    for side in ("L", "R"):
        h, n = build_hand(sp, J, side)
        hands.append(h)
        nails += n
    sp["face"]["zone"] = lambda lc: "Hair" if abs(lc.x) > 0.072 and -0.045 < lc.y < 0.005 and -0.035 < lc.z < 0.03 else None
    head, head_rigid, eyes, mouth_z = build_head(sp, J)
    extras = []
    extras += hoodie_details(sp, J, body, "Hoodie", "HoodieTrim")
    # Baja-Streifen als aufgesetzte Ringe: quer um den Rumpf und um die Aermel
    for zc, role, h in ((1.205, "Stripe1", 0.024), (1.24, "Stripe2", 0.028), (1.275, "Stripe1", 0.024), (1.36, "Stripe3", 0.02)):
        extras.append((band(body, Vector((0, 0, zc * s)), (0, 0, 1), (1, 0, 0), h * s, 0.001 * s, 0.0008 * s, role, n=72, rows=3,
                            name="Stripe"), "body"))
    for side, sg in (("L", 1), ("R", -1)):
        for xc, role, h in ((0.395, "Stripe1", 0.024), (0.428, "Stripe2", 0.028), (0.461, "Stripe1", 0.024), (0.62, "Stripe3", 0.022)):
            x = sg * (0.185 * sp["sw"] + (xc - 0.185)) * s
            extras.append((band(body, Vector((x, 0.012 * s, 1.43 * s)), (1, 0, 0), (0, 0, 1), h * s, 0.001 * s, 0.0008 * s, role,
                                n=48, rows=3, name="Stripe"), "body"))
    # Cargo-Taschen mit Klappe
    for sg in (1, -1):
        c = Vector((sg * 0.1 * s, -0.005 * s, 0.63 * s))
        extras.append((patch(body, c, (0, -1, 0), (0, 0, 1), rounded_rect(0.12 * s, 0.14 * s, 0.014 * s), (-sg, 0, 0),
                             0.004 * s, 0.004 * s, "PantsDark", name="Cargo", puff=0.008 * s), "body"))
        extras.append((patch(body, c + Vector((0, 0, 0.083 * s)), (0, -1, 0), (0, 0, 1), rounded_rect(0.13 * s, 0.04 * s, 0.01 * s),
                             (-sg, 0, 0), 0.013 * s, 0.003 * s, "Pants", name="Flap"), "body"))
    for side in ("L", "R"):
        extras.append((sneaker(sp, J, side, dict(kind="sneaker", sole=0.03, cap="ShoeCap")), "custom"))
    # Holzperlen-Armband links
    w = J["wrist_L"]
    beads = []
    for i in range(14):
        a = 2 * math.pi * i / 14
        c = w + Vector((0.035 * s, 0.027 * s * math.cos(a), 0.027 * s * math.sin(a)))
        beads.append(ellipsoid("Bead", c, Vector((0.0055, 0.0055, 0.0055)) * s, "Beads" if i % 3 else "Beads2", 10, 6))
    extras.append((join(beads), "LowerArm_L"))

    # Ziegenbart (am Kiefer) und Schnurrbart
    goatee = join([ellipsoid("Goatee", p(0, -0.079, -0.113), sz(0.017, 0.014, 0.026), "Goatee", 24, 16),
                   ellipsoid("GoateeTip", p(0, -0.083, -0.136), sz(0.009, 0.009, 0.013), "Goatee", 16, 10),
                   ellipsoid("Soul", p(0, -0.0935, -0.081), sz(0.006, 0.004, 0.007), "Goatee", 12, 8)])
    remesh(goatee, 0.0016 * k, 0.5, 2)
    displace(goatee, lambda c, n: 0.0008 * k * noise.noise(c * (400 / k)))
    set_mat(goatee, "Goatee")
    extras.append((goatee, "Jaw"))
    stache = [ellipsoid("Stache", p(sg * 0.0125, -0.0995, mouth_z + 0.0095), sz(0.0145, 0.0048, 0.0042), "Goatee", 20, 10,
                        rot=(0, sg * 0.38, sg * 0.25)) for sg in (1, -1)]
    extras.append((join(stache), "Head"))
    # Muetze: weite Beanie, nach hinten haengend, Rippenbund
    def band_line(lc):
        yaw = math.atan2(lc.x, -lc.y)
        return 0.0 + 0.055 * (0.5 + 0.5 * math.cos(yaw))

    cap = cut_ellipsoid("BeanieBase", p(0, 0.012, 0.03), sz(0.104, 0.119, 0.12), "Beanie", lambda c: local(c).z > band_line(local(c)))
    solidify(cap, 0.008 * k, offset=-1.0)
    slouch = ellipsoid("Slouch", p(0, 0.08, 0.075), sz(0.086, 0.08, 0.072), "Beanie", 40, 24, rot=(-0.6, 0, 0))
    beanie = join([cap, slouch], "Beanie")
    remesh(beanie, 0.003 * k, 0.5, 4)
    decimate(beanie, 0.22)

    def in_rib(lc):
        return lc.z < band_line(lc) + 0.028 and not (lc.y > 0.05 and lc.z > 0.04)

    displace(beanie, lambda c, n: 0.0016 * k * math.sin(math.atan2(local(c).x, -local(c).y) * 36) if in_rib(local(c))
             else 0.0008 * k * noise.noise(c * (220 / k)))
    assign_regions(beanie, lambda c, n: "BeanieRib" if in_rib(local(c)) else "Beanie")
    extras.append((beanie, "Head"))

    rnd = random.Random(7)
    strands = []
    for i in range(40):
        yaw = math.radians(rnd.uniform(64, 296))
        d = Vector((math.sin(yaw), -math.cos(yaw), 0))
        bl = 0.0 + 0.055 * (0.5 + 0.5 * math.cos(yaw))
        zz = bl - rnd.uniform(0.004, 0.014)
        back = max(0.0, -math.cos(yaw))   # 1 = hinten
        L = (0.12 + 0.24 * back ** 1.5) * rnd.uniform(0.85, 1.15)
        start = p(0, 0.01, zz) + d * 0.097 * k
        pts = []
        for j in range(8):
            t = j / 7
            off = d * (0.012 + 0.03 * t * (0.6 + back)) * k + Vector((0, 0, -L * t)) * k
            off += Vector((rnd.uniform(-1, 1), rnd.uniform(-1, 1), 0)) * 0.006 * k * t
            off += Vector((0, 0.02 * back * t, 0)) * k
            r = 1.0 + 0.18 * math.sin(t * 22 + i) - 0.25 * t
            pts.append((start + off, r))
        strands.append(pts)
    dreads_a = hair_curves("Dreads", strands[0::2], "Dreads", 0.0115 * k, bevel_res=1, res_u=3)
    dreads_b = hair_curves("Dreads2", strands[1::2], "DreadsDark", 0.0105 * k, bevel_res=1, res_u=3)
    neck_z = J["neck"].z

    def dread_w(co):
        t = smoothstep(neck_z - 0.04 * s, neck_z + 0.08 * s, co.z)
        return {"Head": t, "Neck": (1 - t) * 0.4, CHEST: (1 - t) * 0.6}

    for d_ in (dreads_a, dreads_b):
        weight_fn(d_, dread_w)
        extras.append((d_, "custom"))

    joint, tip = joint_prop(sp, J, "R")
    extras.append((joint, "Middle1_R"))

    arm = build_armature(sp, J, eyes)
    # Glut-Spitze als eigener Knochen (fuer Rauch in Unity)
    select_only(arm)
    bpy.ops.object.mode_set(mode='EDIT')
    b = arm.data.edit_bones.new("JointTip")
    b.head = tip
    b.tail = tip + Vector((0, 0, 0.02)) * s
    b.parent = arm.data.edit_bones["Middle1_R"]
    b.use_deform = False
    bpy.ops.object.mode_set(mode='OBJECT')

    full = finish_character(sp, J, body, hands, nails, head, head_rigid, extras, arm)
    rig = Rig(arm)
    jojo_anims(rig, sp, J, mouth_z)
    return arm, full, sp, J


def stand_base(rig, sp, J, lean=0.0):
    s = sp["s"]
    return pose({
        "loc": (0.012 * s * lean, 0, -0.012 * s),
        "Hips": [(Z, 4 * lean), (Y, -2 * lean)],
        "Spine": [(X, 4), (Y, 1.5 * lean)], "Chest": [(X, 5)], "Neck": [(X, 2)], "Head": [(X, 4), (Y, 6)],
        "Shoulder_*": [(Y, 6)],
        "UpperArm_*": [(Y, 74), (X, -5)],
        "LowerArm_*": [(Z, -14)],
        "Hand_*": [(Z, -6), (Y, 4)],
        "UpperLeg_L": [(X, -4), (Y, -4)], "UpperLeg_R": [(X, -1), (Y, 3)],
        "LowerLeg_L": [(X, 10)], "LowerLeg_R": [(X, 3)],
        "Foot_L": [(X, -5), (Z, 8)], "Foot_R": [(X, -2), (Z, -6)],
    })


def head_point(rig, P, rel, sp, J):
    k, p, sz, local = head_frame(sp, J)
    return rig.point(P, "Head", p(*rel))


def jojo_anims(rig, sp, J, mouth_z):
    s = sp["s"]
    base = stand_base(rig, sp, J, lean=1.0)
    base = pose(curl(0.9), base)

    def hold(P, lift=0.0):
        # rechte Hand mit Joint locker vor der Huefte, Handflaeche nach oben/innen
        wrist = Vector((-0.2, -0.25, 1.12 + lift)) * s
        return arm_ik(rig, P, "R", wrist, (-0.3, 0.6, -1.0), fingers=(0.1, -0.95, 0.25), palm=(0.85, 0.0, 0.5))

    idle0 = hold(base)
    lids = lambda deg: {"Lid_*": [(X, deg)]}
    a = Anim(rig, "Idle", 120)
    breath_in = pose({"Chest": [(X, -1.5)], "Shoulder_*": [(Y, -1.2)], "Head": [(X, -1)]}, idle0)
    breath_out = pose({"Chest": [(X, 1.0)], "Head": [(X, 1.5), (Z, 3)]}, idle0)
    a.key(0, idle0)
    a.key(30, pose(lids(0), breath_in))
    a.key(60, pose({"loc": (-0.01 * s, 0, 0), "Hips": [(Z, -3)]}, breath_out))
    a.key(70, pose({"loc": (-0.01 * s, 0, 0), "Hips": [(Z, -3)], "Eye_L": [(Z, 8)], "Eye_R": [(Z, 8)]}, breath_out))
    a.key(76, pose(lids(28), pose({"loc": (-0.01 * s, 0, 0), "Hips": [(Z, -3)]}, breath_out)))
    a.key(82, pose({"loc": (-0.008 * s, 0, 0), "Hips": [(Z, -2)], "Eye_L": [(Z, 4)], "Eye_R": [(Z, 4)]}, breath_out))
    a.key(95, pose(lids(6), breath_in))
    a.write()

    # Zug am Joint: Hand zum Mund, Augen zu, ausatmen mit Kopf in den Nacken
    sm = Anim(rig, "Smoke", 150, loop=False)
    sm.key(0, idle0)
    lean_in = pose({"Head": [(X, 6)], "Neck": [(X, 3)]}, idle0)
    mouth = head_point(rig, lean_in, (-0.018, -0.1, mouth_z - 0.004), sp, J)
    fingers = Vector((0.05, -0.45, 0.89)).normalized()
    palm = Vector((0.92, 0.38, 0.0)).normalized()
    # Filter liegt ~0.123 vom Handgelenk Richtung Finger und 0.022 zur Handflaeche
    wrist = mouth - fingers * 0.123 * s - palm * 0.022 * s
    at_mouth = arm_ik(rig, lean_in, "R", wrist, (-0.4, 0.4, -1.0), fingers=fingers, palm=palm)
    sm.key(14, blend(idle0, at_mouth, 0.5))
    sm.key(28, pose(lids(14), at_mouth))
    sm.key(62, pose(pose(lids(30), {"Chest": [(X, -4)], "Shoulder_*": [(Y, -4)]}), at_mouth))
    away = pose({"Head": [(X, -16)], "Neck": [(X, -6)], "Chest": [(X, -2)], "Jaw": [(X, 7)]}, base)
    away = arm_ik(rig, away, "R", Vector((-0.22, -0.26, 1.18)) * s, (0.2, 0.5, -1.0), fingers=(0.3, -0.8, 0.5), palm=(-0.4, -0.3, 0.85))
    sm.key(78, pose(lids(18), away))
    sm.key(100, pose(pose(lids(10), {"Jaw": [(X, -3)]}), away))
    sm.key(122, blend(away, idle0, 0.6))
    sm.key(150, idle0)
    sm.write()

    laugh(rig, sp, idle0)


def laugh(rig, sp, P0, hand_to_mouth=None):
    la = Anim(rig, "Laugh", 66, loop=False)
    la.key(0, P0)
    up = pose({"Head": [(X, -14)], "Neck": [(X, -4)], "Chest": [(X, -4)], "Jaw": [(X, 14)], "Lid_*": [(X, 22)]}, P0)
    down = pose({"Head": [(X, -6)], "Chest": [(X, 3)], "Spine": [(X, 3)], "Jaw": [(X, 9)], "Lid_*": [(X, 26)]}, P0)
    if hand_to_mouth:
        up, down = hand_to_mouth(up), hand_to_mouth(down)
    la.key(8, up)
    for i, f in enumerate(range(14, 50, 6)):
        la.key(f, down if i % 2 == 0 else up)
    la.key(66, P0)
    la.write()


# ---------------------------------------------------------------- KALLE: Sofa-Kalle mit Bucket-Hat, Sonnenbrille, Chips

COUCH_SEAT = 0.47


def build_kalle():
    CUR.name = "Kalle"
    CUR.pal = {
        "Skin": "EDB592", "Lips": "D08A7A", "Nails": "F6D2BC", "EyeWhite": "FBE8E4", "EyeRed": "F2AFAA", "Iris": "6B4A2E",
        "Pupil": "1A1414", "Shine": "FFFFFF", "Lid": "E6A985", "Lash": "6A3A22", "Brows": "B5643A", "MouthIn": "4A1E26",
        "Teeth": "F4EFE2", "Tongue": "D9707A",
        "Hoodie": "6E5BA8", "HoodieTrim": "5C4B92", "Strings": "F2E9D8", "Accent": "F6C945",
        "Pants": "A3A3AF", "PantsTrim": "8C8C99", "Socks": "F4F2EC", "Sole": "26262E", "Strap": "26262E",
        "Hat": "E3CF9C", "HatBand": "6E5BA8", "HatStitch": "BFAA76", "Hair": "B5643A", "Stubble": "D2946B",
        "Frame": "1E1B24", "Lens": "3B2A55", "Snack": "FF8A2A", "Snack2": "F6C945", "SnackFoil": "D9D9E0",
        "Patch": "F6C945", "PatchInk": "2A2A33",
    }
    sp = dict(s=0.955, sw=1.08, hw=1.12, head=1.04, neck_drop=0.04,
              r=dict(pelvis=1.22, waist=1.38, belly=1.5, chest=1.2, neck=1.3, arm=1.3, fore=1.22, finger=1.15,
                     leg=1.32, knee=1.25, shin=1.15, ankle=0.72),
              shape=dict(belly=0.06, waist_in=0.0, butt=0.02),
              face=dict(cw=1.02, jw=1.12, fl=0.95, chin=0.95, cheek=1.3, nose=1.25, nose_len=0.95, lips=1.1, ear=1.05,
                        smile=0.005, lid=2.0, bags=1.3, double_chin=True, nw=1.25, brow_h=0.0, brow_size=1.1, mouth_w=1.05))
    J = make_joints(sp)
    s = sp["s"]
    k, p, sz, local = head_frame(sp, J)
    body = build_body(sp, J)
    displace(body, sleeve_folds(sp, J, 0.004, ankle_amp=0.0, bunch_wrist=1.5, fabric=0.002))
    decimate(body, 0.42)

    def zone(c, n):
        zn = c.z / s
        ax = abs(c.x) / s
        if zn > 1.47 and ax < 0.1:
            return "Skin"
        return "Pants" if zn < 1.0 else "Hoodie"

    assign_regions(body, zone)
    hands, nails = [], []
    for side in ("L", "R"):
        h, n = build_hand(sp, J, side)
        hands.append(h)
        nails += n
    mz = -0.0625

    def stubble(lc):
        if lc.y > 0.025 or lc.z < -0.15:
            return None
        if abs(lc.x) < 0.03 and lc.y < -0.08 and mz + 0.012 < lc.z < mz + 0.02:
            return "Stubble"  # Oberlippenbart
        line = -0.03 - 0.025 * max(0.0, 1 - abs(lc.x) / 0.06)  # Bartlinie: an den Seiten hoeher, vorn tiefer
        return "Stubble" if lc.z < line else None

    sp["face"]["zone"] = stubble
    head, head_rigid, eyes, mouth_z = build_head(sp, J)
    extras = hoodie_details(sp, J, body, "Hoodie", "HoodieTrim")
    # Smiley-Aufnaeher auf der Brust
    pc = Vector((0.085 * s, 0, 1.33 * s))
    extras.append((patch(body, pc, (1, 0, 0), (0, 0, 1), ellipse_outline(0.062 * s, 0.062 * s, 40), (0, 1, 0), 0.003 * s, 0.002 * s,
                         "Patch", rings=4, name="Patch"), "body"))
    for e in (-0.01, 0.01):
        extras.append((patch(body, pc + Vector((e * s, 0, 0.009 * s)), (1, 0, 0), (0, 0, 1), ellipse_outline(0.007 * s, 0.013 * s, 16),
                             (0, 1, 0), 0.0052 * s, 0.0006 * s, "PatchInk", rings=2, name="SmileyEye"), "body"))
    extras.append((patch(body, pc, (1, 0, 0), (0, 0, 1), crescent(0.019 * s, 0.014 * s, 200, 340, 12, -0.002 * s), (0, 1, 0),
                         0.0052 * s, 0.0006 * s, "PatchInk", rings=1, name="SmileyMouth"), "body"))
    # Jogginghose: Buendchen am Knoechel
    for side in ("L", "R"):
        a, kn = J["ankle_" + side], J["knee_" + side]
        ax_ = (a - kn).normalized()
        extras.append((band(body, kn.lerp(a, 0.86), ax_, (0, -1, 0), 0.06 * s, 0.005 * s, 0.004 * s, "PantsTrim",
                            ripple=0.0012 * s, ripple_n=30, name="PantsCuff"), "body"))
    for side in ("L", "R"):
        extras.append((sneaker(sp, J, side, dict(kind="slide", sole=0.025)), "custom"))
    extras.append((hair_cap(sp, J, "Hair", "Hair", 0.04, -0.075, 0.006, 0.006, grooves=0.0012), "head"))
    # Bucket-Hat
    crown = cylinder("Crown", p(0, 0.006, 0.095), 0.108 * k, 0.09 * k, 0.1 * k, "Hat", segs=40, scale=(1, 1.08, 1))
    top = ellipsoid("CrownTop", p(0, 0.006, 0.145), sz(0.09, 0.097, 0.02), "Hat", 40, 12)
    brim = cylinder("Brim", p(0, 0.006, 0.03), 0.17 * k, 0.106 * k, 0.04 * k, "Hat", segs=48, caps=False, scale=(1, 1.08, 1))
    solidify(brim, 0.004 * k, offset=0)
    hat = join([crown, top, brim], "Hat")
    remesh(hat, 0.0028 * k, 0.4, 2)
    decimate(hat, 0.25)
    set_mat(hat, "Hat")
    displace(hat, lambda c, n: 0.0008 * k * noise.noise(c * (180 / k)))
    hb = torus("HatBand", p(0, 0.006, 0.047), 0.104 * k, 0.006 * k, "HatBand", scale=(1, 1.08, 1.5), seg=48)
    stitches = [torus("Stitch", p(0, 0.006, 0.03 - 0.0048 * i), (0.123 + 0.015 * i) * k, 0.0012 * k, "HatStitch", scale=(1, 1.08, 1), seg=60, mseg=6)
                for i in range(3)]
    extras += [(hat, "Head"), (hb, "Head")] + [(st, "Head") for st in stitches]
    # Runde Sonnenbrille, weit auf die Nase gerutscht
    glasses = []
    for sg in (1, -1):
        E = eyes["L" if sg > 0 else "R"]
        c = E + sz(0, -0.032, -0.011)
        glasses.append(torus("Rim", c, 0.021 * k, 0.0022 * k, "Frame", rot=(math.pi / 2, 0, 0), seg=40, mseg=8))
        glasses.append(ellipsoid("Lens", c + sz(0, 0.0005, 0), sz(0.0205, 0.0022, 0.0205), "Lens", 32, 12))
        hinge = c + sz(sg * 0.021, 0, 0)
        ear = p(sg * 0.089, 0.02, 0.004)
        glasses.append(capsule("Temple", hinge, ear, 0.0018 * k, "Frame", 8))
    bridge = torus("Bridge", (eyes["L"] + eyes["R"]) * 0.5 + sz(0, -0.033, -0.004), 0.009 * k, 0.0018 * k, "Frame",
                   rot=(math.pi / 2, 0, 0), arc=(math.radians(20), math.radians(160)), seg=12, mseg=6)
    glasses.append(bridge)
    extras.append((join(glasses), "Head"))

    arm = build_armature(sp, J, eyes)
    rig = Rig(arm)
    poses = kalle_poses(rig, sp, J, mouth_z)
    # Chips-Tuete: in der Sitzpose in die linke Hand legen und auf die Ruhepose zurueckrechnen
    P_hold = poses["idle"]
    D = rig.fk(P_hold)
    hand_m = D["Hand_L"]
    wl = hand_m @ rig.rest["Hand_L"][0]
    hq = hand_m.to_quaternion()
    fdir = hq @ Vector((1, 0, 0))
    pdir = hq @ Vector((0, 0, -1))
    bag_c = wl + fdir * 0.05 * s + pdir * 0.045 * s + Vector((0, 0, 0.05)) * s
    bag = box("Bag", bag_c, Vector((0.06, 0.17, 0.2)) * s, "Snack", bevel=0.02 * s, subsurf=2)
    displace(bag, lambda c, n: 0.004 * s * noise.noise(c * (35 / s)) + 0.01 * s * max(0.0, 1 - abs((c.z - bag_c.z) / (0.1 * s))) * 0.6)
    bag_band = shell(bag, "BagBand", lambda c, n: abs(c.z - bag_c.z) < 0.03 * s, 0.0015 * s, 0.001 * s, "Snack2")
    crimp = box("Crimp", bag_c + Vector((0, 0, 0.105)) * s, Vector((0.012, 0.165, 0.02)) * s, "SnackFoil", bevel=0.004 * s)
    chips = [ellipsoid("Chip", bag_c + Vector((0.0, rnd_y * s, 0.097 * s)), Vector((0.016, 0.022, 0.004)) * s, "Snack2", 12, 6,
                       rot=(0.3, 0.6, rnd_y * 8)) for rnd_y in (-0.03, 0.01, 0.035)]
    bag_o = join([bag, bag_band, crimp] + chips, "Kalle_Chips")
    inv = hand_m.inverted()
    for v in bag_o.data.vertices:
        v.co = inv @ v.co
    bag_o.data.update()
    extras.append((bag_o, "Hand_L"))

    full = finish_character(sp, J, body, hands, nails, head, head_rigid, extras, arm)
    kalle_anims(rig, sp, J, mouth_z, poses, bag_c + Vector((0, 0, 0.1)) * s)
    return arm, full, sp, J


def kalle_poses(rig, sp, J, mouth_z):
    s = sp["s"]
    # Sitzen: Becken ueber der Sitzflaeche, Oberkoerper laesst sich ins Sofa sinken, Kopf nach vorn zu den Chips
    hip_h = COUCH_SEAT - 0.025 + 0.1 * s
    drop = hip_h - J["pelvis"].z + 0.05 * s
    P = pose({
        "loc": (0, 0.0, drop),
        "Hips": [(X, -16)],
        "Spine": [(X, -6)], "Chest": [(X, 2)], "Neck": [(X, 10)], "Head": [(X, 12), (Y, -5)],
        "Shoulder_*": [(Y, 8)],
    })
    for side, sg in (("L", 1), ("R", -1)):
        ankle = Vector((sg * 0.19, -0.5, 0.1)) * s
        P = leg_ik(rig, P, side, ankle, (sg * 0.35, -1.0, 0.3), toe_dir=(sg * 0.25, -1, 0))
    P = pose(curl(0.8), P)
    # links: Tuete auf dem Oberschenkel, rechts: Hand auf dem Bauch
    P = arm_ik(rig, P, "L", Vector((0.16, -0.33, 0.66)) * s, (1.0, 0.3, -0.4), fingers=(-0.2, -0.95, 0.25), palm=(-0.95, 0.1, -0.1))
    rest_r = Vector((-0.02, -0.25, 0.86)) * s
    P = arm_ik(rig, P, "R", rest_r, (-1.0, 0.4, -0.5), fingers=(0.95, -0.1, -0.2), palm=(0.05, 0.95, -0.15))
    return {"idle": P}


def kalle_anims(rig, sp, J, mouth_z, poses, bag_top):
    s = sp["s"]
    P0 = poses["idle"]
    lids = lambda deg: {"Lid_*": [(X, deg)]}
    a = Anim(rig, "Idle", 132)
    inhale = pose({"Spine": [(X, -1.5)], "Chest": [(X, -1.5)], "Head": [(X, -2)]}, P0)
    a.key(0, P0)
    a.key(36, inhale)
    a.key(66, pose({"Head": [(Z, 8), (Y, -3)]}, P0))
    a.key(84, pose(lids(30), pose({"Head": [(Z, 8)]}, P0)))
    a.key(90, pose(lids(4), pose({"Head": [(Z, 7)]}, P0)))
    a.key(104, inhale)
    a.write()

    # Snack: rechte Hand in die Tuete, zum Mund, kauen
    grab = arm_ik(rig, pose({"Head": [(X, 4)]}, P0), "R", bag_top + Vector((-0.02, 0.0, 0.07)) * s, (-1.0, 0.2, 0.3),
                  fingers=(0.2, -0.3, -0.93), palm=(0.9, 0.0, 0.25))
    grab = pose(curl(0.5), grab)
    eat_base = pose({"Head": [(X, -2)], "Neck": [(X, -4)]}, P0)
    mouth = head_point(rig, eat_base, (0, -0.1, mouth_z), sp, J)
    fingers = Vector((0.6, -0.2, 0.78)).normalized()
    palm = Vector((0.1, 0.98, -0.1)).normalized()
    eat = arm_ik(rig, eat_base, "R", mouth - fingers * 0.12 * s - palm * 0.015 * s + Vector((0, -0.02, -0.035)) * s,
                 (-1.0, 0.3, -0.6), fingers=fingers, palm=palm)
    eat = pose(curl(1.3), eat)
    sn = Anim(rig, "Snack", 150, loop=False)
    sn.key(0, P0)
    sn.key(18, grab)
    sn.key(26, pose(curl(0.5), grab))
    sn.key(46, pose({"Jaw": [(X, 10)]}, eat))
    sn.key(56, pose({"Jaw": [(X, 2)]}, eat))
    back = pose({"Head": [(X, 2)]}, P0)
    for i, f in enumerate(range(70, 126, 9)):
        sn.key(f, pose({"Jaw": [(X, 7 if i % 2 == 0 else 1)], "Head": [(X, 1 if i % 2 else 3)]}, back))
    sn.key(150, P0)
    sn.write()
    laugh(rig, sp, P0)


# ---------------------------------------------------------------- LUNA: pastellpinke Haare, Flanell, Schlaghose

def build_luna():
    CUR.name = "Luna"
    CUR.pal = {
        "Skin": "B07552", "Lips": "8E4E44", "Nails": "C9A7F2", "EyeWhite": "FBEAE6", "EyeRed": "F3B8B4", "Iris": "5A3A22",
        "Pupil": "140F0F", "Shine": "FFFFFF", "Lid": "A06A4A", "Lash": "1E1414", "Brows": "2E1E18", "MouthIn": "3E1A22",
        "Teeth": "F4EFE2", "Tongue": "D06A76",
        "Flannel": "2E6F73", "Flannel2": "1F2E4A", "FlannelLine": "E8C25A", "Top": "F3EEDC",
        "Jeans": "8FB0D9", "JeansDark": "6E92C2", "Button": "E8C25A", "Shoes": "F7F4EE", "ShoeCap": "C9A7F2",
        "Sole": "F7F4EE", "Laces": "F7F4EE", "Accent": "C9A7F2", "Hair": "F4A6C8", "HairDark": "D9799F", "Tie": "F6C945",
        "Gold": "E8C25A", "Frame": "E8C25A", "Lens": "F2A541", "Phones": "2A2A33", "Phones2": "C9A7F2",
    }
    sp = dict(s=0.925, sw=0.88, hw=1.06, head=0.97,
              r=dict(pelvis=1.04, waist=0.84, belly=0.86, chest=0.92, neck=0.85, arm=1.0, fore=0.78, finger=0.88,
                     leg=1.06, knee=1.02, shin=1.2, ankle=1.35),
              shape=dict(bust=0.035, butt=0.03, waist_in=0.1),
              face=dict(cw=0.98, jw=0.86, fl=0.97, chin=0.85, chin_y=0.008, cheek=0.95, nose=0.85, nose_len=0.88, nose_up=0.003, lips=1.02,
                        ear=0.92, smile=0.004, lid=4.0, bags=0.9, brow_h=0.003, brow_size=0.9, brow_tilt=0.08))
    J = make_joints(sp)
    s = sp["s"]
    k, p, sz, local = head_frame(sp, J)
    body = build_body(sp, J)
    displace(body, sleeve_folds(sp, J, 0.003, ankle_amp=0.004, bunch_wrist=0.0))

    def plaid(u, v):
        a = int(math.floor(u / 0.045)) % 2
        b = int(math.floor(v / 0.045)) % 2
        if abs((u / 0.045) % 3 - 1.5) < 0.12 or abs((v / 0.045) % 3 - 1.5) < 0.12:
            return "FlannelLine"
        return "Flannel" if (a + b) % 2 == 0 else "Flannel2"

    def zone(c, n):
        zn = c.z / s
        ax = abs(c.x) / s
        if zn > 1.465 and ax < 0.09:
            return "Skin"
        if zn < 1.02:
            return "Jeans"
        if ax > 0.42:  # hochgekrempelt: Unterarme frei
            return "Skin"
        front = c.y < -0.01 * s and ax < 0.075
        if front and zn < 1.16:
            return "Skin"
        if front:
            return "Top"
        if ax > 0.25:
            return plaid(ax, math.atan2(c.z - J["shoulder_L"].z, c.y) * 0.06 / s)
        return plaid(c.x / s + 1.0, zn)

    assign_regions(body, zone)
    hands, nails = [], []
    for side in ("L", "R"):
        h, n = build_hand(sp, J, side)
        hands.append(h)
        nails += n
    head, head_rigid, eyes, mouth_z = build_head(sp, J)
    extras = []
    # offenes Hemd: Kanten vorn, Kragen, Aermel-Umschlag; Jeans-Bund mit Knopf
    for sg in (1, -1):
        extras.append((patch(body, Vector((sg * 0.083 * s, 0, 1.215 * s)), (1, 0, 0), (0, 0, 1), rounded_rect(0.034 * s, 0.42 * s, 0.008 * s),
                             (0, 1, 0), 0.005 * s, 0.003 * s, "Flannel", rings=3, name="Placket"), "body"))
    collar = band(body, Vector((0, 0.008 * s, 1.445 * s)), (0, 0, 1), (1, 0, 0), 0.055 * s, 0.008 * s, 0.0, "Flannel2", n=64, name="Collar")
    delete_verts_fn(collar, lambda c: c.y < 0 and abs(c.x) < 0.045 * s)
    solidify(collar, 0.003 * s, offset=-1.0)
    extras.append((collar, "body"))
    for side, sg in (("L", 1), ("R", -1)):
        e = J["elbow_" + side]
        extras.append((band(body, e - Vector((sg * 0.02 * s, 0, 0)), (1, 0, 0), (0, 0, 1), 0.05 * s, 0.006 * s, 0.004 * s, "Flannel2",
                            ripple=0.0015 * s, ripple_n=7, name="Roll"), "body"))
    extras.append((band(body, Vector((0, 0, 1.018 * s)), (0, 0, 1), (1, 0, 0), 0.04 * s, 0.004 * s, 0.003 * s, "JeansDark", n=72, name="Waistband"), "body"))
    extras.append((cylinder("Button", surf(body, 0, 1.018 * s, 0.008 * s), 0.007 * s, 0.007 * s, 0.004 * s, "Button", rot=(math.pi / 2, 0, 0)), "body"))
    for side in ("L", "R"):
        extras.append((sneaker(sp, J, side, dict(kind="sneaker", sole=0.05, wavy=True, cap="ShoeCap", len=0.95)), "custom"))
    # Haare: Kappe mit Straehnen-Rillen, Dutt mit Haargummi, Straehnen ins Gesicht
    extras.append((hair_cap(sp, J, "Hair", "Hair", 0.055, -0.085, 0.009, 0.007, grooves=0.0018), "head"))
    bun_c = p(0, 0.035, 0.122)
    rnd = random.Random(3)
    bun = join([ellipsoid("Bun", bun_c + sz(rnd.uniform(-0.02, 0.02), rnd.uniform(-0.02, 0.02), rnd.uniform(-0.01, 0.02)),
                          sz(1, 1, 1) * rnd.uniform(0.032, 0.042), "Hair", 24, 14) for _ in range(6)])
    remesh(bun, 0.0028 * k, 0.5, 3)
    decimate(bun, 0.3)
    displace(bun, lambda c, n: 0.003 * k * math.sin(math.atan2(c.x - bun_c.x, c.y - bun_c.y) * 7 + (c.z - bun_c.z) / k * 120))
    set_mat(bun, "Hair")
    tie = torus("Tie", bun_c + sz(0, 0, -0.03), 0.026 * k, 0.005 * k, "Tie", seg=30)
    extras += [(bun, "Head"), (tie, "Head")]
    strands = []
    for sg in (1, -1):
        for j in range(2):
            st = p(sg * (0.045 + j * 0.014), -0.075 + j * 0.012, 0.065)
            pts = [(st, 1.0), (p(sg * (0.07 + j * 0.01), -0.078 + j * 0.01, 0.02), 1.1),
                   (p(sg * (0.082 + j * 0.008), -0.066 + j * 0.01, -0.035), 1.0), (p(sg * (0.078 + j * 0.006), -0.058 + j * 0.01, -0.085 - j * 0.012), 0.55)]
            strands.append(pts)
    loose = hair_curves("Strands", strands, "Hair", 0.0085 * k, bevel_res=2, res_u=6)
    extras.append((loose, "Head"))
    # Ohrringe, Nasenring
    for sg in (1, -1):
        extras.append((torus("Hoop", p(sg * 0.092, 0.002, -0.05), 0.011 * k, 0.0013 * k, "Gold", rot=(0, math.pi / 2, 0), seg=24, mseg=6), "Head"))
    extras.append((torus("NoseRing", p(0.013, -0.1, -0.034), 0.0042 * k, 0.0008 * k, "Gold", rot=(0, math.pi / 2, 0.3), seg=16, mseg=6), "Head"))
    # Sonnenbrille oben auf dem Kopf
    gl = []
    for sg in (1, -1):
        c = p(sg * 0.034, -0.072, 0.1)
        gl.append(torus("Rim", c, 0.023 * k, 0.0022 * k, "Frame", rot=(math.radians(50), 0, 0), seg=36, mseg=8))
        gl.append(ellipsoid("Lens", c, sz(0.0225, 0.0225, 0.0022), "Lens", 28, 10, rot=(math.radians(50), 0, 0)))
        gl.append(capsule("Temple", c + sz(sg * 0.023, 0, 0), p(sg * 0.085, 0.03, 0.06), 0.0018 * k, "Frame", 8))
    extras.append((join(gl), "Head"))
    arm = build_armature(sp, J, eyes)
    full = finish_character(sp, J, body, hands, nails, head, head_rigid, extras, arm)
    rig = Rig(arm)
    luna_anims(rig, sp, J, mouth_z)
    return arm, full, sp, J


def luna_anims(rig, sp, J, mouth_z):
    s = sp["s"]
    base = stand_base(rig, sp, J, lean=-0.6)
    base = pose(pose({"Head": [(Y, -10), (X, 2)], "UpperArm_*": [(Y, -4)], "LowerArm_*": [(Z, -10)]}), base)
    base = pose(curl(0.7, spread=1.0), base)
    lids = lambda deg: {"Lid_*": [(X, deg)]}
    # Vibe: Kopfnicken im Takt (24 Bilder = 75 bpm), Huefte pendelt ueber zwei Takte, Schultern rollen
    v = Anim(rig, "Idle", 96)
    for i in range(5):
        f = i * 24
        side = 1 if (i // 2) % 2 == 0 else -1
        side = 1 if i in (0, 4) else (-1 if i == 2 else 0)
        sway = {"loc": (0.018 * s * side, 0, 0), "Hips": [(Y, 3 * side), (Z, 4 * side)], "Spine": [(Y, -2 * side)],
                "LowerLeg_L": [(X, 8 if side < 0 else 2)], "LowerLeg_R": [(X, 8 if side > 0 else 2)]}
        down = pose(pose({"Head": [(X, 9)], "Neck": [(X, 3)], "Shoulder_*": [(Y, 3)], "Chest": [(X, 2)]}, sway), base)
        up = pose(pose({"Head": [(X, -2)], "Shoulder_*": [(Y, -3)], "Chest": [(X, -1)], "Lid_*": [(X, 10)]}, sway), base)
        v.key(f, down)
        if f + 12 <= 96:
            v.key(f + 12, up)
    v.write()
    # zweite Variante als eigene Animation: Augen zu, Arme hoch, ganz im Song
    vb = Anim(rig, "Vibe", 96)
    for i in range(5):
        f = i * 24
        side = 1 if i % 2 == 0 else -1
        P = pose({"loc": (0.02 * s * side, 0, 0), "Hips": [(Y, 4 * side), (Z, 6 * side)], "Chest": [(Y, -3 * side)],
                  "Head": [(Y, -7 * side), (X, -6 if i % 2 else -10)], "Lid_*": [(X, 30)],
                  "LowerLeg_L": [(X, 9 if side < 0 else 2)], "LowerLeg_R": [(X, 9 if side > 0 else 2)]}, base)
        head = rig.point(P, "Head", rig.rest["Head"][3])
        # rechter Arm hoch, Hand pendelt ueber dem Kopf
        P = arm_ik(rig, P, "R", head + Vector((-0.17 + 0.05 * side, -0.06, 0.2)) * s, (-1.0, 0.3, -0.2),
                   fingers=(0.25 * side, -0.2, 0.95), palm=(0.2, 0.97, 0.0))
        # linke Hand vor der Brust, schnippt im Takt
        chest = rig.point(P, "Spine2", rig.rest["Spine2"][3])
        P = arm_ik(rig, P, "L", chest + Vector((0.16, -0.24, -0.06 + 0.03 * side)) * s, (1.0, 0.2, -0.6),
                   fingers=(-0.35, -0.6, 0.7), palm=(-0.5, 0.3, 0.8))
        P = pose(curl(1.1 if i % 2 else 0.6, per={"Index": 0.3, "Middle": 1.4}), P)
        vb.key(f, P)
    vb.write()

    # Kichern mit der Hand vor dem Mund
    def giggle_hand(P):
        mouth = head_point(rig, P, (0, -0.1, mouth_z), sp, J)
        # Fingerspitzen an die Lippen, Hand vor dem Kinn
        fingers = Vector((-0.25, -0.3, 0.92)).normalized()
        palm = Vector((0.1, 0.97, 0.2)).normalized()
        return arm_ik(rig, P, "L", mouth - fingers * 0.16 * s - palm * 0.035 * s, (0.8, 0.2, -1.0), fingers=fingers, palm=palm)

    laugh(rig, sp, base, hand_to_mouth=giggle_hand)


# ---------------------------------------------------------------- Gassen-Crew: Kapuze auf, Baggy-Klamotten

WALL_Y = 0.15     # Nix lehnt mit dem Ruecken an einer Wand so weit hinter dem Ursprung (Unity: AlleyCrew.WallGap)
CRATE_H = 0.34    # Hoehe des umgedrehten Bierkastens, auf dem Moe sitzt
CRATE_Y = -0.03   # Kasten-Mitte so weit hinter Moes Ursprung (steckt schon im Modell: Kasten und Moe am selben Punkt aufstellen)


def hood_up(sp, J, role, trim, inner, open_w=0.066, open_top=0.082, strings="Strings", aglet="Accent"):
    """Aufgesetzte Kapuze: Schale um den Kopf mit U-foermiger Gesichtsoeffnung, dicker Saum, Kordeln.
    Oben geht sie mit dem Kopf, unten in Hals und Brust ueber."""
    k, p, sz, local = head_frame(sp, J)
    s = sp["s"]
    hood = join([ellipsoid("HoodUp", p(0, 0.028, 0.03), sz(0.122, 0.138, 0.15), role, 64, 40),
                 ellipsoid("HoodNape", p(0, 0.05, -0.1), sz(0.113, 0.106, 0.11), role, 48, 28)], "HoodUp")
    remesh(hood, 0.003 * k, 0.5, 3)
    zc = open_top - 0.075

    def half_w(z):
        if z <= zc:
            return open_w
        t = (z - zc) / (open_top - zc)
        return open_w * math.sqrt(max(0.0, 1 - t * t))

    delete_verts_fn(hood, lambda c: (local(c).y < -0.015 and abs(local(c).x) < half_w(local(c).z)) or local(c).z < -0.19)
    displace(hood, lambda c, n: 0.003 * k * noise.noise(c * (22 / k)))
    solidify(hood, 0.007 * k, offset=-1.0)
    centre = p(0, 0.01, -0.01)
    # Innenseite dunkler (Schatten in der Kapuze)
    assign_regions(hood, lambda c, n: inner if n.dot(c - centre) < 0 else role)
    decimate(hood, 0.45)
    neck_z = J["neck"].z

    def hood_w(co):
        t = smoothstep(neck_z - 0.01 * s, neck_z + 0.11 * s, co.z)
        return {"Head": t, "Neck": (1 - t) * 0.5, CHEST: (1 - t) * 0.5}

    weight_fn(hood, hood_w)
    out = [(hood, "custom")]
    # Saum als Wulst entlang der Oeffnung (Punkte auf der Aussenseite der Kapuze)
    bvh = bvh_of(hood)
    n_arc = 14
    path = [(open_w, z) for z in (-0.11, -0.07, -0.03, zc)]
    path += [(open_w * math.cos(math.pi * i / n_arc), zc + (open_top - zc) * math.sin(math.pi * i / n_arc)) for i in range(1, n_arc)]
    path += [(-open_w, z) for z in (zc, -0.03, -0.07, -0.11)]
    pts = []
    for x, z in path:
        x2 = x + math.copysign(0.003, x) if abs(x) > 1e-4 else x
        z2 = z + 0.004 if z > zc else z
        hit = bvh.ray_cast(p(x2, -0.4, z2), Vector((0, 1, 0)), 1.0)
        if hit[0] is not None:
            pts.append((hit[0] + hit[1] * 0.003 * k, 1.0))
    if len(pts) > 4:
        rim = hair_curves("HoodHem", [pts], trim, 0.0135 * k, bevel_res=2, res_u=4)
        weight_fn(rim, hood_w)
        out.append((rim, "custom"))
        # Kordeln aus den unteren Enden
        for end in (pts[0][0], pts[-1][0]):
            cps = [(end, 1.0), (end + Vector((0, -0.012, -0.06)) * s, 1.0), (end + Vector((0, -0.016, -0.14)) * s, 1.0)]
            st = hair_curves("String", [cps], strings, 0.0035 * s)
            ag = cylinder("Aglet", cps[-1][0] - Vector((0, 0, 0.012)) * s, 0.0045 * s, 0.0045 * s, 0.022 * s, aglet)
            out += [(st, "body"), (ag, "body")]
    # Kragen: Uebergang Kapuze -> Schultern
    collar = torus("HoodCollar", J["neck"] + Vector((0, 0.03, -0.025)) * s, 0.08 * s, 0.026 * s, role, rot=(0.3, 0, 0), scale=(1.2, 1, 1))
    displace(collar, lambda c, n: 0.003 * s * noise.noise(c * (30 / s)))
    out.append((collar, "body"))
    return out


def baggy_hem(body, s, floor=0.052):
    """Hosenbeine liegen auf dem Schuh auf: unten flach statt rund."""
    for v in body.data.vertices:
        if v.co.z < floor * s:
            v.co.z = floor * s + (v.co.z - floor * s) * 0.15
    body.data.update()


def side_curl(amount, side, **kw):
    """curl() nur fuer eine Hand."""
    out = {}
    for key, rots in curl(amount, **kw).items():
        out[key[:-1] + side] = rots if side == "L" else mirror_rots(rots)
    return out


def stubble_zone(role, mouth_z=-0.0625, line_hi=-0.02, line_lo=0.03, mustache=True):
    def fn(lc):
        if lc.y > 0.03 or lc.z < -0.16:
            return None
        if mustache and abs(lc.x) < 0.032 and lc.y < -0.08 and mouth_z + 0.009 < lc.z < mouth_z + 0.02:
            return role
        line = line_hi - line_lo * max(0.0, 1 - abs(lc.x) / 0.07)  # an den Seiten hoeher, vorn tiefer
        return role if lc.z < line else None
    return fn


# ---------------------------------------------------------------- NIX: Ticker in der Gasse, Kapuze ueber der Cap

def build_nix():
    CUR.name = "Nix"
    CUR.pal = {
        "Skin": "E2B59A", "Lips": "B98274", "Nails": "E8C8B4", "EyeWhite": "F8E4E0", "EyeRed": "F0A8A4", "Iris": "7A8F5A",
        "Pupil": "141212", "Shine": "FFFFFF", "Lid": "D2A086", "Lash": "2A2220", "Brows": "3A2E28", "MouthIn": "4A1E26",
        "Teeth": "F4EFE2", "Tongue": "D9707A", "Stubble": "C49A82",
        "Hoodie": "2C2C35", "HoodieTrim": "24242C", "HoodIn": "15151A", "Strings": "B6FF3B", "Silver": "C8C8D0",
        "Print": "B6FF3B", "Pants": "5E6650", "PantsDark": "4A503F", "Camo1": "3E4535", "Camo2": "7D7759",
        "Shoes": "F2F0EA", "ShoeCap": "D9D9D2", "Sole": "F7F4EE", "Laces": "F2F0EA", "Accent": "B6FF3B",
        "Cap": "1C1C22", "CapBrim": "B6FF3B", "Gaiter": "B6FF3B", "Gaiter2": "8FCC2A",
        "Pouch": "1A1A20", "Strap": "B6FF3B", "Zip": "C8C8D0",
    }
    sp = dict(s=1.06, sw=0.95, hw=0.9, head=0.95, soft_flat=True,
              r=dict(pelvis=1.05, waist=1.25, belly=1.22, chest=1.1, neck=0.9, arm=1.32, fore=1.26, finger=0.92,
                     leg=1.36, knee=1.46, shin=1.55, ankle=1.4),
              shape=dict(belly=0.0, waist_in=0.0),
              face=dict(cw=0.95, jw=0.88, fl=1.08, chin=1.05, cheek=0.8, nose=1.0, nose_len=1.12, lips=0.9, ear=1.0,
                        smile=0.0015, lid=3.0, bags=1.45, brow_h=-0.002, brow_tilt=0.1, mouth_w=0.92))
    J = make_joints(sp)
    s = sp["s"]
    k, p, sz, local = head_frame(sp, J)
    body = build_body(sp, J)
    displace(body, sleeve_folds(sp, J, 0.0045, ankle_amp=0.011, bunch_wrist=1.6, fabric=0.002))
    baggy_hem(body, s)
    decimate(body, 0.42)

    def zone(c, n):
        zn = c.z / s
        if zn > 1.47 and abs(c.x) / s < 0.09:
            return "Skin"
        if zn < 0.885:
            v = noise.noise(c * (9.0 / s))
            return "Camo1" if v > 0.28 else "Camo2" if v < -0.32 else "Pants"
        return "Hoodie"

    assign_regions(body, zone)
    pocket_y = surf(body, 0, 1.0 * s).y
    hands, nails = [], []
    for side in ("L", "R"):
        h, n = build_hand(sp, J, side)
        hands.append(h)
        nails += n
    sp["face"]["zone"] = stubble_zone("Stubble", mustache=False, line_hi=-0.045, line_lo=0.03)
    head, head_rigid, eyes, mouth_z = build_head(sp, J)
    extras = []
    # Oversize-Hoodie: langer Bund ueber dem Po, Kaenguru-Tasche weit aufgebauscht (da stecken die Haende drin)
    extras += hoodie_details(sp, J, body, "Hoodie", "HoodieTrim", pocket=True, strings=False, hood=False,
                             hem_z=0.885, pocket_z=1.0, pocket_puff=0.032, neck_rib=False)
    extras += hood_up(sp, J, "Hoodie", "HoodieTrim", "HoodIn", open_w=0.064, open_top=0.085, strings="Strings", aglet="Silver")
    # grosses X auf dem Ruecken
    bc = Vector((0, 0, 1.27 * s))
    for a in (45, -45):
        u = Vector((math.cos(math.radians(a)), 0, math.sin(math.radians(a))))
        v = Vector((-u.z, 0, u.x))
        extras.append((patch(body, bc, u, v, rounded_rect(0.24 * s, 0.05 * s, 0.012 * s), (0, -1, 0), 0.003 * s, 0.0015 * s,
                             "Print", rings=3, name="PrintX"), "body"))
    # Cargo-Taschen aussen an den Hosenbeinen
    for sg in (1, -1):
        c = Vector((sg * 0.14 * s, -0.005 * s, 0.56 * s))
        extras.append((patch(body, c, (0, -1, 0), (0, 0, 1), rounded_rect(0.13 * s, 0.15 * s, 0.014 * s), (-sg, 0, 0),
                             0.004 * s, 0.004 * s, "PantsDark", name="Cargo", puff=0.01 * s), "body"))
        extras.append((patch(body, c + Vector((0, 0, 0.082 * s)), (0, -1, 0), (0, 0, 1), rounded_rect(0.14 * s, 0.035 * s, 0.01 * s),
                             (-sg, 0, 0), 0.0085 * s, 0.003 * s, "Camo1", name="Flap"), "body"))
    for side in ("L", "R"):
        extras.append((sneaker(sp, J, side, dict(kind="sneaker", sole=0.045, cap="ShoeCap", len=1.08, wavy=True)), "custom"))
    # Cap unter der Kapuze: nur Schirm und Stirnteil schauen raus
    crown = cut_ellipsoid("Cap", p(0, 0.012, 0.026), sz(0.096, 0.11, 0.114), "Cap", lambda c: local(c).z > 0.045, 56, 34)
    solidify(crown, 0.004 * k, offset=1.0)
    brim = cut_ellipsoid("Brim", p(0, -0.06, 0.058), sz(0.082, 0.105, 0.0075), "CapBrim", lambda c: local(c).y < -0.07, 48, 16,
                         rot=(-0.12, 0, 0))
    extras += [(crown, "Head"), (brim, "Head")]
    # Schlauchschal unters Kinn geschoben
    gaiter = torus("Gaiter", p(0, 0.008, -0.128), 0.055 * k, 0.022 * k, "Gaiter", scale=(1.05, 1.08, 1.5), seg=40, mseg=12)
    displace(gaiter, lambda c, n: 0.003 * k * math.sin(c.z / k * 260 + 2 * noise.noise(c * (60 / k))))
    assign_regions(gaiter, lambda c, n: "Gaiter2" if math.sin(c.z / k * 260) > 0.6 else "Gaiter")
    extras.append((gaiter, "Neck"))
    # Lippenpiercing
    extras.append((torus("LipRing", p(-0.012, -0.093, -0.074), 0.0048 * k, 0.0009 * k, "Silver", rot=(0, math.pi / 2, 0), seg=16, mseg=6), "Jaw"))
    # Bauchtasche quer ueber der Brust (ueber die rechte Schulter), da kommen die Tueten raus
    pc = surf(body, 0.05 * s, 1.24 * s, 0.035 * s)
    tilt = math.radians(22)
    pouch = box("Pouch", pc, Vector((0.2, 0.06, 0.095)) * s, "Pouch", rot=(0, tilt, 0), bevel=0.025 * s, subsurf=2)
    zip_ = box("PouchZip", pc + Vector((math.sin(tilt) * 0.03, -0.03, math.cos(tilt) * 0.03)) * s, Vector((0.17, 0.008, 0.008)) * s, "Zip", rot=(0, tilt, 0))
    tag = box("PouchTag", pc + Vector((-0.03, -0.031, -0.01)) * s, Vector((0.05, 0.004, 0.022)) * s, "Accent", rot=(0, tilt, 0))
    end_l = pc + Vector((math.cos(tilt) * 0.1, 0, -math.sin(tilt) * 0.1)) * s
    end_r = pc - Vector((math.cos(tilt) * 0.1, 0, -math.sin(tilt) * 0.1)) * s
    strap_pts = [(end_l, 1.0), (surf(body, 0.15 * s, 1.17 * s, 0.012 * s), 1.0), (Vector((0.2 * s, 0.0, 1.2 * s)), 1.0),
                 (surf(body, 0.08 * s, 1.26 * s, 0.012 * s, side=1), 1.0), (surf(body, -0.09 * s, 1.42 * s, 0.014 * s, side=1), 1.0),
                 (Vector((-0.12 * s, 0.005 * s, 1.53 * s)), 1.0), (surf(body, -0.09 * s, 1.40 * s, 0.014 * s), 1.0), (end_r, 1.0)]
    strap = hair_curves("Strap", [strap_pts], "Strap", 0.0065 * s, bevel_res=1, res_u=6)
    extras += [(pouch, "body"), (zip_, "body"), (tag, "body"), (strap, "body")]

    arm = build_armature(sp, J, eyes)
    full = finish_character(sp, J, body, hands, nails, head, head_rigid, extras, arm)
    rig = Rig(arm)
    nix_anims(rig, sp, J, mouth_z, pocket_y)
    return arm, full, sp, J


def nix_pose(rig, sp, J, pocket_y, look=0.0, nod=0.0):
    """An die Wand gelehnt (Wand bei +Y), rechter Fuss mit der Sohle an der Wand, Haende in der Kaenguru-Tasche."""
    s = sp["s"]
    P = pose({
        "loc": (0, -0.10 * s, -0.025 * s),
        "Hips": [(X, -7), (Z, 3)],
        "Spine": [(X, -4)], "Chest": [(X, -3)], "Neck": [(X, 9)], "Head": [(X, 7 + nod), (Z, look)],
        "Shoulder_*": [(Y, 3)],
    })
    P = leg_ik(rig, P, "L", Vector((0.12, -0.24, 0.1)) * s, (0.2, -1.0, 0.0), toe_dir=(0.3, -1, 0))
    ankle_r = Vector((-0.11 * s, WALL_Y - 0.1 * s, 0.42 * s))
    P = limb_ik(rig, P, "UpperLeg_R", "LowerLeg_R", "Foot_R", ankle_r, (-0.25, -1.0, 0.3), (1, 0, 0), 1,
                (Vector((0.06, 0.0, -1.0)), Vector((0, 1, 0))), ((0, -1, 0), (0, 0, -1)))
    D = rig.fk(P)
    qs = D["Spine"].to_quaternion()
    for side, sg in (("L", 1), ("R", -1)):
        wrist = D["Spine"] @ Vector((sg * 0.118 * s, pocket_y + 0.022 * s, 0.985 * s))
        P = arm_ik(rig, P, side, wrist, (sg * 1.0, 0.6, -0.4), fingers=qs @ Vector((-sg, -0.15, -0.25)).normalized(), palm=qs @ Vector((0, 1, 0)))
    return pose(curl(0.5), P)


def nix_anims(rig, sp, J, mouth_z, pocket_y):
    s = sp["s"]
    lids = lambda deg: {"Lid_*": [(X, deg)]}
    eyes = lambda deg: {"Eye_L": [(Z, deg)], "Eye_R": [(Z, deg)]}
    base = nix_pose(rig, sp, J, pocket_y)
    a = Anim(rig, "Idle", 150)
    inhale = pose({"Chest": [(X, -1.5)], "Shoulder_*": [(Y, -1.5)]}, nix_pose(rig, sp, J, pocket_y, nod=-1))
    look = nix_pose(rig, sp, J, pocket_y, look=16)
    a.key(0, base)
    a.key(35, inhale)
    a.key(62, pose(eyes(10), look))
    a.key(78, pose(lids(30), pose(eyes(10), look)))
    a.key(84, pose(eyes(6), look))
    a.key(108, inhale)
    a.key(124, nix_pose(rig, sp, J, pocket_y, nod=-7))  # schniefen
    a.key(131, base)
    a.write()

    # Schmiere stehen: nach links und rechts die Gasse runter schauen
    lo = Anim(rig, "Lookout", 130, loop=False)
    left = pose(eyes(14), nix_pose(rig, sp, J, pocket_y, look=50, nod=-3))
    right = pose(eyes(-14), nix_pose(rig, sp, J, pocket_y, look=-50, nod=-3))
    lo.key(0, base)
    lo.key(16, left)
    lo.key(44, pose(eyes(18), left))
    lo.key(62, right)
    lo.key(92, pose(eyes(-18), right))
    lo.key(112, pose(lids(14), base))
    lo.key(130, base)
    lo.write()

    # Deal: rechte Hand kommt aus der Tasche, Handflaeche nach oben nach vorn (Unity legt die Tuete hinein), Nicken
    de = Anim(rig, "Deal", 110, loop=False)
    chest = rig.point(base, CHEST, rig.rest[CHEST][0])
    reach = pose({"Head": [(X, 6)], "Spine": [(X, 3)]}, base)
    reach = arm_ik(rig, reach, "R", chest + Vector((-0.07, -0.42, -0.2)) * s, (-1.0, 0.3, -0.7), fingers=(0.12, -0.97, 0.05), palm=(0.1, 0.05, 1.0))
    reach = pose(side_curl(0.35, "R"), pose(side_curl(-0.5, "R"), reach))
    de.key(0, base)
    de.key(16, blend(base, reach, 0.45))
    de.key(30, reach)
    de.key(44, pose({"Head": [(X, 9)]}, reach))
    de.key(54, pose({"Head": [(X, 2)]}, reach))
    de.key(72, reach)
    de.key(88, blend(base, reach, 0.4))
    de.key(110, base)
    de.write()

    laugh(rig, sp, base)


# ---------------------------------------------------------------- MOE: der Chef der Gasse, Kapuze unter der Daunenweste

def build_moe():
    CUR.name = "Moe"
    CUR.pal = {
        "Skin": "8A5A3C", "Lips": "6E3E30", "Nails": "B88A70", "EyeWhite": "F6E8E2", "EyeRed": "F0B4AE", "Iris": "3A2416",
        "Pupil": "110C0C", "Shine": "FFFFFF", "Lid": "7A4C32", "Lash": "1A1210", "Brows": "1E1614", "MouthIn": "3A1820",
        "Teeth": "F6F2E6", "Tongue": "C86470", "Beard": "241A16",
        "Hoodie": "9A9AA4", "HoodieTrim": "84848E", "HoodIn": "4E4E58", "Strings": "F2F0EA", "Accent": "F2F0EA",
        "Puffer": "E8642A", "PufferDark": "B9481A", "Zip": "2A2A30",
        "Jeans": "34507E", "JeansDark": "283F66", "Boxers": "D84A5A", "Boxers2": "F2F0EA", "Belt": "1E1E22",
        "Shoes": "C88A3E", "ShoeCap": "B27832", "Sole": "5A3A22", "Laces": "E8C25A",
        "Gold": "F0C040", "Cash": "7FB86A", "Cash2": "5E9A4E", "CashBand": "F2E6B8",
    }
    sp = dict(s=0.985, sw=1.1, hw=1.05, head=1.0, neck_drop=0.02, soft_flat=True,
              r=dict(pelvis=1.12, waist=1.25, belly=1.3, chest=1.2, neck=1.18, arm=1.3, fore=1.2, finger=1.08,
                     leg=1.38, knee=1.42, shin=1.45, ankle=1.3),
              shape=dict(belly=0.02, waist_in=0.0),
              face=dict(cw=1.0, jw=1.1, fl=1.0, chin=1.1, cheek=1.05, nose=1.22, nose_len=0.95, lips=1.18, ear=1.0,
                        smile=0.006, lid=7.0, bags=1.0, brow_h=0.0, brow_size=1.15, brow_tilt=-0.06, mouth_w=1.05))
    J = make_joints(sp)
    s = sp["s"]
    k, p, sz, local = head_frame(sp, J)
    body = build_body(sp, J)
    displace(body, sleeve_folds(sp, J, 0.004, ankle_amp=0.01, bunch_wrist=1.3, fabric=0.002))
    baggy_hem(body, s)
    decimate(body, 0.42)

    def zone(c, n):
        zn = c.z / s
        if zn > 1.47 and abs(c.x) / s < 0.1:
            return "Skin"
        if zn >= 1.0:
            return "Hoodie"
        if zn >= 0.925:  # Boxershorts schauen ueber der tief sitzenden Jeans raus
            return "Boxers2" if int(math.floor((c.x / s + 1.0) / 0.03)) % 3 == 0 else "Boxers"
        return "Jeans"

    assign_regions(body, zone)
    hands, nails = [], []
    for side in ("L", "R"):
        h, n = build_hand(sp, J, side)
        hands.append(h)
        nails += n
    sp["face"]["zone"] = stubble_zone("Beard", line_hi=-0.015, line_lo=0.035)
    head, head_rigid, eyes, mouth_z = build_head(sp, J)
    extras = []
    extras += hoodie_details(sp, J, body, "Hoodie", "HoodieTrim", pocket=False, strings=False, hood=False, hem_z=1.0, neck_rib=False)
    extras += hood_up(sp, J, "Hoodie", "HoodieTrim", "HoodIn", open_w=0.07, open_top=0.09)
    # Daunenweste: Schicht ueber dem Rumpf, Armloecher frei, gesteppt
    sw = sp["sw"]

    def vest_pred(c, n):
        zn = c.z / s
        ax = abs(c.x) / s
        if not 1.0 < zn < 1.47:
            return False
        if ax > 0.215 * sw:
            return False
        return not (ax > 0.165 * sw and zn > 1.27)

    vest = shell(body, "Vest", vest_pred, 0.02 * s, 0.014 * s, "Puffer")
    if vest:
        remesh(vest, 0.004 * s, 0.6, 4)
        decimate(vest, 0.35)
        displace(vest, lambda c, n: 0.008 * s * abs(math.sin((c.z / s) * math.pi / 0.07)))
        assign_regions(vest, lambda c, n: "PufferDark" if abs(math.sin((c.z / s) * math.pi / 0.07)) < 0.25 else "Puffer")
        extras.append((vest, "body"))
    zip_pts = [(surf(body, 0, z * s, 0.03 * s), 1.0) for z in (1.01, 1.1, 1.2, 1.3, 1.4, 1.46)]
    extras.append((hair_curves("VestZip", [zip_pts], "Zip", 0.004 * s, bevel_res=1), "body"))
    vc = torus("VestCollar", J["neck"] + Vector((0, 0.012, -0.035)) * s, 0.082 * s, 0.03 * s, "Puffer", rot=(0.2, 0, 0), scale=(1.25, 1.1, 1.3), seg=40, mseg=12)
    extras.append((vc, "body"))
    # Goldkette mit Medaillon ueber der Weste
    chain = []
    for i in range(15):
        x = -0.085 + 0.17 * i / 14
        z = 1.44 - 0.13 * (1 - (x / 0.085) ** 2)
        chain.append((surf(body, x * s, z * s, 0.042 * s), 1.0))
    extras.append((hair_curves("Chain", [chain], "Gold", 0.0055 * s, bevel_res=1, res_u=3), "body"))
    med_c = chain[7][0] + Vector((0, -0.006, -0.022)) * s
    extras.append((join([cylinder("Medal", med_c, 0.022 * s, 0.022 * s, 0.005 * s, "Gold", rot=(math.pi / 2, 0, 0), segs=28),
                         torus("MedalRim", med_c + Vector((0, -0.003, 0)) * s, 0.017 * s, 0.002 * s, "Gold", rot=(math.pi / 2, 0, 0), seg=28, mseg=6)]),
                   "body"))
    # Guertel auf Hueft-Hoehe, Uhr links
    extras.append((band(body, Vector((0, 0, 0.92 * s)), (0, 0, 1), (1, 0, 0), 0.035 * s, 0.004 * s, 0.003 * s, "Belt", n=64, name="Belt"), "body"))
    extras.append((box("Buckle", surf(body, 0, 0.92 * s, 0.009 * s), Vector((0.05, 0.008, 0.035)) * s, "Gold", bevel=0.004 * s), "body"))
    w = J["wrist_L"]
    extras.append((torus("Watch", w + Vector((0.025, 0, 0)) * s, 0.031 * s, 0.007 * s, "Gold", rot=(0, math.pi / 2, 0), seg=28, mseg=8), "LowerArm_L"))
    for side in ("L", "R"):
        extras.append((sneaker(sp, J, side, dict(kind="sneaker", sole=0.04, cap="ShoeCap", len=1.05)), "custom"))
    # Geldbuendel auf der linken Handflaeche (Ruhepose: Finger +X, Handflaeche nach unten)
    cc = J["wrist_L"] + Vector((0.065, 0.0, -0.026)) * s
    cash = join([box("Cash", cc, Vector((0.075, 0.13, 0.024)) * s, "Cash", bevel=0.003 * s),
                 box("CashTop", cc + Vector((0, 0, -0.0125)) * s, Vector((0.072, 0.126, 0.002)) * s, "Cash2"),
                 box("CashBand", cc, Vector((0.08, 0.03, 0.028)) * s, "CashBand")], "Moe_Cash")
    extras.append((cash, "Hand_L"))

    arm = build_armature(sp, J, eyes)
    full = finish_character(sp, J, body, hands, nails, head, head_rigid, extras, arm)
    rig = Rig(arm)
    moe_anims(rig, sp, J, mouth_z)
    return arm, full, sp, J


def moe_pose(rig, sp, J, nod=0.0, look=0.0):
    """Auf dem Bierkasten, vorgebeugt, Unterarme auf den Knien, Haende mit dem Geld dazwischen."""
    s = sp["s"]
    drop = CRATE_H + 0.17 * s - J["pelvis"].z
    P = pose({"loc": (0, 0.03 * s, drop), "Hips": [(X, 4)], "Spine": [(X, 9)], "Chest": [(X, 8)],
              "Neck": [(X, -6)], "Head": [(X, -8 + nod), (Z, look)], "Shoulder_*": [(Y, 5)]})
    for side, sg in (("L", 1), ("R", -1)):
        P = leg_ik(rig, P, side, Vector((sg * 0.23, -0.42, 0.1)) * s, (sg * 0.7, -1.0, 0.2), toe_dir=(sg * 0.3, -1, 0))
    knees = [rig.point(P, "LowerLeg_" + sd, rig.rest["LowerLeg_" + sd][0]) for sd in ("L", "R")]
    mid = (knees[0] + knees[1]) * 0.5
    P = arm_ik(rig, P, "L", mid + Vector((0.07, -0.05, -0.02)) * s, (1.0, 0.2, -0.7), fingers=(-0.75, -0.6, -0.1), palm=(0.0, 0.25, 1.0))
    P = arm_ik(rig, P, "R", mid + Vector((-0.06, -0.08, 0.03)) * s, (-1.0, 0.2, -0.7), fingers=(0.7, -0.6, -0.35), palm=(0.15, 0.3, -0.95))
    return pose(side_curl(0.75, "R"), pose(side_curl(0.35, "L"), P))


def moe_anims(rig, sp, J, mouth_z):
    s = sp["s"]
    lids = lambda deg: {"Lid_*": [(X, deg)]}
    P0 = moe_pose(rig, sp, J)
    a = Anim(rig, "Idle", 140)
    # nickt zur Musik, schaut zwischendurch die Gasse runter
    for f, d in ((0, 4), (20, -1), (40, 4)):
        a.key(f, moe_pose(rig, sp, J, nod=d))
    side = moe_pose(rig, sp, J, look=24)
    a.key(58, pose({"Eye_L": [(Z, 10)], "Eye_R": [(Z, 10)]}, side))
    a.key(78, pose(lids(28), side))
    a.key(84, side)
    for f, d in ((104, -1), (122, 4)):
        a.key(f, moe_pose(rig, sp, J, nod=d))
    a.write()

    # Geld zaehlen: Buendel vor die Brust, rechter Daumen blaettert, am Ende kurzer Blick hoch
    co = Anim(rig, "Count", 130, loop=False)
    chest = rig.point(P0, CHEST, rig.rest[CHEST][0])
    up = moe_pose(rig, sp, J, nod=18)
    up = arm_ik(rig, up, "L", chest + Vector((0.06, -0.3, -0.16)) * s, (1.0, 0.0, -0.8), fingers=(-0.8, -0.55, 0.15), palm=(0.0, 0.3, 1.0))
    up = arm_ik(rig, up, "R", chest + Vector((-0.04, -0.33, -0.09)) * s, (-1.0, 0.0, -0.8), fingers=(0.85, -0.5, -0.1), palm=(0.0, 0.25, -1.0))
    up = pose(side_curl(0.4, "R"), up)
    thumb = lambda deg: {"Thumb1_R": mirror_rots([((0.8, 0.6, 0.0), deg * 0.4)]), "Thumb2_R": mirror_rots([((0.8, 0.6, 0.0), deg)]),
                         "Thumb3_R": mirror_rots([((0.8, 0.6, 0.0), deg * 0.8)])}
    co.key(0, P0)
    co.key(20, up)
    for i, f in enumerate(range(28, 88, 6)):
        co.key(f, pose(thumb(26 if i % 2 == 0 else -8), up))
    co.key(98, pose({"Head": [(X, -16)], "Lid_*": [(X, 8)]}, up))
    co.key(112, blend(up, P0, 0.6))
    co.key(130, P0)
    co.write()

    laugh(rig, sp, P0)


def build_crate():
    """Umgedrehter Bierkasten (Moes Sitz)."""
    CUR.name = "Crate"
    CUR.pal = {"Crate": "C8302E", "CrateDark": "8E1E1E", "Label": "F2E6B8"}
    W, D, H = 0.42, 0.32, CRATE_H
    parts = [box("Shell", (0, CRATE_Y, H / 2), (W, D, H), "Crate", bevel=0.015)]
    for sg in (1, -1):
        parts.append(box("Grip", (sg * (W / 2 + 0.002), CRATE_Y, H - 0.075), (0.01, 0.12, 0.04), "CrateDark", bevel=0.008))
        for i in range(4):
            parts.append(box("Rib", (-0.15 + i * 0.1, CRATE_Y + sg * (D / 2 + 0.003), H / 2), (0.018, 0.008, H - 0.06), "CrateDark"))
    for i in range(1, 4):
        parts.append(box("GridX", (-W / 2 + i * W / 4, CRATE_Y, H + 0.002), (0.012, D - 0.04, 0.006), "CrateDark"))
    for i in range(1, 3):
        parts.append(box("GridY", (0, CRATE_Y - D / 2 + i * D / 3, H + 0.002), (W - 0.04, 0.012, 0.006), "CrateDark"))
    parts.append(box("Label", (0, CRATE_Y - (D / 2 + 0.008), H * 0.55), (0.16, 0.006, 0.07), "Label"))
    return join(parts, "NPC_Crate")


# ---------------------------------------------------------------- Sofa

def build_couch():
    CUR.name = "Couch"
    CUR.pal = {"Cord": "C9962E", "Cushion": "D6A63E", "Wood": "4A3222", "Tape": "B8B8C0", "Patch": "8C5A9E",
               "Stain": "A9792A", "Button": "8A6420", "Pipe": "B3842A"}
    parts = []
    parts.append(box("Base", (0, 0, 0.215), (1.9, 0.85, 0.23), "Cord", bevel=0.03, subsurf=1))
    parts.append(box("Back", (0, 0.31, 0.56), (1.9, 0.23, 0.5), "Cord", bevel=0.05, subsurf=1))
    for sg in (1, -1):
        parts.append(box("Arm", (sg * 0.85, 0.0, 0.47), (0.2, 0.85, 0.3), "Cord", bevel=0.04, subsurf=1))
        parts.append(cylinder("ArmRoll", (sg * 0.86, -0.0, 0.62), 0.11, 0.11, 0.84, "Cord", rot=(math.pi / 2, 0, 0), segs=24))
        for sy in (1, -1):
            parts.append(cylinder("Leg", (sg * 0.84, sy * 0.36, 0.05), 0.03, 0.022, 0.1, "Wood", segs=12))
    for i, sg in enumerate((1, -1)):
        cush = box("Seat", (sg * 0.37, -0.1, COUCH_SEAT - 0.08), (0.74, 0.62, 0.16), "Cushion", bevel=0.05, subsurf=2)
        displace(cush, lambda c, n, sg=sg: -0.025 * gauss(c.x - sg * 0.37, 0.25) * gauss(c.y + 0.1, 0.2) if n.z > 0.5 else 0.0)
        parts.append(cush)
        bc = box("BackCushion", (sg * 0.37, 0.14, 0.66), (0.72, 0.17, 0.4), "Cushion", rot=(-0.2, 0, 0), bevel=0.05, subsurf=2)
        parts.append(bc)
        parts.append(cylinder("Btn", (sg * 0.37, 0.05, 0.69), 0.014, 0.014, 0.012, "Button", rot=(math.pi / 2 - 0.2, 0, 0), segs=10))
    # Flicken, Panzerband, Fleck
    parts.append(box("Patch", (0.55, -0.43, 0.26), (0.2, 0.012, 0.12), "Patch", rot=(0, 0, 0.0), bevel=0.01))
    for i in range(2):
        parts.append(box("Tape", (-0.86, -0.1 + i * 0.07, 0.735), (0.24, 0.05, 0.006), "Tape", rot=(0.0, 0.25, 0.0)))
    parts.append(ellipsoid("Stain", (-0.32, -0.15, COUCH_SEAT - 0.012), (0.09, 0.06, 0.004), "Stain", 20, 6))
    couch = join(parts, "NPC_Couch")
    return couch


# ================================================================ Vorschau

def preview(objs, path, cam_loc, target, lens=50, res=(1100, 1300)):
    cam_data = bpy.data.cameras.new("Cam")
    cam = link(bpy.data.objects.new("Cam", cam_data))
    cam.location = Vector(cam_loc)
    d = Vector(target) - cam.location
    cam.rotation_euler = d.to_track_quat('-Z', 'Y').to_euler()
    cam_data.lens = lens
    scene.camera = cam
    for o in scene.objects:
        if o.type in ('MESH',):
            o.hide_render = o not in objs
    scene.render.engine = 'BLENDER_WORKBENCH'
    scene.display.shading.light = 'STUDIO'
    scene.display.shading.color_type = 'MATERIAL'
    scene.display.shading.show_object_outline = True
    scene.display.shading.show_cavity = True
    scene.display.shading.cavity_type = 'BOTH'
    scene.render.resolution_x, scene.render.resolution_y = res
    scene.render.filepath = path
    bpy.ops.render.render(write_still=True)
    delete(cam)
    log("vorschau", path)


# ================================================================ Ablauf

import os

built = []
builders = [("jojo", build_jojo), ("kalle", build_kalle), ("luna", build_luna), ("nix", build_nix), ("moe", build_moe)]
for key, fn in builders:
    if ONLY and ONLY not in (key, "all"):
        continue
    arm, full, sp, J = fn()
    export_fbx(arm, [full], os.path.join(OUT_DIR, f"NPC_{CUR.name}.fbx"))
    built.append((CUR.name, arm, full, sp))

couch = None
if not ONLY or ONLY in ("couch", "all", "kalle"):
    couch = build_couch()
    export_static([couch], os.path.join(OUT_DIR, "NPC_Couch.fbx"))

crate = None
if not ONLY or ONLY in ("crate", "all", "moe"):
    crate = build_crate()
    export_static([crate], os.path.join(OUT_DIR, "NPC_Crate.fbx"))

wall = None
if PREVIEW_DIR:
    # nur fuer die Vorschau: Wand hinter Nix
    CUR.name = "Preview"
    CUR.pal = {"Wall": "8A7F96"}
    wall = box("PreviewWall", (0, WALL_Y + 0.05, 1.2), (1.6, 0.1, 2.4), "Wall")

if PREVIEW_DIR:
    for name, arm, full, sp in built:
        h = 1.8 * sp["s"]
        # T-Pose (Ruhe) und Idle
        arm.data.pose_position = 'REST'
        preview([full], os.path.join(PREVIEW_DIR, f"npc_{name.lower()}_tpose.png"), (0, -3.6, h * 0.55), (0, 0, h * 0.5), 50, (1300, 1100))
        preview([full], os.path.join(PREVIEW_DIR, f"npc_{name.lower()}_face.png"), (0.12, -0.75, h * 0.93), (0, 0, h * 0.925), 85, (900, 900))
        arm.data.pose_position = 'POSE'
        set_frame_pose(arm, "Idle", 0)
        if couch:
            couch.location.x = -0.37 if name == "Kalle" else 0.0
        props = {"Kalle": couch, "Moe": crate, "Nix": wall}.get(name)
        props = [props] if props else []
        preview([full] + props, os.path.join(PREVIEW_DIR, f"npc_{name.lower()}_idle.png"),
                (1.3, -2.6, h * 0.62), (0, 0, h * 0.45), 50, (1000, 1200))
        if name in ("Nix", "Moe"):
            preview([full] + props, os.path.join(PREVIEW_DIR, f"npc_{name.lower()}_side.png"), (-2.8, -0.6, h * 0.55), (0, 0, h * 0.45), 50, (1000, 1200))
        extra = {"Jojo": ("Smoke", 40), "Kalle": ("Snack", 46), "Luna": ("Laugh", 14), "Nix": ("Deal", 40), "Moe": ("Count", 50)}[name]
        set_frame_pose(arm, extra[0], extra[1])
        hz = 0.5 if name == "Moe" else 0.72
        preview([full] + props, os.path.join(PREVIEW_DIR, f"npc_{name.lower()}_{extra[0].lower()}.png"),
                (0.7, -1.6, h * (hz + 0.08)), (0, 0, h * hz), 50, (1000, 1100))
        if name == "Nix":
            set_frame_pose(arm, "Lookout", 30)
            preview([full] + props, os.path.join(PREVIEW_DIR, "npc_nix_lookout.png"), (0.7, -1.6, h * 0.8), (0, 0, h * 0.72), 50, (1000, 1100))
        for t in arm.animation_data.nla_tracks:
            t.mute = False
log("fertig")
