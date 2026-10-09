using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace DriftSkate
{
    /// <summary>
    /// Die Gassen-Crew: Nix (Ticker, lehnt an der Hauswand) und Moe (sitzt auf einem Bierkasten und vergibt Jobs).
    /// Sie stehen in der Seitengasse (Luecke zwischen zwei Haeusern eines Haeuserblocks), deren Einfahrt dem Brunnenplatz
    /// am naechsten liegt. Die Gasse wird zur Laufzeit aus den Hauswaenden der Stadt-Szene gesucht, die Stadt bleibt unveraendert.
    /// Nur lokal, ohne Netzwerk (wie die Stoner).
    /// </summary>
    public static class AlleyCrew
    {
        /// <summary>Abstand Nix-Ursprung bis Wand (WALL_Y in Tools/Blender/build_stoners.py).</summary>
        public const float WallGap = 0.15f;
        const float MinGap = 4f, MaxGap = 9f, MinLength = 12f;

        public struct Alley
        {
            public Vector3 entrance;  // Mitte der Einfahrt am Bordstein
            public Vector3 inward;    // in die Gasse hinein
            public Vector3 across;    // quer, zur Wand, an der Nix lehnt
            public float halfWidth, length;
        }

        static bool _searched;
        static Alley _alley;
        static bool _found;

        public static bool Found { get { Search(); return _found; } }
        public static Alley Current { get { Search(); return _alley; } }

        public static Vector3 NixPosition => NixAt(Current);
        public static Vector3 NixFacing => -Current.across;
        public static Vector3 MoePosition => MoeAt(Current);
        /// <summary>Moe schaut zur Einfahrt, leicht zu Nix gedreht.</summary>
        public static Vector3 MoeFacing => (-Current.inward + Current.across * 0.4f).normalized;

        static Vector3 NixAt(Alley a) => a.entrance + a.inward * 5.5f + a.across * (a.halfWidth - WallGap);
        static Vector3 MoeAt(Alley a) => a.entrance + a.inward * 10.5f - a.across * (a.halfWidth * 0.3f);

        /// <summary>Fuer das Admin-Menue: auf dem Gehweg vor der Gasse.</summary>
        public static Vector3 TeleportPoint => Found ? Current.entrance - Current.inward * 2f + Vector3.up * 0.5f : new Vector3(0f, 0.5f, -26f);

        /// <summary>Gasse neu suchen (fuer Tests nach einem Szenenwechsel).</summary>
        public static void Reset() => _searched = false;

        static void Search()
        {
            if (_searched) return;
            _searched = true;
            _found = false;
            var scene = SceneManager.GetActiveScene();
            if (!scene.IsValid()) return;
            var candidates = new List<Alley>();
            foreach (var root in scene.GetRootGameObjects())
            {
                foreach (var block in root.GetComponentsInChildren<Transform>(true))
                {
                    if (!block.name.StartsWith("Block_") || !block.name.EndsWith("_B")) continue;
                    var houses = new List<Bounds>();
                    foreach (Transform child in block)
                        if (child.name == "Building" && child.TryGetComponent(out Collider col)) houses.Add(col.bounds);
                    Vector3 blockCenter = Vector3.zero;
                    foreach (var h in houses) blockCenter += h.center;
                    if (houses.Count > 0) blockCenter /= houses.Count;
                    for (int i = 0; i < houses.Count; i++)
                        for (int j = 0; j < houses.Count; j++)
                            for (int axis = 0; axis < 2; axis += 1)
                                if (i != j) Between(houses[i], houses[j], axis == 0 ? 0 : 2, blockCenter, candidates);
                }
            }
            // Einfahrt am naechsten zum Brunnenplatz (Stadtmitte); Platz fuer beide muss frei sein
            candidates.Sort((a, b) => new Vector2(a.entrance.x, a.entrance.z).sqrMagnitude.CompareTo(new Vector2(b.entrance.x, b.entrance.z).sqrMagnitude));
            foreach (var c in candidates)
            {
                if (!Free(NixAt(c) - c.across * 0.25f) || !Free(MoeAt(c))) continue;
                _alley = c;
                _found = true;
                break;
            }
            if (!_found && candidates.Count > 0)
            {
                _alley = candidates[0];
                _found = true;
            }
            if (_found) _alley.entrance = StonerNpc.Ground(_alley.entrance);
        }

        /// <summary>Luecke zwischen Haus a und Haus b (b liegt auf der Achse axis dahinter). Beide Enden, die zur Strasse gehen, sind Einfahrten.</summary>
        static void Between(Bounds a, Bounds b, int axis, Vector3 blockCenter, List<Alley> into)
        {
            int along = axis == 0 ? 2 : 0;
            float gap = b.min[axis] - a.max[axis];
            if (gap < MinGap || gap > MaxGap) return;
            float lo = Mathf.Max(a.min[along], b.min[along]);
            float hi = Mathf.Min(a.max[along], b.max[along]);
            if (hi - lo < MinLength) return;
            float mid = (a.max[axis] + b.min[axis]) * 0.5f;
            foreach (bool atHi in new[] { false, true })
            {
                float end = atHi ? hi : lo;
                if (Mathf.Abs(end - blockCenter[along]) < 15f) continue; // Ende in der Blockmitte (Kreuzung zweier Gassen), keine Einfahrt
                var e = Vector3.zero;
                e[axis] = mid;
                e[along] = end;
                var inward = Vector3.zero;
                inward[along] = atHi ? -1f : 1f;
                var across = Vector3.zero;
                across[axis] = 1f; // zur Wand von Haus b
                into.Add(new Alley { entrance = e, inward = inward, across = across, halfWidth = gap * 0.5f, length = hi - lo });
            }
        }

        /// <summary>Steht da schon was (Muelltonne, Rail, Baum)? Hauswaende und Boden zaehlen nicht.</summary>
        static bool Free(Vector3 pos)
        {
            pos = StonerNpc.Ground(pos);
            foreach (var c in Physics.OverlapBox(pos + Vector3.up * 0.95f, new Vector3(0.4f, 0.7f, 0.4f), Quaternion.identity, ~0, QueryTriggerInteraction.Ignore))
            {
                if (c.name == "Building" || c.name == "Sidewalk" || c.name == "Ground") continue;
                return false;
            }
            return true;
        }

        // ------------------------------------------------------------------ Aufstellen

        public static void Spawn()
        {
            if (!Found)
            {
                Debug.LogWarning("AlleyCrew: keine Seitengasse gefunden");
                return;
            }
            if (Resources.Load<GameObject>("Characters/NPC_Nix") == null) return;
            var group = new GameObject("AlleyCrew").transform;
            StonerNpc.Spawn("Nix", StonerNpc.Ground(NixPosition), NixFacing, group);
            Vector3 moe = StonerNpc.Ground(MoePosition);
            SpawnCrate(moe, MoeFacing, group);
            StonerNpc.Spawn("Moe", moe, MoeFacing, group);
        }

        static void SpawnCrate(Vector3 position, Vector3 facing, Transform parent)
        {
            var prefab = Resources.Load<GameObject>("Characters/NPC_Crate");
            if (prefab == null) return;
            var root = new GameObject("Crate");
            root.transform.SetParent(parent, false);
            root.transform.SetPositionAndRotation(position, Quaternion.LookRotation(facing, Vector3.up));
            var model = Object.Instantiate(prefab, root.transform, false);
            model.name = "Model";
            StonerNpc.ApplyToon(model);
            var box = root.AddComponent<BoxCollider>();
            box.center = new Vector3(0f, 0.17f, 0f);
            box.size = new Vector3(0.42f, 0.34f, 0.42f);
        }
    }
}
