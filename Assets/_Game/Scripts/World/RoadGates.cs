using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace DriftSkate
{
    /// <summary>
    /// Beim Laden der Stadt (Szene bleibt unveraendert):
    /// - Haengebruecke im Osten befahrbar: Hitboxen fuer Deck und Seitenbarrieren, Abschluss-Mauer am Ende.
    ///   Statt der festen Absperrung an der Kaikante eine automatische Schranke, die unsichtbare Kai-Wand bekommt dort
    ///   eine Luecke.
    /// - Die steile Auffahrt aus der Szene (ein gekippter Quader, 26 % Steigung) wird ausgeblendet und durch eine
    ///   lange Platte mit sanftem Profil ersetzt (ca. 10 % Steigung, weicher Ein- und Auslauf, Gelaender, Pfeiler);
    ///   das Deck beginnt entsprechend weiter draussen, die Haengeseile reichen dort bis zur Rampe herunter.
    /// - Automatische Schranken an beiden Einfahrten des Parkhauses.
    /// </summary>
    public static class RoadGates
    {
        public static readonly List<AutoGate> Gates = new List<AutoGate>();
        /// <summary>Bruecke: Fahrbahnmitte an der Schranke und am Ende (fuer Tests und Teleport).</summary>
        public static Vector3 BridgeGate { get; private set; }
        public static Vector3 BridgeEnd { get; private set; }
        /// <summary>Bruecke: x, ab dem die Auffahrt in das ebene Deck uebergeht, und maximale Steigung der Auffahrt.</summary>
        public static float BridgeDeckStart { get; private set; }
        public static float BridgeRampGrade { get; private set; }
        public static bool BridgeOpen { get; private set; }

        /// <summary>Auffahrt: Laenge ab der Kaikante und Laenge der weichen Uebergaenge an beiden Enden (siehe MeshFactory.RampProfile).</summary>
        const float RampLength = 180f, RampEase = 30f;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Register()
        {
            SceneManager.sceneLoaded -= Loaded;
            SceneManager.sceneLoaded += Loaded;
        }

        static void Loaded(Scene scene, LoadSceneMode mode)
        {
            if (scene.name != "City") return;
            Gates.Clear();
            BridgeOpen = false;
            var group = new GameObject("RoadGates").transform;
            SetupBridge(group);
            SetupParkhaus(group);
        }

        static float GroundY(Vector3 p, float fallback)
        {
            int mask = ~LayerMask.GetMask("Skater", "Car", "Ignore Raycast", "Rail");
            return Physics.Raycast(p + Vector3.up * 3f, Vector3.down, out RaycastHit hit, 8f, mask, QueryTriggerInteraction.Ignore) ? hit.point.y : fallback;
        }

        /// <summary>Zwei Schranken-Einheiten, deren Baeume sich in der Mitte treffen.</summary>
        static void Pair(Transform group, string name, Vector3 center, Vector3 across, float halfWidth)
        {
            across = across.normalized;
            for (int s = -1; s <= 1; s += 2)
            {
                Vector3 post = center + across * (s * (halfWidth + 0.45f));
                post.y = GroundY(post, center.y);
                var lane = center;
                Gates.Add(AutoGate.Build(group, name + (s < 0 ? "_A" : "_B"), post, -s * across, halfWidth + 0.15f, lane));
            }
        }

        // ------------------------------------------------------------------ Bruecke

        static void SetupBridge(Transform group)
        {
            var bridge = GameObject.Find("Bridge");
            var perimeter = GameObject.Find("Perimeter");
            if (bridge == null || perimeter == null) { Debug.LogWarning("RoadGates: Bruecke oder Kai fehlt"); return; }
            Transform deck = null, approach = null;
            var barriers = new List<Transform>();
            var piers = new List<Transform>();
            var hangers = new List<Transform>();
            var cables = new List<Transform>();
            float firstPylon = float.MaxValue;
            foreach (Transform t in bridge.transform)
            {
                switch (t.name)
                {
                    case "Deck": deck = t; break;
                    case "Approach": approach = t; break;
                    case "Barrier": barriers.Add(t); break;
                    case "Pier": piers.Add(t); break;
                    case "Hanger": hangers.Add(t); break;
                    case "Cable": cables.Add(t); break;
                    case "Pylon": firstPylon = Mathf.Min(firstPylon, t.position.x - t.lossyScale.x * 0.5f); break;
                }
            }
            if (deck == null || approach == null) { Debug.LogWarning("RoadGates: Deck oder Auffahrt fehlt"); return; }
            Vector3 outward = Vector3.right, along = Vector3.Cross(Vector3.up, outward);
            float half = CityBuilder.Half, deckTop = deck.position.y + deck.lossyScale.y * 0.5f;
            float deckEnd = deck.position.x + deck.lossyScale.x * 0.5f, roadHalf = deck.lossyScale.z * 0.5f;
            BuildApproach(group, deck, approach, barriers, piers, hangers, cables, firstPylon, half, deckTop, deckEnd, roadHalf);

            // Absperrung und Verbotsschilder an der Kaikante weg, die unsichtbare Kai-Wand bekommt eine Luecke
            const float gap = 12.5f;
            foreach (Transform t in perimeter.transform)
            {
                Vector3 p = t.position;
                if (p.x < half || Mathf.Abs(p.z) > 40f) continue;
                if ((t.name == "Barrier" || t.name == "BarrierPost" || t.name.StartsWith("Sign")) && Mathf.Abs(p.z) < gap + 1f && p.x < half + 6f)
                    t.gameObject.SetActive(false);
                else if (t.name == "Boundary" && t.TryGetComponent(out BoxCollider wall) && p.x < half + 6f)
                {
                    // Kai-Wand (quer ueber die ganze Hafenoeffnung): in zwei Stuecke links und rechts der Auffahrt teilen
                    float len = wall.size.z;
                    float piece = len * 0.5f - gap;
                    if (piece <= 0f) { wall.enabled = false; continue; }
                    wall.enabled = false;
                    for (int s = -1; s <= 1; s += 2)
                    {
                        var go = new GameObject("BoundaryPart");
                        go.layer = t.gameObject.layer;
                        go.transform.SetParent(t.parent, false);
                        go.transform.position = p + Vector3.forward * (s * (gap + piece * 0.5f));
                        go.AddComponent<BoxCollider>().size = new Vector3(wall.size.x, wall.size.y, piece);
                    }
                }
            }

            // Schranke an der Kaikante (wo die Absperrung stand), Baeume quer ueber die 22 m breite Auffahrt
            Vector3 gateCenter = outward * (half + 3.4f);
            gateCenter.y = GroundY(gateCenter, 0f);
            Pair(group, "BridgeGate", gateCenter, along, roadHalf);
            BridgeGate = gateCenter;

            // Am Ende: Betonschwellen rot-weiss mit Hitbox, damit niemand ins Wasser faehrt
            var end = new GameObject("BridgeEnd").transform;
            end.SetParent(group, false);
            float x = deckEnd - 1.2f;
            var blocks = new MeshFactory.BoxBatch();
            var stripes = new MeshFactory.BoxBatch();
            for (float z = -roadHalf + 1f; z < roadHalf - 0.5f; z += 2.2f)
            {
                blocks.Add(new Vector3(x, deckTop + 0.5f, z), new Vector3(0.9f, 1f, 2f));
                stripes.Add(new Vector3(x - 0.46f, deckTop + 0.55f, z), new Vector3(0.04f, 0.3f, 2f));
            }
            blocks.Build(end, "Blocks", Palette.Hex("D9D4DE"), "bridge_end_blocks");
            stripes.Build(end, "Stripes", Palette.Red, "bridge_end_stripes");
            var col = new GameObject("EndWall");
            col.transform.SetParent(end, false);
            col.transform.position = new Vector3(x, deckTop + 0.75f, 0f);
            col.AddComponent<BoxCollider>().size = new Vector3(1f, 1.5f, roadHalf * 2f);
            BridgeEnd = new Vector3(x - 6f, deckTop, 0f);
            BridgeOpen = true;
        }

        /// <summary>
        /// Die Auffahrt aus der Szene (gekippter Quader von der Kaikante bis zum Deck, 15 m auf 58 m) ist zu steil. Hier
        /// kommt stattdessen eine 180 m lange Platte mit sanftem Profil hin (MeshFactory.RampProfile), das Deck und seine
        /// Seitenbarrieren werden um dieses Stueck gekuerzt. Dazu Gelaender auf der Rampe (wie auf dem Deck), neue Pfeiler
        /// darunter, und die Haengeseile, die vorher am Deck endeten, reichen jetzt bis zur Rampe.
        /// Die Teile aus der Szene sind statisch gebatcht: Verschieben oder Skalieren aendert nur die Hitbox, nicht das
        /// Bild. Darum werden Deck, Barrieren und betroffene Haenger ausgeblendet und als neue Quader gebaut.
        /// </summary>
        static void BuildApproach(Transform group, Transform deck, Transform approach, List<Transform> barriers, List<Transform> piers,
                                  List<Transform> hangers, List<Transform> cables, float firstPylon, float half, float deckTop, float deckEnd, float roadHalf)
        {
            float rampStart = half + 4f;                       // Kaikante, Hoehe 0 (wie CityBuilder.BuildBridge)
            float rampEnd = Mathf.Min(rampStart + RampLength, firstPylon - 8f, deckEnd - 60f);
            float rampLen = rampEnd - rampStart, rise = deckTop, thickness = deck.lossyScale.y;
            float RampTop(float x) => MeshFactory.RampProfile(x - rampStart, rampLen, rise, RampEase);
            BridgeDeckStart = rampEnd;
            BridgeRampGrade = rise / (rampLen - RampEase);

            approach.gameObject.SetActive(false);
            foreach (var p in piers) if (p.position.x < rampEnd) p.gameObject.SetActive(false);

            // Deck und Barrieren beginnen erst am Ende der Rampe: alte ausblenden, kuerzere Kopien mit Hitbox bauen
            // (Kopien in Weltkoordinaten unter 'group', nicht unter dem gedrehten Rampen-Knoten)
            foreach (var t in new[] { deck }.Concat(barriers))
            {
                float end = t.position.x + t.lossyScale.x * 0.5f;
                var copy = Shapes.Box(group, new Vector3((rampEnd + end) * 0.5f, t.position.y, t.position.z),
                    new Vector3(end - rampEnd, t.lossyScale.y, t.lossyScale.z), Color.white, collider: true, name: t.name);
                copy.GetComponent<Renderer>().sharedMaterial = t.GetComponent<Renderer>().sharedMaterial;
                t.gameObject.SetActive(false);
            }

            var root = new GameObject("BridgeApproach").transform;
            root.SetParent(group, false);
            root.SetPositionAndRotation(new Vector3(rampStart, 0f, 0f), Quaternion.LookRotation(Vector3.right, Vector3.up)); // lokal +Z = nach aussen
            Slab(root, "Approach", MeshFactory.RampSlab(roadHalf * 2f, rampLen, rise, RampEase, thickness, 0f), deck.GetComponent<Renderer>().sharedMaterial, 0f);
            var railMesh = MeshFactory.RampSlab(0.6f, rampLen, rise, RampEase, 1.2f, 1.1f);
            var railMat = barriers.Count > 0 ? barriers[0].GetComponent<Renderer>().sharedMaterial : ToonMaterials.Get(Palette.Hex("E2475B"), 0.3f);
            foreach (var b in barriers) Slab(root, "Barrier", railMesh, railMat, b.position.z);

            // Pfeiler unter der Rampe (die ersten stehen noch auf dem Kai und reichen darum nur knapp aus dem Boden)
            for (float x = rampStart + 16f; x < rampEnd - 6f; x += 22f)
            {
                float top = RampTop(x) - thickness + 0.3f, bottom = -2.5f;
                if (top < bottom + 1f) continue;
                Shapes.Box(group, new Vector3(x, (top + bottom) * 0.5f, 0f), new Vector3(4f, top - bottom, 13f), Palette.Hex("8F88A6"), 0.4f, name: "Pier");
            }

            // Haengeseile ueber der Rampe: bis knapp unter die Fahrflaeche verlaengern statt in der Luft zu enden
            foreach (var h in hangers)
            {
                float x = h.position.x;
                if (x >= rampEnd) continue;
                float top = h.position.y + h.lossyScale.y * 0.5f, bottom = RampTop(x) - 0.6f;
                var copy = Shapes.Box(group, new Vector3(x, (top + bottom) * 0.5f, h.position.z),
                    new Vector3(h.lossyScale.x, top - bottom, h.lossyScale.z), Color.white, 0f, name: "Hanger");
                copy.GetComponent<Renderer>().sharedMaterial = h.GetComponent<Renderer>().sharedMaterial;
                h.gameObject.SetActive(false);
            }
            // Die Tragseile beginnen auf Hoehe des alten Deckanfangs und haengen dort jetzt in der Luft: je Seite ein
            // Haenger vom Seilanfang (unterstes Ende des Seilstuecks mit dem kleinsten x) bis zur Rampe
            if (hangers.Count > 0 && cables.Count > 0)
            {
                var hangerMat = hangers[0].GetComponent<Renderer>().sharedMaterial;
                float hangerW = hangers[0].lossyScale.x;
                foreach (int sgn in new[] { -1, 1 })
                {
                    Vector3 lowest = Vector3.zero;
                    float minX = float.MaxValue;
                    foreach (var c in cables)
                    {
                        if (Mathf.Sign(c.position.z) != sgn) continue;
                        Vector3 half2 = c.forward * (c.lossyScale.z * 0.5f - 0.2f);
                        Vector3 a = c.position - half2, b = c.position + half2;
                        Vector3 end = a.x < b.x ? a : b;
                        if (end.x < minX) { minX = end.x; lowest = end; }
                    }
                    if (minX >= rampEnd) continue;
                    float top = lowest.y + 0.35f, bottom = RampTop(lowest.x) - 0.6f;
                    var post = Shapes.Box(group, new Vector3(lowest.x, (top + bottom) * 0.5f, lowest.z), new Vector3(hangerW, top - bottom, hangerW), Color.white, 0f, name: "Hanger");
                    post.GetComponent<Renderer>().sharedMaterial = hangerMat;
                }
            }
        }

        /// <summary>Rampen-Mesh unter 'parent' (lokal +Z = nach aussen) mit Hitbox; z ist die Welt-Querposition.</summary>
        static void Slab(Transform parent, string name, Mesh mesh, Material material, float z)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = new Vector3(-z, 0f, 0f); // lokal +X zeigt nach Welt -Z
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            go.AddComponent<MeshRenderer>().sharedMaterial = material;
            go.AddComponent<MeshCollider>().sharedMesh = mesh;
        }

        // ------------------------------------------------------------------ Parkhaus

        static void SetupParkhaus(Transform group)
        {
            Transform ph = null;
            foreach (var t in Object.FindObjectsByType<Transform>(FindObjectsSortMode.None))
                if (t.name == ParkhausSite.PrefabName && t.parent != null && t.parent.name == ParkhausSite.BlockName) { ph = t; break; }
            if (ph == null) return;
            // Das Modell hat an beiden Einfahrten feste Schrankenarme (nur Deko, im Gebaeude-Mesh). Die werden ausgeschnitten,
            // an ihrer Stelle gehen bewegliche Baeume von den Ticket-Automaten (x = 11,2 und 24,8) zur Mitte hoch
            // (Modell-Masse: Arme bei z = 30,8 m, 0,95 m hoch, je Einfahrt zwei Arme mit kleiner Luecke bei x = 18).
            var model = ph.Find("Model");
            if (model != null)
                foreach (var mf in model.GetComponentsInChildren<MeshFilter>())
                {
                    if (mf.sharedMesh == null || !mf.sharedMesh.isReadable) continue;
                    var mesh = mf.sharedMesh;
                    int removed = 0;
                    foreach (float sx in new[] { -1f, 1f })
                    {
                        var area = new Bounds(new Vector3(sx * 18f, 0.95f, 30.8f), new Vector3(13.28f, 0.16f, 0.16f));
                        area = new Bounds(mf.transform.InverseTransformPoint(model.TransformPoint(area.center)), area.size);
                        mesh = CityPlots.Cut(mesh, area, out int n);
                        removed += n;
                    }
                    if (removed > 0) mf.sharedMesh = mesh;
                }
            foreach (float gx in new[] { -18f, 18f })
            {
                Vector3 lane = ph.TransformPoint(new Vector3(gx, 0f, 30.8f));
                lane.y = GroundY(lane, ph.position.y);
                string name = gx < 0f ? "ParkhausGateWest" : "ParkhausGateEast";
                for (int s = -1; s <= 1; s += 2)
                {
                    Vector3 post = ph.TransformPoint(new Vector3(gx + s * 6.64f, 0f, 30.8f));
                    post.y = lane.y;
                    Gates.Add(AutoGate.Build(group, name + (s < 0 ? "_A" : "_B"), post, ph.right * -s, 5.94f, lane, false, true));
                }
            }
        }
    }
}
