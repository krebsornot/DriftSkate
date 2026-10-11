using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace DriftSkate.EditorTools
{
    /// <summary>
    /// Vermisst den Imbisswagen im Prefab (Arbeitsflaeche, Laengsachse, Seite mit Schirm und Schildmast) und vergleicht mit
    /// den Werten in HotdogStand, nach denen Bodo und Udo hinter den Wagen gestellt werden. Prueft dazu die Rigs der beiden.
    /// Menue: DriftSkate → Tests → Imbiss pruefen. Batch: -executeMethod DriftSkate.EditorTools.HotdogTests.MeasureCart -quit
    /// </summary>
    public static class HotdogTests
    {
        [MenuItem("DriftSkate/Tests/Imbiss pruefen")]
        public static void MeasureCart()
        {
            Directory.CreateDirectory("Logs/hotdog");
            var lines = new List<string>();
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PropAssets.HotdogCart.PrefabPath);
            if (prefab == null) throw new Exception("Imbisswagen-Prefab fehlt: " + PropAssets.HotdogCart.PrefabPath);
            var body = prefab.transform.Find("Visual/Body");
            var mesh = body != null ? body.GetComponent<MeshFilter>().sharedMesh : null;
            if (mesh == null) throw new Exception("Body-Mesh fehlt");
            // Punkte im Prefab-Raum (Visual traegt die Skalierung)
            var toRoot = prefab.transform.worldToLocalMatrix * body.localToWorldMatrix;
            var pts = mesh.vertices.Select(v => toRoot.MultiplyPoint3x4(v)).ToList();

            // Arbeitsflaeche: duenne Schicht knapp unter der Oberkante (1,15 m)
            var work = pts.Where(p => p.y > 1.07f && p.y < 1.17f).ToList();
            float top = work.Max(p => p.y);
            lines.Add($"Arbeitsflaeche bis {top:0.000} m hoch, {work.Count} Punkte");
            // Hauptachse in XZ (Kovarianz)
            float mx = work.Average(p => p.x), mz = work.Average(p => p.z);
            float sxx = work.Average(p => (p.x - mx) * (p.x - mx)), szz = work.Average(p => (p.z - mz) * (p.z - mz)), sxz = work.Average(p => (p.x - mx) * (p.z - mz));
            float ang = 0.5f * Mathf.Atan2(2f * sxz, sxx - szz);
            var axis = new Vector3(Mathf.Cos(ang), 0f, Mathf.Sin(ang));
            if (Vector3.Dot(axis, Vector3.forward) < 0f) axis = -axis; // zum Schild (Prefab-Vorwaerts)
            var across = Vector3.Cross(Vector3.up, axis); // rechts von der Laengsachse
            float aMin = work.Min(p => Vector3.Dot(p, axis)), aMax = work.Max(p => Vector3.Dot(p, axis));
            float cMin = work.Min(p => Vector3.Dot(p, across)), cMax = work.Max(p => Vector3.Dot(p, across));
            Vector3 center = axis * (aMin + aMax) * 0.5f + across * (cMin + cMax) * 0.5f;
            lines.Add($"Laengsachse {axis.x:0.000}, {axis.z:0.000} ({Mathf.Atan2(axis.x, axis.z) * Mathf.Rad2Deg:0.0} Grad), Laenge {aMax - aMin:0.00} m, Tiefe {cMax - cMin:0.00} m");
            lines.Add($"Mitte der Arbeitsflaeche {center.x:0.000}, {center.z:0.000}");

            // Schirmstange (und Schildmast) stehen auf einer Seite: dort ist der Verkaeufer, gegenueber der Kunde.
            // Die Stange hat nur oben und unten Punkte, also die Mitte des Schirmdachs nehmen
            var canopy = pts.Where(p => p.y > 2.2f).ToList();
            float mastSide = canopy.Average(p => Vector3.Dot(p - center, across));
            float mastAlong = canopy.Average(p => Vector3.Dot(p - center, axis));
            Vector3 serve = mastSide > 0f ? -across : across;
            lines.Add($"Schirm ({canopy.Count} Punkte) {mastSide:0.00} m seitlich, {mastAlong:0.00} m laengs -> Kundenseite {serve.x:0.000}, {serve.z:0.000}");

            bool ok = Vector3.Angle(axis, HotdogStand.CartAxis) < 3f && Vector3.Angle(serve, HotdogStand.ServeSide) < 3f &&
                      Vector3.Distance(new Vector3(center.x, 0f, center.z), HotdogStand.CartCenter) < 0.06f && Mathf.Abs(top - 1.15f) < 0.05f;
            lines.Add(ok ? "OK: passt zu HotdogStand" : $"FEHLT: HotdogStand anpassen (dort Achse {HotdogStand.CartAxis}, Kundenseite {HotdogStand.ServeSide}, Mitte {HotdogStand.CartCenter})");

            // Rigs der Verkaeufer
            foreach (var id in new[] { "Bodo", "Udo" })
            {
                var model = Resources.Load<GameObject>("Characters/NPC_" + id);
                var anim = model != null ? model.GetComponentInChildren<Animator>() : null;
                var clips = Resources.LoadAll<AnimationClip>("Characters/NPC_" + id).Select(c => c.name).Where(n => !n.StartsWith("__preview__")).ToList();
                bool rig = anim != null && anim.avatar != null && anim.avatar.isValid && anim.avatar.isHuman;
                bool all = new[] { "Idle", "Grill", "Serve", "Laugh" }.All(clips.Contains);
                lines.Add($"{(rig && all ? "OK" : "FEHLT")}: {id} Humanoid {rig}, Clips {string.Join(", ", clips)}");
                ok &= rig && all;
            }
            File.WriteAllLines("Logs/hotdog/measure.txt", lines);
            foreach (var l in lines) Debug.Log("[Imbiss] " + l);
            if (!ok && Application.isBatchMode) EditorApplication.Exit(3);
        }
    }
}
