using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace DriftSkate.EditorTools
{
    /// <summary>
    /// Setzt die schwebenden Engelsfluegel (Easter Egg) in die Stadt und legt den Resources-Verweis fuer die Fluegel am
    /// Ruecken an. Das Objekt "AngelWingsPickup" kann danach im Editor frei verschoben werden (z. B. auf ein Dach);
    /// ein erneuter Aufruf laesst die Position in Ruhe.
    /// </summary>
    public static class AngelWingsPickupSetup
    {
        const string WingsPrefab = "Assets/_Game/Accessories/AngelWings/AngelWings.prefab";
        const string LinkPath = "Assets/_Game/Resources/Items/ItemPrefabs.asset";

        [MenuItem("DriftSkate/Angel Wings/Pickup in die Stadt setzen")]
        public static void Run()
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(WingsPrefab);
            if (prefab == null) throw new System.Exception("AngelWings.prefab fehlt: " + WingsPrefab);
            EnsureLink(prefab);

            var scene = EditorSceneManager.OpenScene(ProjectBuilder.CityPath);
            var pickup = Object.FindAnyObjectByType<AngelWingsPickup>(FindObjectsInactive.Include);
            if (pickup == null)
            {
                // Vorerst gut sichtbar am Brunnenplatz; spaeter im Editor auf ein Dach ziehen
                Vector3 pos = new Vector3(6f, 0f, -22f);
                if (Physics.Raycast(pos + Vector3.up * 30f, Vector3.down, out RaycastHit hit, 60f, ~LayerMask.GetMask("Rail", "Skater", "Car"), QueryTriggerInteraction.Ignore))
                    pos = hit.point;
                var go = new GameObject("AngelWingsPickup");
                go.transform.position = pos + Vector3.up * 1.4f;
                pickup = go.AddComponent<AngelWingsPickup>();
            }
            pickup.gameObject.isStatic = false;
            var visual = pickup.transform.Find("Visual");
            if (visual == null)
            {
                var inst = (GameObject)PrefabUtility.InstantiatePrefab(prefab, pickup.transform);
                inst.name = "Visual";
                inst.transform.localPosition = Vector3.zero;
                inst.transform.localRotation = Quaternion.identity;
                inst.transform.localScale = Vector3.one * WingsOnBack.Fit(inst, pickup.floatSpan);
            }
            foreach (var t in pickup.GetComponentsInChildren<Transform>(true)) t.gameObject.isStatic = false;
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            Debug.Log($"[AngelWings] Pickup bei {pickup.transform.position} (im Editor verschiebbar), Verweis: {LinkPath}");
        }

        static void EnsureLink(GameObject prefab)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(LinkPath));
            var link = AssetDatabase.LoadAssetAtPath<ItemPrefabs>(LinkPath);
            if (link == null)
            {
                link = ScriptableObject.CreateInstance<ItemPrefabs>();
                AssetDatabase.CreateAsset(link, LinkPath);
            }
            if (link.angelWings == prefab) return;
            link.angelWings = prefab;
            EditorUtility.SetDirty(link);
            AssetDatabase.SaveAssets();
        }
    }
}
