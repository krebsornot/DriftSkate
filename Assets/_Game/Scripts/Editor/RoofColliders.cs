using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace DriftSkate.EditorTools
{
    /// <summary>
    /// Die Dachplatten der Haeuser hatten keinen Collider: man fuhr auf dem Hauskoerper darunter und steckte 0,5 m im Dach.
    /// Setzt in der fertigen City-Szene Collider auf Dach, Klimageraete, Wassertanks und Werbetafeln (wie CityBuilder es
    /// jetzt auch beim Neubau macht), ohne die Stadt neu zu bauen. Menue: DriftSkate → Stadt → Dach-Hitboxen setzen.
    /// </summary>
    public static class RoofColliders
    {
        [MenuItem("DriftSkate/Stadt/Dach-Hitboxen setzen")]
        public static void Run()
        {
            var scene = EditorSceneManager.OpenScene(ProjectBuilder.CityPath);
            int boxes = 0, tanks = 0;
            foreach (var t in scene.GetRootGameObjects().SelectMany(g => g.GetComponentsInChildren<Transform>(true)))
            {
                string n = t.name;
                if (n == "Roof" || n == "AC" || n == "Billboard" || n == "BillboardPole")
                {
                    if (t.GetComponent<Collider>() != null) continue;
                    t.gameObject.AddComponent<BoxCollider>(); // Einheitswuerfel, skaliert = genau die sichtbare Platte
                    boxes++;
                }
                else if (n == "WaterTank")
                {
                    if (t.GetComponent<Collider>() != null) continue;
                    var mc = t.gameObject.AddComponent<MeshCollider>();
                    mc.sharedMesh = t.GetComponent<MeshFilter>().sharedMesh;
                    mc.convex = true;
                    tanks++;
                }
            }
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            Debug.Log($"[Roof] {boxes} Box-Collider (Dach, Klima, Werbetafel) und {tanks} Wassertanks gesetzt");
        }
    }
}
