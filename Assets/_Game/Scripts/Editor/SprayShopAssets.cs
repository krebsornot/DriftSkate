using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace DriftSkate.EditorTools
{
    public static partial class SprayShopAssets
    {
        const string CityPath = "Assets/_Game/Scenes/City.unity";
        const string Root = "Assets/_Game/Shop";
        static Color Ink => Palette.Hex("292B49");
        static Color Cream => Palette.Hex("F6E8CB");
        static Color Pink => Palette.Hex("FF5389");
        static Color Teal => Palette.Hex("65CEC5");
        static Transform shop;

        [MenuItem("DriftSkate/Spray Shop/In Stadt einbauen")]
        public static void Build()
        {
            if (!Application.isBatchMode && !EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            Directory.CreateDirectory(Root + "/Materials"); Directory.CreateDirectory(Root + "/Meshes");
            Directory.CreateDirectory("Art/Shop"); Directory.CreateDirectory("Logs");
            AssetDatabase.Refresh();
            var scene = EditorSceneManager.OpenScene(CityPath);
            var existing = UnityEngine.Object.FindFirstObjectByType<SprayShop>();
            if (existing != null) { shop = existing.transform; Polish(); Validate(); EditorSceneManager.MarkSceneDirty(scene); EditorSceneManager.SaveScene(scene,CityPath); AssetDatabase.SaveAssets(); Render(false); Render(true); return; }
            var building = UnityEngine.Object.FindObjectsByType<MeshRenderer>(FindObjectsSortMode.None)
                .Where(r => r.name == "Building" && r.bounds.size.x >= 14 && r.bounds.size.z >= 14)
                .OrderBy(r => new Vector2(r.bounds.center.x,r.bounds.center.z).sqrMagnitude).First();
            Bounds bounds = building.bounds;
            Vector3 normal = Mathf.Abs(bounds.center.x) > Mathf.Abs(bounds.center.z)
                ? new Vector3(-Mathf.Sign(bounds.center.x),0,0) : new Vector3(0,0,-Mathf.Sign(bounds.center.z));
            Vector3 face = new Vector3(bounds.center.x,bounds.min.y,bounds.center.z) + Vector3.Scale(normal,bounds.extents);
            shop = new GameObject("CAN CLUB - Spray Shop").transform;
            shop.SetPositionAndRotation(face,Quaternion.LookRotation(normal));
            Render(false,"Art/Shop/Before.png");
            File.Copy(CityPath,"Logs/City_before_spray_shop_" + DateTime.Now.ToString("yyyyMMdd_HHmmss") + ".unity",false);
            float width = Mathf.Abs(normal.x) > .5f ? bounds.size.z : bounds.size.x;
            float depth = Mathf.Abs(normal.x) > .5f ? bounds.size.x : bounds.size.z;
            float h = bounds.size.y;
            // Keep the original for reversible editing. Only this solid is replaced by a hollow shell.
            building.gameObject.SetActive(false);
            building.name = "SprayShop_OriginalBuilding";
            var facade = building.sharedMaterial;
            Shell("Upper floors",new Vector3(0,(h+3.65f)/2,-depth/2),new Vector3(width,h-3.65f,depth),facade);
            Shell("Left building mass",new Vector3(-(width+10)/4,1.825f,-depth/2),new Vector3((width-10)/2,3.65f,depth),facade);
            Shell("Right building mass",new Vector3((width+10)/4,1.825f,-depth/2),new Vector3((width-10)/2,3.65f,depth),facade);
            Shell("Rear building mass",new Vector3(0,1.825f,-(depth+9)/2),new Vector3(10,3.65f,depth-9),facade);
            ClearOldFront();
            Box("Floor",new Vector3(0,.025f,-4.5f),new Vector3(10,.05f,9),Cream,true);
            Box("Back wall",new Vector3(0,1.8f,-8.85f),new Vector3(10,3.6f,.2f),Teal,true);
            for(int s=-1;s<=1;s+=2) Box("Side wall",new Vector3(s*4.9f,1.8f,-4.5f),new Vector3(.2f,3.6f,9),Cream,true);
            for(int z=0;z<9;z++) for(int x=0;x<10;x++) if((x+z)%2==0)
                Box("Floor tile",new Vector3(x-4.5f,.057f,-z-.5f),new Vector3(.97f,.008f,.97f),Palette.Hex("D6C9DB"));
            Box("Shop sign",new Vector3(0,3.05f,.14f),new Vector3(10,.95f,.3f),Ink);
            Label("CAN CLUB",new Vector3(0,3.11f,.31f),.30f,Cream);
            Label("SPRAY SUPPLY  /  OPEN",new Vector3(0,2.77f,.32f),.085f,Teal);
            Box("Pink canopy",new Vector3(0,3.58f,.65f),new Vector3(10.5f,.2f,1.6f),Pink);
            for(int i=-5;i<=5;i++) Box("Canopy stripe",new Vector3(i*.88f,3.695f,.65f),new Vector3(.35f,.025f,1.55f),Cream);
            for(int s=-1;s<=1;s+=2)
            {
                Box("Window sill",new Vector3(s*3.1f,.45f,0),new Vector3(3.6f,.9f,.22f),Teal,true);
                Box("Window top",new Vector3(s*3.1f,2.55f,0),new Vector3(3.6f,.12f,.22f),Ink);
                foreach(float x in new[]{s*1.3f,s*4.9f}) Box("Window frame",new Vector3(x,1.7f,0),new Vector3(.12f,1.8f,.22f),Ink,true);
                var pane=new GameObject("Clear display window"); pane.transform.SetParent(shop,false); pane.transform.localPosition=new Vector3(s*3.1f,1.7f,0);
                pane.AddComponent<BoxCollider>().size=new Vector3(3.5f,1.6f,.1f);
                for(int i=0;i<5;i++) Can(new Vector3(s*3.1f+(i-2)*.48f,.91f,-.2f),i,.95f);
            }
            var door=Shapes.Group(shop,"Automatic entrance door",new Vector3(-1.15f,0,.03f));
            var doorPanel=Box("Door lower panel",new Vector3(0,0,0),new Vector3(2.3f,.85f,.10f),Teal,true);
            doorPanel.transform.SetParent(door,false); doorPanel.transform.localPosition=new Vector3(1.15f,.43f,0);
            foreach(var item in new[]{new Vector3(0,1.3f,0),new Vector3(2.3f,1.3f,0)})
            { var p=Box("Door stile",Vector3.zero,new Vector3(.09f,2.6f,.10f),Ink); p.transform.SetParent(door,false); p.transform.localPosition=item; }
            var top=Box("Door lintel",Vector3.zero,new Vector3(2.3f,.10f,.10f),Ink); top.transform.SetParent(door,false); top.transform.localPosition=new Vector3(1.15f,2.55f,0);
            var handle=Box("Door handle",Vector3.zero,new Vector3(.07f,.42f,.10f),Cream); handle.transform.SetParent(door,false); handle.transform.localPosition=new Vector3(2.12f,1.25f,.12f);
            for(int s=-1;s<=1;s+=2)
            {
                Box("Shelf back",new Vector3(s*3.15f,1.55f,-8.55f),new Vector3(2.9f,2.8f,.16f),Ink,true);
                for(int row=0;row<4;row++)
                {
                    float y=.4f+row*.62f;
                    Box("Shelf",new Vector3(s*3.15f,y,-8.23f),new Vector3(2.95f,.09f,.75f),Cream,true);
                    for(int i=0;i<7;i++) Can(new Vector3(s*3.15f+(i-3)*.37f,y+.055f,-8.16f),i+row,1);
                }
            }
            Label("COLOUR YOUR CITY",new Vector3(0,3.13f,-8.68f),.2f,Ink);
            Box("Counter base",new Vector3(2.3f,.56f,-5.4f),new Vector3(3.6f,1.12f,1.1f),Pink,true);
            Box("Countertop",new Vector3(2.3f,1.17f,-5.4f),new Vector3(3.8f,.12f,1.25f),Ink,true);
            Label("PAY HERE",new Vector3(2.3f,.67f,-4.835f),.17f,Cream);
            Box("Cash drawer",new Vector3(2.6f,1.29f,-5.4f),new Vector3(.68f,.16f,.55f),Cream);
            Box("Register screen",new Vector3(2.6f,1.6f,-5.48f),new Vector3(.55f,.42f,.12f),Ink);
            Box("Register display",new Vector3(2.6f,1.6f,-5.405f),new Vector3(.43f,.29f,.02f),Teal);
            Label("100",new Vector3(2.6f,1.61f,-5.38f),.09f,Ink);
            for(int i=0;i<3;i++) Can(new Vector3(1.1f+i*.3f,1.24f,-5.35f),i,1);
            Box("Display island",new Vector3(-2.5f,.45f,-4),new Vector3(1.6f,.9f,1.7f),Teal,true);
            for(int x=0;x<3;x++) for(int z=0;z<3;z++) Can(new Vector3(-3+x*.45f,.91f,-4.5f+z*.45f),x+z,1.1f);
            Label("100 / CAN",new Vector3(-2.5f,.48f,-3.13f),.11f,Ink);
            var till=Shapes.Group(shop,"Purchase point",new Vector3(2.3f,1.1f,-4.35f));
            var component=shop.gameObject.AddComponent<SprayShop>(); component.door=door; component.register=till;
            foreach(var pos in new[]{new Vector3(-2,3.3f,-3),new Vector3(2,3.3f,-7)})
            { Box("Ceiling light",pos,new Vector3(2,.08f,.5f),Cream); var l=Shapes.Group(shop,"Interior light",pos-Vector3.up*.15f).gameObject.AddComponent<Light>(); l.type=LightType.Point;l.range=9;l.intensity=2;l.color=new Color(1,.83f,.67f); }
            PersistMaterials();
            Polish();
            Physics.SyncTransforms(); Validate();
            EditorSceneManager.MarkSceneDirty(scene); EditorSceneManager.SaveScene(scene,CityPath); AssetDatabase.SaveAssets();
            Render(false); Render(true);
            File.WriteAllText("Art/Shop/Location.txt","CAN CLUB entrance (Unity world): " + shop.position + "\nWalk in with B, approach the till, E to shop. 1 / 6 / 12 cans: 100 / 500 / 900.\n");
        }

        static GameObject Box(string name,Vector3 p,Vector3 size,Color color,bool collision=false)
            => Shapes.Box(shop,p,size,color,.2f,collider:collision,name:name);
        static void Shell(string name,Vector3 p,Vector3 size,Material material)
        { var go=Box(name,p,size,Cream,true); go.GetComponent<Renderer>().sharedMaterial=material; }
        static Transform Label(string text,Vector3 p,float size,Color color)
        {
            var t=Shapes.Group(shop,text,p);t.localRotation=Quaternion.Euler(0,180,0);
            var label=t.gameObject.AddComponent<TextMesh>();label.text=text;label.fontSize=64;label.characterSize=size;label.anchor=TextAnchor.MiddleCenter;label.alignment=TextAlignment.Center;label.color=color;
            return t;
        }
        static void Can(Vector3 p,int index,float scale)
        {
            Color[] colors={Pink,Teal,Palette.Yellow,Palette.Purple,Palette.Blue,Palette.Orange,Palette.Lime};
            var parent=Shapes.Group(shop,"Spray can",p);parent.localScale=Vector3.one*scale;
            Shapes.Part(PrimitiveType.Cylinder,parent,new Vector3(0,.17f,0),new Vector3(.18f,.17f,.18f),colors[index%colors.Length],.1f,name:"Can body");
            Shapes.Part(PrimitiveType.Cylinder,parent,new Vector3(0,.335f,0),new Vector3(.17f,.025f,.17f),Cream,.1f,name:"Shoulder");
            Shapes.Part(PrimitiveType.Cylinder,parent,new Vector3(0,.385f,0),new Vector3(.075f,.026f,.075f),Ink,.1f,name:"Spray nozzle");
            Shapes.Box(parent,new Vector3(0,.16f,.092f),new Vector3(.11f,.10f,.008f),Cream,0,name:"Label");
        }
        static void ClearOldFront()
        {
            foreach(var filter in UnityEngine.Object.FindObjectsByType<MeshFilter>(FindObjectsSortMode.None))
            {
                if(filter.name!="ShopWindows" && filter.name!="Doors" && filter.name!="Trim") continue;
                var mesh=filter.sharedMesh;if(mesh==null)continue;
                var vertices=mesh.vertices;var triangles=mesh.triangles;var kept=new List<int>();
                for(int i=0;i<triangles.Length;i+=3)
                {
                    var a=shop.InverseTransformPoint(filter.transform.TransformPoint(vertices[triangles[i]]));
                    var b=shop.InverseTransformPoint(filter.transform.TransformPoint(vertices[triangles[i+1]]));
                    var c=shop.InverseTransformPoint(filter.transform.TransformPoint(vertices[triangles[i+2]]));
                    var bounds=new Bounds(a,Vector3.zero);bounds.Encapsulate(b);bounds.Encapsulate(c);
                    if(bounds.Intersects(new Bounds(new Vector3(0,1.6f,0),new Vector3(10.1f,3.3f,.6f))))continue;
                    kept.Add(triangles[i]);kept.Add(triangles[i+1]);kept.Add(triangles[i+2]);
                }
                if(kept.Count==triangles.Length)continue;
                var copy=UnityEngine.Object.Instantiate(mesh);copy.triangles=kept.ToArray();copy.RecalculateBounds();
                string path=Root+"/Meshes/"+filter.name+".asset";AssetDatabase.CreateAsset(copy,path);filter.sharedMesh=copy;
            }
            foreach(var renderer in UnityEngine.Object.FindObjectsByType<MeshRenderer>(FindObjectsSortMode.None))
            { if(renderer.name!="Awning")continue;var p=shop.InverseTransformPoint(renderer.bounds.center);if(Mathf.Abs(p.z)<2 && Mathf.Abs(p.x)<15 && p.y<4)renderer.gameObject.SetActive(false); }
        }
        static void PersistMaterials()
        {
            var saved=new Dictionary<Material,Material>();
            foreach(var renderer in shop.GetComponentsInChildren<MeshRenderer>())
            {
                var mat=renderer.sharedMaterial;if(mat==null || AssetDatabase.Contains(mat) || renderer.GetComponent<TextMesh>()!=null)continue;
                if(!saved.TryGetValue(mat,out var output))
                {
                    string safeName=System.Text.RegularExpressions.Regex.Replace(mat.name,"[^a-zA-Z0-9_-]","_");
                    string path=Root+"/Materials/Decor_"+safeName+".mat";
                    output=AssetDatabase.LoadAssetAtPath<Material>(path);
                    if(output==null) { output=UnityEngine.Object.Instantiate(mat);AssetDatabase.CreateAsset(output,path); }
                    saved.Add(mat,output);
                }
                renderer.sharedMaterial=output;
            }
        }
        static void Polish()
        {
            DressInterior();
            var shader=Shader.Find("DriftSkate/ShopText");
            foreach(var label in shop.GetComponentsInChildren<TextMesh>())
            {
                if(label.GetComponent<ShopText>()!=null)continue;
                label.characterSize *= .32f;
                string path=Root+"/Materials/ShopText.mat";
                var mat=AssetDatabase.LoadAssetAtPath<Material>(path);
                if(mat==null) { mat=new Material(shader);mat.mainTexture=label.GetComponent<MeshRenderer>().sharedMaterial.mainTexture;AssetDatabase.CreateAsset(mat,path); }
                label.GetComponent<MeshRenderer>().sharedMaterial=mat;
                label.gameObject.AddComponent<ShopText>();
            }
            foreach(var body in UnityEngine.Object.FindObjectsByType<Rigidbody>(FindObjectsSortMode.None))
            {
                if(body.name!="RecyclingBin")continue;
                var p=shop.InverseTransformPoint(body.position);
                if(Mathf.Abs(p.x)<2.5f && p.z>0 && p.z<3f) body.transform.position += shop.right*6.5f;
            }
            // Finish the ceiling instead of exposing the underside of the brick building.
            if(shop.Find("Interior ceiling")==null)
            {
                var ceiling=Box("Interior ceiling",new Vector3(0,3.58f,-4.5f),new Vector3(9.7f,.1f,8.7f),Cream);
                ceiling.GetComponent<Renderer>().sharedMaterial=shop.Find("Floor").GetComponent<Renderer>().sharedMaterial;
            }
            PersistMaterials();
        }
        static void Validate()
        {
            var p=new PlayerProfile{money=1000,sprayCans=0};
            if(!SprayShop.Purchase(p,1) || p.money!=500 || p.sprayCans!=6)throw new Exception("Purchase failed");
            if(SprayShop.Purchase(p,2) || p.money!=500 || p.sprayCans!=6)throw new Exception("Insufficient funds mutated profile");
            p.sprayCans=99;if(SprayShop.Purchase(p,0))throw new Exception("Capacity check failed");
            if(SprayShop.Purchase(p,-1))throw new Exception("Invalid product accepted");
            var reloaded=JsonUtility.FromJson<PlayerProfile>(JsonUtility.ToJson(p));if(reloaded.sprayCans!=99)throw new Exception("Inventory serialization failed");
            // Opening and central aisle must not intersect the old solid building.
            Physics.SyncTransforms();
            foreach(float z in new[]{-.8f,-2f,-3.5f})
                if(Physics.CheckCapsule(shop.TransformPoint(new Vector3(0,.5f,z)),shop.TransformPoint(new Vector3(0,1.6f,z)),.28f,~0,QueryTriggerInteraction.Ignore))throw new Exception("Shop aisle obstructed: "+z);
            Debug.Log("SPRAY SHOP PASS: purchases, insufficient funds, capacity, serialization, clear entrance aisle. Position "+shop.position);
        }
        static void Render(bool interior,string path=null)
        {
            var cam=Camera.main;
            cam.transform.position=shop.TransformPoint(interior?new Vector3(-.6f,2.2f,-.7f):new Vector3(12,7,18));
            cam.transform.LookAt(shop.TransformPoint(interior?new Vector3(.3f,1.35f,-6):new Vector3(0,2.3f,-1)));
            cam.fieldOfView=interior?77:55;
            var rt=new RenderTexture(1400,900,24);var old=RenderTexture.active;var tex=new Texture2D(1400,900,TextureFormat.RGB24,false);
            try{cam.targetTexture=rt;cam.Render();cam.targetTexture=null;RenderTexture.active=rt;tex.ReadPixels(new Rect(0,0,1400,900),0,0);tex.Apply();File.WriteAllBytes(path??("Art/Shop/"+(interior?"Interior":"Exterior")+".png"),tex.EncodeToPNG());}
            finally{RenderTexture.active=old;UnityEngine.Object.DestroyImmediate(rt);UnityEngine.Object.DestroyImmediate(tex);}
        }
    }
}
