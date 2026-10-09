using System.Collections.Generic;
using System.Text;
using UnityEngine;

namespace DriftSkate
{
    /// <summary>
    /// Findet die Knochen einer Figur anhand ueblicher Namen (eigene, Mixamo, Blender/Rigify, VRM, 3ds Max Biped ...).
    /// Eigene Zuordnungen kommen aus mod.json als "Ziel=Knochenname", z. B. "Hips=pelvis".
    /// </summary>
    public class SkeletonMap
    {
        public Transform hips, spine, chest, neck, head;
        public readonly Transform[] upperArm = new Transform[2], lowerArm = new Transform[2], hand = new Transform[2];
        public readonly Transform[] upperLeg = new Transform[2], lowerLeg = new Transform[2], foot = new Transform[2];

        public static readonly string[] RequiredNames =
        {
            "Hips", "Spine", "Chest", "Neck", "Head",
            "UpperArm_L", "LowerArm_L", "Hand_L", "UpperArm_R", "LowerArm_R", "Hand_R",
            "UpperLeg_L", "LowerLeg_L", "Foot_L", "UpperLeg_R", "LowerLeg_R", "Foot_R"
        };

        // Achtung: keine Praefixe, mit denen "right..." beginnt (z. B. "rig").
        static readonly string[] Prefixes = { "mixamorig", "bip01", "bip001", "jbip", "def", "org", "mch", "armature", "skeleton" };

        /// <summary>Kleinbuchstaben, ohne Trennzeichen und ohne typische Praefixe.</summary>
        public static string Normalize(string name)
        {
            var sb = new StringBuilder(name.Length);
            foreach (char ch in name.ToLowerInvariant())
                if (char.IsLetterOrDigit(ch)) sb.Append(ch);
            string n = sb.ToString();
            bool changed = true;
            while (changed)
            {
                changed = false;
                foreach (var p in Prefixes)
                {
                    if (n.Length > p.Length + 2 && n.StartsWith(p))
                    {
                        n = n.Substring(p.Length);
                        changed = true;
                    }
                }
                // "j_bip_c_hips" -> "chips": Seitenkennung c (center) entfernen
                if (n.Length > 3 && n.StartsWith("c") && (n == "chips" || n == "cspine" || n == "cchest" || n == "cneck" || n == "chead" || n == "cupperchest"))
                {
                    n = n.Substring(1);
                    changed = true;
                }
            }
            return n;
        }

        static bool SideMatch(string n, string keyword, int side)
        {
            string[] tags = side == 0 ? new[] { "left", "l" } : new[] { "right", "r" };
            foreach (var t in tags)
                if (n == t + keyword || n == keyword + t) return true;
            return false;
        }

        static Transform FindSide(List<Transform> all, int side, params string[] keywords)
        {
            foreach (var kw in keywords)
                foreach (var t in all)
                    if (SideMatch(Normalize(t.name), kw, side)) return t;
            return null;
        }

        static int Depth(Transform t)
        {
            int d = 0;
            while (t.parent != null) { d++; t = t.parent; }
            return d;
        }

        public static SkeletonMap Resolve(Transform root, string[] overrides, out string missing)
        {
            var all = new List<Transform>(root.GetComponentsInChildren<Transform>(true));
            var map = new SkeletonMap();

            Transform Exact(params string[] names)
            {
                foreach (var nm in names)
                    foreach (var t in all)
                        if (Normalize(t.name) == nm) return t;
                return null;
            }

            map.hips = Exact("hips", "hip", "pelvis");
            map.neck = Exact("neck", "neck1", "neck01");
            map.head = Exact("head");

            // Wirbelsaeule: unterste Spine ist "Spine", oberste "Chest"
            var spines = all.FindAll(t =>
            {
                string n = Normalize(t.name);
                return n == "spine" || n == "spine0" || n == "spine1" || n == "spine01" || n == "spine2" || n == "spine02" ||
                       n == "spine3" || n == "spine03" || n == "chest" || n == "upperchest" || n == "abdomen" || n == "thorax";
            });
            spines.Sort((a, b) => Depth(a).CompareTo(Depth(b)));
            if (spines.Count > 0)
            {
                map.spine = spines[0];
                map.chest = spines[spines.Count - 1];
                var explicitChest = spines.Find(t => Normalize(t.name) == "chest");
                if (explicitChest != null) map.chest = explicitChest;
            }

            for (int s = 0; s < 2; s++)
            {
                map.upperArm[s] = FindSide(all, s, "upperarm", "arm", "uparm");
                map.lowerArm[s] = FindSide(all, s, "lowerarm", "forearm", "elbow");
                map.hand[s] = FindSide(all, s, "hand", "wrist");
                map.upperLeg[s] = FindSide(all, s, "upperleg", "upleg", "thigh", "hip");
                map.lowerLeg[s] = FindSide(all, s, "lowerleg", "leg", "shin", "calf", "knee");
                map.foot[s] = FindSide(all, s, "foot", "ankle");
            }

            // Eigene Zuordnungen aus mod.json
            if (overrides != null)
            {
                foreach (var entry in overrides)
                {
                    int eq = entry.IndexOf('=');
                    if (eq <= 0) continue;
                    string key = entry.Substring(0, eq).Trim(), boneName = entry.Substring(eq + 1).Trim();
                    var t = all.Find(x => x.name == boneName);
                    if (t != null) map.Assign(key, t);
                }
            }

            if (map.spine == null) map.spine = map.chest;
            if (map.chest == null) map.chest = map.spine;
            if (map.neck == null && map.head != null) map.neck = map.head.parent;

            var miss = new List<string>();
            for (int i = 0; i < RequiredNames.Length; i++)
                if (map.Get(i) == null) miss.Add(RequiredNames[i]);
            missing = miss.Count > 0 ? string.Join(", ", miss) : null;
            return map;
        }

        Transform Get(int i)
        {
            switch (i)
            {
                case 0: return hips;
                case 1: return spine;
                case 2: return chest;
                case 3: return neck;
                case 4: return head;
                case 5: return upperArm[0];
                case 6: return lowerArm[0];
                case 7: return hand[0];
                case 8: return upperArm[1];
                case 9: return lowerArm[1];
                case 10: return hand[1];
                case 11: return upperLeg[0];
                case 12: return lowerLeg[0];
                case 13: return foot[0];
                case 14: return upperLeg[1];
                case 15: return lowerLeg[1];
                default: return foot[1];
            }
        }

        void Assign(string key, Transform t)
        {
            switch (key)
            {
                case "Hips": hips = t; break;
                case "Spine": spine = t; break;
                case "Chest": chest = t; break;
                case "Neck": neck = t; break;
                case "Head": head = t; break;
                case "UpperArm_L": upperArm[0] = t; break;
                case "LowerArm_L": lowerArm[0] = t; break;
                case "Hand_L": hand[0] = t; break;
                case "UpperArm_R": upperArm[1] = t; break;
                case "LowerArm_R": lowerArm[1] = t; break;
                case "Hand_R": hand[1] = t; break;
                case "UpperLeg_L": upperLeg[0] = t; break;
                case "LowerLeg_L": lowerLeg[0] = t; break;
                case "Foot_L": foot[0] = t; break;
                case "UpperLeg_R": upperLeg[1] = t; break;
                case "LowerLeg_R": lowerLeg[1] = t; break;
                case "Foot_R": foot[1] = t; break;
            }
        }
    }
}
