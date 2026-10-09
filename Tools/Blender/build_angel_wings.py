"""Rebuilds the generated angel wings. Blender Z up, character faces -Y.
Origin is the upper-back attachment point; Unity forward is +Z.
"""
import bpy, math, os, json
from mathutils import Vector, Quaternion
ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), '../..'))
ART = os.path.join(ROOT, 'Art/Accessories/AngelWings')
OUT = os.path.join(ROOT, 'Assets/_Game/Accessories/AngelWings')
os.makedirs(ART, exist_ok=True); os.makedirs(OUT, exist_ok=True)
bpy.ops.object.select_all(action='SELECT'); bpy.ops.object.delete(use_global=False)
scene = bpy.context.scene
scene.unit_settings.system = 'METRIC'
scene.render.fps = 30
materials = []
for name, color in [('Ivory',(0.94,.9,.79,1)),('Pearl',(1,.98,.92,1)),('Lavender',(.62,.58,.76,1))]:
    m=bpy.data.materials.new(name); m.diffuse_color=color; materials.append(m)
arm_data=bpy.data.armatures.new('AngelWingSkeleton')
arm=bpy.data.objects.new('AngelWingsRig',arm_data); scene.collection.objects.link(arm)
bpy.context.view_layer.objects.active=arm; arm.select_set(True)
bpy.ops.object.mode_set(mode='EDIT')
def bone(name,a,b,parent=None):
    ob=arm_data.edit_bones.new(name); ob.head=a; ob.tail=b
    if parent: ob.parent=arm_data.edit_bones[parent]
    return ob
bone('WingRoot',(0,0,-.08),(0,0,.12))
for s,side in [(1,'L'),(-1,'R')]:
    bone('Shoulder.'+side,(s*.10,0,0),(s*.58,0,.44),'WingRoot')
    bone('Elbow.'+side,(s*.58,0,.44),(s*1.05,0,.64),'Shoulder.'+side)
    bone('Tip.'+side,(s*1.05,0,.64),(s*1.55,0,.7),'Elbow.'+side)
    for i in range(7):
        x=1.02+i*.075; z=.58+i*.012
        bone('Feather%02d.%s'%(i+1,side),(s*x,0,z),(s*(x+.27),0,z-.36),'Tip.'+side)
bpy.ops.object.mode_set(mode='OBJECT')
verts=[]; faces=[]; slots=[]; weights=[]
def feather(a,b,width,depth,group,mat):
    a=Vector(a); b=Vector(b); d=(b-a).normalized(); across=Vector((-d.z,0,d.x))
    # Lenticular, faceted feather with a raised central quill and curved tip.
    base=len(verts); rings=9; around=8
    for j in range(rings):
        t=j/(rings-1); profile=max(.025,math.sin(math.pi*t)**.65)*(1-.30*t)
        center=a.lerp(b,t)+Vector((0, math.sin(math.pi*t)*.035,0))
        for k in range(around):
            angle=k*2*math.pi/around
            p=center+across*(math.cos(angle)*width*profile)+Vector((0,math.sin(angle)*depth*profile,0))
            verts.append(tuple(p)); weights.append(group)
    for j in range(rings-1):
        for k in range(around):
            faces.append((base+j*around+k,base+j*around+(k+1)%around,base+(j+1)*around+(k+1)%around,base+(j+1)*around+k)); slots.append(mat if k>=4 else 2)
    faces.append(tuple(base+k for k in reversed(range(around)))); slots.append(mat)
    faces.append(tuple(base+(rings-1)*around+k for k in range(around))); slots.append(mat)
for s,side in [(1,'L'),(-1,'R')]:
    # Long outer flight feathers: separated tips form a recognisable angel silhouette.
    for i in range(7):
        x=1.02+i*.075; z=.58+i*.012
        feather((s*x,.015,z),(s*(1.12+i*.14),.06,-.60+i*.16),.145,.045,'Feather%02d.%s'%(i+1,side),i%2)
    for i in range(7):
        x=.28+i*.125; z=.14+i*.072
        feather((s*x,-.012,z),(s*(x+.12),0,z-.62),.145,.052,'Shoulder.'+side if i<3 else 'Elbow.'+side,i%2)
    # Two staggered rows of overlapping coverts conceal the joints.
    for row in range(2):
        for i in range(10):
            x=.16+i*.138; z=.08+min(x,1.10)*.53-row*.12
            group=('Shoulder.' if x<.58 else 'Elbow.' if x<1.05 else 'Tip.')+side
            feather((s*x,-.065-row*.045,z),(s*(x+.19),-.08-row*.045,z-.27),.108,.050,group,1 if row else 0)
mesh=bpy.data.meshes.new('LayeredFeathers'); mesh.from_pydata(verts,[],[tuple(reversed(f)) for f in faces]); mesh.update()
obj=bpy.data.objects.new('AngelWings',mesh); scene.collection.objects.link(obj)
for m in materials: mesh.materials.append(m)
for p,idx in zip(mesh.polygons,slots): p.material_index=idx; p.use_smooth=False
for name in sorted(set(weights)):
    vg=obj.vertex_groups.new(name=name); vg.add([i for i,w in enumerate(weights) if w==name],1,'REPLACE')
obj.parent=arm; mod=obj.modifiers.new('Wing skin','ARMATURE'); mod.object=arm
arm.show_in_front=True; arm.data.display_type='OCTAHEDRAL'
# Loop with delayed wrists and feather follow-through; no root movement.
scene.frame_start=1; scene.frame_end=49
for f in range(1,50):
    phase=(f-1)/48*math.tau
    for s,side in [(1,'L'),(-1,'R')]:
        for prefix,amp,delay in [('Shoulder',.50,0),('Elbow',.22,.5),('Tip',.18,.9)]:
            pb=arm.pose.bones[prefix+'.'+side]; pb.rotation_mode='QUATERNION'
            axis=pb.bone.matrix_local.to_3x3().inverted() @ Vector((0,1,0))
            pb.rotation_quaternion=Quaternion(axis,s*amp*math.sin(phase-delay))
            pb.keyframe_insert(data_path='rotation_quaternion',frame=f,group=pb.name)
        for i in range(7):
            pb=arm.pose.bones['Feather%02d.%s'%(i+1,side)]; pb.rotation_mode='XYZ'
            pb.rotation_euler.x=.1*math.sin(phase-1.1-i*.08)
            pb.keyframe_insert(data_path='rotation_euler',frame=f,group=pb.name)
action=arm.animation_data.action; action.name='WingFlap'
track=arm.animation_data.nla_tracks.new(); track.name='WingFlap'
track.strips.new('WingFlap',1,action); arm.animation_data.action=None
scene.frame_set(1)
bpy.ops.object.select_all(action='DESELECT'); arm.select_set(True); obj.select_set(True); bpy.context.view_layer.objects.active=arm
bpy.ops.export_scene.fbx(filepath=os.path.join(OUT,'AngelWings.fbx'),use_selection=True,object_types={'ARMATURE','MESH'},axis_forward='-Z',axis_up='Y',add_leaf_bones=False,bake_anim=True,bake_anim_use_all_actions=False,bake_anim_use_nla_strips=True,bake_anim_simplify_factor=0,armature_nodetype='NULL')
# Preview setup is retained in the editable source, but excluded from the FBX.
cam_data=bpy.data.cameras.new('PreviewCamera'); cam=bpy.data.objects.new('PreviewCamera',cam_data); scene.collection.objects.link(cam)
cam.location=(.5,-6,2.3); target=Vector((0,0,.08)); cam.rotation_euler=(target-Vector(cam.location)).to_track_quat('-Z','Y').to_euler(); cam_data.type='ORTHO'; cam_data.ortho_scale=4.3; scene.camera=cam
scene.render.engine='BLENDER_WORKBENCH'; scene.display.shading.light='STUDIO'; scene.display.shading.color_type='MATERIAL'; scene.display.shading.show_shadows=True; scene.display.shading.show_cavity=True; scene.display.shading.cavity_type='BOTH'; scene.display.shading.show_object_outline=True; scene.display.shading.background_type='WORLD'; scene.world.color=(.045,.055,.085)
scene.render.resolution_x=1400; scene.render.resolution_y=1000; scene.render.resolution_percentage=100
# Show rest shape in the still and leave the animation available in the timeline.
arm.data.pose_position='REST'
scene.render.filepath=os.path.join(ART,'AngelWings_preview.png'); bpy.ops.render.render(write_still=True)
arm.data.pose_position='POSE'
bpy.ops.wm.save_as_mainfile(filepath=os.path.join(ART,'AngelWings.blend'))
assert all(len(v.groups)>0 for v in mesh.vertices)
report={'bones':len(arm.data.bones),'vertices':len(mesh.vertices),'triangles':sum(len(p.vertices)-2 for p in mesh.polygons),'unweighted_vertices':0,'clip':'WingFlap','seconds':1.6}
with open(os.path.join(ART,'validation.json'),'w') as f: json.dump(report,f,indent=2)
print('ANGEL WINGS PASS',report)


