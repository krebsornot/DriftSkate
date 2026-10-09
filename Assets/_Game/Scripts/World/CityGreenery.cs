using System.Collections.Generic;
using UnityEngine;

namespace DriftSkate
{
    /// <summary>
    /// Baeume, Hecken, Baenke und Brunnen-Details fuer die oeffentlichen Plaetze. Eigener Zufall (_green), damit die
    /// Aufteilung der Stadt und die Fassaden gleich bleiben. Vor jedem Teil wird geprueft, ob dort schon etwas steht
    /// (Rails, Ledges, Wände); der Platz am Sofa der Stoner bleibt frei.
    /// </summary>
    public partial class CityBuilder
    {
        readonly System.Random _green = new System.Random(5150);
        float G(float min, float max) => min + (float)_green.NextDouble() * (max - min);

        static readonly Color[] LeafGreen = { Palette.Hex("4CC27A"), Palette.Hex("3AA867"), Palette.Hex("72D893") };
        static readonly Color[] LeafTeal = { Palette.Hex("3DBFA0"), Palette.Hex("2C9F88"), Palette.Hex("6ADBBF") };
        static readonly Color[] LeafBlossom = { Palette.Hex("FF9EC4"), Palette.Hex("F47BAE"), Palette.Hex("FFC6DD") };
        static readonly Color Bark = Palette.Hex("7A4B2A");
        static readonly Color HedgeDark = Palette.Hex("3A9A60"), HedgeLight = Palette.Hex("52B676");
        static readonly Color[] FlowerColors = { Palette.Pink, Palette.Yellow, Palette.White, Palette.Hex("B98CFF"), Palette.Orange };

        // ------------------------------------------------------------------ Baum

        /// <summary>
        /// Toon-Baum: Stamm mit Wurzelansatz und zwei Aesten, Krone aus mehreren Blaetter-Baellen in drei Toenen.
        /// kind: 0 gruen, 1 Kirschbluete, 2 tuerkis, -1 zufaellig (meist gruen).
        /// </summary>
        void Tree(Transform parent, Vector3 basePos, float scale = 1f, int kind = -1)
        {
            var t = Shapes.Group(parent, "Tree");
            t.position = basePos;
            t.rotation = Quaternion.Euler(0f, G(0f, 360f), 0f);
            t.localScale = Vector3.one * scale;
            if (kind < 0)
            {
                double r = _green.NextDouble();
                kind = r < 0.2 ? 1 : r < 0.32 ? 2 : 0;
            }
            var leaves = kind == 1 ? LeafBlossom : kind == 2 ? LeafTeal : LeafGreen;

            Shapes.Part(PrimitiveType.Cylinder, t, new Vector3(0f, 1.5f, 0f), new Vector3(0.42f, 1.5f, 0.42f), Bark, 0.3f, keepCollider: true, name: "Trunk");
            Shapes.Part(PrimitiveType.Cylinder, t, new Vector3(0f, 0.12f, 0f), new Vector3(0.72f, 0.12f, 0.72f), Bark, 0.3f, name: "TrunkBase");
            Shapes.Part(PrimitiveType.Cylinder, t, new Vector3(0.42f, 2.85f, 0f), new Vector3(0.2f, 0.7f, 0.2f), Bark, 0.3f, new Vector3(0f, 0f, -38f), name: "Branch");
            Shapes.Part(PrimitiveType.Cylinder, t, new Vector3(-0.3f, 3f, 0.22f), new Vector3(0.17f, 0.55f, 0.17f), Bark, 0.3f, new Vector3(22f, 0f, 40f), name: "Branch");

            // Krone: grosser Kern, fuenf Baelle drumherum, heller Ball obendrauf (Licht von oben)
            float h = G(-0.2f, 0.3f);
            Shapes.Part(PrimitiveType.Sphere, t, new Vector3(0f, 4.3f + h, 0f), new Vector3(3.4f, 2.9f, 3.4f), leaves[0], 0.5f, name: "Crown");
            float start = G(0f, 72f);
            for (int i = 0; i < 5; i++)
            {
                float a = (start + i * 72f + G(-18f, 18f)) * Mathf.Deg2Rad;
                float rad = G(1.2f, 1.65f), s = G(1.9f, 2.6f);
                Shapes.Part(PrimitiveType.Sphere, t, new Vector3(Mathf.Cos(a) * rad, G(3.5f, 4.6f) + h, Mathf.Sin(a) * rad), new Vector3(s, s * 0.85f, s),
                            leaves[i % 2 == 0 ? 1 : 0], 0.5f, name: "CrownPuff");
            }
            Shapes.Part(PrimitiveType.Sphere, t, new Vector3(G(-0.4f, 0.4f), 5.35f + h, G(-0.4f, 0.4f)), new Vector3(2.3f, 1.8f, 2.3f), leaves[2], 0.5f, name: "CrownTop");
        }

        /// <summary>Kleiner Busch aus drei Baellen, optional mit Bluetenpunkten.</summary>
        void Bush(Transform parent, Vector3 pos, float size, bool flowers)
        {
            for (int i = 0; i < 3; i++)
            {
                Vector3 o = new Vector3(G(-0.35f, 0.35f), 0f, G(-0.35f, 0.35f)) * size;
                float s = size * G(0.75f, 1.05f);
                Shapes.Part(PrimitiveType.Sphere, parent, pos + o + Vector3.up * s * 0.38f, new Vector3(s, s * 0.8f, s), i == 0 ? HedgeLight : HedgeDark, 0.4f, name: "Bush");
            }
            if (!flowers) return;
            Color fc = FlowerColors[_green.Next(FlowerColors.Length)];
            for (int i = 0; i < 4; i++)
            {
                float a = G(0f, Mathf.PI * 2f);
                Vector3 p = pos + new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * size * 0.42f + Vector3.up * size * G(0.55f, 0.8f);
                Shapes.Part(PrimitiveType.Sphere, parent, p, Vector3.one * 0.2f, fc, 0f, emission: 0.15f, name: "Flower");
            }
        }

        // ------------------------------------------------------------------ Hecke, Bank, Muelleimer

        /// <summary>Hecke (fest, 85 cm hoch) mit hellerer Oberkante und ein paar Blueten obendrauf.</summary>
        void Hedge(Transform parent, Vector3 center, Vector3 along, float length)
        {
            Vector3 n = Vector3.Cross(along, Vector3.up);
            Vector3 size = Abs(along * length + n * 1.1f) + Vector3.up * 0.85f;
            Shapes.Box(parent, center + Vector3.up * 0.425f, size, HedgeDark, 0.35f, collider: true, name: "Hedge");
            Shapes.Box(parent, center + Vector3.up * 0.9f, Abs(along * (length - 0.2f) + n * 0.9f) + Vector3.up * 0.12f, HedgeLight, 0.3f, name: "HedgeTop");
            for (float x = -length * 0.5f + 0.8f; x < length * 0.5f - 0.6f; x += G(1.1f, 2f))
            {
                if (_green.NextDouble() > 0.55) continue;
                Shapes.Part(PrimitiveType.Sphere, parent, center + along * x + n * G(-0.35f, 0.35f) + Vector3.up * 0.98f, Vector3.one * 0.22f,
                            FlowerColors[_green.Next(FlowerColors.Length)], 0f, emission: 0.15f, name: "Flower");
            }
        }

        /// <summary>Parkbank aus Holzlatten auf schwarzem Gestell; Vorderkante der Sitzflaeche ist grindbar.</summary>
        void Bench(Transform parent, Vector3 pos, Vector3 facing)
        {
            var b = Shapes.Group(parent, "Bench");
            b.position = pos;
            b.rotation = Quaternion.LookRotation(facing);
            Color wood = Palette.Hex("D69A62"), frame = Palette.Ink;
            for (int s = -1; s <= 1; s += 2)
            {
                Shapes.Box(b, new Vector3(s * 1.05f, 0.22f, 0f), new Vector3(0.1f, 0.44f, 0.5f), frame, 0.15f, name: "BenchLeg");
                Shapes.Box(b, new Vector3(s * 1.05f, 0.72f, -0.27f), new Vector3(0.08f, 0.55f, 0.08f), frame, 0.15f, new Vector3(-10f, 0f, 0f), name: "BenchBackPost");
            }
            for (int i = -1; i <= 1; i++)
                Shapes.Box(b, new Vector3(0f, 0.47f, i * 0.17f), new Vector3(2.4f, 0.06f, 0.14f), wood, 0.2f, name: "BenchSlat");
            Shapes.Box(b, new Vector3(0f, 0.72f, -0.29f), new Vector3(2.4f, 0.13f, 0.05f), wood, 0.2f, new Vector3(-10f, 0f, 0f), name: "BenchBack");
            Shapes.Box(b, new Vector3(0f, 0.93f, -0.33f), new Vector3(2.4f, 0.13f, 0.05f), wood, 0.2f, new Vector3(-10f, 0f, 0f), name: "BenchBack");
            var seat = b.gameObject.AddComponent<BoxCollider>();
            seat.center = new Vector3(0f, 0.25f, 0f);
            seat.size = new Vector3(2.3f, 0.5f, 0.55f);
            var back = b.gameObject.AddComponent<BoxCollider>();
            back.center = new Vector3(0f, 0.8f, -0.3f);
            back.size = new Vector3(2.3f, 0.6f, 0.1f);
            CreateRailPath(new List<Vector3> { b.TransformPoint(new Vector3(-1.15f, 0.52f, 0.2f)), b.TransformPoint(new Vector3(1.15f, 0.52f, 0.2f)) }, "BENCH");
        }

        void Bin(Transform parent, Vector3 pos) => StreetBin(parent, pos);

        // ------------------------------------------------------------------ Platz-Rand

        /// <summary>Steht im Quader (ueber dem Boden) schon etwas Festes? Rails zaehlen mit.</summary>
        static bool Free(Vector3 groundCenter, Vector3 halfExtents, Quaternion rot)
        {
            Physics.SyncTransforms();
            int mask = ~LayerMask.GetMask("Skater", "Car", "Ignore Raycast");
            return !Physics.CheckBox(groundCenter + Vector3.up * (0.2f + halfExtents.y), halfExtents, rot, mask, QueryTriggerInteraction.Ignore);
        }

        /// <summary>Hier sitzen zur Laufzeit die Stoner auf dem Sofa (wird erst im Spiel aufgestellt).</summary>
        static bool NearHangout(Vector3 p) => Vector3.ProjectOnPlane(p - StonerNpc.HangoutPosition, Vector3.up).magnitude < 6.5f;

        /// <summary>
        /// Rand eines Platzes: pro Seite zwei Heckenstuecke (Mitte und Ecken bleiben als Einfahrt offen),
        /// Baeume an den Heckenenden, davor eine Bank mit Muelleimer.
        /// </summary>
        void PlazaEdges(Transform block, Vector3 c)
        {
            Vector3[] sides = { Vector3.forward, Vector3.right, Vector3.back, Vector3.left };
            const float edge = 31.2f, segMid = 16f, segLen = 12f;
            foreach (var n in sides)
            {
                Vector3 along = Vector3.Cross(Vector3.up, n);
                Quaternion rot = Quaternion.LookRotation(-n);
                for (int seg = -1; seg <= 1; seg += 2)
                {
                    Vector3 hc = c + n * edge + along * (seg * segMid);
                    if (NearHangout(hc) || !Free(hc, new Vector3(segLen * 0.5f, 0.4f, 0.55f), rot)) continue;
                    Hedge(block, hc, along, segLen);
                    Tree(block, hc - along * (segLen * 0.5f - 0.4f), G(0.85f, 1.05f));
                    Tree(block, hc + along * (segLen * 0.5f - 0.4f), G(0.85f, 1.05f));

                    // Bank davor (mit Platz zum Anfahren), daneben ein Muelleimer
                    Vector3 bp = c + n * 29.5f + along * (seg * segMid);
                    if (NearHangout(bp) || !Free(bp - n * 1.6f, new Vector3(1.5f, 0.5f, 2f), rot)) continue;
                    Bench(block, bp, -n);
                    Vector3 binPos = bp + along * (seg * 1.9f);
                    if (Free(binPos, new Vector3(0.35f, 0.4f, 0.35f), rot)) Bin(block, binPos);
                }
            }
        }

        // ------------------------------------------------------------------ Brunnenplatz

        /// <summary>Runde Baum-Insel mit grindbarem Rand (fuer die Ecken des Brunnenplatzes).</summary>
        void TreeIsland(Transform block, Vector3 p)
        {
            var rim = Shapes.Part(PrimitiveType.Cylinder, block, p + Vector3.up * 0.25f, new Vector3(6f, 0.25f, 6f), Palette.Hex("9E8FB8"), 0.35f, name: "Island", surface: Surface.Concrete);
            var mc = rim.AddComponent<MeshCollider>();
            mc.sharedMesh = rim.GetComponent<MeshFilter>().sharedMesh;
            Shapes.Part(PrimitiveType.Cylinder, block, p + Vector3.up * 0.51f, new Vector3(5.2f, 0.01f, 5.2f), Palette.Hex("5FB86E"), 0f, name: "Grass");
            var pts = new List<Vector3>();
            for (int i = 0; i <= 20; i++)
            {
                float a = i / 20f * Mathf.PI * 2f;
                pts.Add(p + new Vector3(Mathf.Cos(a) * 2.88f, 0.52f, Mathf.Sin(a) * 2.88f));
            }
            CreateRailPath(pts, "ISLAND");
            Tree(block, p + Vector3.up * 0.5f, G(1.05f, 1.2f), _green.NextDouble() < 0.5 ? 1 : 0);
            for (int i = 0; i < 4; i++)
            {
                float a = (i * 90f + 45f + G(-15f, 15f)) * Mathf.Deg2Rad;
                Bush(block, p + new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * 1.7f + Vector3.up * 0.5f, G(0.8f, 1.05f), i % 2 == 0);
            }
        }

        /// <summary>Brunnen mit drei Etagen: Becken (bleibt), mittlere Schale, obere Schale, Wasserstrahl und herabfallendes Wasser.</summary>
        void FountainTiers(Transform block, Vector3 c)
        {
            Color stone = Palette.White, bowlCol = Palette.Hex("F2EEF8");
            Shapes.Part(PrimitiveType.Cylinder, block, c + new Vector3(0f, 1.62f, 0f), new Vector3(4.8f, 0.16f, 4.8f), bowlCol, 0.45f, name: "FountainBowl", surface: Surface.Concrete);
            Shapes.Part(PrimitiveType.Cylinder, block, c + new Vector3(0f, 1.79f, 0f), new Vector3(4.2f, 0.01f, 4.2f), Palette.Cyan, 0f, emission: 0.4f, name: "Water", surface: Surface.Water);
            Shapes.Part(PrimitiveType.Cylinder, block, c + new Vector3(0f, 4.35f, 0f), new Vector3(0.55f, 0.35f, 0.55f), stone, 0.4f, name: "FountainStem");
            Shapes.Part(PrimitiveType.Cylinder, block, c + new Vector3(0f, 4.75f, 0f), new Vector3(2.5f, 0.12f, 2.5f), bowlCol, 0.45f, name: "FountainBowlTop", surface: Surface.Concrete);
            Shapes.Part(PrimitiveType.Cylinder, block, c + new Vector3(0f, 4.88f, 0f), new Vector3(2.1f, 0.01f, 2.1f), Palette.Cyan, 0f, emission: 0.4f, name: "Water", surface: Surface.Water);
            Shapes.Part(PrimitiveType.Sphere, block, c + new Vector3(0f, 5.35f, 0f), Vector3.one * 1.1f, Palette.Pink, 0.5f, name: "FountainBall");

            WaterFx(block, c + new Vector3(0f, 5.8f, 0f), true, 0f);
            WaterFx(block, c + new Vector3(0f, 4.86f, 0f), false, 1.25f);
            WaterFx(block, c + new Vector3(0f, 1.76f, 0f), false, 2.4f);
        }

        /// <summary>Wasser als Partikel: Strahl nach oben bzw. Ring, der ueber den Schalenrand faellt.</summary>
        static void WaterFx(Transform parent, Vector3 pos, bool jet, float radius)
        {
            var go = new GameObject(jet ? "FountainJet" : "FountainCascade");
            go.transform.SetParent(parent, false);
            go.transform.position = pos;
            var ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = ps.main;
            main.loop = true;
            main.playOnAwake = true;
            main.startLifetime = jet ? new ParticleSystem.MinMaxCurve(0.9f, 1.15f) : new ParticleSystem.MinMaxCurve(0.6f, 0.8f);
            main.startSpeed = jet ? new ParticleSystem.MinMaxCurve(4.2f, 5.4f) : new ParticleSystem.MinMaxCurve(0.5f, 0.9f);
            main.startSize = jet ? new ParticleSystem.MinMaxCurve(0.14f, 0.26f) : new ParticleSystem.MinMaxCurve(0.18f, 0.32f);
            main.gravityModifier = 1f;
            main.startColor = new Color(0.82f, 0.95f, 1f, 0.8f);
            main.maxParticles = 300;
            main.simulationSpace = ParticleSystemSimulationSpace.Local;
            var em = ps.emission;
            em.rateOverTime = jet ? 40f : radius * 34f;
            var shape = ps.shape;
            if (jet)
            {
                shape.shapeType = ParticleSystemShapeType.Cone;
                shape.angle = 7f;
                shape.radius = 0.05f;
                shape.rotation = new Vector3(-90f, 0f, 0f);
            }
            else
            {
                shape.shapeType = ParticleSystemShapeType.Circle;
                shape.radius = radius;
                shape.radiusThickness = 0f;
                shape.rotation = new Vector3(90f, 0f, 0f);
            }
            var size = ps.sizeOverLifetime;
            size.enabled = true;
            size.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, 1f, 1f, 0.55f));
            var col = ps.colorOverLifetime;
            col.enabled = true;
            var g = new Gradient();
            g.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                      new[] { new GradientAlphaKey(0.9f, 0f), new GradientAlphaKey(0.8f, 0.7f), new GradientAlphaKey(0f, 1f) });
            col.color = g;
            var r = go.GetComponent<ParticleSystemRenderer>();
            r.sharedMaterial = Resources.Load<Material>("SmokeMat");
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            r.receiveShadows = false;
            ps.Play();
        }
    }
}
