using System.IO;
using System.Text;
using UnityEngine;

namespace DriftSkate.EditorTools
{
    /// <summary>Gibt Knochen, Ausrichtung und Materialien der importierten Figur aus (Logs/character_inspect.txt).</summary>
    public static class CharacterInspect
    {
        public static void Run()
        {
            var prefab = Resources.Load<GameObject>("Characters/Skater");
            var sb = new StringBuilder();
            if (prefab == null) { sb.AppendLine("Kein Modell gefunden"); }
            else
            {
                var go = Object.Instantiate(prefab);
                foreach (var t in go.GetComponentsInChildren<Transform>())
                    sb.AppendLine($"{t.name,-14} pos {t.position}  up {t.up}  fwd {t.forward}  parent {(t.parent ? t.parent.name : "-")}");
                foreach (var r in go.GetComponentsInChildren<Renderer>())
                {
                    sb.Append($"RENDERER {r.name} ({r.GetType().Name}) mats:");
                    foreach (var m in r.sharedMaterials) sb.Append(" " + (m ? m.name : "null"));
                    sb.AppendLine($"  bounds {r.bounds}");
                }
                Object.DestroyImmediate(go);
            }
            Directory.CreateDirectory("Logs");
            File.WriteAllText("Logs/character_inspect.txt", sb.ToString());
        }
    }
}
