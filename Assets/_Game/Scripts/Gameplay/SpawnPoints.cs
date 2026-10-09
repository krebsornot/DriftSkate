using UnityEngine;

namespace DriftSkate
{
    /// <summary>Startplaetze fuer Autos. Jedes Kind-Objekt ist ein Startplatz.</summary>
    public class SpawnPoints : MonoBehaviour
    {
        static SpawnPoints _instance;

        void Awake() => _instance = this;

        public static Transform Get(int index)
        {
            if (_instance == null) _instance = FindAnyObjectByType<SpawnPoints>();
            if (_instance == null || _instance.transform.childCount == 0)
            {
                var fallback = new GameObject("FallbackSpawn").transform;
                fallback.position = new Vector3(0, 1, 0);
                return fallback;
            }
            return _instance.transform.GetChild(Mathf.Abs(index) % _instance.transform.childCount);
        }
    }
}
