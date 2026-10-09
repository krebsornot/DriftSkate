using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace DriftSkate.EditorTools
{
    /// <summary>
    /// Prueft die Stoner-NPCs: Import als Humanoid, Knochen-Zuordnung, Animationen; rendert Vorschau-Bilder in der Stadt.
    /// Protokoll: Logs/npc_test.txt, Bilder: Logs/npc_*.png
    /// </summary>
    public static class NpcTests
    {
        static readonly string[] Ids = { "Jojo", "Kalle", "Luna", "Nix", "Moe" };

        [MenuItem("DriftSkate/Tests/NPCs pruefen und rendern")]
        public static void Run()
        {
            Directory.CreateDirectory("Logs");
            var log = new StringBuilder();
            int problems = 0;
            foreach (var id in Ids)
            {
                string path = $"Assets/_Game/Resources/Characters/NPC_{id}.fbx";
                var mi = AssetImporter.GetAtPath(path) as ModelImporter;
                if (mi == null) { log.AppendLine($"{id}: FBX fehlt"); problems++; continue; }
                var assets = AssetDatabase.LoadAllAssetsAtPath(path);
                var avatar = assets.OfType<Avatar>().FirstOrDefault();
                var clips = assets.OfType<AnimationClip>().Where(c => !c.name.StartsWith("__preview__")).ToArray();
                bool human = avatar != null && avatar.isValid && avatar.isHuman;
                if (!human) problems++;
                log.AppendLine($"{id}: Typ {mi.animationType}, Avatar gueltig/humanoid: {(avatar != null ? avatar.isValid + "/" + avatar.isHuman : "fehlt")}");
                if (avatar != null)
                {
                    var map = avatar.humanDescription.human;
                    log.AppendLine($"  {map.Length} Knochen zugeordnet: " + string.Join(", ", map.Select(h => h.humanName + "=" + h.boneName)));
                    foreach (var need in new[] { "Chest", "UpperChest", "LeftEye", "RightEye", "Jaw", "Left Thumb Proximal", "Right Little Distal", "LeftToes" })
                        if (!map.Any(h => h.humanName == need)) { log.AppendLine("  FEHLT: " + need); problems++; }
                    // Unity verwirft Drehungen von Knochen, die zwischen zugeordneten Knochen haengen (steht als Warnung in der .meta)
                    if (File.Exists(path + ".meta") && File.ReadAllText(path + ".meta").Contains("inbetween humanoid"))
                    {
                        log.AppendLine("  Import-Warnung: Animation eines Zwischenknochens wird verworfen");
                        problems++;
                    }
                }
                log.AppendLine("  Clips: " + string.Join(", ", clips.Select(c => $"{c.name} ({c.length:0.00}s{(c.isLooping ? ", Schleife" : "")})")));
                foreach (var need in new[] { "Idle", "Laugh" })
                    if (!clips.Any(c => c.name == need)) { log.AppendLine("  FEHLT Clip " + need); problems++; }
                var mats = assets.OfType<Material>().Select(m => m.name).ToArray();
                log.AppendLine($"  {mats.Length} Materialien, z. B. " + string.Join(", ", mats.Take(4)));
            }

            // In der Stadt aufstellen und aus mehreren Blickwinkeln rendern
            EditorSceneManager.OpenScene(ProjectBuilder.CityPath);
            SkyLook.ApplySelected();
            StonerNpc.All.Clear();
            StonerNpc.SpawnHangout();
            AlleyCrew.Reset();
            AlleyCrew.Spawn();
            log.AppendLine(AlleyCrew.Found ? $"Gasse: Einfahrt {AlleyCrew.Current.entrance}, Breite {AlleyCrew.Current.halfWidth * 2f:0.0} m, Laenge {AlleyCrew.Current.length:0} m, Nix {AlleyCrew.NixPosition}, Moe {AlleyCrew.MoePosition}"
                                           : "Gasse: NICHT GEFUNDEN");
            if (!AlleyCrew.Found) problems++;
            MiruAssets.EnsureRuntimeLink();
            ParkMiru.Reset();
            ParkMiru.Spawn();
            log.AppendLine(ParkMiru.Found ? $"Park: Miru sitzt bei {ParkMiru.SeatPosition}, steht auf nach {ParkMiru.StandPosition}, Blick {ParkMiru.Facing}" : "Park: KEINE FREIE BANK");
            if (!ParkMiru.Found) problems++;
            foreach (var npc in StonerNpc.All)
                foreach (var smr in npc.GetComponentsInChildren<SkinnedMeshRenderer>()) smr.forceMatrixRecalculationPerRender = true;
            log.AppendLine($"Aufgestellt: {StonerNpc.All.Count} Figuren");
            if (StonerNpc.All.Count != 6) problems++;
            var cam = Camera.main;
            Vector3 c = StonerNpc.HangoutPosition;
            Vector3 f = StonerNpc.HangoutFacing;
            Vector3 right = Vector3.Cross(Vector3.up, f);
            foreach (var npc in StonerNpc.All)
            {
                npc.Preview("Idle", 0.5f);
                var smr = npc.GetComponentInChildren<SkinnedMeshRenderer>();
                log.AppendLine($"  {npc.Id}: Kopf bei {npc.Head.position}, Clips {string.Join(",", npc.ClipNames)}");
            }
            cam.fieldOfView = 40f;
            cam.transform.position = c + f * 5.2f + Vector3.up * 1.5f;
            cam.transform.LookAt(c + Vector3.up * 0.9f);
            SimTests.Capture(cam, "Logs/npc_group.png");
            cam.transform.position = c + f * 3.4f - right * 2.6f + Vector3.up * 1.7f;
            cam.transform.LookAt(c + Vector3.up * 0.8f);
            SimTests.Capture(cam, "Logs/npc_group_side.png");

            // Gasse: von der Einfahrt hinein, und quer von gegenueber auf Nix
            if (AlleyCrew.Found)
            {
                var a = AlleyCrew.Current;
                cam.fieldOfView = 50f;
                cam.transform.position = a.entrance - a.inward * 1.5f + Vector3.up * 1.7f;
                cam.transform.LookAt(a.entrance + a.inward * 9f + Vector3.up * 0.9f);
                SimTests.Capture(cam, "Logs/npc_alley.png");
                cam.fieldOfView = 40f;
                cam.transform.position = AlleyCrew.NixPosition - a.across * (a.halfWidth * 2f - 0.6f) + a.inward * 1.2f + Vector3.up * 1.5f;
                cam.transform.LookAt(AlleyCrew.NixPosition + Vector3.up * 1.1f);
                SimTests.Capture(cam, "Logs/npc_alley_nix.png");
                cam.transform.position = AlleyCrew.MoePosition + AlleyCrew.MoeFacing * 3.2f + Vector3.up * 1.3f;
                cam.transform.LookAt(AlleyCrew.MoePosition + Vector3.up * 0.7f);
                SimTests.Capture(cam, "Logs/npc_alley_moe.png");
            }

            // Park: Miru sitzt auf der Bank (von vorn, von der Seite), dann halb aufgestanden und stehend
            var miru = StonerNpc.All.FirstOrDefault(n => n.Id == "Miru");
            if (ParkMiru.Found && miru != null)
            {
                Vector3 m = ParkMiru.SeatPosition, mf = ParkMiru.Facing, mr = Vector3.Cross(Vector3.up, mf);
                miru.SetSitting(true, true);
                miru.Preview("Idle", 1f);
                var anim = miru.Animator;
                Vector3 hips = miru.transform.InverseTransformPoint(anim.GetBoneTransform(HumanBodyBones.Hips).position);
                Vector3 footL = miru.transform.InverseTransformPoint(anim.GetBoneTransform(HumanBodyBones.LeftFoot).position);
                log.AppendLine($"  Miru sitzend (lokal): Huefte {hips}, linker Fuss {footL}, Clips {string.Join(",", miru.ClipNames)}");
                if (!miru.HasClip("Sitting")) { log.AppendLine("  FEHLT Clip Sitting (Miru)"); problems++; }
                cam.fieldOfView = 40f;
                cam.transform.position = m + mf * 4.2f - mr * 1.2f + Vector3.up * 1.4f;
                cam.transform.LookAt(m + Vector3.up * 0.7f);
                SimTests.Capture(cam, "Logs/npc_park_miru.png");
                cam.transform.position = m + mf * 1.0f + mr * 2.6f + Vector3.up * 0.9f;
                cam.transform.LookAt(m + mf * 0.1f + Vector3.up * 0.6f);
                SimTests.Capture(cam, "Logs/npc_park_miru_side.png");
                cam.transform.position = m + mf * 1.9f + mr * 0.6f + Vector3.up * 1.2f;
                cam.transform.LookAt(m + Vector3.up * 0.85f);
                SimTests.Capture(cam, "Logs/npc_park_miru_close.png");
                // Aufstehen: halb und ganz (Position wie MiruTalk)
                foreach (var (amount, file) in new[] { (0.5f, "npc_park_miru_rising.png"), (0f, "npc_park_miru_standing.png") })
                {
                    miru.SetSitting(amount > 0f, true);
                    var mt = typeof(StonerNpc).GetField("_sitWeight", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                    mt.SetValue(miru, amount);
                    miru.transform.position = Vector3.Lerp(ParkMiru.StandPosition, m, miru.SitAmount);
                    miru.Preview("Idle", 1f);
                    cam.transform.position = m + mf * 1.2f + mr * 3.2f + Vector3.up * 1.2f;
                    cam.transform.LookAt(m + mf * 0.3f + Vector3.up * 0.8f);
                    SimTests.Capture(cam, "Logs/" + file);
                }
                miru.transform.position = m;
                miru.SetSitting(true, true);
            }

            // Nahaufnahmen der eigenen Animationen
            var shots = new (string id, string clip, float t)[] { ("Jojo", "Smoke", 1.2f), ("Kalle", "Snack", 1.6f), ("Luna", "Vibe", 0.4f), ("Luna", "Laugh", 0.5f), ("Jojo", "Idle", 0f), ("Kalle", "Idle", 0f),
                                                                 ("Nix", "Idle", 0f), ("Nix", "Deal", 1.2f), ("Nix", "Lookout", 1f), ("Moe", "Idle", 0f), ("Moe", "Count", 1.8f), ("Miru", "Idle", 0f) };
            foreach (var s in shots)
            {
                var npc = StonerNpc.All.FirstOrDefault(n => n.Id == s.id);
                if (npc == null) continue;
                foreach (var other in StonerNpc.All) other.Preview("Idle", 0.5f);
                var handR = npc.Animator.GetBoneTransform(HumanBodyBones.RightHand);
                Vector3 before = handR.position;
                npc.Preview(s.clip, s.t);
                log.AppendLine($"  {s.id} {s.clip}@{s.t}: rechte Hand {before} -> {handR.position}, Aktion {npc.CurrentAction}");
                Vector3 head = npc.Head.position;
                Vector3 fwd = npc.transform.forward;
                cam.fieldOfView = 30f;
                cam.transform.position = head + fwd * 1.7f + Vector3.Cross(Vector3.up, fwd) * 0.5f + Vector3.up * 0.05f;
                cam.transform.LookAt(head + Vector3.down * 0.25f);
                SimTests.Capture(cam, $"Logs/npc_{s.id.ToLower()}_{s.clip.ToLower()}.png");
            }
            log.AppendLine(problems == 0 ? "OK" : $"PROBLEME: {problems}");
            File.WriteAllText("Logs/npc_test.txt", log.ToString());
            Debug.Log("[DriftSkate] NPC-Test:\n" + log);
        }
    }
}
