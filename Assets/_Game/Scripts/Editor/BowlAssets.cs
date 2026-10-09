using System;
using System.IO;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using UnityEditor;
using UnityEngine;
using UnityEditor.SceneManagement;
using UnityEngine.SceneManagement;
using System.Reflection;

namespace DriftSkate.EditorTools
{
    /// <summary>Recessed pool: deck at zero, floor -2.6m, rideable 60 degree transitions.</summary>
    public static class BowlAssets
    {
        const string Root = "Assets/_Game/SkateparkTest";
        const int Segments = 160, Steps = 48;
        const float Depth=2.6f, Radius=5.2f, LipAngle=Mathf.PI/3;
        static float Run => Radius*Mathf.Sin(LipAngle);
        static readonly List<Mesh> Meshes = new List<Mesh>();
        static readonly List<Material> Materials = new List<Material>();
        static Vector3 Edge(float a) => new Vector3(6*Mathf.Cos(a),0,3.5f*Mathf.Sin(a)*(1+.18f*Mathf.Cos(a)));
        static Vector3 Outward(float a) => new Vector3(3.5f*(Mathf.Cos(a)+.18f*Mathf.Cos(2*a)),0,6*Mathf.Sin(a)).normalized;
        static Vector3 Ring(float a,float offset,float y) => Edge(a)+Outward(a)*offset+Vector3.up*y;
        static Vector3 Surface(float a,float b) => Ring(a,Radius*Mathf.Sin(b),-Depth+Radius*(1-Mathf.Cos(b)));

        [MenuItem("DriftSkate/Skatepark-Testset/Bowl erstellen")]
        public static void Build()
        {
            if(Application.isBatchMode)EditorSceneManager.OpenScene("Assets/_Game/Scenes/Garage.unity");
            foreach (string folder in new[] {"Meshes", "Materials", "Prefabs"}) Directory.CreateDirectory(Root + "/" + folder);
            Directory.CreateDirectory("Art/Environment/Bowl");
            AssetDatabase.Refresh();
            Meshes.Clear(); Materials.Clear();
            var root = new GameObject("NeonPool_Bowl");
            try
            {
                var blue = Mat("PoolBlue", "B3DBDD");
                blue.SetFloat("_Threshold",.58f);blue.SetFloat("_Softness",.055f);
                var cream = Mat("PoolDeck", "C9C3D6");
                var pink = Mat("PoolPink", "FF3D8B");
                var teal = Mat("PoolTeal", "327C91");
                var ink = Mat("PoolInk", "23263A");
                var white = Mat("PoolCoping", "F7F4EE");
                var v = new List<Vector3>(); var t = new List<int>();
                v.Add(new Vector3(0,-Depth,0));
                for (int j=0;j<=Steps;j++)
                {
                    float b = j/(float)Steps * LipAngle;
                    for (int i=0;i<Segments;i++) v.Add(Surface(i*2*Mathf.PI/Segments,b));
                }
                for(int i=0;i<Segments;i++) t.AddRange(new[]{0,1+(i+1)%Segments,1+i});
                for(int j=0;j<Steps;j++) for(int i=0;i<Segments;i++)
                {
                    int a=1+j*Segments+i, b=1+j*Segments+(i+1)%Segments, c=a+Segments,d=b+Segments;
                    t.AddRange(new[]{a,b,c,b,d,c});
                }
                Part(root,"PoolSurface",v,t,blue,true);
                Strip(root,"TransitionPaint",0,.045f,-Depth+.009f,-Depth+.009f,teal,false);
                v=new List<Vector3>();t=new List<int>();
                for(int row=0;row<3;row++)
                {
                    float z=-1.2f+row*.9f;int k=v.Count;
                    v.Add(new Vector3(-1.2f,-Depth+.012f,z));v.Add(new Vector3(0,-Depth+.012f,z+.6f));
                    v.Add(new Vector3(1.2f,-Depth+.012f,z));v.Add(new Vector3(1.2f,-Depth+.012f,z+.2f));
                    v.Add(new Vector3(0,-Depth+.012f,z+.8f));v.Add(new Vector3(-1.2f,-Depth+.012f,z+.2f));
                    t.AddRange(new[]{k,k+1,k+4,k,k+4,k+5,k+1,k+2,k+3,k+1,k+3,k+4});
                }
                for(int i=0;i<t.Count;i+=3){int swap=t[i+1];t[i+1]=t[i+2];t[i+2]=swap;}
                Part(root,"FlowArrows",v,t,pink,false);
                Strip(root,"Deck",Run,Run+2.2f,0,0,cream,true);
                Strip(root,"DeckFascia",Run+2.2f,Run+2.2f,0,-.22f,ink,false);
                Strip(root,"PaintedLip",Run+.16f,Run+.24f,.006f,.006f,teal,false);
                // Subtle expansion joints on the deck, grouped into a single mesh.
                v=new List<Vector3>();t=new List<int>();
                for(int i=0;i<Segments;i+=5)
                {
                    float a=i*2*Mathf.PI/Segments;int k=v.Count;
                    v.Add(Ring(a,Run+.32f,.012f));v.Add(Ring(a+.004f,Run+.32f,.012f));
                    v.Add(Ring(a,Run+2.19f,.012f));v.Add(Ring(a+.004f,Run+2.19f,.012f));
                    t.AddRange(new[]{k,k+1,k+2,k+1,k+3,k+2});
                }
                Part(root,"DeckJoints",v,t,ink,false);
                // Pool tiles follow the actual transition, with a tiny normal offset.
                for(int colour=0;colour<2;colour++)
                {
                    v=new List<Vector3>();t=new List<int>();
                    for(int i=colour;i<Segments;i+=2)
                    {
                        int start=v.Count;
                        foreach(float b in new[]{LipAngle-.07f,LipAngle-.012f}) foreach(int k in new[]{i,i+1})
                        {
                            float a=k*2*Mathf.PI/Segments;
                            v.Add(Surface(a,b)+.008f*(Vector3.up*Mathf.Cos(b)-Outward(a)*Mathf.Sin(b)));
                        }
                        t.AddRange(new[]{start,start+1,start+2,start+1,start+3,start+2});
                    }
                    Part(root,colour==0?"WhiteTiles":"BlueTiles",v,t,colour==0?white:teal,false);
                }
                v=new List<Vector3>();t=new List<int>();
                const int tube=10;
                for(int i=0;i<Segments;i++)
                {
                    float a=i*2*Mathf.PI/Segments;
                    Vector3 n=Outward(a);
                    for(int j=0;j<tube;j++)
                    {
                        float b=j*2*Mathf.PI/tube;
                        v.Add(Ring(a,Run,.01f)+.065f*(n*Mathf.Cos(b)+Vector3.up*Mathf.Sin(b)));
                    }
                }
                for(int i=0;i<Segments;i++)for(int j=0;j<tube;j++)
                {
                    int a=i*tube+j,b=((i+1)%Segments)*tube+j,c=i*tube+(j+1)%tube,d=((i+1)%Segments)*tube+(j+1)%tube;
                    t.AddRange(new[]{a,c,b,b,c,d});
                }
                Part(root,"Coping",v,t,ink,false);
                var rail=root.transform.Find("Coping").gameObject.AddComponent<GrindRail>();
                rail.label="POOL COPING"; rail.points=new Vector3[Segments+1];
                for(int i=0;i<=Segments;i++)rail.points[i]=Ring(i*2*Mathf.PI/Segments,Run,.075f);
                rail.Rebuild();
                PrefabUtility.SaveAsPrefabAsset(root,Root+"/Prefabs/NeonPool_Bowl.prefab");
                AssetDatabase.SaveAssets();
                Export(root);
                Validate(root);
                TestAndRender(root);
                Debug.Log("[Bowl] Prefab, OBJ and preview created successfully.");
            }
            finally { UnityEngine.Object.DestroyImmediate(root); }
        }
        static Material Mat(string name,string hex)
        {
            string path=Root+"/Materials/"+name+".mat";
            var m=AssetDatabase.LoadAssetAtPath<Material>(path);
            if(m==null){m=new Material(Shader.Find("DriftSkate/Toon"));AssetDatabase.CreateAsset(m,path);}
            m.name=name;m.SetColor("_BaseColor",Palette.Hex(hex));m.SetFloat("_OutlineWidth",0);
            m.SetShaderPassEnabled("SRPDefaultUnlit",false); m.enableInstancing=true;
            EditorUtility.SetDirty(m);Materials.Add(m);return m;
        }
        static Mesh SaveMesh(Mesh mesh,string name)
        {
            mesh.name=name;
            string path=Root+"/Meshes/"+name+".asset";
            var existing=AssetDatabase.LoadAssetAtPath<Mesh>(path);
            if(existing!=null){EditorUtility.CopySerialized(mesh,existing);mesh=existing;}
            else AssetDatabase.CreateAsset(mesh,path);
            Meshes.Add(mesh);return mesh;
        }
        static void Part(GameObject root,string name,List<Vector3> v,List<int> t,Material mat,bool collision)
        {
            var mesh=new Mesh{name=name};mesh.SetVertices(v);mesh.SetTriangles(t,0);
            var uv=new Vector2[v.Count];for(int i=0;i<v.Count;i++)uv[i]=new Vector2(v[i].x,v[i].z)*.2f;
            mesh.uv=uv;mesh.RecalculateNormals();mesh.RecalculateBounds();mesh.RecalculateTangents();
            mesh=SaveMesh(mesh,"Pool_"+name);
            var go=new GameObject(name);go.transform.SetParent(root.transform,false);
            go.AddComponent<MeshFilter>().sharedMesh=mesh;go.AddComponent<MeshRenderer>().sharedMaterial=mat;
            if(collision)go.AddComponent<MeshCollider>().sharedMesh=mesh;
        }
        static void Strip(GameObject root,string name,float inner,float outer,float y0,float y1,Material mat,bool collision)
        {
            var v=new List<Vector3>();var t=new List<int>();
            for(int i=0;i<Segments;i++){float a=i*2*Mathf.PI/Segments;v.Add(Ring(a,inner,y0));v.Add(Ring(a,outer,y1));}
            for(int i=0;i<Segments;i++){int a=i*2,b=((i+1)%Segments)*2;t.AddRange(new[]{a,b,a+1,b,b+1,a+1});}
            Part(root,name,v,t,mat,collision);
        }
        static string F(float n)=>n.ToString("0.######",CultureInfo.InvariantCulture);
        static void Export(GameObject root)
        {
            var s=new StringBuilder("# DriftSkate NeonPool; metres, Y up\nmtllib NeonPool_Bowl.mtl\n");int offset=1;
            foreach(var f in root.GetComponentsInChildren<MeshFilter>())
            {
                s.AppendLine("o "+f.name);s.AppendLine("usemtl "+f.GetComponent<MeshRenderer>().sharedMaterial.name);
                var m=f.sharedMesh;
                foreach(var p in m.vertices){var q=f.transform.TransformPoint(p);s.AppendLine($"v {F(q.x)} {F(q.y)} {F(q.z)}");}
                foreach(var p in m.normals){var q=f.transform.TransformDirection(p);s.AppendLine($"vn {F(q.x)} {F(q.y)} {F(q.z)}");}
                var t=m.triangles;for(int i=0;i<t.Length;i+=3)s.AppendLine($"f {t[i]+offset}//{t[i]+offset} {t[i+1]+offset}//{t[i+1]+offset} {t[i+2]+offset}//{t[i+2]+offset}");
                offset+=m.vertexCount;
            }
            File.WriteAllText("Art/Environment/Bowl/NeonPool_Bowl.obj",s.ToString());
            s=new StringBuilder();foreach(var m in Materials){var c=m.GetColor("_BaseColor");s.AppendLine($"newmtl {m.name}\nKd {F(c.r)} {F(c.g)} {F(c.b)}\nKs 0 0 0\nNs 0\n");}
            File.WriteAllText("Art/Environment/Bowl/NeonPool_Bowl.mtl",s.ToString());
        }
        static void Validate(GameObject root)
        {
            int triangles=0;
            foreach(var f in root.GetComponentsInChildren<MeshFilter>())
            {
                var m=f.sharedMesh;var v=m.vertices;var t=m.triangles;triangles+=t.Length/3;
                for(int i=0;i<t.Length;i+=3)if(Vector3.Cross(v[t[i+1]]-v[t[i]],v[t[i+2]]-v[t[i]]).sqrMagnitude<1e-12f)
                    throw new InvalidOperationException("Degenerate triangle in "+f.name);
            }
            var floor=root.transform.Find("PoolSurface").GetComponent<MeshFilter>().sharedMesh;
            if(floor.normals[0].y<.99f)throw new InvalidOperationException("Bowl floor winding incorrect");
            float minimumNormal=1;
            foreach(var n in floor.normals)minimumNormal=Mathf.Min(minimumNormal,n.y);
            if(minimumNormal<=.45f)throw new InvalidOperationException("Transition exceeds controller slope limit");
            File.WriteAllText("Art/Environment/Bowl/validation.txt",$"Triangles: {triangles}\nFloor normal: {floor.normals[0]}\nMinimum riding normal Y: {minimumNormal}\nMesh colliders: {root.GetComponentsInChildren<MeshCollider>().Length}\nClosed grind path: {Segments+1} points\nNo degenerate triangles.\n");
        }
        static void TestAndRender(GameObject root)
        {
            var previous=SceneManager.GetActiveScene();
            var scene=EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Additive);
            SceneManager.SetActiveScene(scene);
            var copy=UnityEngine.Object.Instantiate(root);
            SceneManager.MoveGameObjectToScene(copy,scene);
            Strip(copy,"SurroundingGround",Run+2.2f,40,-.02f,-.02f,Mat("PoolTestGround","4A4E63"),true);
            var inactive=new List<GameObject>();
            foreach(var go in previous.GetRootGameObjects())if(go.activeSelf){inactive.Add(go);go.SetActive(false);}
            var oldMode=Physics.simulationMode;
            float oldDt=Time.fixedDeltaTime;
            try
            {
                // Remove the original during queries: only test the prefab clone.
                root.SetActive(false);
                Physics.simulationMode=SimulationMode.Script;Time.fixedDeltaTime=.01f;
                var report=new StringBuilder();
                var flags=BindingFlags.Instance|BindingFlags.NonPublic;
                foreach(float heading in new[]{0f,90f,180f,270f})
                {
                    var go=new GameObject("ControllerProbe");go.layer=LayerMask.NameToLayer("Skater");
                    var rb=go.AddComponent<Rigidbody>();var cap=go.AddComponent<CapsuleCollider>();
                    cap.center=new Vector3(0,.95f,0);cap.height=1.5f;cap.radius=.28f;
                    cap.sharedMaterial=AssetDatabase.LoadAssetAtPath<PhysicsMaterial>("Assets/_Game/Generated/SkaterPhysics.physicMaterial");rb.mass=75;
                    var sc=go.AddComponent<SkaterController>();
                    typeof(SkaterController).GetMethod("Awake",flags).Invoke(sc,null);
                    sc.inputEnabled=false;sc.Place(new Vector3(0,-Depth+.03f,0),heading);
                    rb.linearVelocity=Quaternion.Euler(0,heading,0)*Vector3.forward*6;
                    Physics.SyncTransforms();
                    float maxY=-Depth;int bailed=0,riding=0;float minimumY=0;
                    for(int step=0;step<700;step++)
                    {
                        sc.InjectInput(Vector2.zero,false);
                        typeof(SkaterController).GetMethod("FixedUpdate",flags).Invoke(sc,null);
                        Physics.Simulate(.01f);Physics.SyncTransforms();
                        maxY=Mathf.Max(maxY,rb.position.y);minimumY=Mathf.Min(minimumY,rb.position.y);
                        if(sc.State==SkaterState.Bailed)bailed++;
                        if(sc.State==SkaterState.Riding)riding++;
                    }
                    report.AppendLine($"Heading {heading}: max root height {maxY:0.00}, min {minimumY:0.00}, riding {riding}/700, bailed {bailed}");
                    File.WriteAllText("Art/Environment/Bowl/skating-test.txt",report.ToString());
                    UnityEngine.Object.DestroyImmediate(go);
                    if(minimumY < -Depth-.3f)throw new InvalidOperationException("Skater fell through bowl");
                    if(maxY < -Depth+.5f)throw new InvalidOperationException("Skater failed to ride transition");
                    if(bailed!=0 || riding!=700)throw new InvalidOperationException("Skater lost contact during transition test");
                }
                File.WriteAllText("Art/Environment/Bowl/skating-test.txt",report.ToString());
                Physics.simulationMode=oldMode;Time.fixedDeltaTime=oldDt;
                var sun=new GameObject("Sun").AddComponent<Light>();sun.type=LightType.Directional;sun.intensity=1.25f;
                sun.transform.rotation=Quaternion.Euler(48,-40,0);sun.shadows=LightShadows.Soft;
                RenderSettings.sun=sun;RenderSettings.ambientMode=UnityEngine.Rendering.AmbientMode.Flat;
                RenderSettings.ambientLight=new Color(.45f,.48f,.55f);
                var cam=new GameObject("BowlCamera").AddComponent<Camera>();cam.tag="MainCamera";
                cam.clearFlags=CameraClearFlags.SolidColor;cam.backgroundColor=Palette.Hex("23263A");
                cam.transform.position=new Vector3(22,22,-27);cam.transform.LookAt(new Vector3(0,-1,0));cam.fieldOfView=38;
                cam.nearClipPlane=.1f;cam.farClipPlane=120;
                // A real skater provides an honest visual scale reference in the test scene.
                var skater=new GameObject("TestSkater");skater.layer=LayerMask.NameToLayer("Skater");
                skater.AddComponent<Rigidbody>();var capsule=skater.AddComponent<CapsuleCollider>();
                capsule.center=new Vector3(0,.95f,0);capsule.height=1.5f;capsule.radius=.28f;
                capsule.sharedMaterial=AssetDatabase.LoadAssetAtPath<PhysicsMaterial>("Assets/_Game/Generated/SkaterPhysics.physicMaterial");
                var controller=skater.AddComponent<SkaterController>();
                SkaterBuilder.Build(skater.transform,SkaterBuilder.Outfit.FromIds(null),Catalog.Boards[0],null,controller);
                skater.transform.position=new Vector3(2.5f,-Depth+.03f,0);
                int assetIndex=0;
                foreach(var filter in skater.GetComponentsInChildren<MeshFilter>())
                    if(filter.sharedMesh!=null && string.IsNullOrEmpty(AssetDatabase.GetAssetPath(filter.sharedMesh)))
                        filter.sharedMesh=SaveMesh(filter.sharedMesh,"BowlTestSkater_"+(assetIndex++));
                foreach(var renderer in skater.GetComponentsInChildren<Renderer>())
                {
                    var mats=renderer.sharedMaterials;
                    for(int i=0;i<mats.Length;i++)
                        if(mats[i]!=null && string.IsNullOrEmpty(AssetDatabase.GetAssetPath(mats[i])))
                        {
                            string path=Root+"/Materials/BowlTestSkater_"+(assetIndex++)+".mat";
                            var existing=AssetDatabase.LoadAssetAtPath<Material>(path);
                            if(existing!=null){EditorUtility.CopySerialized(mats[i],existing);mats[i]=existing;}
                            else AssetDatabase.CreateAsset(mats[i],path);
                        }
                    renderer.sharedMaterials=mats;
                }
                AssetDatabase.SaveAssets();
                Physics.SyncTransforms();
                SimTests.Capture(cam,"Art/Environment/Bowl/NeonPool_Bowl.png");
                cam.transform.position=new Vector3(13,7,-17);cam.transform.LookAt(new Vector3(0,-1.2f,0));cam.fieldOfView=57;
                SimTests.Capture(cam,"Art/Environment/Bowl/NeonPool_Bowl_detail.png");
                cam.transform.position=new Vector3(0,7,-13);cam.transform.LookAt(skater.transform.position+Vector3.up);
                Directory.CreateDirectory(Root+"/Scenes");
                EditorSceneManager.SaveScene(scene,Root+"/Scenes/BowlTest.unity");
            }
            finally
            {
                foreach(var go in inactive)go.SetActive(true);
                root.SetActive(true);Physics.simulationMode=oldMode;Time.fixedDeltaTime=oldDt;
                SceneManager.SetActiveScene(previous);EditorSceneManager.CloseScene(scene,true);
            }
        }
        public static void VerifySavedScene()
        {
            EditorSceneManager.OpenScene(Root+"/Scenes/BowlTest.unity");
            var root=GameObject.Find("NeonPool_Bowl(Clone)");
            if(root==null)throw new InvalidOperationException("Saved bowl missing");
            foreach(var renderer in root.GetComponentsInChildren<MeshRenderer>())
                if(renderer.GetComponent<MeshFilter>().sharedMesh==null || renderer.sharedMaterial==null)
                    throw new InvalidOperationException("Saved mesh/material missing: "+renderer.name);
            if(root.GetComponentsInChildren<MeshCollider>().Length!=3)throw new InvalidOperationException("Saved collision missing");
            if(GameObject.Find("TestSkater").GetComponent<SkaterController>()==null)throw new InvalidOperationException("Saved skater missing");
            File.AppendAllText("Art/Environment/Bowl/validation.txt","Saved test scene reloaded; bowl meshes, materials, colliders and controller verified.\n");
            Debug.Log("[Bowl] Saved scene verified.");
        }
    }
}
