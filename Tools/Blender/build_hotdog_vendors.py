# Die Wurst-Brueder Bodo und Udo: Verkaeufer an den Imbisswagen in den beiden Parks (Unity: Gameplay/HotdogStand.cs).
# Gleiche Uniform (rot-weiss gestreiftes Hemd mit kurzen Aermeln, Senf-Schuerze mit Ketchup-Flecken, Papiermuetze),
# sonst ganz verschieden: Bodo klein und rund mit Walross-Schnauzer, Udo lang und duenn mit rotem Bart und Brille.
# Beide stehen hinter dem Wagen (Theke vorn, Grill dahinter) und halten rechts eine Grillzange.
# Animationen: Idle (Haende auf der Theke, schaut sich um), Grill (Wuerstchen wenden), Serve (links einen Hotdog
# rueberreichen, Unity legt ihn in die Hand) und Laugh. Modell- und Rig-Helfer kommen aus build_stoners.py.
#
# blender --background --factory-startup --python build_hotdog_vendors.py -- <FBX-Ordner> [Vorschau-Ordner] [bodo|udo]
from pathlib import Path

_src = Path(__file__).with_name("build_stoners.py")
exec(compile(_src.read_text(encoding="utf-8-sig").split("# ================================================================ Ablauf")[0], str(_src), "exec"))
import os

# Wagen relativ zum Ursprung der Figur (Figur schaut nach -Y). Passt zu HotdogStand.VendorBack/VendorAlong in Unity:
# Arbeitsflaeche 1,15 m hoch, Vorderkante 22 cm vor dem Bauch, Grill-Mitte 85 cm vor der Figur.
COUNTER_Z = 1.15
COUNTER_Y = -0.22
GRILL = Vector((0.02, -0.62, 1.24))   # Wuerstchen auf der nahen Haelfte des Grills
TONG_LEN = 0.30                        # Faust bis Zangenspitze
CART_Y = -0.85                         # Mitte des Wagens (nur fuer die Vorschau)

FLAT.update({"Stain", "Stain2", "BadgeInk", "Frame", "Steel", "ShirtStripe"})

UNIFORM = {
    "Shirt": "F4EEE4", "ShirtStripe": "D8373A", "Trim": "D8373A",
    "Apron": "F2B632", "ApronDark": "D99A1E", "ApronTie": "D8373A", "Stain": "C8302F", "Stain2": "B9861A",
    "Badge": "F7F3EA", "BadgeInk": "D8373A",
    "Pants": "2E2A35",
    "Shoes": "2A2730", "ShoeCap": "35313C", "Sole": "E9E4DA", "Laces": "2A2730", "Accent": "D8373A",
    "Cap": "F7F3EA", "CapStripe": "D8373A",
    "Steel": "C8CCD4", "Grip": "2A2730",
}

VENDORS = {
    "Bodo": dict(
        pal={"Skin": "E2A47E", "Lips": "B86F5E", "Nails": "F0C9AE", "EyeWhite": "F7E8E2", "EyeRed": "F0B0A8", "Iris": "5A3B26",
             "Pupil": "181212", "Shine": "FFFFFF", "Lid": "CC8C68", "Lash": "2E211C", "Brows": "3A2A22", "MouthIn": "4A1E26",
             "Teeth": "F2ECDD", "Tongue": "D06A72", "Hair": "3A2A22", "Stache": "3A2A22", "Stubble": "C98F70"},
        sp=dict(s=0.95, sw=1.14, hw=1.12, head=1.04, neck_drop=0.035, soft_flat=True,
                r=dict(pelvis=1.25, waist=1.45, belly=1.6, chest=1.25, neck=1.3, arm=1.4, fore=1.32, finger=1.15,
                       leg=1.38, knee=1.3, shin=1.25, ankle=1.15),
                shape=dict(belly=0.07, waist_in=0.0, butt=0.02),
                face=dict(cw=1.03, jw=1.15, fl=0.97, chin=1.0, cheek=1.3, nose=1.35, nose_len=1.0, lips=1.05, ear=1.08,
                          smile=0.006, lid=6.0, bags=1.2, double_chin=True, nw=1.25, brow_h=0.002, brow_size=1.4,
                          brow_tilt=-0.08, mouth_w=1.05)),
        cap_tilt=7.0, stains=((0.06, 1.08, 0.022, 0.018, "Stain"), (-0.09, 0.84, 0.03, 0.022, "Stain"), (0.11, 0.7, 0.018, 0.026, "Stain2"),
                              (-0.03, 1.22, 0.014, 0.012, "Stain"), (0.02, 0.62, 0.02, 0.014, "Stain")),
    ),
    "Udo": dict(
        pal={"Skin": "F0C4A4", "Lips": "C98878", "Nails": "F6D8C4", "EyeWhite": "FBEAE6", "EyeRed": "F3B8B4", "Iris": "4F7A8A",
             "Pupil": "141212", "Shine": "FFFFFF", "Lid": "DDA889", "Lash": "6A3A22", "Brows": "B5552E", "MouthIn": "4A1E26",
             "Teeth": "F4EFE2", "Tongue": "D9707A", "Hair": "B5552E", "Beard": "A84A28", "Frame": "2A2730"},
        sp=dict(s=1.07, sw=0.9, hw=0.9, head=0.98, soft_flat=True,
                r=dict(pelvis=0.98, waist=0.95, belly=0.92, chest=0.95, neck=0.92, arm=1.05, fore=1.0, finger=0.95,
                       leg=1.05, knee=1.05, shin=1.1, ankle=1.15),
                shape=dict(belly=0.0, waist_in=0.02),
                face=dict(cw=0.95, jw=0.88, fl=1.12, chin=1.1, cheek=0.85, nose=1.15, nose_len=1.25, lips=0.92, ear=1.1,
                          smile=0.004, lid=5.0, bags=1.0, brow_h=0.003, brow_size=1.0, brow_tilt=0.06, mouth_w=0.95)),
        cap_tilt=-5.0, stains=((-0.05, 1.12, 0.016, 0.014, "Stain"), (0.08, 0.78, 0.022, 0.03, "Stain2"), (-0.07, 0.66, 0.016, 0.012, "Stain")),
    ),
}


# ---------------------------------------------------------------- Kleidung

def apron_skirt(body, sp, J, top_z, bot_z, half_w):
    """Schuerze unter dem Latz: oben eng am Bund, darunter haengt sie gerade herunter (vor Bauch und Oberschenkeln),
    die Seiten biegen sich etwas nach hinten, aber nie hinter den Koerper (sonst stechen die Beine durch)."""
    s = sp["s"]
    nx, nz = 40, 30
    bm = bmesh.new()
    grid = []
    lowest = [None] * (nx + 1)
    for j in range(nz + 1):
        v = j / nz
        z = top_z + (bot_z - top_z) * v
        row = []
        for i in range(nx + 1):
            u = -1 + 2 * i / nx
            x = u * half_w * (0.74 + 0.26 * smoothstep(0.0, 0.35, v) + 0.06 * v)  # am Bund gerafft
            hit = surf(body, max(-0.85 * half_w, min(0.85 * half_w, x)), z)
            cur = hit.y if abs(hit.y) > 1e-6 else (lowest[i] if lowest[i] is not None else -0.1 * s)
            lowest[i] = cur if lowest[i] is None else min(lowest[i], cur)  # weiter vorn als alles darueber: haengt
            hang = min(lowest[i] - 0.012 * s + 0.15 * s * abs(u) ** 5, cur - 0.007 * s)  # Seiten legen sich um die Huefte
            y = (cur - 0.004 * s) + (hang - (cur - 0.004 * s)) * smoothstep(0.0, 0.2, v)
            row.append(bm.verts.new(Vector((x, y, z))))
        grid.append(row)
    for j in range(nz):
        for i in range(nx):
            bm.faces.new((grid[j][i], grid[j][i + 1], grid[j + 1][i + 1], grid[j + 1][i]))
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    o = from_bm("ApronSkirt", bm)
    if sum(p.normal.y for p in o.data.polygons) > 0:
        for p in o.data.polygons:
            p.flip()
    solidify(o, 0.004 * s, offset=-1.0)
    smooth(o)
    return o


def apron_bib(body, s, z_bot, z_top, w_top, w_bot, offset, thickness):
    """Latz als regelmaessiges Gitter auf die Brust projiziert (glatter als ein Flicken mit Ringen: keine Knicke zu den Ecken)."""
    nx, nz = 16, 18
    bm = bmesh.new()
    grid = []
    for j in range(nz + 1):
        v = j / nz
        z = z_bot + (z_top - z_bot) * v
        half = (w_bot + (w_top - w_bot) * smoothstep(0.0, 0.7, v)) * 0.5
        grid.append([bm.verts.new(surf(body, (-1 + 2 * i / nx) * half, z, offset)) for i in range(nx + 1)])
    for j in range(nz):
        for i in range(nx):
            bm.faces.new((grid[j][i], grid[j][i + 1], grid[j + 1][i + 1], grid[j + 1][i]))
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    o = from_bm("ApronBib", bm)
    if sum(p.normal.y for p in o.data.polygons) > 0:
        for p in o.data.polygons:
            p.flip()
    solidify(o, thickness, offset=-1.0)
    set_mat(o, "Apron")
    smooth(o)
    return o


def splat(rx, rz, seed, n=22):
    """Klecks-Umriss: Ellipse mit unregelmaessigem Rand und einer kleinen Nase nach unten (getropft)."""
    rnd = random.Random(seed)
    jit = [rnd.uniform(0.78, 1.18) for _ in range(n)]
    pts = []
    for i in range(n):
        a = 2 * math.pi * i / n
        r = (jit[i - 1] + 2 * jit[i] + jit[(i + 1) % n]) / 4
        drip = 1 + 0.6 * max(0.0, -math.sin(a)) ** 8
        pts.append((rx * math.cos(a) * r, rz * math.sin(a) * r * drip))
    return pts


def stains(apron, spots, s):
    """Ketchup- und Senfflecken als flache Kleckse auf der Schuerze (sauberer Rand statt Dreiecks-Treppen)."""
    out = []
    for i, (x, z, rx, rz, role) in enumerate(spots):
        o = patch(apron, Vector((x * s, -0.2, z * s)), (1, 0, 0), (0, 0, 1), splat(rx * s, rz * s, 31 + i * 7), (0, 1, 0),
                  0.0012 * s, 0.0, role, rings=2, name="Stain")
        if o and len(o.data.vertices) > 0:
            out.append(o)
    return out


def paper_cap(p, k, tilt_deg, z0=0.072, rx=0.081, ry=0.132, h=0.072):
    """Papier-Schiffchen: unten Ellipse um den Kopf, oben ein First von vorn nach hinten, an den Enden tiefer."""
    n, rows = 56, 8
    tilt = Matrix.Rotation(math.radians(tilt_deg), 3, 'Y')
    pivot = Vector((0, 0, z0))
    bm = bmesh.new()
    grid = []
    for r in range(rows + 1):
        t = r / rows
        row = []
        for i in range(n):
            a = 2 * math.pi * i / n
            sa, ca = math.sin(a), math.cos(a)
            bx, by, bz = rx * ca, ry * sa, z0 + 0.008 * sa * sa
            ty, tz = ry * 0.86 * sa, z0 + h * (1 - 0.42 * sa ** 4)
            x = bx * (1 - t ** 2.5) * (1 + 0.06 * math.sin(math.pi * t))  # Seiten fast senkrecht, oben zum First gekniffen
            y = by + (ty - by) * t
            z = bz + (tz - bz) * t
            q = tilt @ (Vector((x, y, z)) - pivot) + pivot
            row.append(bm.verts.new(p(q.x, q.y, q.z)))
        grid.append(row)
    for r in range(rows):
        for i in range(n):
            bm.faces.new((grid[r][i], grid[r][(i + 1) % n], grid[r + 1][(i + 1) % n], grid[r + 1][i]))
    bmesh.ops.remove_doubles(bm, verts=bm.verts, dist=0.0006 * k)
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    o = from_bm("Cap", bm)
    solidify(o, 0.003 * k, offset=-1.0)
    set_mat(o, "Cap")
    smooth(o)
    return o


def tongs(J, s):
    """Grillzange in der rechten Faust (Ruhepose: Finger -X, Handflaeche unten, Daumen nach vorn = Zangenrichtung)."""
    grip = J["wrist_R"] + Vector((-0.072, 0.0, -0.027)) * s
    fwd = Vector((0, -1, 0))
    parts = []
    for sg in (1, -1):
        back = grip - fwd * 0.07 + Vector((0, 0, sg * 0.004))
        mid = grip + Vector((0, 0, sg * 0.007))
        tip = grip + fwd * (TONG_LEN - 0.02) + Vector((0, 0, sg * 0.017))
        parts.append(capsule("TongArm", back, mid, 0.0042, "Steel", 8))
        parts.append(capsule("TongArm", mid, tip, 0.0042, "Steel", 8))
        parts.append(ellipsoid("TongPad", tip + fwd * 0.014 + Vector((0, 0, sg * 0.002)), (0.013, 0.022, 0.004), "Steel", 16, 8))
        parts.append(capsule("TongGrip", grip - fwd * 0.055 + Vector((0, 0, sg * 0.0055)), grip + fwd * 0.05 + Vector((0, 0, sg * 0.008)),
                             0.0068, "Grip", 10))
    parts.append(torus("TongLoop", grip - fwd * 0.08, 0.009, 0.0025, "Steel", rot=(0, math.pi / 2, 0), seg=16, mseg=6))
    return join(parts, CUR.name + "_Tongs")


def round_glasses(eyes, sz, p, k):
    """Nur Gestell (ohne Glaeser, sonst verschwinden im Toon-Look die Augen)."""
    parts = []
    for sg in (1, -1):
        E = eyes["L" if sg > 0 else "R"]
        c = E + sz(0, -0.03, -0.004)
        parts.append(torus("Rim", c, 0.02 * k, 0.0021 * k, "Frame", rot=(math.pi / 2, 0, 0), scale=(1.2, 1, 0.85), seg=36, mseg=8))
        hinge = c + sz(sg * 0.024, 0, 0.002)
        parts.append(capsule("Temple", hinge, p(sg * 0.089, 0.02, 0.006), 0.0017 * k, "Frame", 8))
    parts.append(torus("Bridge", (eyes["L"] + eyes["R"]) * 0.5 + sz(0, -0.032, 0.0), 0.009 * k, 0.0017 * k, "Frame",
                       rot=(math.pi / 2, 0, 0), arc=(math.radians(20), math.radians(160)), seg=12, mseg=6))
    return join(parts, "Glasses")


# ---------------------------------------------------------------- Figur

def build_vendor(name):
    v = VENDORS[name]
    CUR.name = name
    CUR.pal = dict(UNIFORM, **v["pal"])
    sp = v["sp"]
    bodo = name == "Bodo"
    J = make_joints(sp)
    s, sw = sp["s"], sp["sw"]
    k, p, sz, local = head_frame(sp, J)
    body = build_body(sp, J)
    sleeve_x = 0.185 * sw + 0.125  # Ende der kurzen Aermel (geteilt durch s)
    displace(body, lambda co, n: 0.0018 * s * noise.noise(co * (38 / s)) if abs(co.x) < sleeve_x * s and co.z > 0.95 * s else 0.0)
    decimate(body, 0.42)
    shoulder_z = J["shoulder_L"].z

    def zone(c, n):
        zn = c.z / s
        ax = abs(c.x) / s
        if zn > 1.47 and ax < 0.1:
            return "Skin"
        if zn < 0.96 and ax < 0.32:
            return "Pants"
        return "Skin" if ax > sleeve_x else "Shirt"

    assign_regions(body, zone)
    hands, nails = [], []
    for side in ("L", "R"):
        h, n_ = build_hand(sp, J, side)
        hands.append(h)
        nails += n_
    if bodo:
        sp["face"]["zone"] = stubble_zone("Stubble", mustache=False, line_hi=-0.05, line_lo=0.025)
    else:
        sp["face"]["zone"] = stubble_zone("Beard", mustache=False, line_hi=-0.05, line_lo=0.03)
    head, head_rigid, eyes, mouth_z = build_head(sp, J)
    extras = []

    # Hemd: rote Ringel um Rumpf und Aermel (als aufgesetzte Ringe, so bleiben die Kanten sauber), Kragen, Buendchen
    nr = sp["r"]["neck"]
    extras.append((torus("Collar", J["neck"] + Vector((0, 0.008, -0.03)) * s, (0.052 * nr + 0.014) * s, 0.015 * s, "Trim",
                         scale=(1.2, 1.05, 1), seg=36, mseg=8), CHEST))
    for zc in (1.04, 1.12, 1.2, 1.28):
        extras.append((band(body, Vector((0, 0, zc * s)), (0, 0, 1), (1, 0, 0), 0.03 * s, 0.0012 * s, 0.0008 * s, "ShirtStripe",
                            n=80, rows=3, name="Stripe"), "body"))
    for sg in (1, -1):
        for xo, role, hgt, off in ((0.04, "ShirtStripe", 0.024, 0.0012), (0.082, "ShirtStripe", 0.024, 0.0012), (sleeve_x - 0.185 * sw, "Trim", 0.026, 0.004)):
            c = Vector((sg * (0.185 * sw + xo) * s, 0.015 * s, shoulder_z - 0.003 * s))
            extras.append((band(body, c, (1, 0, 0), (0, 0, 1), hgt * s, off * s, (0.004 if role == "Trim" else 0.0008) * s, role,
                                n=40, rows=3, name="Sleeve"), "body"))
    for side in ("L", "R"):
        extras.append((sneaker(sp, J, side, dict(kind="sneaker", sole=0.035, cap="ShoeCap", len=1.04)), "custom"))

    # Schuerze: Latz auf der Brust, Rock bis uebers Knie, Bund mit Schleife hinten, Halsband
    bib_top = 1.36
    hem = 0.5 if bodo else 0.55
    bib = apron_bib(body, s, 0.98 * s, bib_top * s, 0.2 * sw * s, 0.3 * sw * s, 0.008 * s, 0.004 * s)
    skirt = apron_skirt(body, sp, J, 1.02 * s, hem * s, (0.22 * sp["hw"] + 0.03) * s)
    assign_regions(skirt, lambda c, n: "ApronDark" if c.z < (hem + 0.03) * s else "Apron")
    extras.append((bib, "body"))
    extras.append((skirt, "Hips"))
    for st_ in stains(bib, [sp_ for sp_ in v["stains"] if sp_[1] > 1.0], s):
        extras.append((st_, "body"))
    for st_ in stains(skirt, [sp_ for sp_ in v["stains"] if sp_[1] <= 1.0], s):
        extras.append((st_, "Hips"))
    extras.append((band(body, Vector((0, 0, 1.0 * s)), (0, 0, 1), (1, 0, 0), 0.024 * s, 0.006 * s, 0.003 * s, "ApronTie", n=72, name="ApronBelt"), "body"))
    back = surf(body, 0, 1.0 * s, 0.012 * s, side=1)
    bow = [ellipsoid("Bow", back + Vector((sg * 0.035, 0.004, 0.0)) * s, Vector((0.032, 0.012, 0.018)) * s, "ApronTie", 16, 10,
                     rot=(0, sg * 0.3, 0)) for sg in (1, -1)]
    bow.append(ellipsoid("Knot", back + Vector((0, 0.006, 0)) * s, Vector((0.012, 0.012, 0.014)) * s, "ApronTie", 12, 8))
    for sg in (1, -1):
        bow.append(capsule("BowEnd", back + Vector((sg * 0.006, 0.008, -0.005)) * s, back + Vector((sg * 0.03, 0.01, -0.12)) * s, 0.006 * s, "ApronTie", 8))
    extras.append((join(bow, "Bow"), "body"))
    # Halsband: von den Latz-Ecken ueber die Schultern um den Nacken (Punkte von oben auf die Schulter gelegt)
    def top_surf(x, y):
        loc, nor, _, _ = bvh_of(body).ray_cast(Vector((x, y, 3.0)), Vector((0, 0, -1)))
        return loc + nor * 0.01 * s if loc is not None else Vector((x, y, 1.5 * s))

    ring_r = (0.052 * nr + 0.024) * s
    ny = J["neck"].y
    neck_pts = [(surf(body, 0.095 * sw * s, (bib_top - 0.01) * s, 0.012 * s), 1.0)]
    for a in (50, 100, 150, 210, 260, 310):
        ar = math.radians(a)
        neck_pts.append((top_surf(math.sin(ar) * ring_r, ny - math.cos(ar) * ring_r), 1.0))
    neck_pts.append((surf(body, -0.095 * sw * s, (bib_top - 0.01) * s, 0.012 * s), 1.0))
    extras.append((hair_curves("ApronStrap", [neck_pts], "Apron", 0.007 * s, bevel_res=1, res_u=6), "body"))
    # Namensschild links auf dem Latz
    bc = Vector((0.05 * sw * s, -0.2, 1.29 * s))
    extras.append((patch(bib, bc, (1, 0, 0), (0, 0, 1), rounded_rect(0.07 * s, 0.036 * s, 0.006 * s), (0, 1, 0), 0.0016 * s, 0.0,
                         "Badge", rings=3, name="Badge"), "body"))
    extras.append((patch(bib, bc + Vector((0, 0, 0.008 * s)), (1, 0, 0), (0, 0, 1), rounded_rect(0.052 * s, 0.008 * s, 0.003 * s), (0, 1, 0),
                         0.0026 * s, 0.0, "BadgeInk", rings=2, name="BadgeName"), "body"))

    # Kopf: Haare, Muetze, Bart/Schnauzer, Brille
    extras.append((hair_cap(sp, J, "Hair", "Hair", 0.035 if not bodo else 0.02, -0.07, 0.005, 0.007, grooves=0.0008), "head"))
    cap = paper_cap(p, k, v["cap_tilt"])
    assign_regions(cap, lambda c, n: "CapStripe" if local(c).z < 0.072 + 0.017 else "Cap")
    extras.append((cap, "Head"))
    if bodo:
        st = []
        for sg in (1, -1):
            st.append(ellipsoid("Stache", p(sg * 0.02, -0.099, mouth_z + 0.007), sz(0.026, 0.012, 0.012), "Stache", 24, 12,
                                rot=(0.15, sg * 0.35, sg * 0.3)))
            st.append(ellipsoid("StacheTip", p(sg * 0.041, -0.09, mouth_z - 0.007), sz(0.011, 0.009, 0.014), "Stache", 16, 10,
                                rot=(0.1, 0, sg * 0.55)))
        stache = join(st, "Stache")
        remesh(stache, 0.0015 * k, 0.5, 2)
        displace(stache, lambda c, n: 0.0009 * k * noise.noise(c * (420 / k)))
        set_mat(stache, "Stache")
        extras.append((stache, "Head"))
    else:
        rng = random.Random(23)
        tufts = [ellipsoid("BeardTuft", p(rng.uniform(-0.035, 0.035), -0.078, rng.uniform(-0.128, -0.1)), sz(0.016, 0.015, 0.018), "Beard", 12, 8)
                 for _ in range(9)]
        beard = join(tufts, "Beard")
        remesh(beard, 0.0028 * k, 0.4, 3)
        decimate(beard, 0.4)
        set_mat(beard, "Beard")
        extras.append((beard, "Jaw"))
        stache = join([ellipsoid("Stache", p(sg * 0.014, -0.099, mouth_z + 0.009), sz(0.017, 0.0065, 0.006), "Beard", 16, 8,
                                 rot=(0, sg * 0.3, sg * 0.2)) for sg in (1, -1)], "Stache")
        extras.append((stache, "Head"))
        extras.append((round_glasses(eyes, sz, p, k), "Head"))

    extras.append((tongs(J, s), "Hand_R"))

    arm = build_armature(sp, J, eyes)
    full = finish_character(sp, J, body, hands, nails, head, head_rigid, extras, arm)
    rig = Rig(arm)
    vendor_anims(rig, sp, J, mouth_z)
    return arm, full, sp, J


# ---------------------------------------------------------------- Posen

def tong_hand(t, palm_hint=(1, 0, 0), roll=0.0):
    """Hand-Richtungen fuer die rechte Faust mit Zange: Daumen (= Zange) zeigt nach t, Handflaeche moeglichst nach palm_hint."""
    t = Vector(t).normalized()
    pa = Vector(palm_hint)
    pa = (pa - t * pa.dot(t)).normalized()
    if roll:
        pa = Quaternion(t, math.radians(roll)) @ pa
    f = pa.cross(t)  # rechte Hand: Daumen = Finger x Handflaeche
    return f, pa, t


def right_tongs(rig, P, sp, tip, t, palm_hint=(1, 0, 0), roll=0.0):
    s = sp["s"]
    f, pa, t = tong_hand(t, palm_hint, roll)
    grip = Vector(tip) - t * TONG_LEN
    wrist = grip - f * 0.072 * s - pa * 0.027 * s
    return arm_ik(rig, P, "R", wrist, (-1.0, 0.45, -0.55), fingers=f, palm=pa)


def vendor_pose(rig, sp, J, look=0.0, nod=0.0, lean=0.0, breathe=0.0, left=None, right=None):
    """Steht hinter der Theke: links die flache Hand auf der Arbeitsflaeche, rechts die Faust mit der Zange daneben."""
    s = sp["s"]
    P = stand_base(rig, sp, J, lean=0.0)
    P = pose({"Spine": [(X, 2 + lean * 0.45)], "Chest": [(X, 2 + lean * 0.55 - breathe)], "Shoulder_*": [(Y, -breathe * 0.8)],
              "Neck": [(X, -3 - lean * 0.3)], "Head": [(X, -3 + nod - lean * 0.3), (Z, look)]}, P)
    if left is None:
        P = arm_ik(rig, P, "L", Vector((0.2, COUNTER_Y - 0.09, COUNTER_Z + 0.032)), (1.0, 0.5, -0.6),
                   fingers=(-0.3, -0.95, 0.0), palm=(0.0, 0.0, -1.0))
        P = pose(side_curl(0.25, "L"), P)
    else:
        P = left(P)
    if right is None:
        P = right_tongs(rig, P, sp, Vector((-0.17, COUNTER_Y - 0.08, COUNTER_Z + 0.045)) + Vector((0.45, -0.89, -0.03)).normalized() * TONG_LEN,
                        (0.45, -0.89, -0.03), palm_hint=(0.6, 0.0, -0.8))
    else:
        P = right(P)
    return pose(side_curl(1.15, "R"), P)


def vendor_anims(rig, sp, J, mouth_z):
    s = sp["s"]
    lids = lambda deg: {"Lid_*": [(X, deg)]}
    eyes = lambda deg: {"Eye_L": [(Z, deg)], "Eye_R": [(Z, deg)]}
    base = vendor_pose(rig, sp, J)

    a = Anim(rig, "Idle", 180)
    a.key(0, base)
    a.key(40, vendor_pose(rig, sp, J, breathe=1.6, nod=-1))
    left = pose(eyes(10), vendor_pose(rig, sp, J, look=22, nod=1))
    a.key(70, left)
    a.key(86, pose(lids(30), left))
    a.key(92, left)
    a.key(122, pose(eyes(-8), vendor_pose(rig, sp, J, look=-16, nod=3)))
    a.key(150, vendor_pose(rig, sp, J, breathe=1.0))
    a.write()

    # Wuerstchen wenden: vorbeugen, Zange auf den Grill, zweimal drehen, zufrieden nicken
    gr = Anim(rig, "Grill", 150, loop=False)
    t_dir = (0.25, -0.75, -0.62)

    def at(dx, roll, lift=0.0, nod=6.0):
        tip = GRILL + Vector((dx, 0, lift))
        return vendor_pose(rig, sp, J, lean=11, nod=nod, right=lambda P: right_tongs(rig, P, sp, tip, t_dir, roll=roll))

    gr.key(0, base)
    gr.key(18, at(0.0, 0, lift=0.06))
    gr.key(28, at(0.0, 0))
    gr.key(38, at(0.0, -55))
    gr.key(46, at(0.0, -55, lift=0.01))
    gr.key(58, at(0.08, 0, lift=0.04))
    gr.key(66, at(0.08, 0))
    gr.key(76, at(0.08, -55))
    gr.key(84, at(0.08, -50, lift=0.01))
    gr.key(98, pose({"Head": [(X, -6)]}, at(0.04, 0, lift=0.07, nod=0)))
    gr.key(108, pose({"Head": [(X, 2)]}, at(0.04, 0, lift=0.07, nod=0)))
    gr.key(126, blend(base, at(0.04, 0, lift=0.07, nod=0), 0.35))
    gr.key(150, base)
    gr.write()

    # Hotdog rueberreichen: linke Hand mit der Handflaeche nach oben ueber die Theke, Nicken
    sv = Anim(rig, "Serve", 120, loop=False)
    arm_len = (J["elbow_L"] - J["shoulder_L"]).length + (J["wrist_L"] - J["elbow_L"]).length

    def reach(P, out=1.0):
        # Weit ueber die Theke, aber nie ganz gestreckt (Bodo hat kurze Arme, Udo lange)
        shoulder = rig.point(P, "UpperArm_L", rig.rest["UpperArm_L"][0])
        far = shoulder + Vector((-0.12, -0.9, -0.2)).normalized() * arm_len * 0.9
        wrist = Vector((0.2, COUNTER_Y - 0.09, COUNTER_Z + 0.04)).lerp(far, out)
        P = arm_ik(rig, P, "L", wrist, (1.0, 0.3, -0.8), fingers=(-0.2, -0.97, 0.12), palm=(0.0, 0.12, 1.0))
        return pose(side_curl(0.3, "L"), P)

    hand_out = vendor_pose(rig, sp, J, lean=6, left=reach)
    sv.key(0, base)
    sv.key(16, vendor_pose(rig, sp, J, lean=3, left=lambda P: reach(P, 0.5)))
    sv.key(30, hand_out)
    sv.key(42, pose({"Head": [(X, 9)]}, hand_out))
    sv.key(52, pose({"Head": [(X, 1)]}, hand_out))
    sv.key(76, hand_out)
    sv.key(94, vendor_pose(rig, sp, J, lean=3, left=lambda P: reach(P, 0.4)))
    sv.key(120, base)
    sv.write()

    laugh(rig, sp, base)


def build_preview_cart():
    CUR.name = "Preview"
    CUR.pal = {"Cart": "E8862A", "Top": "D9D4DE", "Grill": "2A2730", "Wurst": "B8483A"}
    parts = [box("Cabinet", (0.1, CART_Y, 0.74), (2.2, 1.15, 0.7), "Cart"),
             box("Worktop", (0.1, CART_Y, COUNTER_Z - 0.035), (2.3, 1.27, 0.07), "Top"),
             box("Grill", (0.0, CART_Y + 0.01, COUNTER_Z + 0.045), (0.76, 0.5, 0.09), "Grill")]
    for i in range(4):
        parts.append(capsule("Wurst", (-0.25, CART_Y + 0.23 - i * 0.11, 1.225), (0.2, CART_Y + 0.23 - i * 0.11, 1.225), 0.022, "Wurst", 10))
    return join(parts, "PreviewCart")


# ================================================================ Ablauf

os.makedirs(OUT_DIR, exist_ok=True)
if PREVIEW_DIR:
    os.makedirs(PREVIEW_DIR, exist_ok=True)
built = []
for key in ("Bodo", "Udo"):
    if ONLY and ONLY not in (key.lower(), "all"):
        continue
    arm, full, sp, J = build_vendor(key)
    export_fbx(arm, [full], os.path.join(OUT_DIR, "NPC_" + key + ".fbx"))
    built.append((key, arm, full, sp))

if PREVIEW_DIR:
    cart = build_preview_cart()
    for name, arm, full, sp in built:
        h = 1.8 * sp["s"]
        for o in scene.objects:
            if o.type == 'MESH' and o not in (full, cart):
                o.hide_render = True
        arm.data.pose_position = 'REST'
        preview([full], os.path.join(PREVIEW_DIR, f"vendor_{name.lower()}_tpose.png"), (0, -3.6, h * 0.55), (0, 0, h * 0.5), 50, (1300, 1100))
        preview([full], os.path.join(PREVIEW_DIR, f"vendor_{name.lower()}_face.png"), (0.12, -0.75, h * 0.95), (0, 0, h * 0.93), 85, (900, 900))
        arm.data.pose_position = 'POSE'
        for clip, frame in (("Idle", 0), ("Grill", 38), ("Serve", 40), ("Laugh", 14)):
            set_frame_pose(arm, clip, frame)
            preview([full, cart], os.path.join(PREVIEW_DIR, f"vendor_{name.lower()}_{clip.lower()}.png"), (1.9, -3.2, 1.75), (0, -0.35, 1.05), 45, (1000, 1100))
        set_frame_pose(arm, "Grill", 38)
        preview([full, cart], os.path.join(PREVIEW_DIR, f"vendor_{name.lower()}_grill_side.png"), (3.0, -0.45, 1.4), (0, -0.4, 1.15), 45, (1100, 900))
        for t in arm.animation_data.nla_tracks:
            t.mute = False
        full.hide_render = True
log("fertig")
