using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace DriftSkate
{
    /// <summary>
    /// Miru sitzt im leeren Park (Platz Block 2/3, gegenueber dem Stoner-Sofa) auf einer Bank und raucht.
    /// Spricht man sie an, steht sie auf (MiruTalk). Die Bank wird zur Laufzeit aus der Stadt-Szene gesucht
    /// (die dem Brunnenplatz naechste mit freiem Platz davor), die Stadt bleibt unveraendert. Nur lokal, ohne Netzwerk.
    /// </summary>
    public static class ParkMiru
    {
        const string ParkBlock = "Block_23_P";
        /// <summary>Sitzflaeche der Bank (Oberkante der Latten, CityBuilder.Bench) und Sitzhoehe der Animation ueber dem Ursprung.</summary>
        const float BenchSeat = 0.5f, AnimSeat = 0.45f;
        /// <summary>Wo Mirus Ursprung beim Sitzen liegt (Bank-Koordinaten): etwas links der Mitte, Huefte (liegt ueber dem Ursprung) mittig auf der Sitzflaeche.</summary>
        static readonly Vector3 SeatLocal = new Vector3(-0.4f, 0f, -0.1f);
        /// <summary>Wo sie nach dem Aufstehen steht: vor der Bank.</summary>
        static readonly Vector3 StandLocal = new Vector3(-0.4f, 0f, 0.4f);

        static bool _searched, _found;
        static Vector3 _seat, _stand, _facing;

        public static bool Found { get { Search(); return _found; } }
        public static Vector3 SeatPosition { get { Search(); return _seat; } }
        public static Vector3 StandPosition { get { Search(); return _stand; } }
        public static Vector3 Facing { get { Search(); return _facing; } }

        /// <summary>Fuer das Admin-Menue: drei Meter vor Miru.</summary>
        public static Vector3 TeleportPoint => Found ? StandPosition + Facing * 3f + Vector3.up * 0.5f
                                                     : new Vector3(CityBuilder.BlockCenter(3), 0.5f, CityBuilder.BlockCenter(2));

        public static void Reset() => _searched = false;

        static void Search()
        {
            if (_searched) return;
            _searched = true;
            _found = false;
            var scene = SceneManager.GetActiveScene();
            if (!scene.IsValid()) return;
            var benches = new List<Transform>();
            foreach (var root in scene.GetRootGameObjects())
                foreach (var t in root.GetComponentsInChildren<Transform>(true))
                    if (t.name == ParkBlock)
                        foreach (Transform child in t)
                            if (child.name == "Bench") benches.Add(child);
            // Die Bank am naechsten zur Stadtmitte, dort kommt man am haeufigsten vorbei
            benches.Sort((a, b) => new Vector2(a.position.x, a.position.z).sqrMagnitude.CompareTo(new Vector2(b.position.x, b.position.z).sqrMagnitude));
            foreach (var bench in benches)
            {
                Vector3 stand = StonerNpc.Ground(bench.TransformPoint(StandLocal));
                if (!Free(stand + bench.forward * 0.3f)) continue;
                _stand = stand;
                _seat = bench.TransformPoint(SeatLocal);
                _seat.y = bench.position.y + BenchSeat - AnimSeat;
                _facing = bench.forward;
                _found = true;
                return;
            }
        }

        /// <summary>Steht da schon was (Muelleimer, Baum, Rail)? Boden zaehlt nicht.</summary>
        static bool Free(Vector3 pos) =>
            !Physics.CheckBox(pos + Vector3.up * 0.95f, new Vector3(0.35f, 0.7f, 0.35f), Quaternion.identity, ~0, QueryTriggerInteraction.Ignore);

        public static void Spawn()
        {
            var prefab = NpcPrefabs.Instance != null ? NpcPrefabs.Instance.miru : null;
            if (prefab == null)
            {
                Debug.LogWarning("ParkMiru: Prefab fehlt (DriftSkate → Miru → Prefab und Materialien aktualisieren)");
                return;
            }
            if (!Found)
            {
                Debug.LogWarning("ParkMiru: keine freie Bank im Park gefunden");
                return;
            }
            var group = new GameObject("ParkMiru").transform;
            var npc = StonerNpc.SpawnPrefab("Miru", prefab, SeatPosition, Facing, group);
            if (npc == null) return;
            npc.SetSitting(true, true);
            npc.gameObject.AddComponent<MiruTalk>().Setup(SeatPosition, StandPosition);
        }
    }
}
