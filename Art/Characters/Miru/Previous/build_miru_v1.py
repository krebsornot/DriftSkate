"""Miru: adult goth streetwear NPC. Reuses the project's Blender character helpers.
Run Blender --background --factory-startup --python Tools/Blender/build_miru.py
"""
import os, sys, math, json
from pathlib import Path
ROOT = Path(__file__).resolve().parents[2]
# Load helpers only; do not run the existing character export loop.
helper = ROOT / 'Tools/Blender/build_stoners.py'
exec(compile(helper.read_text(encoding='utf-8-sig').split('# ================================================================ Ablauf')[0], str(helper), 'exec'))
OUT = ROOT / 'Assets/_Game/Resources/Characters/NPC_Miru.fbx'
SOURCE = ROOT / 'Art/Characters/Miru/Miru.blend'
PREVIEW = ROOT / 'Art/Characters/Miru'
SOURCE.parent.mkdir(parents=True, exist_ok=True)
CUR.name = 'Miru'
CUR.pal = dict(Skin='D5A18C', Lips='613040', Nails='18151E', EyeWhite='F4E9E3', EyeRed='E6B8AD',
 Iris='8EAE8B', Pupil='120F19', Shine='FFFFFF', Lid='C18C7D', Lash='18121D', Brows='70202D',
 MouthIn='331A28', Teeth='EEE5D8', Tongue='B96C79', Jacket='23212E', JacketTrim='393543',
 Top='17151D', Pants='292735', PantsDark='1C1A25', Shoes='24212C', ShoeCap='3B3543',
 Sole='77737D', Laces='CBCAD3', Accent='A72E43', Hair='C33242', HairDark='6E182F',
 Gold='B8C1CD', Button='B8C1CD', Strings='B8C1CD', Paper='F1E6D1', Ember='FF622A', Crutch='C8AD84')
sp = dict(s=.98,sw=.94,hw=1.02,head=.98,
 r=dict(pelvis=1.03,waist=.93,belly=.93,chest=.97,neck=.91,arm=1.05,fore=.94,finger=.92,leg=1.2,knee=1.13,shin=1.25,ankle=1.3),
 shape=dict(bust=.015,butt=.015,waist_in=.04),
 face=dict(cw=.98,jw=.91,fl=1.02,chin=1.,cheek=.94,nose=.95,nose_len=1.,lips=.96,ear=.96,
 smile=.001,lid=3.,bags=.6,brow_h=.002,brow_tilt=-.07))
J=make_joints(sp); s=sp['s']; k,p,sz,local=head_frame(sp,J)
body=build_body(sp,J)
displace(body,sleeve_folds(sp,J,.0035,ankle_amp=.006,bunch_wrist=1.1)); decimate(body,.5)
def zone(c,n):
 z=c.z/s; ax=abs(c.x)/s
 if z>1.47 and ax<.09: return 'Skin'
 if z<1.015: return 'Pants'
 if c.y<-.02*s and ax<.065 and z<1.42: return 'Top'
 return 'Jacket'
assign_regions(body,zone)
hands=[]; nails=[]
for side in ('L','R'):
 h,n=build_hand(sp,J,side); hands.append(h); nails+=n
head,head_rigid,eyes,mouth_z=build_head(sp,J)
extras=[]
# Cropped jacket collar, front seams, cuffs, silver zipper.
extras+=hoodie_details(sp,J,body,'Jacket','JacketTrim',pocket=False,strings=False,hood=False)
for sg in (-1,1):
 extras.append((patch(body,Vector((sg*.075*s,0,1.23*s)),(1,0,0),(0,0,1),rounded_rect(.022*s,.35*s,.005*s),(0,1,0),.006*s,.002*s,'JacketTrim',name='Jacket seam'),'body'))
 for z in (1.10,1.18,1.26,1.34):
  extras.append((ellipsoid('Jacket stud',surf(body,sg*.085*s,z*s,.01*s),(.004*s,.004*s,.004*s),'Gold',10,6),'body'))
for side in ('L','R'):
 extras.append((sneaker(sp,J,side,dict(kind='sneaker',sole=.055,cap='ShoeCap')),'custom'))
extras.append((band(body,Vector((0,0,1.018*s)),(0,0,1),(1,0,0),.035*s,.005*s,.003*s,'PantsDark',name='Belt'),'body'))
# Silver hip chain follows upper leg, deliberately separate editable links.
for i in range(18):
 t=i/17; c=Vector((.17*s+ .035*s*math.sin(math.pi*t), -.055*s, (1.00-.18*math.sin(math.pi*t))*s))
 extras.append((torus('Hip chain',c,.008*s,.0017*s,'Gold',rot=(math.pi/2,0,(i%2)*math.pi/2),seg=12,mseg=5),'UpperLeg_L'))
# Dark choker with silver ring.
extras.append((cylinder('Choker',(0,.012*s,1.53*s),.046*s,.046*s,.022*s,'Top',segs=24),'Neck'))
extras.append((torus('Choker ring',(0,-.035*s,1.53*s),.008*s,.0018*s,'Gold',rot=(math.pi/2,0,0),seg=16,mseg=6),'Neck'))
# Red undercut cap plus sweeping long fringe and longer side locks.
extras.append((hair_cap(sp,J,'Undercut','HairDark',.05,-.07,.006,.006,grooves=.001),'head'))
strands=[]
for i in range(6):
 x=-.005+i*.014
 strands.append([(p(x-.025,-.038,.106),1.25),(p(x+.003,-.075,.070),1.4),
  (p(x+.012,-.088,.042-i*.007),1.05),(p(x+.015,-.086,.024-i*.013),.08)])
extras.append((hair_curves('Red swept fringe',strands,'Hair',.012*k,bevel_res=1,res_u=5),'Head'))
for sg in (-1,1):
 for i in range(4):
  pts=[(p(sg*.078,.01+i*.016,.058),1.),(p(sg*(.095+i*.003),.025+i*.015,-.02),1.1),
   (p(sg*.096,.04+i*.014,-.13),.85),(p(sg*.081,.055+i*.014,-.19),.05)]
  extras.append((hair_curves('Red side lock',[pts],'Hair' if i%2==0 else 'HairDark',.010*k,bevel_res=1,res_u=4),'Head'))
for sg in (-1,1):
 extras.append((torus('Ear hoop',p(sg*.095,.001,-.052),.009*k,.0015*k,'Gold',rot=(0,math.pi/2,0),seg=18,mseg=6),'Head'))
extras.append((torus('Septum',p(0,-.107,-.035),.0045*k,.001*k,'Gold',rot=(math.pi/2,0,0),arc=(math.pi,2*math.pi),seg=14,mseg=6),'Head'))
joint,tip=joint_prop(sp,J,'R'); extras.append((joint,'Middle1_R'))
arm=build_armature(sp,J,eyes)
select_only(arm); bpy.ops.object.mode_set(mode='EDIT')
b=arm.data.edit_bones.new('JointTip'); b.head=tip; b.tail=tip+Vector((0,0,.02))*s; b.parent=arm.data.edit_bones['Middle1_R']; b.use_deform=False
bpy.ops.object.mode_set(mode='OBJECT')
full=finish_character(sp,J,body,hands,nails,head,head_rigid,extras,arm)
rig=Rig(arm)
base=stand_base(rig,sp,J,lean=0.)
base=pose(curl(.55,per={'Index':.25,'Middle':.25}),base)
a=Anim(rig,'Idle',120)
for f in (0,30,60,90,120):
 phase=math.sin(f/120*2*math.pi)
 a.key(f,pose({'Spine':[(X,phase*.6)],'Head':[(Y,phase*2)],'Lid_*':[(X,22 if f==90 else 0)]},base))
a.write()
export_fbx(arm,[full],str(OUT))
for track in arm.animation_data.nla_tracks: track.mute=False
arm.data.pose_position='POSE'; set_frame_pose(arm,'Idle',0)
bpy.ops.wm.save_as_mainfile(filepath=str(SOURCE))
preview([full],str(PREVIEW/'Miru_preview.png'),(2.3,-4.2,1.95),(0,0,.91),58,(1000,1200))
preview([full],str(PREVIEW/'Miru_face.png'),(.18,-.90,1.77),(0,-.01,1.66),65,(900,900))
report={'character':'Miru','adult':True,'vertices':len(full.data.vertices),'triangles':sum(len(f.vertices)-2 for f in full.data.polygons),
 'bones':len(arm.data.bones),'unweighted_vertices':sum(not any(g.weight>.001 for g in v.groups) for v in full.data.vertices),
 'animations':['Idle'],'source':str(SOURCE),'fbx':str(OUT)}
(PREVIEW/'validation.json').write_text(json.dumps(report,indent=2),encoding='utf-8')
print('MIRU_VALIDATED',json.dumps(report))

