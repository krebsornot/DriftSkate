using System;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace DriftSkate.EditorTools
{
    public static class SkateparkTestAssets
    {
        const string Root = "Assets/_Game/SkateparkTest";
        [MenuItem("DriftSkate/Skatepark-Testset/Fehlende Assets erstellen")]
        public static void Build()
        {
            Directory.CreateDirectory(Root + "/Materials");
            Directory.CreateDirectory(Root + "/Meshes");
            Directory.CreateDirectory(Root + "/Prefabs");
            AssetDatabase.Refresh();
            var concrete = Material("Concrete", new Color(.67f,.70f,.73f));
            var steel = Material("Steel", new Color(.22f,.27f,.32f));
            var accent = Material("Teal", new Color(.12f,.78f,.65f));
            var rampPath = Root + "/Prefabs/KickerRamp.prefab";
            if (!File.Exists(rampPath))
            {
                var root = new GameObject("KickerRamp");
                try
                {
                    var meshPath = Root + "/Meshes/KickerRamp.asset";
                    var mesh = AssetDatabase.LoadAssetAtPath<Mesh>(meshPath);
                    if (mesh == null)
                    {
                        mesh = RampMesh();
                        AssetDatabase.CreateAsset(mesh, meshPath);
                    }
                    var body = new GameObject("Ramp surface");
                    body.transform.SetParent(root.transform, false);
                    body.AddComponent<MeshFilter>().sharedMesh = mesh;
                    body.AddComponent<MeshRenderer>().sharedMaterial = concrete;
                    body.AddComponent<MeshCollider>().sharedMesh = mesh;
                    Box(root, "Rear accent", new Vector3(0,.57f,1.48f), new Vector3(2.9f,.12f,.04f), accent, false);
                    Box(root, "Launch edge", new Vector3(0,1.19f,1.45f), new Vector3(3,.02f,.10f), steel, false);
                    Save(root, rampPath);
                }
                finally { UnityEngine.Object.DestroyImmediate(root); }
            }
            var railPath = Root + "/Prefabs/GrindRail.prefab";
            if (!File.Exists(railPath))
            {
                var root = new GameObject("GrindRail");
                try
                {
                    Box(root, "Flat grind bar", new Vector3(0,.75f,0), new Vector3(.12f,.10f,4), steel);
                    foreach (float z in new[] {-1.45f,1.45f})
                    {
                        Box(root, "Support", new Vector3(0,.37f,z), new Vector3(.09f,.66f,.09f), accent);
                        Box(root, "Foot", new Vector3(0,.035f,z), new Vector3(.6f,.07f,.3f), steel);
                    }
                    Save(root, railPath);
                }
                finally { UnityEngine.Object.DestroyImmediate(root); }
            }
            var ledgePath = Root + "/Prefabs/SkateLedge.prefab";
            if (!File.Exists(ledgePath))
            {
                var root = new GameObject("SkateLedge");
                try
                {
                    Box(root, "Concrete block", new Vector3(0,.28f,0), new Vector3(.8f,.56f,3), concrete);
                    Box(root, "Steel cap", new Vector3(0,.58f,0), new Vector3(.82f,.04f,3.02f), steel);
                    foreach (float x in new[] {-.405f,.405f})
                        Box(root, "Side accent", new Vector3(x,.17f,0), new Vector3(.01f,.08f,2.8f), accent, false);
                    Save(root, ledgePath);
                }
                finally { UnityEngine.Object.DestroyImmediate(root); }
            }
            AssetDatabase.SaveAssets();
            Validate();
            Debug.Log("[SkateparkTest] Three prefabs generated and validated at " + Root);
        }
        static Material Material(string name, Color color)
        {
            string path = Root + "/Materials/" + name + ".mat";
            var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (mat != null) return mat;
            var shader = Shader.Find("DriftSkate/Toon");
            if (shader == null) throw new InvalidOperationException("DriftSkate/Toon shader missing");
            mat = new Material(shader) { name = name, enableInstancing = true };
            mat.SetColor("_BaseColor", color);
            mat.SetFloat("_OutlineWidth", 0);
            mat.SetShaderPassEnabled("SRPDefaultUnlit", false);
            AssetDatabase.CreateAsset(mat, path);
            return mat;
        }
        static void Box(GameObject parent, string name, Vector3 position, Vector3 size, Material mat, bool collision = true)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = name;
            go.transform.SetParent(parent.transform, false);
            go.transform.localPosition = position;
            go.transform.localScale = size;
            go.GetComponent<MeshRenderer>().sharedMaterial = mat;
            if (!collision) UnityEngine.Object.DestroyImmediate(go.GetComponent<Collider>());
        }
        static Mesh RampMesh()
        {
            // Closed wedge: entry at z=-1.5, launch at z=+1.5, metres.
            var p = new[] {new Vector3(-1.5f,0,-1.5f),new Vector3(1.5f,0,-1.5f),
                new Vector3(-1.5f,0,1.5f),new Vector3(1.5f,0,1.5f),
                new Vector3(-1.5f,1.2f,1.5f),new Vector3(1.5f,1.2f,1.5f)};
            int[] faces = {0,4,5, 0,5,1, 2,3,5, 2,5,4, 0,2,4, 1,5,3, 0,1,3, 0,3,2};
            var v = new Vector3[faces.Length];
            var uv = new Vector2[faces.Length];
            var triangles = new int[faces.Length];
            for (int i=0;i<faces.Length;i++) { v[i]=p[faces[i]]; uv[i]=new Vector2(v[i].x,v[i].z+v[i].y); triangles[i]=i; }
            var mesh = new Mesh { name = "KickerRamp", vertices = v, uv = uv, triangles = triangles };
            mesh.RecalculateNormals(); mesh.RecalculateBounds(); mesh.RecalculateTangents();
            return mesh;
        }
        static void Save(GameObject root, string path)
        {
            if (PrefabUtility.SaveAsPrefabAsset(root, path) == null) throw new InvalidOperationException("Failed to save " + path);
        }
        public static void Validate()
        {
            foreach (var name in new[] {"KickerRamp","GrindRail","SkateLedge"})
            {
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(Root + "/Prefabs/" + name + ".prefab");
                if (prefab == null || prefab.transform.localScale != Vector3.one || prefab.transform.localPosition != Vector3.zero)
                    throw new InvalidOperationException("Invalid prefab root: " + name);
                if (prefab.GetComponentsInChildren<Collider>().Length == 0) throw new InvalidOperationException("Missing collision: " + name);
                foreach (var renderer in prefab.GetComponentsInChildren<MeshRenderer>())
                {
                    if (renderer.sharedMaterial == null || renderer.sharedMaterial.shader.name != "DriftSkate/Toon")
                        throw new InvalidOperationException("Invalid material: " + name);
                    var filter = renderer.GetComponent<MeshFilter>();
                    if (filter == null || filter.sharedMesh == null) throw new InvalidOperationException("Missing mesh: " + name);
                }
            }
        }
    }
}
