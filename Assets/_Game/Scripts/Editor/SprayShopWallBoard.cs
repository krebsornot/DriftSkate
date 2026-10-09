using System.IO;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;

namespace DriftSkate.EditorTools
{
    public static partial class SprayShopAssets
    {
        static void UpgradeWallBoard(Transform atmosphere)
        {
            var previous = atmosphere.Find("Wall skateboard");
            if (previous != null && previous.Find("Street Pro model") != null) return;
            string prefabPath = Root + "/StreetProDisplay.prefab";
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
            if (prefab == null)
            {
                var source = new GameObject("Street Pro Display");
                try
                {
                    string texturePath = Root + "/StreetProGraphic.asset";
                    var texture = AssetDatabase.LoadAssetAtPath<Texture2D>(texturePath);
                    if (texture == null)
                    {
                        texture = new Texture2D(256,256,TextureFormat.RGBA32,false) {name="Street Pro Graphic"};
                        GraffitiPainter.DrawDefaultTag(texture);
                        AssetDatabase.CreateAsset(texture,texturePath);
                    }
                    BoardBuilder.Build(source.transform,Catalog.Board("streetpro"),texture);
                    foreach (var filter in source.GetComponentsInChildren<MeshFilter>())
                    {
                        var mesh = filter.sharedMesh;
                        if (mesh == null || AssetDatabase.Contains(mesh)) continue;
                        string path = Root + "/Meshes/WallBoard_" + Safe(mesh.name) + ".asset";
                        var saved = AssetDatabase.LoadAssetAtPath<Mesh>(path);
                        if (saved == null) { saved=UnityEngine.Object.Instantiate(mesh);AssetDatabase.CreateAsset(saved,path); }
                        filter.sharedMesh=saved;
                    }
                    foreach(var renderer in source.GetComponentsInChildren<MeshRenderer>())
                    {
                        var materials=renderer.sharedMaterials;
                        for(int i=0;i<materials.Length;i++)
                        {
                            var material=materials[i];
                            if(material==null || AssetDatabase.Contains(material))continue;
                            string path=Root+"/Materials/WallBoard_"+Safe(material.name)+".mat";
                            var saved=AssetDatabase.LoadAssetAtPath<Material>(path);
                            if(saved==null){saved=UnityEngine.Object.Instantiate(material);AssetDatabase.CreateAsset(saved,path);}
                            materials[i]=saved;
                        }
                        renderer.sharedMaterials=materials;
                    }
                    prefab=PrefabUtility.SaveAsPrefabAsset(source,prefabPath);
                }
                finally { UnityEngine.Object.DestroyImmediate(source); }
            }
            if(previous!=null) UnityEngine.Object.DestroyImmediate(previous.gameObject);
            var mount=Shapes.Group(atmosphere,"Wall skateboard",new Vector3(4.60f,1.86f,-4.5f));
            var instance=(GameObject)PrefabUtility.InstantiatePrefab(prefab,mount);
            instance.name="Street Pro model";
            instance.transform.localPosition=Vector3.zero;
            instance.transform.localRotation=Quaternion.AngleAxis(-8,Vector3.right)*Quaternion.LookRotation(Vector3.up,Vector3.right);
            instance.transform.localScale=Vector3.one*1.3f;
            foreach(float y in new[]{-.28f,.28f})
                Shapes.Box(mount,new Vector3(.17f,y,0),new Vector3(.15f,.04f,.21f),Ink,.1f,name:"Wall bracket");
            Debug.Log("CAN CLUB wall board replaced with detailed Street Pro: curved deck, graphic, trucks, wheels and bearings.");
        }

        static string Safe(string name) => Regex.Replace(name,"[^a-zA-Z0-9_-]","_");
    }
}
