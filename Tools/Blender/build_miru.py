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
exec(compile((ROOT / 'Tools/Blender/miru_shapes.py').read_text(encoding='utf-8-sig'), 'miru_shapes.py', 'exec'))
CUR.name = 'Miru'
CUR.pal = dict(Skin='D8AB98', Lips='824C58', Nails='18151E', EyeWhite='F4E9E3', EyeRed='E6B8AD',
 Iris='8EAE8B', Pupil='120F19', Shine='FFFFFF', Lid='C18C7D', Lash='18121D', Brows='70202D',
 MouthIn='331A28', Teeth='EEE5D8', Tongue='B96C79', Jacket='454354', JacketTrim='292B39',
 Top='81818C', Pants='333644', PantsDark='242936', Shoes='24212C', ShoeCap='3B3543',
 Sole='ADA9AE', Laces='CBCAD3', Accent='A72E43', Hair='BA293D', HairDark='852137', HairLight='D7464F',
 Gold='B8C1CD', Button='B8C1CD', Strings='B8C1CD', Paper='F1E6D1', Ember='FF622A', Crutch='C8AD84')
sp = dict(s=.98,sw=1.02,hw=1.02,head=1.03,soft_flat=True,
 r=dict(pelvis=1.03,waist=.93,belly=.93,chest=.97,neck=.91,arm=1.22,fore=1.12,finger=.92,leg=1.2,knee=1.13,shin=1.15,ankle=1.12),
 shape=dict(bust=.0,butt=.015,waist_in=.04),
 face=dict(cw=.98,jw=.91,fl=1.02,chin=1.,cheek=.94,nose=.95,nose_len=1.,lips=.96,ear=.96,
 smile=.001,lid=3.,bags=.6,brow_h=.002,brow_tilt=-.07))
J=make_joints(sp); s=sp['s']; k,p,sz,local=head_frame(sp,J)
body=build_body(sp,J)
displace(body,sleeve_folds(sp,J,.0012,ankle_amp=.002,bunch_wrist=.5)); decimate(body,.5)
def zone(c,n):
 z=c.z/s; ax=abs(c.x)/s
 if z>1.505 and ax<.09: return 'Skin'
 if z<1.015: return 'Pants'

 return 'Jacket'
assign_regions(body,zone)
hands=[]; nails=[]
for side in ('L','R'):
 h,n=build_hand(sp,J,side); hands.append(h); nails+=n
head,head_rigid,eyes,mouth_z=clean_head()
extras=clothing_details(body)
extras+=build_miru_hair()
for sg in (-1,1):
 extras.append((torus('Ear hoop',p(sg*.095,.001,-.052),.009*k,.0015*k,'Gold',rot=(0,math.pi/2,0),seg=18,mseg=6),'Head'))
extras.append((torus('Septum',p(0,-.111,-.037),.0035*k,.0008*k,'Gold',rot=(math.pi/2,0,0),arc=(math.pi,2*math.pi),seg=14,mseg=6),'Head'))
joint,tip=joint_prop(sp,J,'R'); extras.append((joint,'Middle1_R'))
arm=build_armature(sp,J,eyes)
select_only(arm); bpy.ops.object.mode_set(mode='EDIT')
b=arm.data.edit_bones.new('JointTip'); b.head=tip; b.tail=tip+Vector((0,0,.02))*s; b.parent=arm.data.edit_bones['Middle1_R']; b.use_deform=False
bpy.ops.object.mode_set(mode='OBJECT')
full=finish_character(sp,J,body,hands,nails,head,head_rigid,extras,arm)
rig=Rig(arm)
base=stand_base(rig,sp,J,lean=0.)
base=pose({'Spine':[(X,0)],'Chest':[(X,0)],'Neck':[(X,0)],'Head':[(Y,-3)],'loc':(0,0,0)},base)
base=pose(curl(.9,per={'Index':.25,'Middle':.35}),base)
base=pose({'LowerArm_R':[(Z,42)],'Hand_R':[(Z,0)]},base)
a=Anim(rig,'Idle',120)
for f in (0,30,60,90,120):
 phase=math.sin(f/120*2*math.pi)
 a.key(f,pose({'Spine':[(X,phase*.6)],'Head':[(Y,-3+phase*1.2)]},base))
a.write()
# Seated breathing loop, authored for a 45 cm seat; root stays at ground level.
seated=pose({'loc':(0,0,.57-J['pelvis'].z),'Hips':[(X,-3)],
 'Spine':[(X,5)],'Chest':[(X,2)],'Neck':[(X,-3)],'Head':[(Y,-4)],'Shoulder_*':[(Y,5)]})
for side,sg in (('L',1),('R',-1)):
 seated=leg_ik(rig,seated,side,Vector((sg*.18,-.43,.10))*s,(sg*.4,-1,.2),toe_dir=(sg*.12,-1,0))
seated=arm_ik(rig,seated,'L',Vector((.17,-.27,.66))*s,(1,.2,-.3),fingers=(0,-.95,-.3),palm=(0,.3,-.95))
seated=arm_ik(rig,seated,'R',Vector((-.27,-.24,.80))*s,(-1,.2,-.4),fingers=(0,-.9,.4),palm=(0,.4,.9))
seated=pose(curl(.65,per={'Index':.2,'Middle':.3}),seated)
sit=Anim(rig,'Sitting',120)
for f in (0,30,60,90,120):
 phase=math.sin(f/120*2*math.pi)
 sit.key(f,pose({'Spine':[(X,5+phase*.6)],'Head':[(Y,-4+phase*1.5)]},seated))
sit.write()
export_fbx(arm,[full],str(OUT))
for track in arm.animation_data.nla_tracks: track.mute=False
arm.data.pose_position='POSE'; set_frame_pose(arm,'Idle',0)
bpy.ops.wm.save_as_mainfile(filepath=str(SOURCE))
preview([full],str(PREVIEW/'Miru_preview.png'),(2.3,-4.2,1.95),(0,0,.91),58,(1000,1200))
preview([full],str(PREVIEW/'Miru_face.png'),(.18,-.90,1.77),(0,-.01,1.66),65,(900,900))
set_frame_pose(arm,'Sitting',0)
preview([full],str(PREVIEW/'Miru_sitting_blender.png'),(2.5,-3.5,1.65),(0,-.12,.70),60,(1000,1100))
report={'character':'Miru','revision':2,'adult':True,'vertices':len(full.data.vertices),'triangles':sum(len(f.vertices)-2 for f in full.data.polygons),
 'bones':len(arm.data.bones),'unweighted_vertices':sum(not any(g.weight>.001 for g in v.groups) for v in full.data.vertices),
 'animations':['Idle','Sitting'],'seat_height_m':.45,'source':str(SOURCE),'fbx':str(OUT)}
(PREVIEW/'validation.json').write_text(json.dumps(report,indent=2),encoding='utf-8')
print('MIRU_VALIDATED',json.dumps(report))




