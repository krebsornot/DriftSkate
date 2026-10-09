using UnityEngine;

namespace DriftSkate
{
    /// <summary>Kleine Helfer, um Objekte aus Grundformen mit Toon-Material zu bauen.</summary>
    public static class Shapes
    {
        public static GameObject Part(PrimitiveType type, Transform parent, Vector3 localPos, Vector3 localScale,
                                      Color color, float outline = 0.35f, Vector3? euler = null, bool keepCollider = false,
                                      float emission = 0f, string name = null, Surface surface = Surface.None)
        {
            var go = GameObject.CreatePrimitive(type);
            if (name != null) go.name = name;
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPos;
            go.transform.localRotation = Quaternion.Euler(euler ?? Vector3.zero);
            go.transform.localScale = localScale;
            bool box = type == PrimitiveType.Cube;
            go.GetComponent<Renderer>().sharedMaterial = ToonMaterials.Get(color, outline, box, emission, surface);
            if (!keepCollider) RemoveCollider(go);
            return go;
        }

        public static GameObject Box(Transform parent, Vector3 pos, Vector3 size, Color color, float outline = 0.35f,
                                     Vector3? euler = null, bool collider = false, float emission = 0f, string name = null,
                                     Surface surface = Surface.None)
        {
            return Part(PrimitiveType.Cube, parent, pos, size, color, outline, euler, collider, emission, name, surface);
        }

        public static void RemoveCollider(GameObject go)
        {
            // Sofort entfernen: sonst haengt der Collider einen Frame lang am Rigidbody des Autos.
            var col = go.GetComponent<Collider>();
            if (col != null) Object.DestroyImmediate(col);
        }

        public static void DestroySafe(Object obj)
        {
            if (obj == null) return;
            if (Application.isPlaying) Object.Destroy(obj);
            else Object.DestroyImmediate(obj);
        }

        public static Transform Group(Transform parent, string name, Vector3 localPos = default)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPos;
            return go.transform;
        }

        /// <summary>Ein flaches Quad mit Textur (z. B. Graffiti), das in Richtung 'normal' zeigt.</summary>
        public static GameObject Decal(Transform parent, Vector3 pos, Vector2 size, Vector3 normalLocal, Material mat, string name = "Decal")
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Quad);
            go.name = name;
            RemoveCollider(go);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = pos;
            // Unity-Quads zeigen mit ihrer Vorderseite in -Z.
            go.transform.localRotation = Quaternion.LookRotation(-normalLocal, Vector3.up);
            go.transform.localScale = new Vector3(size.x, size.y, 1f);
            var r = go.GetComponent<Renderer>();
            r.sharedMaterial = mat;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            return go;
        }
    }
}
