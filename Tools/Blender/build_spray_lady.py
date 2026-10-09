"""Spray shop lady: editable, skinned Blender model and Unity FBX."""
import os, sys, math, json
from pathlib import Path
ROOT = Path(__file__).resolve().parents[2]
helper = ROOT/'Tools/Blender/build_stoners.py'
exec(compile(helper.read_text(encoding='utf-8-sig').split('# ================================================================ Ablauf')[0],str(helper),'exec'))
exec(compile((ROOT/'Tools/Blender/miru_shapes.py').read_text(encoding='utf-8-sig'),'miru_shapes.py','exec'))
DEST=ROOT/'Art/Characters/SprayShopLady'
DEST.mkdir(parents=True,exist_ok=True)
CUR.name='SprayLady'
CUR.pal=dict(Skin='E7B39C',Lips='A85364',Nails='AC3979',EyeWhite='FFF4EE',EyeRed='F0C0B8',Iris='8C659D',Pupil='211928',Shine='FFFFFF',Lid='C99082',Lash='352535',Brows='9B827F',MouthIn='623D48',Teeth='F2E8DA',Tongue='B9727C',Jacket='303249',JacketTrim='8580A6',Top='F4EBDD',Pants='414158',PantsDark='292C40',Shoes='34354E',ShoeCap='E8DDD6',Sole='EDE4D8',Laces='F9F0E3',Accent='B72E7C',Hair='EEE7E3',HairDark='BEB7C9',HairLight='FFF7EC',Gold='CFCDDD',Button='CFCDDD',Strings='CFCDDD')
sp=dict(s=1.,sw=.94,hw=1.04,head=1.07,soft_flat=True,r=dict(pelvis=1.08,waist=.85,belly=.90,chest=.91,neck=.86,arm=1.05,fore=.76,finger=.88,leg=1.17,knee=1.25,shin=1.48,ankle=1.52),shape=dict(bust=.030,butt=.02,waist_in=.08),face={})
J=make_joints(sp);s=sp['s'];k,p,sz,local=head_frame(sp,J)
body=build_body(sp,J)
# Widen the original continuous trouser mesh towards the hem.
for v in body.data.vertices:
    z=v.co.z
    if z<.77:
        t=max(0,min(1,(.77-z)/.61));sg=1 if v.co.x>=0 else -1
        center=sg*.112
        v.co.x=center+(v.co.x-center)*(1+.48*t)
        v.co.y*=1+.33*t
        if z<.25:v.co.z-=.035*math.sin(max(0,z)/.25*math.pi)
# Cut and extend clean open hems; retain continuous topology up to the hips.
bm=bmesh.new();bm.from_mesh(body.data)
bmesh.ops.bisect_plane(bm,geom=list(bm.verts)+list(bm.edges)+list(bm.faces),dist=.00001,plane_co=(0,0,.20),plane_no=(0,0,1),clear_inner=True)
hem=[e for e in bm.edges if e.is_boundary and all(abs(v.co.z-.20)<.0001 for v in e.verts)]
result=bmesh.ops.extrude_edge_only(bm,edges=hem)
for v in result['geom']:
    if isinstance(v,bmesh.types.BMVert):v.co.z=.105
bmesh.ops.recalc_face_normals(bm,faces=bm.faces);bm.to_mesh(body.data);bm.free()
displace(body,sleeve_folds(sp,J,.001,ankle_amp=.003,fabric=.001,bunch_wrist=.1));decimate(body,.55)
def zone(c,n):
    x,y,z=c
    if z<1.065:return 'Pants'
    if abs(x)>.535:return 'Skin'
    if z>1.46 and abs(x)<.08:return 'Top' if z<1.545 else 'Skin'
    return 'Jacket'
assign_regions(body,zone)
hands=[];nails=[]
for side in ('L','R'):
    h,n=build_hand(sp,J,side);hands.append(h);nails+=n
# Use the shared face shape without the socket boolean that produced brow dents.
original_boolean=boolean_diff
boolean_diff=lambda *args: delete(args[1])
head,head_rigid,eyes,mouth_z=clean_head()
boolean_diff=original_boolean
extras=[]
def panel(name,points,role,mode='body',thickness=.004):
    me=bpy.data.meshes.new(name);me.from_pydata(points,[],[tuple(range(len(points)))]);me.update()
    o=link(bpy.data.objects.new(name,me));set_mat(o,role);solidify(o,thickness);smooth(o);extras.append((o,mode));return o
# Smooth surface-fitted ivory insert avoids faceted material boundaries.
verts=[];faces=[]
for row in range(33):
    z=1.084+row/32*.395;w=.046+(z-1.084)*.15
    if z>1.39:w=.092-(z-1.39)*.60
    for col in range(9):verts.append(tuple(surf(body,(col/8*2-1)*w,z,.005)))
for row in range(32):
    for col in range(8):
        a=row*9+col;faces.append((a,a+1,a+10,a+9))
me=bpy.data.meshes.new('Ivory fitted blouse');me.from_pydata(verts,[],faces);me.update()
o=link(bpy.data.objects.new('Ivory fitted blouse',me));set_mat(o,'Top');smooth(o);extras.append((o,'body'))
# Open tailored coat with separate lapels and long panels around the hips.
for sg in (-1,1):
    panel('Silk lapel',[(sg*.062,-.066,1.483),(sg*.154,-.087,1.399),(sg*.11,-.137,1.28),(sg*.075,-.120,1.145)],'Accent')
    panel('Notched blazer lapel',[(sg*.074,-.078,1.47),(sg*.168,-.084,1.395),(sg*.13,-.127,1.345),(sg*.156,-.113,1.321),(sg*.063,-.131,1.10)],'Jacket')
    panel('Long tailored coat front',[(sg*.054,-.119,1.135),(sg*.147,-.09,1.16),(sg*.209,-.028,.82),(sg*.15,-.125,.86)],'Jacket')
    panel('Magenta coat lining',[(sg*.16,-.11,1.05),(sg*.218,-.038,.82),(sg*.153,-.135,.863)],'Accent')
    # Turned-back sleeve cuff in the rig's rest pose.
    extras.append((cylinder('Rolled lavender cuff',(sg*.523,.013,1.43),.052,.052,.055,'JacketTrim',rot=(0,math.pi/2,0),segs=32),'body'))
    for i in range(4):
        extras.append((ellipsoid('Paint fleck',(sg*(.507+i*.009),-.038,1.448+(i%2)*.012),(.005,.002,.007),'Accent' if i%2 else 'Top',12,8),'body'))
    extras.append((sneaker(sp,J,'L' if sg==1 else 'R',dict(kind='sneaker',sole=.032,cap='ShoeCap',len=1.08)),'custom'))
    extras.append((torus('Silver hoop',p(sg*.094,-.002,-.062),.014,.002,'Gold',rot=(math.pi/2,0,0),seg=24,mseg=8),'Head'))
    # Tailored pressed seams follow the trousers surface.
    line=[(surf(body,sg*(.113+(1-z)*.015),z,.003),1) for z in (.18,.28,.4,.52,.64,.78,.90,1.04)]
    extras.append((hair_curves('Trouser crease',[line],'PantsDark',.0012,bevel_res=1,res_u=5),'body'))
extras.append((band(body,Vector((0,0,1.065)),(0,0,1),(1,0,0),.043,.006,.003,'PantsDark',name='Tailored belt'),'body'))
extras.append((torus('Oval silver buckle',(0,-.122,1.065),.024,.0035,'Gold',rot=(math.pi/2,0,0),seg=32,mseg=8),'body'))
extras.append((cylinder('Ivory high collar',(0,.012,1.514),.047,.044,.05,'Top',segs=48),'body'))
# Swept, layered white hair; wide flattened locks, each with a flowing S curve.
scalp=hair_shell();set_mat(scalp,'Hair');extras.append((scalp,'Head'))
for i in range(3):
    extras.append((hair_lock('Right swept temple',[(.012+i*.018,-.014,.124),(.087+i*.011,-.079,.141),(.111+i*.01,-.085,.049),(.11+i*.014,-.022,-.1)],.021,.013,'HairLight' if i==0 else 'Hair'),'Head'))
for i in range(5):
    off=i*.019
    extras.append((hair_lock('Side swept white fringe',[(.049-off,.00,.12),(.05-off,-.113,.176),(-.084-off*.2,-.123,.09),(-.102-off*.2,-.075,-.04-i*.012)],.023,.011,'HairLight' if i%2==0 else 'Hair'),'Head'))
def long_lock(name,x,y,length,width,phase,role):
    verts=[];faces=[];rows=48;rings=10
    for j in range(rows+1):
        t=j/rows;z=.12-t*(length+.055)
        blend=min(1,t/.24);blend=blend*blend*(3-2*blend)
        xx=(x*.40)*(1-blend)+(x+math.sin(t*math.pi*3+phase)*(.014+.025*t))*blend
        yy=.035*(1-blend)+(y+.055*math.sin(t*math.pi)+.020*math.sin(t*math.pi*3+phase))*blend
        taper=max(.008,(1-t)**.45)*min(1,.30+t*7)
        for i in range(rings):
            a=2*math.pi*i/rings;verts.append(tuple(p(xx+width*taper*math.cos(a),yy+.018*taper*math.sin(a),z)))
    for j in range(rows):
        for i in range(rings):
            a=j*rings+i;b=j*rings+(i+1)%rings;faces.append((a,b,b+rings,a+rings))
    faces+=[tuple(reversed(range(rings))),tuple(rows*rings+i for i in range(rings))]
    me=bpy.data.meshes.new(name);me.from_pydata(verts,[],faces);me.update()
    bm=bmesh.new();bm.from_mesh(me);bmesh.ops.recalc_face_normals(bm,faces=bm.faces);bm.to_mesh(me);bm.free()
    o=link(bpy.data.objects.new(name,me));set_mat(o,role);smooth(o);extras.append((o,'Head'))
for i in range(9):
    x=(i-4)*.033
    long_lock('Long flowing back lock',x,.09+.035*(1-abs(i-4)/4),.68+.08*math.cos(i),.032,i*.6,'Hair' if i%3 else 'HairLight')
for sg in (-1,1):
    for i in range(3):long_lock('Face framing wave',sg*(.105+i*.025),-.005+i*.035,.48+i*.07,.026,i*.8+sg,'HairLight' if i==0 else 'Hair')
arm=build_armature(sp,J,eyes)
# Spray can anchored to hand in its rest frame.
hand=J['wrist_R'];canloc=hand+Vector((-.105,0,-.036))
# The cylinder runs across the palm, perpendicular to the curled fingers.
can=cylinder('Spray can',canloc,.028,.028,.135,'Accent',rot=(-math.pi/2,0,0),segs=32)
extras.append((can,'Hand_R'))
extras.append((cylinder('Can silver rim',canloc+Vector((0,.069,0)),.026,.023,.012,'Gold',rot=(-math.pi/2,0,0),segs=32),'Hand_R'))
extras.append((cylinder('Can nozzle',canloc+Vector((0,.080,0)),.009,.009,.012,'Jacket',rot=(-math.pi/2,0,0),segs=20),'Hand_R'))
full=finish_character(sp,J,body,hands,nails,head,head_rigid,extras,arm)
rig=Rig(arm);base=stand_base(rig,sp,J,lean=.25)
base=pose({'Spine':[(X,0)],'Chest':[(X,0)],'Neck':[(X,0)],'Head':[(Y,-2)],'loc':(0,0,0)},base)
base=arm_ik(rig,base,'R',Vector((-.28,-.23,1.19)),(-.5,.15,-1),fingers=(0,-1,.08),palm=(-1,0,0))
base=arm_ik(rig,base,'L',Vector((.275,-.04,.965)),(.8,.2,-1))
base['Hand_L']=rq((X,25))
# Independent hands: closed cylindrical grip on the right, relaxed on the left.
for side,amount,thumb in [('R',2.25,1.5),('L',.65,.4)]:
    spec=curl(amount,thumb=thumb,per={'Index':1.10,'Middle':1.,'Ring':.90,'Pinky':.82})
    for name,rots in spec.items():
        base=pose({name[:-1]+side:mirror_rots(rots) if side=='R' else rots},base)
a=Anim(rig,'Idle',120)
for f in (0,30,60,90,120):
    phase=math.sin(f/120*math.pi*2)
    a.key(f,pose({'Spine':[(X,phase*.35)],'Head':[(Y,-2+phase*.8)]},base))
a.write()
OUT=ROOT/'Assets/_Game/Resources/Characters/NPC_SprayLady.fbx'
export_fbx(arm,[full],str(OUT))
for track in arm.animation_data.nla_tracks:track.mute=False
arm.data.pose_position='POSE';set_frame_pose(arm,'Idle',0)
bpy.ops.wm.save_as_mainfile(filepath=str(DEST/'SprayShopLady.blend'))
preview([full],str(DEST/'SprayShopLady_blender.png'),(2.1,-4.4,1.9),(0,0,.92),58,(1000,1200))
preview([full],str(DEST/'SprayShopLady_face.png'),(.22,-1.,1.75),(0,-.01,1.65),65,(900,900))
preview([full],str(DEST/'SprayShopLady_hands.png'),(.15,-2.5,1.42),(0,-.1,1.16),65,(1200,900))
report=dict(vertices=len(full.data.vertices),triangles=sum(len(f.vertices)-2 for f in full.data.polygons),bones=len(arm.data.bones),unweighted_vertices=sum(not any(g.weight>.001 for g in v.groups) for v in full.data.vertices),animations=['Idle'])
(DEST/'validation.json').write_text(json.dumps(report,indent=2))
print('SPRAY_LADY_VALIDATED',json.dumps(report))
