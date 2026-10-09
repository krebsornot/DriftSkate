"""Miru-specific sculpted face and broad hair locks. Executed by build_miru.py."""
def face_line(name, coords, role, depth):
    return hair_curves(name,[[(p(*v),radius) for v,radius in coords]],role,depth*k,bevel_res=1,res_u=5)

def clean_head():
    # A unified oval jaw and small integrated nose, without separate chin/bag spheres.
    parts=[ellipsoid('Skull',p(0,.012,.025),sz(.088,.098,.108),'Skin',40,28),
           ellipsoid('Face',p(0,-.015,-.024),sz(.074,.081,.087),'Skin',40,28),
           ellipsoid('Jaw',p(0,-.014,-.069),sz(.057,.063,.040),'Skin',32,20),
           ellipsoid('Bridge',p(0,-.087,-.009),sz(.009,.012,.024),'Skin',24,16),
           ellipsoid('Nose',p(0,-.099,-.027),sz(.009,.009,.009),'Skin',24,16)]
    for sg in (-1,1):
        parts.append(ellipsoid('Ear',p(sg*.083,.008,-.012),sz(.011,.018,.026),'Skin',24,16))
    head=join(parts,'Miru_Head'); remesh(head,.0019*k,.6,9); decimate(head,.40)
    eyes={}; rigid=[]
    for sg,side in ((1,'L'),(-1,'R')):
        E=p(sg*.032,-.080,.006); eyes[side]=E
        boolean_diff(head,ellipsoid('Socket',E,sz(.022,.023,.0125),'Skin',32,20))
        white=ellipsoid('Eye',E+sz(0,-.007,0),sz(.0205,.008,.0105),'EyeWhite',32,16)
        iris=ellipsoid('Iris',E+sz(0,-.014,0),sz(.0078,.002,.009),'Iris',24,16)
        pupil=ellipsoid('Pupil',E+sz(0,-.0155,0),sz(.0034,.001,.0065),'Pupil',20,12)
        shine=ellipsoid('Catchlight',E+sz(-.002,-.0165,.003),sz(.0017,.0008,.0017),'Shine',12,8)
        rigid.append((join([white,iris,pupil,shine]),'Eye_'+side))
        upper=[((sg*.012,-.094,.009),.5),((sg*.025,-.100,.017),1),((sg*.044,-.095,.016),1),((sg*.059,-.088,.022),.05)]
        rigid.append((face_line('Winged liner',upper,'Lash',.0019),'Head'))
        lower=[((sg*.014,-.094,.002),.3),((sg*.032,-.100,-.003),.6),((sg*.049,-.091,.003),.05)]
        rigid.append((face_line('Lower liner',lower,'Lash',.001),'Head'))
        brow=[((sg*.016,-.085,.033),.6),((sg*.030,-.087,.038),1),((sg*.048,-.078,.035),.3)]
        rigid.append((face_line('Brow',brow,'Brows',.0027),'Head'))
    rigid.append((ellipsoid('Lower lip',p(0,-.091,-.061),sz(.020,.006,.0035),'Lips',32,12),'Head'))
    lip=[((-.022,-.090,-.056),.15),((-.01,-.096,-.056),.6),((0,-.097,-.057),.7),((.014,-.094,-.056),.6),((.022,-.089,-.054),.1)]
    rigid.append((face_line('Lip line',lip,'MouthIn',.0016),'Head'))
    smooth(head); weight_rigid(head,'Head')
    return head,rigid,eyes,-.057

def hair_shell():
    verts=[tuple(p(0,.012,.148))]; faces=[]; segments=64; rows=18
    for j in range(1,rows+1):
        for i in range(segments):
            a=2*math.pi*i/segments
            edge=-.070+.128*max(0,math.cos(a))**.5
            theta=math.acos((edge-.024)/.124)*j/rows
            verts.append(tuple(p(.098*math.sin(theta)*math.sin(a),.012-.109*math.sin(theta)*math.cos(a),.024+.124*math.cos(theta))))
    for i in range(segments): faces.append((0,1+i,1+(i+1)%segments))
    for j in range(rows-1):
        for i in range(segments):
            a=1+j*segments+i; b=1+j*segments+(i+1)%segments
            faces.append((a,a+segments,b+segments,b))
    mesh=bpy.data.meshes.new('Hair scalp'); mesh.from_pydata(verts,[],faces); mesh.update()
    o=link(bpy.data.objects.new('Hair scalp',mesh)); set_mat(o,'HairDark')
    bm=bmesh.new(); bm.from_mesh(mesh); bmesh.ops.recalc_face_normals(bm,faces=bm.faces); bm.to_mesh(mesh); bm.free()
    solidify(o,.004*k); smooth(o); return o

def hair_lock(name,controls,width,depth,role):
    # Flattened tapered locks, not round tubes.
    a,b,c,d=[Vector(v) for v in controls]; verts=[]; faces=[]; rows=18; rings=10
    for j in range(rows+1):
        t=j/rows; q=(1-t)**3*a+3*(1-t)**2*t*b+3*(1-t)*t*t*c+t**3*d
        tangent=(-3*(1-t)**2*a+3*(1-t)*(1-3*t)*b+3*t*(2-3*t)*c+3*t*t*d).normalized()
        across=Vector((tangent.z,0,-tangent.x)).normalized()
        profile=max(.012,(1-t)**.5)*(.8+.2*math.sin(math.pi*t))
        for i in range(rings):
            angle=2*math.pi*i/rings
            v=q+across*(width*profile*math.cos(angle))+Vector((0,depth*profile*math.sin(angle),0))
            verts.append(tuple(p(*v)))
    for j in range(rows):
        for i in range(rings):
            a0=j*rings+i; b0=j*rings+(i+1)%rings
            faces.append((a0,b0,b0+rings,a0+rings))
    faces.extend([tuple(reversed(range(rings))),tuple(rows*rings+i for i in range(rings))])
    mesh=bpy.data.meshes.new(name); mesh.from_pydata(verts,[],faces); mesh.update()
    o=link(bpy.data.objects.new(name,mesh)); set_mat(o,role)
    bm=bmesh.new(); bm.from_mesh(mesh); bmesh.ops.recalc_face_normals(bm,faces=bm.faces); bm.to_mesh(mesh); bm.free()
    smooth(o); return o

def build_miru_hair():
    out=[(hair_shell(),'Head')]
    for i in range(4):
        off=i*.020
        controls=[(-.055+off,-.015,.107),(-.040+off,-.107,.125),(.023+off*.72,-.116,.056),(.023+off*.87,-.096,.033-i*.017)]
        out.append((hair_lock('Swept fringe',controls,.022,.009,'Hair' if i%2 else 'HairLight'),'Head'))
    for sg in (-1,1):
        for i in range(3):
            controls=[(sg*.063,.00+i*.022,.074),(sg*.108,.00+i*.037,.01),(sg*.108,.026+i*.036,-.07),(sg*(.081+i*.006),.044+i*.025,-.155-(.025 if sg==1 else 0)+i*.013)]
            out.append((hair_lock('Bob side',controls,.024,.013,'Hair' if i<2 else 'HairDark'),'Head'))
    for i in range(5):
        x=-.07+i*.035
        out.append((hair_lock('Bob back',[(x,.070,.079),(x,.126,.02),(x,.130,-.08),(x*.85,.102,-.145)],.026,.013,'HairDark' if i%2 else 'Hair'),'Head'))
    return out

def clothing_details(body):
    out=hoodie_details(sp,J,body,'Jacket','JacketTrim',pocket=False,strings=False,hood=False,neck_rib=False)
    # Explicit small collar geometry: radial projection can otherwise hit the T-pose sleeves.
    out.append((cylinder('Bomber collar',(0,.012*s,1.474*s),.091*s,.054*s,.062*s,'JacketTrim',segs=48,scale=(1,.91,1)),'body'))
    zipper=[(surf(body,0,z*s,.006*s),1) for z in (1.025,1.10,1.18,1.26,1.34,1.42,1.455)]
    out.append((hair_curves('Zip piping',[zipper],'Gold',.0018*s,bevel_res=1,res_u=3),'body'))
    out.append((box('Zip pull',surf(body,0,1.43*s,.009*s),(.009*s,.004*s,.02*s),'Gold',bevel=.002*s),'body'))
    for sg in (-1,1):
        out.append((patch(body,Vector((sg*.1*s,0,1.09*s)),(1,0,0),(0,0,1),rounded_rect(.075*s,.012*s,.003*s),(0,1,0),.007*s,.002*s,'Gold',name='Pocket zip'),'body'))
        out.append((box('Cargo pocket',surf(body,sg*.13*s,.69*s,.006*s),(.10*s,.035*s,.14*s),'PantsDark',bevel=.010*s),'body'))
        out.append((box('Cargo flap',surf(body,sg*.13*s,.750*s,.023*s),(.11*s,.022*s,.028*s),'Jacket',bevel=.006*s),'body'))
    out.append((box('Red chest patch',surf(body,.081*s,1.35*s,.007*s),(.055*s,.008*s,.036*s),'Accent',bevel=.005*s),'body'))
    for side in ('L','R'):
        out.append((sneaker(sp,J,side,dict(kind='sneaker',sole=.035,cap='ShoeCap',len=1.03)),'custom'))
    out.append((band(body,Vector((0,0,1.018*s)),(0,0,1),(1,0,0),.035*s,.006*s,.003*s,'PantsDark',name='Belt'),'body'))
    out.append((box('Buckle',surf(body,0,1.015*s,.014*s),(.038*s,.01*s,.025*s),'Gold',bevel=.003*s),'body'))
    for i in range(20):
        t=i/19; x=(.045+.16*t)*s; z=(1.005-.092*math.sin(math.pi*t))*s
        out.append((torus('Hip chain',surf(body,x,z,.018*s),.006*s,.0014*s,'Gold',rot=(math.pi/2,(i%2)*.8,0),seg=12,mseg=5),'body'))
    out.append((cylinder('Choker',(0,.012*s,1.535*s),.051*s,.050*s,.018*s,'JacketTrim',segs=40),'body'))
    out.append((torus('Choker ring',(0,-.041*s,1.535*s),.006*s,.0012*s,'Gold',rot=(math.pi/2,0,0),seg=18,mseg=6),'body'))
    return out
