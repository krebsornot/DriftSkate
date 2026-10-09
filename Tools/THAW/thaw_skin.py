"""
Liest THAW-PC-Figuren (*.skin.wpc): Materialien, Teil-Meshes mit Positionen, UVs und Knochen-Gewichten.
Format selbst ausgelesen (Tony Hawk's American Wasteland PC):
  0x000  u16 Version, u16 ?, 7x Fueller CABAAAFA
  0x020  u16 0x1002, u16 Materialanzahl, u32 Groesse, ...
  0x030  Materialien, je 0x120 Byte (u32 Name, u32 Name, ..., Pass-Daten)
  danach BABEFACE-Block mit Mesh-Koepfen (je Mesh: Material, Vertex-Stride, Vertex-/Indexanzahl)
  Offsets zu Vertex- und Index-Puffern stehen direkt im Mesh-Kopf.
"""
import struct


class Mesh:
    def __init__(self):
        self.material = 0
        self.positions = []
        self.uvs = []
        self.bones = []
        self.weights = []
        self.normals = []
        self.colors = []
        self.strip = []

    def triangles(self):
        """Triangle-Strip -> Dreiecke (degenerierte werden verworfen)."""
        tris = []
        s = self.strip
        for i in range(len(s) - 2):
            a, b, c = s[i], s[i + 1], s[i + 2]
            if a == b or b == c or a == c:
                continue
            tris.append((a, b, c) if i % 2 == 0 else (b, a, c))
        return tris


def u32(d, o): return struct.unpack_from('<I', d, o)[0]


def unpack_normal(v):
    # 11-11-10 Bit, vorzeichenbehaftet (Xbox-360/PC-Format "DEC3N"-artig); nur zur Kontrolle benutzt
    def s(x, bits):
        if x & (1 << (bits - 1)):
            x -= 1 << bits
        return x / float((1 << (bits - 1)) - 1)
    return (s(v & 0x7FF, 11), s((v >> 11) & 0x7FF, 11), s((v >> 22) & 0x3FF, 10))


def parse(path):
    d = open(path, 'rb').read()
    mat_count = struct.unpack_from('<H', d, 0x22)[0]
    materials = [u32(d, 0x30 + i * 0x120) for i in range(mat_count)]
    bab = d.find(struct.pack('<I', 0xBABEFACE))
    if bab < 0:
        raise ValueError('BABEFACE not found')
    base = bab + 16

    heads = []
    for m in materials:
        m_bytes = struct.pack('<I', m)
        idx = bab
        while True:
            pos = d.find(m_bytes, idx)
            if pos < 0: break
            if pos + 0x70 <= len(d) and d[pos+5:pos+12] == b'\x00\x01\xff\x18\x0c\x00\x20':
                stride = d[pos+4]
                vcount = struct.unpack_from('<H', d, pos + 0x12)[0]
                icount = struct.unpack_from('<I', d, pos + 0x14)[0]
                ipad = struct.unpack_from('<I', d, pos + 0x24)[0]
                offset_ib = struct.unpack_from('<I', d, pos + 0x48)[0]
                offset_vb = struct.unpack_from('<I', d, pos + 0x68)[0]
                heads.append((pos, m, stride, vcount, icount, ipad, base + offset_vb, base + offset_ib))
            idx = pos + 4
    heads.sort()

    meshes = []
    for (_, m, stride, vcount, icount, ipad, vb_pos, ib_pos) in heads:
        mesh = Mesh()
        mesh.material = m
        mesh.uv_sets = [[] for _ in range((stride - 32) // 8)]
        for v in range(vcount):
            p = vb_pos + v * stride
            x, y, z, w, b0, b1, b2, b3, n = struct.unpack_from('<3fI4HI', d, p)
            mesh.positions.append((x, y, z))
            raw = (w & 0x7FF, (w >> 11) & 0x7FF, (w >> 22) & 0x3FF)
            total = float(sum(raw)) or 1.0
            mesh.weights.append(tuple(x / total for x in raw))
            mesh.bones.append((b0, b1, b2, b3))
            mesh.normals.append(unpack_normal(n))
            mesh.colors.append(tuple(d[p + 28:p + 32]))
            for k, uv_set in enumerate(mesh.uv_sets):
                uv_set.append(struct.unpack_from('<2f', d, p + 32 + k * 8))
            mesh.uvs = mesh.uv_sets[0]

        first_u32 = struct.unpack_from('<I', d, ib_pos)[0]
        if first_u32 in (icount * 2, ipad * 2) or abs(first_u32 - icount * 2) < 64:
            actual_ib = ib_pos + 4
        else:
            actual_ib = ib_pos
        mesh.strip = list(struct.unpack_from(f'<{icount}H', d, actual_ib))
        meshes.append(mesh)

    return materials, meshes


if __name__ == '__main__':
    import sys
    mats, meshes = parse(sys.argv[1])
    print('Materialien:', [hex(m) for m in mats])
    for m in meshes:
        bones = sorted({b for bs, ws in zip(m.bones, m.weights) for b, w in zip(bs, ws) if w > 0})
        xs = [p[0] for p in m.positions]; ys = [p[1] for p in m.positions]; zs = [p[2] for p in m.positions]
        print(f'Mesh {m.material:#010x}: {len(m.positions)} Vertices, {len(m.triangles())} Dreiecke, '
              f'X {min(xs):.1f}..{max(xs):.1f} Y {min(ys):.1f}..{max(ys):.1f} Z {min(zs):.1f}..{max(zs):.1f}')
        print('   Knochen:', bones)
