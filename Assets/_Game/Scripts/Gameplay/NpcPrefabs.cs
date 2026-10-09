using UnityEngine;

namespace DriftSkate
{
    /// <summary>
    /// Verweise auf fertige NPC-Prefabs, die nicht in Resources liegen (z. B. Miru mit ihren eigenen Toon-Materialien).
    /// Liegt als Resources/Characters/NpcPrefabs.asset; wird von DriftSkate → Miru → Prefab aktualisieren angelegt.
    /// </summary>
    public class NpcPrefabs : ScriptableObject
    {
        public GameObject miru;

        static NpcPrefabs _instance;
        public static NpcPrefabs Instance => _instance != null ? _instance : (_instance = Resources.Load<NpcPrefabs>("Characters/NpcPrefabs"));
    }
}
