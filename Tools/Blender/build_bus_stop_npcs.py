# Zwei sitzende Figuren fuer die Haltestelle. Gemeinsame Modell- und Rig-Helfer wiederverwenden.
# blender --background --factory-startup --python build_bus_stop_npcs.py -- <FBX-Ordner> <Vorschau-Ordner>
from pathlib import Path
source = Path(__file__).with_name('build_stoners.py').read_text(encoding='utf-8-sig')
exec(compile(source.split('# ================================================================ Ablauf')[0], str(Path(__file__).with_name('build_stoners.py')), 'exec'))
import os
os.makedirs(OUT_DIR, exist_ok=True)
os.makedirs(PREVIEW_DIR, exist_ok=True)
CRATE_H = 0.48

def build_resident(name, slim):
    CUR.name = name
    CUR.pal = dict(Skin='C19A7C', Lips='92685D', Nails='BAAB94', EyeWhite='E9DFCC', EyeRed='D5B9AA', Iris='676B58',
                   Pupil='201B19', Shine='FFFFFF', Lid='B18B70', Lash='51473E', Brows='675D50', MouthIn='412D2A',
                   Teeth='DDD0B1', Tongue='AE726A', Beard='8C8980', Hair='675447' if slim else '979488',
                   Jacket='746A57' if slim else '526454', Trim='4B443E', Shirt='AB977E', Pants='595568' if slim else '49556A',
                   Patch='9B8964', Stitch='D0BDA0', Shoes='51483D', ShoeCap='6C6254', Sole='272A28', Laces='ADA18A',
                   Accent='827C6B', Hat='865F42')
    sp = dict(s=0.98 if slim else 1.02, sw=0.94 if slim else 1.12, hw=1.02, head=1, soft_flat=True,
              r=dict(pelvis=1.1, waist=1.05 if slim else 1.3, belly=1.05 if slim else 1.4, chest=1.12, neck=1,
                     arm=1.32, fore=1.18, finger=1, leg=1.4, knee=1.3, shin=1.3, ankle=1.2),
              shape=dict(belly=0.01 if slim else 0.035, waist_in=0),
              face=dict(cw=1, jw=0.9 if slim else 1.12, fl=1.07, chin=1, cheek=0.9, nose=1.18, nose_len=1.1,
                        lips=0.95, ear=1, smile=0.001, lid=5, bags=1.2, brow_h=0, brow_size=1, mouth_w=1))
    J = make_joints(sp)
    s = sp['s']
    k,p,sz,local = head_frame(sp,J)
    body = build_body(sp,J)
    displace(body,sleeve_folds(sp,J,0.004,ankle_amp=0.008,fabric=0.002))
    decimate(body,0.38)
    def clothing(c,n):
        z=c.z/s
        if z>1.47 and abs(c.x)<0.1*s: return 'Skin'
        if z<0.97: return 'Pants'
        return 'Jacket'
    assign_regions(body,clothing)
    hands,nails=[],[]
    for side in ('L','R'):
        h,n=build_hand(sp,J,side); hands.append(h); nails+=n
    sp['face']['zone']=stubble_zone('Beard',line_hi=-0.015,line_lo=0.02)
    head,head_rigid,eyes,mz=build_head(sp,J)
    extras=[]
    # Hemdeinsatz und Kragen verdecken die grobe Materialgrenze am Hals.
    extras.append((patch(body,Vector((0,0,1.27*s)),(1,0,0),(0,0,1),rounded_rect(.115*s,.31*s,.01*s),(0,1,0),.012*s,.003*s,'Shirt',rings=8,name='ShirtFront'),'body'))
    collar=torus('Collar',J['neck']+Vector((0,.01,-.02))*s,.065*s,.022*s,'Trim',scale=(1.25,1,1),seg=32,mseg=8)
    extras.append((collar,'Spine2'))
    for side in ('L','R'):
        extras.append((sneaker(sp,J,side,dict(kind='sneaker',sole=0.035,cap='ShoeCap',len=1.05)),'custom'))
    # Aufgesetzte Flicken mit deutlich sichtbaren Stichen.
    for x,z,w,h in ((0.12,0.64,0.115,0.14),(-0.1,1.16,0.09,0.075),(0.09,1.32,0.07,0.09)):
        center=Vector((x*s,0,z*s))
        extras.append((patch(body,center,(1,0,0),(0,0,1),rounded_rect(w*s,h*s,0.009*s),(0,1,0),0.014*s,0.003*s,'Patch',rings=8,name='ClothPatch'),'body'))
        for i in range(5):
            xx=(x-w*0.4+i*w*0.2)*s
            for zz in (z-h*0.4,z+h*0.4):
                q=surf(body,xx,zz*s,0.019*s)
                extras.append((capsule('Stitch',q-Vector((0.002,0,0.004))*s,q+Vector((0.002,0,0.004))*s,0.0015*s,'Stitch',6),'body'))
    extras.append((hair_cap(sp,J,'Hair','Hair',0.04,-0.075,0.01,0.009),'Head'))
    rng=random.Random(19 if slim else 31)
    if slim:
        # Unregelmaessige Haarbueschel statt einer glatten Haarkappe.
        strands=[]
        for i in range(95):
            angle=rng.uniform(0,2*math.pi); elev=rng.uniform(0.12,1.5)
            x=0.09*math.cos(angle)*math.cos(elev); y=0.01+0.095*math.sin(angle)*math.cos(elev); z=0.035+0.11*math.sin(elev)
            root=p(x,y,z)
            direction=Vector((math.cos(angle)*.6,math.sin(angle)*.6,rng.uniform(.15,1))).normalized()*k
            tip=root+direction*rng.uniform(.035,.075)
            strands.append([(root,1),(root+direction*.025,1.2),(tip,.02)])
        extras.append((hair_curves('TousledHair',strands,'Hair',.015*k,bevel_res=1,res_u=3),'Head'))
    else:
        extras.append((cut_ellipsoid('Beanie',p(0,.012,.085),sz(.108,.116,.105),'Hat',lambda c:c.z>p(0,0,.045).z,segs=32,rings=20),'Head'))
        extras.append((torus('BeanieCuff',p(0,.01,.052),.103*k,.016*k,'Hat',scale=(1,1.12,1),seg=32,mseg=8),'Head'))
        beard=[]
        for i in range(22):
            beard.append(ellipsoid('BeardTuft',p(rng.uniform(-.05,.05),-.079,rng.uniform(-.13,-.07)),sz(.025,.024,.035),'Beard',12,8))
        beard=join(beard,'Beard')
        remesh(beard,.003*k,.4,3)
        decimate(beard,.35)
        extras.append((beard,'Head'))
    arm=build_armature(sp,J,eyes)
    full=finish_character(sp,J,body,hands,nails,head,head_rigid,extras,arm)
    rig=Rig(arm)
    def sitting(nod=-3,look=0):
        return pose(side_curl(1.8,'R'),moe_pose(rig,sp,J,nod=nod,look=look))
    base=sitting()
    idle=Anim(rig,'Idle',210)
    for f,nod,look in ((0,-3,0),(60,0,5),(120,-2,-7),(180,-3,0)):
        idle.key(f,sitting(nod,look))
    idle.write()
    action=Anim(rig,'Smoke' if slim else 'Drink',180,loop=False)
    mouth=rig.point(base,'Head',p(0,-.12,-.055))
    up=arm_ik(rig,base,'R',mouth+Vector((-.055,-.10,-.075 if slim else -.14))*s,(-1,-.1,-.5),fingers=(.65,.1,.65),palm=(0,-1,.1))
    for f,P in ((0,base),(35,up),(82,up),(112,base),(180,base)): action.key(f,P)
    action.write()
    laugh(rig,sp,base)
    export_fbx(arm,[full],os.path.join(OUT_DIR,'NPC_'+name+'.fbx'))
    set_frame_pose(arm,'Idle',0)
    preview([full],os.path.join(PREVIEW_DIR,name+'_idle.png'),(1.6,-3,1.5),(0,-.1,.75),55,(1000,1100))
    set_frame_pose(arm,'Smoke' if slim else 'Drink',55)
    preview([full],os.path.join(PREVIEW_DIR,name+'_action.png'),(.65,-2.4,1.5),(0,-.1,.95),60,(1000,1100))

build_resident('Bruno',False)
build_resident('Fiete',True)
