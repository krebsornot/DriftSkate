"""
Wandelt eine THAW-PC-Figur (*.skin.wpc + *.tex.wpc, z. B. aus reTHAWed-UserMods) in eine GLB-Datei
fuer den Mods-Ordner von DRIFT x SKATE um. Nur reines Python, keine Zusatzpakete.

  python thaw_to_glb.py <figur.skin.wpc> <ausgabe.glb> [--keep-outline] [--board]

Boards (CAS-Boards aus UserMods/CAS) werden automatisch erkannt (Vertices nur an Board-Knochen) und als
statisches Mesh ohne Skelett exportiert; dafuer gehoeren sie im Spiel nach Mods/Boards.

Ablauf:
  * Teil-Meshes, UVs und Knochen-Gewichte aus der .skin.wpc lesen (siehe thaw_skin.py).
  * Texturen (DXT1/DXT5) aus der .tex.wpc dekodieren und als PNG einbetten.
  * Skelett: THAW-Standardskelett fuer Skater (52 Knochen). Die Knochen-Indizes im Vertex sind
    Index * 3 (drei Matrix-Register pro Knochen). Weil Mod-Figuren oft ganz andere Proportionen
    haben, werden die Gelenke aus dem Mesh selbst berechnet (Mitte der Vertices, die zwischen
    zwei Knochen gewichtet sind); nur fehlende Gelenke kommen skaliert aus dem Standardskelett.
  * Schwarze Umriss-Huellen (eigenes Material mit schwarzer Textur) werden weggelassen,
    der Toon-Shader im Spiel zeichnet selbst Outlines.
THAW-Koordinaten (Y oben, Blick +Z, links +X, Zoll) entsprechen glTF bis auf die Einheit.
"""
import json
import math
import os
import struct
import sys
import zlib

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import thaw_skin  # noqa: E402

INCH = 0.0254

# THAW-Skaterskelett: (Pruefsumme, Elternindex, Gelenk im Bindepose-Raum [Zoll], Name im GLB).
# Namen so gewaehlt, dass SkeletonMap im Spiel die wichtigen Knochen von selbst findet.
SKELETON = [
    (0x1be55811, -1, (0.0, 0.0, 0.0), "Root"),
    (0x63540a6d, 0, (0.0, 39.33, 0.0), "Hips"),
    (0x897b494b, 1, (0.0, 44.29, -0.67), "Spine"),
    (0xe9beedf4, 2, (0.0, 48.77, -0.97), "Spine1"),
    (0x96ec22f2, 3, (0.0, 53.15, -1.56), "Chest"),
    (0xf1c35735, 4, (0.85, 59.79, -1.81), "Collar_L"),
    (0x1addb5f1, 5, (7.01, 58.44, -2.74), "UpperArm_L"),
    (0x9bbb8ddd, 6, (19.18, 58.44, -2.8), "LowerArm_L"),
    (0x82b37cbc, 7, (30.72, 58.49, -2.02), "Hand_L"),
    (0x744087f0, 8, (34.06, 58.38, -2.33), "Fingers_L"),
    (0x437e2114, 9, (35.44, 58.38, -2.56), "FingersTip_L"),
    (0x78a9d53a, 8, (34.3, 58.47, -0.69), "Finger2_L"),
    (0x1c37d3df, 11, (35.64, 58.47, -0.68), "Finger2Tip_L"),
    (0xe039f273, 8, (32.71, 57.77, -0.26), "Thumb_L"),
    (0x7ee14cfe, 7, (27.5, 58.49, -2.04), "WristTwist_L"),
    (0x85fe92a0, 6, (11.06, 58.44, -2.76), "ArmTwist_L"),
    (0xd64a3323, 6, (7.01, 58.44, -2.74), "ShoulderTwist_L"),
    (0x0bcc6a56, 4, (-0.85, 59.79, -1.81), "Collar_R"),
    (0xe0d28892, 17, (-7.01, 58.44, -2.74), "UpperArm_R"),
    (0x61b4b0be, 18, (-19.18, 58.44, -2.74), "LowerArm_R"),
    (0x78bc41df, 19, (-30.73, 58.5, -2.03), "Hand_R"),
    (0x82a6e859, 20, (-34.31, 58.48, -0.72), "Finger2_R"),
    (0xe638eebc, 21, (-35.65, 58.47, -0.72), "Finger2Tip_R"),
    (0x8e4fba93, 20, (-34.06, 58.4, -2.36), "Fingers_R"),
    (0xb9711c77, 23, (-35.44, 58.4, -2.6), "FingersTip_R"),
    (0x1a36cf10, 20, (-32.73, 57.78, -0.28), "Thumb_R"),
    (0x84ee719d, 19, (-27.6, 58.49, -2.03), "WristTwist_R"),
    (0x7ff1afc3, 18, (-11.06, 58.44, -2.74), "ArmTwist_R"),
    (0x2c450e40, 18, (-7.01, 58.44, -2.74), "ShoulderTwist_R"),
    (0x5a0e0860, 4, (0.0, 60.32, -2.31), "Neck"),
    (0xddec28af, 29, (0.0, 64.02, -1.19), "Head"),
    (0xa85e66d7, 30, (0.0, 68.68, -0.38), "HeadTop"),
    (0x2182e17b, 30, (0.0, 66.36, -4.6), "Ponytail"),
    (0xd766309d, 30, (0.0, 65.25, 3.64), "Face"),
    (0xa985affd, 30, (0.0, 64.15, 1.28), "Jaw"),
    (0x0aa1482b, 4, (0.0, 55.57, 2.45), "ChestFront"),
    (0x7f353d60, 1, (5.0, 45.27, 0.5), "Belt_L"),
    (0xef8a20f1, 1, (0.0, 45.27, 0.45), "Belt_C"),
    (0x853a0003, 1, (-5.0, 45.27, 0.5), "Belt_R"),
    (0xdd0cd021, 1, (-4.24, 37.18, 0.61), "UpperLeg_R"),
    (0xe0e63a64, 39, (-4.64, 20.22, 0.11), "LowerLeg_R"),
    (0x9baac767, 40, (-4.28, 3.66, -0.64), "Foot_R"),
    (0x09a99f42, 41, (-4.27, 0.0, 5.67), "Toe_R"),
    (0x2703ed42, 1, (4.24, 37.18, 0.61), "UpperLeg_L"),
    (0x1ae90707, 43, (4.64, 20.22, 0.11), "LowerLeg_L"),
    (0x61a5fa04, 44, (4.28, 3.66, -0.64), "Foot_L"),
    (0xf3a6a221, 45, (4.28, 0.0, 5.66), "Toe_L"),
    (0x98971faf, 0, (0.0, 3.75, 0.0), "BoardRoot"),
    (0x0e9f8a27, 47, (0.0, 3.75, 11.0), "BoardNose"),
    (0xe68d351a, 48, (0.0, 3.75, 10.04), "BoardNoseTip"),
    (0xf25452a9, 47, (0.0, 3.75, -11.0), "BoardTail"),
    (0x1a46ed94, 50, (0.0, 3.75, -10.24), "BoardTailTip"),
]


# ---------------------------------------------------------------------------------------------- Texturen

def _rgb565(c):
    r, g, b = (c >> 11) & 31, (c >> 5) & 63, c & 31
    return (r << 3 | r >> 2, g << 2 | g >> 4, b << 3 | b >> 2)


def _color_block(d, o, rgba, w, bx, by, dxt1):
    c0, c1, bits = struct.unpack_from('<HHI', d, o)
    p0, p1 = _rgb565(c0), _rgb565(c1)
    if c0 > c1 or not dxt1:
        pal = [p0 + (255,), p1 + (255,),
               tuple((2 * a + b) // 3 for a, b in zip(p0, p1)) + (255,),
               tuple((a + 2 * b) // 3 for a, b in zip(p0, p1)) + (255,)]
    else:
        pal = [p0 + (255,), p1 + (255,), tuple((a + b) // 2 for a, b in zip(p0, p1)) + (255,), (0, 0, 0, 0)]
    for i in range(16):
        x, y = bx * 4 + (i & 3), by * 4 + (i >> 2)
        k = (y * w + x) * 4
        rgba[k:k + 4] = bytes(pal[(bits >> (2 * i)) & 3])


def _alpha_block(d, o, rgba, w, bx, by):
    a0, a1 = d[o], d[o + 1]
    bits = int.from_bytes(d[o + 2:o + 8], 'little')
    if a0 > a1:
        pal = [a0, a1] + [((6 - i) * a0 + (i + 1) * a1) // 7 for i in range(6)]
    else:
        pal = [a0, a1] + [((4 - i) * a0 + (i + 1) * a1) // 5 for i in range(4)] + [0, 255]
    for i in range(16):
        x, y = bx * 4 + (i & 3), by * 4 + (i >> 2)
        rgba[(y * w + x) * 4 + 3] = pal[(bits >> (3 * i)) & 7]


def decode_dxt(d, o, w, h, fmt):
    rgba = bytearray(w * h * 4)
    bw, bh = max(1, w // 4), max(1, h // 4)
    for by in range(bh):
        for bx in range(bw):
            if fmt == 'DXT1':
                _color_block(d, o, rgba, w, bx, by, True)
                o += 8
            else:
                _alpha_block(d, o, rgba, w, bx, by)
                _color_block(d, o + 8, rgba, w, bx, by, False)
                o += 16
    return bytes(rgba)


def png(w, h, rgba):
    raw = b''.join(b'\0' + rgba[y * w * 4:(y + 1) * w * 4] for y in range(h))

    def chunk(tag, data):
        return struct.pack('>I', len(data)) + tag + data + struct.pack('>I', zlib.crc32(tag + data) & 0xFFFFFFFF)
    return (b'\x89PNG\r\n\x1a\n' + chunk(b'IHDR', struct.pack('>IIBBBBB', w, h, 8, 6, 0, 0, 0)) +
            chunk(b'IDAT', zlib.compress(raw, 9)) + chunk(b'IEND', b''))


def parse_textures(path):
    """Texturwoerterbuch: Pruefsumme -> (Breite, Hoehe, RGBA)."""
    out = {}
    if not os.path.exists(path):
        return out
    d = open(path, 'rb').read()
    count = struct.unpack_from('<H', d, 6)[0]
    o = 8
    for _ in range(count):
        o = d.find(b'\x0d\xd0\xad\xab', o)
        if o < 0:
            break
        key, w, h = struct.unpack_from('<IHH', d, o + 8)
        size = struct.unpack_from('<I', d, o + 0x18)[0]
        fmt = 'DXT1' if size == w * h // 2 else 'DXT5'
        out[key] = (w, h, decode_dxt(d, o + 0x1C, w, h, fmt))
        o += 0x1C + size
    return out


def material_textures(path):
    """Material-Pruefsumme -> (Textur-Pruefsumme, Farbe). Farben sind in THAW auf 0,5 = neutral normiert."""
    d = open(path, 'rb').read()
    count = struct.unpack_from('<H', d, 0x22)[0]
    out = {}
    for i in range(count):
        o = 0x30 + i * 0x120
        name = struct.unpack_from('<I', d, o)[0]
        tex = struct.unpack_from('<I', d, o + 0x40)[0]
        col = struct.unpack_from('<4f', d, o + 0x50)
        out[name] = (tex, tuple(min(1.0, c * 2.0) for c in col[:3]) + (min(1.0, col[3]),))
    return out


# ---------------------------------------------------------------------------------------------- Skelett

def fit_joints(meshes):
    """Gelenkpositionen aus dem Mesh: Mitte der Vertices, die zwischen Knochen und Eltern gewichtet sind."""
    n = len(SKELETON)
    used = set()
    for m in meshes:
        for bs, ws in zip(m.bones, m.weights):
            for b, w in zip(bs, ws):
                if w > 0:
                    used.add(b // 3)

    def used_ancestor(i):
        p = SKELETON[i][1]
        while p >= 0 and p not in used:
            p = SKELETON[p][1]
        return p

    acc = {}
    for m in meshes:
        for pos, bs, ws in zip(m.positions, m.bones, m.weights):
            wb = {}
            for b, w in zip(bs, ws):
                if w > 0:
                    wb[b // 3] = wb.get(b // 3, 0.0) + w
            for i in wb:
                a = used_ancestor(i)
                if a in wb:
                    k = min(wb[i], wb[a])
                    s = acc.setdefault(i, [0.0, 0.0, 0.0, 0.0, 0])
                    s[0] += pos[0] * k; s[1] += pos[1] * k; s[2] += pos[2] * k; s[3] += k; s[4] += 1

    # Gesamtgroesse fuer die skalierten Ersatzwerte
    ys = [p[1] for m in meshes for p in m.positions]
    scale = (max(ys) - min(ys)) / 70.0

    joints = [None] * n
    joints[0] = (0.0, min(ys), 0.0)
    for i in range(1, n):
        s = acc.get(i)
        if s and s[4] >= 6 and s[3] > 1e-6:
            joints[i] = (s[0] / s[3], s[1] / s[3], s[2] / s[3])
    # Huefte: Mitte zwischen den Hueftgelenken, etwas hoeher (wie im Standardskelett)
    hl, hr = joints[43], joints[39]
    if hl and hr:
        joints[1] = (0.0, (hl[1] + hr[1]) * 0.5 + 2.1 * scale, (hl[2] + hr[2]) * 0.5)
    # Fehlende Gelenke: Abstand zum Eltern-Knochen aus dem Standardskelett, skaliert
    for i in range(1, n):
        if joints[i] is None:
            p = SKELETON[i][1]
            d = [SKELETON[i][2][k] - SKELETON[p][2][k] for k in range(3)]
            joints[i] = tuple(joints[p][k] + d[k] * scale for k in range(3))
    # Seitliche Knochen spiegelgleich halten (Mittelwert aus links und rechts)
    for i, (_, _, _, name) in enumerate(SKELETON):
        if name.endswith('_L'):
            j = next(k for k, b in enumerate(SKELETON) if b[3] == name[:-2] + '_R')
            l, r = joints[i], joints[j]
            x, y, z = (l[0] - r[0]) * 0.5, (l[1] + r[1]) * 0.5, (l[2] + r[2]) * 0.5
            joints[i], joints[j] = (x, y, z), (-x, y, z)
    for i in (2, 3, 4, 29, 30, 31, 32, 33, 34, 35):
        joints[i] = (0.0,) + tuple(joints[i][1:])
    return joints


# ---------------------------------------------------------------------------------------------- GLB

class Glb:
    def __init__(self):
        self.bin = bytearray()
        self.views = []
        self.accessors = []

    def view(self, data, target=None):
        while len(self.bin) % 4:
            self.bin.append(0)
        v = {'buffer': 0, 'byteOffset': len(self.bin), 'byteLength': len(data)}
        if target:
            v['target'] = target
        self.bin += data
        self.views.append(v)
        return len(self.views) - 1

    def accessor(self, fmt, comp, typ, rows, target=None, minmax=False):
        flat = [x for r in rows for x in (r if isinstance(r, (tuple, list)) else (r,))]
        data = struct.pack('<%d%s' % (len(flat), fmt), *flat)
        a = {'bufferView': self.view(data, target), 'componentType': comp, 'count': len(rows), 'type': typ}
        if minmax:
            k = len(rows[0])
            a['min'] = [min(r[i] for r in rows) for i in range(k)]
            a['max'] = [max(r[i] for r in rows) for i in range(k)]
        self.accessors.append(a)
        return len(self.accessors) - 1


def smooth_normals(positions, tris):
    """Glatte Normalen; Vertices an derselben Stelle (UV-Naehte) teilen sich die Normale."""
    key = {}
    group = []
    for p in positions:
        k = (round(p[0], 3), round(p[1], 3), round(p[2], 3))
        group.append(key.setdefault(k, len(key)))
    acc = [[0.0, 0.0, 0.0] for _ in range(len(key))]
    for a, b, c in tris:
        pa, pb, pc = positions[a], positions[b], positions[c]
        u = [pb[i] - pa[i] for i in range(3)]
        v = [pc[i] - pa[i] for i in range(3)]
        n = (u[1] * v[2] - u[2] * v[1], u[2] * v[0] - u[0] * v[2], u[0] * v[1] - u[1] * v[0])
        for x in (a, b, c):
            g = acc[group[x]]
            g[0] += n[0]; g[1] += n[1]; g[2] += n[2]
    out = []
    for i in range(len(positions)):
        n = acc[group[i]]
        l = math.sqrt(n[0] ** 2 + n[1] ** 2 + n[2] ** 2) or 1.0
        out.append((n[0] / l, n[1] / l, n[2] / l))
    return out


def _face_normal(pa, pb, pc):
    u = [pb[i] - pa[i] for i in range(3)]
    v = [pc[i] - pa[i] for i in range(3)]
    return (u[1] * v[2] - u[2] * v[1], u[2] * v[0] - u[0] * v[2], u[0] * v[1] - u[1] * v[0])


def orient_outward(mesh, tris, joints):
    """
    Dreiecke einheitlich nach aussen drehen (wie "Normalen nach aussen neu berechnen" in Blender).
    Noetig, weil Mod-Autoren oft eine Koerperseite spiegeln und dabei die Wicklung umdrehen.
    1. Ueber gemeinsame Kanten die Wicklung innerhalb jedes zusammenhaengenden Stuecks angleichen.
    2. Pro Stueck abstimmen: zeigen die Normalen von der Knochenachse weg (ohne Skelett: von der Mitte
       des Stuecks weg)? Sonst das Stueck umdrehen.
    """
    pos = mesh.positions
    weld = {}
    wid = [weld.setdefault((round(p[0], 3), round(p[1], 3), round(p[2], 3)), len(weld)) for p in pos]
    edges = {}
    for t, tri in enumerate(tris):
        for k in range(3):
            u, v = wid[tri[k]], wid[tri[(k + 1) % 3]]
            if u != v:
                edges.setdefault((min(u, v), max(u, v)), []).append((t, u, v))

    children = {}
    for i, (_, p, _, _) in enumerate(SKELETON):
        if p >= 0:
            children.setdefault(p, []).append(i)

    def centre_of(t):
        a, b, c = tris[t]
        return [(pos[a][i] + pos[b][i] + pos[c][i]) / 3.0 for i in range(3)]

    def radial_vote(t, flipped, mid=None):
        a, b, c = tris[t]
        n = _face_normal(pos[a], pos[b], pos[c])
        if joints is None:
            cen = centre_of(t)
            dot = sum(n[i] * (cen[i] - mid[i]) for i in range(3))
            return -dot if flipped else dot
        bone = mesh.bones[a][0] // 3
        s0 = joints[bone]
        s1 = joints[children[bone][0]] if bone in children else s0
        cen = [(pos[a][i] + pos[b][i] + pos[c][i]) / 3.0 for i in range(3)]
        d = [s1[i] - s0[i] for i in range(3)]
        ll = sum(x * x for x in d)
        k = 0.0 if ll < 1e-9 else max(0.0, min(1.0, sum((cen[i] - s0[i]) * d[i] for i in range(3)) / ll))
        r = [cen[i] - s0[i] - d[i] * k for i in range(3)]
        dot = sum(n[i] * r[i] for i in range(3))
        return -dot if flipped else dot

    flip = [None] * len(tris)
    for seed in range(len(tris)):
        if flip[seed] is not None:
            continue
        flip[seed] = False
        comp, stack = [seed], [seed]
        while stack:
            t = stack.pop()
            tri = tris[t]
            for k in range(3):
                u, v = wid[tri[k]], wid[tri[(k + 1) % 3]]
                if u == v:
                    continue
                if flip[t]:
                    u, v = v, u
                shared = edges[(min(u, v), max(u, v))]
                if len(shared) != 2:
                    continue  # offene oder mehrfach benutzte Kante: nicht weitergeben
                for t2, u2, v2 in shared:
                    if t2 == t or flip[t2] is not None:
                        continue
                    flip[t2] = (u2, v2) == (u, v)  # gleiche Richtung -> umdrehen
                    comp.append(t2)
                    stack.append(t2)
        mid = None
        if joints is None:
            cs = [centre_of(t) for t in comp]
            mid = [sum(c[i] for c in cs) / len(cs) for i in range(3)]
        score = sum(1 if radial_vote(t, flip[t], mid) > 0 else -1 for t in comp)
        if score < 0:
            for t in comp:
                flip[t] = not flip[t]
    return [(a, c, b) if f else (a, b, c) for (a, b, c), f in zip(tris, flip)]


def is_black(tex):
    w, h, rgba = tex
    lum = sum(rgba[i] + rgba[i + 1] + rgba[i + 2] for i in range(0, len(rgba), 4)) / (3.0 * w * h)
    return lum < 12


def convert(skin_path, out_path, keep_outline=False, board=None):
    tex_path = skin_path.replace('.skin.wpc', '.tex.wpc')
    if not os.path.exists(tex_path):
        for f in os.listdir(os.path.dirname(skin_path) or '.'):
            if f.lower().endswith('.tex.wpc'):
                tex_path = os.path.join(os.path.dirname(skin_path), f)
    textures = parse_textures(tex_path)
    mat_tex = material_textures(skin_path)
    _, meshes = thaw_skin.parse(skin_path)

    keep = []
    for m in meshes:
        tex_key = mat_tex.get(m.material, (None,))[0]
        if not keep_outline and tex_key in textures and is_black(textures[tex_key]):
            print(f'  Umriss-Huelle weggelassen: Material {m.material:#010x} ({len(m.positions)} Vertices)')
            continue
        keep.append(m)
    meshes = keep

    # Boards (CAS-Boards, Ped-Boards) haengen nur an den Board-Knochen: dann ohne Skelett exportieren
    used = {b // 3 for m in meshes for bs, ws in zip(m.bones, m.weights) for b, w in zip(bs, ws) if w > 0}
    if board is None:
        board = bool(used) and min(used) >= 47
    g = Glb()
    nodes, materials, images, textures_out, samplers = [], [], [], [], [{'magFilter': 9729, 'minFilter': 9987}]
    tex_index = {}

    joints, skin = None, None
    if not board:
        joints = fit_joints(meshes)
        # Knoten: Skelett (nur Verschiebungen, keine Drehungen), Wurzel des Modells
        for i, (_, parent, _, name) in enumerate(SKELETON):
            jp = joints[i]
            pp = joints[parent] if parent >= 0 else (0.0, 0.0, 0.0)
            nodes.append({'name': name, 'translation': [(jp[k] - pp[k]) * INCH for k in range(3)]})
        for i, (_, parent, _, _) in enumerate(SKELETON):
            if parent >= 0:
                nodes[parent].setdefault('children', []).append(i)
        ibm = []
        for j in joints:
            ibm.append((1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1, 0, -j[0] * INCH, -j[1] * INCH, -j[2] * INCH, 1))
        skin = {'joints': list(range(len(SKELETON))), 'skeleton': 0,
                'inverseBindMatrices': g.accessor('f', 5126, 'MAT4', ibm)}

    prims = []
    flipped = 0
    for m in meshes:
        raw = m.triangles()
        tris = orient_outward(m, raw, joints)  # glTF: gegen den Uhrzeigersinn, Normalen nach aussen
        flipped += sum(1 for x, y in zip(raw, tris) if x != y)
        normals = smooth_normals(m.positions, tris)
        pos = [tuple(c * INCH for c in p) for p in m.positions]
        jts, wts = [], []
        for bs, ws in zip(m.bones, m.weights):
            pair = {}
            for b, w in zip(bs, ws):
                if w > 0:
                    pair[b // 3] = pair.get(b // 3, 0.0) + w
            items = sorted(pair.items(), key=lambda kv: -kv[1])[:4] or [(1, 1.0)]
            total = sum(w for _, w in items)
            items += [(0, 0.0)] * (4 - len(items))
            jts.append(tuple(b for b, _ in items))
            wts.append(tuple(w / total for _, w in items))
        attrs = {
            'POSITION': g.accessor('f', 5126, 'VEC3', pos, 34962, True),
            'NORMAL': g.accessor('f', 5126, 'VEC3', normals, 34962),
            'TEXCOORD_0': g.accessor('f', 5126, 'VEC2', m.uvs, 34962),
        }
        if not board:
            attrs['JOINTS_0'] = g.accessor('H', 5123, 'VEC4', jts, 34962)
            attrs['WEIGHTS_0'] = g.accessor('f', 5126, 'VEC4', wts, 34962)
        idx = g.accessor('I', 5125, 'SCALAR', [i for t in tris for i in t], 34963)

        tex_key, color = mat_tex.get(m.material, (None, (1.0, 1.0, 1.0, 1.0)))
        mat = {'name': f'mat_{m.material:08x}', 'pbrMetallicRoughness': {'baseColorFactor': list(color), 'metallicFactor': 0.0, 'roughnessFactor': 1.0}}
        if tex_key in textures:
            if tex_key not in tex_index:
                w, h, rgba = textures[tex_key]
                images.append({'name': f'tex_{tex_key:08x}', 'mimeType': 'image/png', 'bufferView': g.view(png(w, h, rgba))})
                textures_out.append({'source': len(images) - 1, 'sampler': 0})
                tex_index[tex_key] = len(textures_out) - 1
                if any(rgba[i] < 250 for i in range(3, len(rgba), 4)):
                    mat['alphaMode'] = 'MASK'
            mat['pbrMetallicRoughness']['baseColorTexture'] = {'index': tex_index[tex_key]}
        materials.append(mat)
        prims.append({'attributes': attrs, 'indices': idx, 'material': len(materials) - 1})

    mesh_node = len(nodes)
    nodes.append({'name': 'Body', 'mesh': 0} if board else {'name': 'Body', 'mesh': 0, 'skin': 0})
    root = len(nodes)
    nodes.append({'name': os.path.basename(out_path).rsplit('.', 1)[0], 'children': [mesh_node] if board else [0, mesh_node]})

    doc = {
        'asset': {'version': '2.0', 'generator': 'DriftSkate thaw_to_glb.py'},
        'scene': 0, 'scenes': [{'nodes': [root]}], 'nodes': nodes,
        'meshes': [{'name': 'Body', 'primitives': prims}],
        'materials': materials, 'buffers': [{'byteLength': 0}],
        'bufferViews': g.views, 'accessors': g.accessors,
    }
    if skin:
        doc['skins'] = [skin]
    if images:
        doc.update(images=images, textures=textures_out, samplers=samplers)
    while len(g.bin) % 4:
        g.bin.append(0)
    doc['buffers'][0]['byteLength'] = len(g.bin)
    js = json.dumps(doc, separators=(',', ':')).encode()
    js += b' ' * (-len(js) % 4)
    with open(out_path, 'wb') as f:
        f.write(struct.pack('<III', 0x46546C67, 2, 12 + 8 + len(js) + 8 + len(g.bin)))
        f.write(struct.pack('<II', len(js), 0x4E4F534A) + js)
        f.write(struct.pack('<II', len(g.bin), 0x004E4942) + g.bin)
    verts = sum(len(m.positions) for m in meshes)
    tris = sum(len(m.triangles()) for m in meshes)
    print(f'  {out_path}: {"Board, " if board else ""}{len(meshes)} Teile, {verts} Vertices, {tris} Dreiecke, '
          f'{len(images)} Texturen, {flipped} Dreiecke nach aussen gedreht')
    return joints


if __name__ == '__main__':
    args = [a for a in sys.argv[1:] if not a.startswith('--')]
    convert(args[0], args[1], '--keep-outline' in sys.argv, True if '--board' in sys.argv else None)
