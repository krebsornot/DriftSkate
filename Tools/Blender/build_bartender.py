# Rocco, Barkeeper der MOON BAR auf dem Dach im Nordwesten (Unity: Gameplay/RooftopBarCrew.cs, Spot_Bartender).
# Weisses Hemd mit hochgekrempelten Aermeln, dunkle Weste mit Messingknoepfen, magenta Fliege, schwarze Hueftschuerze,
# zurueckgegelte Haare, Kinnbart, Tattoo am rechten Unterarm. Rechts ein Cocktail-Shaker, links ein Tuch.
# Animationen: Idle (wischt mit dem Tuch in ruhigen Kreisen ueber den Tresen, schaut sich um), Shake (Shaker hoch,
# schuetteln, eingiessen) und Laugh. Modell- und Rig-Helfer kommen aus build_stoners.py (ueber build_hotdog_vendors.py,
# daher auch die Schuerze).
#
# blender --background --factory-startup --python build_bartender.py -- <FBX-Ordner> [Vorschau-Ordner]
from pathlib import Path

_src = Path(__file__).with_name("build_hotdog_vendors.py")
# am letzten Trenner schneiden: das Imbiss-Skript enthaelt den Trenner selbst als Text (fuer build_stoners.py)
exec(compile(_src.read_text(encoding="utf-8-sig").rsplit("# ================================================================ Ablauf", 1)[0], str(_src), "exec"))
import os

# Tresen relativ zum Ursprung der Figur (Figur schaut nach -Y): Oberkante 1,15 m, Hinterkante 32 cm vor ihm
# (Rooftop-Bar: Spot_Bartender steht 0,32 m hinter der Tresenplatte).
BAR_Z = 1.15
BAR_Y = -0.32
SHAKER_DOWN, SHAKER_UP = 0.10, 0.13   # Shaker unter / ueber der Faust

FLAT.update({"Tattoo", "Button"})

PAL = {
    "Skin": "C68B67", "Lips": "A7655A", "Nails": "E7B79C", "EyeWhite": "F7E8E2", "EyeRed": "F0B0A8", "Iris": "3E2A1E",
    "Pupil": "141212", "Shine": "FFFFFF", "Lid": "B97A58", "Lash": "1E1820", "Brows": "1E1A24", "MouthIn": "4A1E26",
    "Teeth": "F2ECDD", "Tongue": "D06A72", "Hair": "1E1A24", "Beard": "1E1A24", "Stubble": "A9765A",
    "Shirt": "EEEAE4", "Trim": "D9D3CB", "Vest": "2A2734", "Button": "C9A95A", "Tie": "E0287A",
    "Apron": "1E1C24", "ApronDark": "1E1C24", "ApronTie": "E0287A",
    "Pants": "22202A", "Shoes": "1A1820", "ShoeCap": "2A2733", "Sole": "2A2733", "Laces": "1A1820", "Accent": "E0287A",
    "Steel": "C8CCD4", "Cloth": "F4F1EA", "Tattoo": "2E4A6B",
}

SP = dict(s=1.02, sw=1.08, hw=1.0, head=1.0, soft_flat=True,
          r=dict(pelvis=1.05, waist=1.05, belly=1.0, chest=1.12, neck=1.15, arm=1.22, fore=1.2, finger=1.05,
                 leg=1.12, knee=1.1, shin=1.1, ankle=1.1),
          shape=dict(belly=0.0, waist_in=0.02),
          face=dict(cw=1.0, jw=1.12, fl=1.02, chin=1.08, cheek=0.95, nose=1.1, nose_len=1.05, lips=1.0, ear=1.0,
                    smile=0.006, lid=4.0, bags=0.8, brow_h=0.002, brow_size=1.2, brow_tilt=-0.1, mouth_w=1.0))


def shaker(J, s):
    """Cocktail-Shaker in der rechten Faust (Ruhepose: Finger -X, Handflaeche unten, Daumen nach vorn = Shaker-Achse)."""
    grip = J["wrist_R"] + Vector((-0.072, 0.0, -0.027)) * s
    fwd = Vector((0, -1, 0))
    rot = (math.pi / 2, 0, 0)
    tin_len = SHAKER_DOWN + 0.05
    parts = [
        cylinder("Tin", grip + fwd * (-SHAKER_DOWN + tin_len * 0.5), 0.036, 0.042, tin_len, "Steel", rot=rot, segs=20),
        cylinder("Cap", grip + fwd * 0.08, 0.040, 0.026, 0.06, "Steel", rot=rot, segs=20),
        cylinder("Top", grip + fwd * (SHAKER_UP - 0.008), 0.016, 0.016, 0.02, "Accent", rot=rot, segs=14),
    ]
    return join(parts, CUR.name + "_Shaker")


def cloth(J, s):
    """Tuch unter der linken Handflaeche (Ruhepose: Finger +X, Handflaeche unten)."""
    c = J["wrist_L"] + Vector((0.075, 0.0, -0.032)) * s
    return ellipsoid(CUR.name + "_Cloth", c, Vector((0.075, 0.06, 0.011)) * s, "Cloth", 16, 8, rot=(0, 0.05, 0.2))


def build_rocco():
    CUR.name = "Rocco"
    CUR.pal = dict(PAL)
    sp = SP
    J = make_joints(sp)
    s, sw = sp["s"], sp["sw"]
    k, p, sz, local = head_frame(sp, J)
    body = build_body(sp, J)
    elbow_x = J["elbow_L"].x / s + 0.02          # hochgekrempelt bis knapp ueber den Ellbogen
    shoulder_x = J["shoulder_L"].x / s
    displace(body, lambda co, n: 0.0016 * s * noise.noise(co * (40 / s)) if abs(co.x) < elbow_x * s and co.z > 0.95 * s else 0.0)
    decimate(body, 0.42)

    def zone(c, n):
        zn = c.z / s
        ax = abs(c.x) / s
        if zn > 1.47 and ax < 0.1:
            return "Skin"
        if zn < 0.96 and ax < 0.32:
            return "Pants"
        if ax > elbow_x:
            return "Skin"
        if ax > shoulder_x - 0.02:
            return "Shirt"                       # Aermel
        if zn > 1.32 and ax > 0.16 * sw:
            return "Shirt"                       # Armloch der Weste
        if c.y < 0 and zn > 1.12 and ax < (zn - 1.12) * 0.28:
            return "Shirt"                       # V-Ausschnitt vorn
        return "Vest" if zn < 1.44 else "Shirt"

    assign_regions(body, zone)
    hands, nails = [], []
    for side in ("L", "R"):
        h, n_ = build_hand(sp, J, side)
        hands.append(h)
        nails += n_
    sp["face"]["zone"] = stubble_zone("Stubble", mustache=False, line_hi=-0.05, line_lo=0.025)
    head, head_rigid, eyes, mouth_z = build_head(sp, J)
    extras = []

    # Hemdkragen, Fliege, Westenknoepfe, hochgekrempelte Aermel
    nr = sp["r"]["neck"]
    extras.append((torus("Collar", J["neck"] + Vector((0, 0.008, -0.03)) * s, (0.052 * nr + 0.014) * s, 0.014 * s, "Shirt",
                         scale=(1.2, 1.05, 1), seg=36, mseg=8), CHEST))
    knot = J["neck"] + Vector((0, -(0.052 * nr + 0.03), -0.045)) * s
    bow = [ellipsoid("Bow", knot + Vector((sg * 0.03, 0.002, 0)) * s, Vector((0.028, 0.01, 0.019)) * s, "Tie", 16, 10,
                     rot=(0, sg * 0.25, 0)) for sg in (1, -1)]
    bow.append(ellipsoid("BowKnot", knot + Vector((0, -0.004, 0)) * s, Vector((0.011, 0.01, 0.013)) * s, "Tie", 12, 8))
    extras.append((join(bow, "BowTie"), CHEST))
    for i, zb in enumerate((0.99, 1.04, 1.09)):
        extras.append((ellipsoid("Button", surf(body, 0.0, zb * s, 0.004 * s), Vector((0.008, 0.005, 0.008)) * s, "Button", 10, 6), "body"))
    for sg, side in ((1, "L"), (-1, "R")):
        e = J["elbow_" + side]
        c = Vector((sg * (elbow_x - 0.012) * s, e.y, e.z))
        extras.append((band(body, c, (1, 0, 0), (0, 0, 1), 0.05 * s, 0.006 * s, 0.006 * s, "Trim", n=40, rows=3, name="Cuff"), "body"))
    # Tattoo: kleiner Stern auf dem rechten Unterarm (Unterseite = vorn in der T-Pose)
    star = []
    for i in range(10):
        a = math.pi / 2 + i * math.pi / 5
        r = 0.022 * s if i % 2 == 0 else 0.009 * s
        star.append((r * math.cos(a), r * math.sin(a)))
    tc = Vector(((J["elbow_R"].x + J["wrist_R"].x) * 0.5, -0.3, J["elbow_R"].z))
    tat = patch(body, tc, (1, 0, 0), (0, 0, 1), star, (0, 1, 0), 0.0012 * s, 0.0, "Tattoo", rings=2, name="Tattoo")
    if tat and len(tat.data.vertices) > 0:
        extras.append((tat, "body"))

    # Hueftschuerze bis uebers Knie, Bund mit Schleife
    skirt = apron_skirt(body, sp, J, 0.98 * s, 0.56 * s, (0.22 * sp["hw"] + 0.03) * s)
    set_mat(skirt, "Apron")
    extras.append((skirt, "Hips"))
    extras.append((band(body, Vector((0, 0, 0.98 * s)), (0, 0, 1), (1, 0, 0), 0.022 * s, 0.006 * s, 0.003 * s, "ApronTie", n=72, name="ApronBelt"), "body"))
    for side in ("L", "R"):
        extras.append((sneaker(sp, J, side, dict(kind="sneaker", sole=0.028, cap="ShoeCap", len=1.02)), "custom"))

    # Kopf: zurueckgegelte Haare, Kinnbart, schmaler Schnauzer
    extras.append((hair_cap(sp, J, "Hair", "Hair", 0.03, -0.07, 0.012, 0.008, grooves=0.0014), "head"))
    rng = random.Random(7)
    tufts = [ellipsoid("BeardTuft", p(rng.uniform(-0.016, 0.016), -0.083, rng.uniform(-0.132, -0.112)), sz(0.013, 0.012, 0.016), "Beard", 12, 8)
             for _ in range(6)]
    beard = join(tufts, "Beard")
    remesh(beard, 0.0026 * k, 0.4, 3)
    decimate(beard, 0.45)
    set_mat(beard, "Beard")
    extras.append((beard, "Jaw"))
    stache = join([ellipsoid("Stache", p(sg * 0.015, -0.099, mouth_z + 0.009), sz(0.018, 0.005, 0.005), "Beard", 16, 8,
                             rot=(0, sg * 0.3, sg * 0.15)) for sg in (1, -1)], "Stache")
    extras.append((stache, "Head"))

    extras.append((shaker(J, s), "Hand_R"))
    extras.append((cloth(J, s), "Hand_L"))

    arm = build_armature(sp, J, eyes)
    full = finish_character(sp, J, body, hands, nails, head, head_rigid, extras, arm)
    rig = Rig(arm)
    bar_anims(rig, sp, J)
    return arm, full, sp, J


# ---------------------------------------------------------------- Posen

def right_shaker(rig, P, sp, grip, t=(0, 0, 1), palm_hint=(1, 0, 0)):
    """Rechte Faust um den Shaker: Achse (Daumen) zeigt nach t, Handflaeche moeglichst nach palm_hint."""
    s = sp["s"]
    f, pa, t = tong_hand(t, palm_hint)
    wrist = Vector(grip) - f * 0.072 * s - pa * 0.027 * s
    return arm_ik(rig, P, "R", wrist, (-1.0, 0.45, -0.55), fingers=f, palm=pa)


def wipe(rig, P, dx=0.0, dy=0.0):
    P = arm_ik(rig, P, "L", Vector((0.2 + dx, BAR_Y - 0.12 + dy, BAR_Z + 0.045)), (1.0, 0.5, -0.6),
               fingers=(-0.3, -0.95, 0.0), palm=(0.0, 0.0, -1.0))
    return pose(side_curl(0.2, "L"), P)


def bar_pose(rig, sp, J, look=0.0, nod=0.0, lean=0.0, breathe=0.0, left=None, right=None):
    """Hinter dem Tresen: links das Tuch auf der Platte, rechts steht der Shaker auf dem Tresen."""
    P = stand_base(rig, sp, J, lean=0.0)
    P = pose({"Spine": [(X, 3 + lean * 0.45)], "Chest": [(X, 2 + lean * 0.55 - breathe)], "Shoulder_*": [(Y, -breathe * 0.8)],
              "Neck": [(X, -3 - lean * 0.3)], "Head": [(X, -2 + nod - lean * 0.3), (Z, look)]}, P)
    P = left(P) if left else wipe(rig, P)
    P = right(P) if right else right_shaker(rig, P, sp, Vector((-0.2, BAR_Y - 0.1, BAR_Z + SHAKER_DOWN)))
    return pose(side_curl(1.15, "R"), P)


def bar_anims(rig, sp, J):
    eyes = lambda deg: {"Eye_L": [(Z, deg)], "Eye_R": [(Z, deg)]}
    base = bar_pose(rig, sp, J)

    # Idle: drei ruhige Wisch-Kreise (je 2 s), zwischendurch umsehen
    a = Anim(rig, "Idle", 180)
    for f in range(0, 180, 10):
        ang = f / 60 * 2 * math.pi
        look = 18 if 60 <= f < 100 else (-12 if 130 <= f < 160 else 0)
        nod = 2 if 60 <= f < 100 else 0
        P = bar_pose(rig, sp, J, look=look, nod=nod, lean=4, breathe=0.8 * math.sin(f / 180 * 2 * math.pi),
                     left=lambda P, ang=ang: wipe(rig, P, 0.07 * math.cos(ang), 0.05 * math.sin(ang)))
        a.key(f, pose(eyes(look * 0.4), P))
    a.write()

    # Shake: Shaker vor die Schulter, kraeftig schuetteln, ueber dem Tresen eingiessen, zurueckstellen
    sh = Anim(rig, "Shake", 160, loop=False)
    up = Vector((-0.2, -0.2, 1.42))

    def held(grip, t=(0, 0, 1), nod=0.0, lean=0.0):
        return bar_pose(rig, sp, J, nod=nod, lean=lean, right=lambda P: right_shaker(rig, P, sp, grip, t))

    sh.key(0, base)
    sh.key(18, held(up, nod=-3))
    for i, f in enumerate(range(24, 84, 5)):
        off = Vector((0.0, -0.04, 0.07)) if i % 2 == 0 else Vector((0.0, 0.03, -0.05))
        sh.key(f, held(up + off, t=(0, -0.15 if i % 2 == 0 else 0.1, 1), nod=-4 if i % 2 == 0 else -1))
    sh.key(96, held(Vector((-0.08, BAR_Y - 0.18, BAR_Z + 0.32)), t=(0.15, -0.55, 0.8), nod=8, lean=6))
    sh.key(106, held(Vector((-0.06, BAR_Y - 0.2, BAR_Z + 0.3)), t=(0.25, -0.8, -0.55), nod=10, lean=8))
    sh.key(124, held(Vector((-0.06, BAR_Y - 0.2, BAR_Z + 0.3)), t=(0.25, -0.8, -0.55), nod=10, lean=8))
    sh.key(136, held(Vector((-0.12, BAR_Y - 0.16, BAR_Z + 0.25)), t=(0.1, -0.3, 0.9), nod=4, lean=4))
    sh.key(160, base)
    sh.write()

    laugh(rig, sp, base)


def build_preview_bar():
    CUR.name = "Preview"
    CUR.pal = {"Bar": "6A3A1E", "Top": "2A2734"}
    parts = [box("Body", (0.0, BAR_Y - 0.6, 0.54), (2.4, 1.1, 1.08), "Bar"),
             box("CounterTop", (0.0, BAR_Y - 0.6, BAR_Z - 0.04), (2.6, 1.3, 0.08), "Top")]
    return join(parts, "PreviewBar")


# ================================================================ Ablauf

os.makedirs(OUT_DIR, exist_ok=True)
arm, full, sp, J = build_rocco()
export_fbx(arm, [full], os.path.join(OUT_DIR, "NPC_Rocco.fbx"))
if PREVIEW_DIR:
    os.makedirs(PREVIEW_DIR, exist_ok=True)
    bar = build_preview_bar()
    h = 1.8 * sp["s"]
    for o in scene.objects:
        if o.type == 'MESH' and o not in (full, bar):
            o.hide_render = True
    arm.data.pose_position = 'REST'
    preview([full], os.path.join(PREVIEW_DIR, "rocco_tpose.png"), (0, -3.6, h * 0.55), (0, 0, h * 0.5), 50, (1300, 1100))
    preview([full], os.path.join(PREVIEW_DIR, "rocco_face.png"), (0.12, -0.75, h * 0.95), (0, 0, h * 0.93), 85, (900, 900))
    arm.data.pose_position = 'POSE'
    for clip, frame in (("Idle", 0), ("Idle", 30), ("Shake", 40), ("Shake", 110), ("Laugh", 14)):
        set_frame_pose(arm, clip, frame)
        preview([full, bar], os.path.join(PREVIEW_DIR, f"rocco_{clip.lower()}_{frame}.png"), (1.9, -3.2, 1.75), (0, -0.35, 1.05), 45, (1000, 1100))
    for t in arm.animation_data.nla_tracks:
        t.mute = False
log("fertig")
