using UnityEngine;

namespace DriftSkate
{
    /// <summary>
    /// Verweise auf Item-Prefabs, die nicht in Resources liegen (z. B. die Engelsfluegel am Ruecken).
    /// Liegt als Resources/Items/ItemPrefabs.asset; wird von DriftSkate → Angel Wings → Pickup in die Stadt setzen angelegt.
    /// </summary>
    public class ItemPrefabs : ScriptableObject
    {
        public GameObject angelWings;

        static ItemPrefabs _instance;
        public static ItemPrefabs Instance => _instance != null ? _instance : (_instance = Resources.Load<ItemPrefabs>("Items/ItemPrefabs"));
    }
}
