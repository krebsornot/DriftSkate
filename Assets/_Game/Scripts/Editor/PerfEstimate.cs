using System.IO;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace DriftSkate.EditorTools
{
    /// <summary>
    /// Grobe Draw-Call-Schaetzung fuer feste Blickpunkte in der Stadt: sichtbare Teile (mit Ebenen-Sichtweite),
    /// Outline-Durchgaenge und Schattenwerfer je Kaskade. Zum Vergleichen vor/nach Aenderungen an der Stadt.
    /// Ergebnis in Logs/perf_estimate.txt.
    /// </summary>
    public static class PerfEstimate
    {
        [MenuItem("DriftSkate/Tests/Draw-Calls schaetzen")]
        public static void Run()
        {
            EditorSceneManager.OpenScene(ProjectBuilder.CityPath);
            Physics.SyncTransforms();
            var cam = Camera.main;
            var urp = GraphicsSettings.currentRenderPipeline as UniversalRenderPipelineAsset;
            int cascades = urp != null ? urp.shadowCascadeCount : 4;
            float shadowDist = urp != null ? urp.shadowDistance : 50f;
            int detail = LayerMask.NameToLayer("Detail");
            float B(int i) => CityBuilder.BlockCenter(i);
            float R(int k) => CityBuilder.RoadCenter(k);
            var views = new (string name, Vector3 pos, Vector3 target)[]
            {
                ("strasse_haeuser", new Vector3(R(1), 1.8f, B(0)), new Vector3(R(1), 1.5f, B(1))),
                ("strasse_quer",    new Vector3(B(1), 1.8f, R(1)), new Vector3(B(3), 1.5f, R(1))),
                ("kreuzung",        new Vector3(R(2) + 6f, 1.8f, R(1) + 6f), new Vector3(R(3), 1.5f, R(0))),
                ("brunnenplatz",    new Vector3(B(2) - 24f, 1.8f, B(2) - 24f), new Vector3(B(2) + 20f, 1.5f, B(2) + 20f)),
                ("platz_sofa",      new Vector3(B(1) + 10f, 1.8f, B(2) - 10f), new Vector3(B(1) - 20f, 1.5f, B(2) + 28f)),
            };
            var renderers = Object.FindObjectsByType<Renderer>(FindObjectsSortMode.None);
            var sb = new StringBuilder();
            sb.AppendLine($"Kaskaden {cascades}, Schattendistanz {shadowDist} m, Renderer gesamt {renderers.Length}");
            int sum = 0;
            foreach (var v in views)
            {
                cam.transform.position = v.pos;
                cam.transform.LookAt(v.target);
                var planes = GeometryUtility.CalculateFrustumPlanes(cam);
                int forward = 0, outline = 0, shadow = 0;
                foreach (var r in renderers)
                {
                    if (!r.enabled || !r.gameObject.activeInHierarchy || r is ParticleSystemRenderer) continue;
                    float dist = Vector3.Distance(cam.transform.position, r.bounds.ClosestPoint(cam.transform.position));
                    if (detail >= 0 && r.gameObject.layer == detail && dist > 85f) continue;
                    var mats = r.sharedMaterials;
                    if (dist < shadowDist && r.shadowCastingMode != ShadowCastingMode.Off) shadow += mats.Length * cascades;
                    if (dist > cam.farClipPlane || !GeometryUtility.TestPlanesAABB(planes, r.bounds)) continue;
                    forward += mats.Length;
                    foreach (var m in mats)
                        if (m != null && m.shader != null && m.shader.name == "DriftSkate/Toon" && m.GetShaderPassEnabled("SRPDefaultUnlit")) outline++;
                }
                int total = forward + outline + shadow;
                sum += total;
                sb.AppendLine($"{v.name,-16} gesamt ~{total,5}  (Teile {forward}, Outline {outline}, Schatten {shadow})");
            }
            sb.AppendLine($"Summe ~{sum}");
            Directory.CreateDirectory("Logs");
            File.WriteAllText("Logs/perf_estimate.txt", sb.ToString());
            Debug.Log("[DriftSkate] " + sb);
        }
    }
}
