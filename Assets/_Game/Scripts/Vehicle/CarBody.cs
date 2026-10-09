using System.Collections.Generic;
using UnityEngine;

namespace DriftSkate
{
    /// <summary>
    /// Rechnet aus einer CarShape die Karosserie als Meshes: Unterbau (Querschnitte als Superellipsen entlang der Laenge,
    /// Radlaeufe ausgeschnitten, Kotfluegel leicht ausgebeult) und Kabine (Glas, Dach, Saeulen). Dazu Abfragen fuer
    /// Punkte auf der Oberflaeche, damit Lichter und Anbauteile genau aufsitzen.
    /// UVs fuer die Folierung (eine Textur, drei Baender): unten oben/Motorhaube/Dach (Draufsicht), Mitte rechte Seite,
    /// oben linke Seite. Seiten so ausgerichtet, dass Bilder von aussen nicht gespiegelt erscheinen.
    /// </summary>
    public class CarBody
    {
        public readonly CarDef def;
        public readonly CarShape s;
        public readonly float L, W, H, R, archR;
        readonly float[] _wheelZ;

        public const float BandTop = 0f, BandRight = 1f / 3f, BandLeft = 2f / 3f, BandH = 1f / 3f, Gutter = 0.006f;
        const int Ring = 30, GlassRing = 20;

        public CarBody(CarDef def, CarShape shape)
        {
            this.def = def;
            s = shape;
            L = def.length;
            W = def.width;
            H = def.height;
            R = def.wheelRadius;
            archR = R + 0.055f;
            _wheelZ = new[] { def.wheelbase * 0.5f, -def.wheelbase * 0.5f };
        }

        public float WheelT(int axle) => _wheelZ[axle] / L;

        // ------------------------------------------------------------------ Linien

        float EndRound(float t)
        {
            const float e = 0.022f;
            float u = Mathf.Clamp01((Mathf.Abs(t) - (0.5f - e)) / e);
            return 1f - Mathf.Sqrt(Mathf.Max(0f, 1f - u * u));
        }

        float ArchTop(float z, float clearance = 0f)
        {
            float y = float.MinValue;
            foreach (float zw in _wheelZ)
            {
                float dz = z - zw, ar = archR + clearance;
                if (Mathf.Abs(dz) < ar) y = Mathf.Max(y, R + Mathf.Sqrt(ar * ar - dz * dz));
            }
            return y;
        }

        public float TopY(float t)
        {
            float y = CarShape.Eval(s.top, t);
            float arch = ArchTop(t * L, 0.02f);
            if (arch > float.MinValue) y = Mathf.Max(y, arch + 0.08f); // Kotfluegel nie duenner als 8 cm
            return y - EndRound(t) * 0.05f;
        }

        public float BottomY(float t)
        {
            float y = CarShape.Eval(s.bottom, t);
            float arch = ArchTop(t * L);
            if (arch > float.MinValue) y = Mathf.Max(y, arch);
            return y + EndRound(t) * 0.04f;
        }

        public float RoofY(float t) => CarShape.Eval(s.roof, t);

        public float HalfW(float t)
        {
            float w = W * 0.5f;
            float noseStart = 0.5f - s.noseRound, tailStart = -0.5f + s.tailRound;
            if (t > noseStart)
            {
                float u = (t - noseStart) / s.noseRound;
                w *= 1f - s.noseTaper * (1f - Mathf.Sqrt(Mathf.Max(0f, 1f - u * u)));
            }
            else if (t < tailStart)
            {
                float u = (tailStart - t) / s.tailRound;
                w *= 1f - s.tailTaper * (1f - Mathf.Sqrt(Mathf.Max(0f, 1f - u * u)));
            }
            // Kotfluegel ueber den Raedern etwas breiter, dazwischen leicht tailliert
            float z = t * L, bulge = 0f;
            foreach (float zw in _wheelZ)
            {
                float d = (z - zw) / (archR * 1.5f);
                bulge = Mathf.Max(bulge, Mathf.Exp(-d * d));
            }
            w += s.flare * bulge - s.flare * 0.35f;
            return w - EndRound(t) * 0.05f;
        }

        // ------------------------------------------------------------------ Querschnitt

        float NTop => s.squareness;
        float NBot => s.squareness * 1.8f;

        /// <summary>Punkt k (0..Ring) des Querschnitts bei t; k = 0 unten Mitte, gegen den Uhrzeigersinn von vorn gesehen.</summary>
        Vector3 SectionPoint(float t, int k)
        {
            float yb = BottomY(t), yt = TopY(t), a = HalfW(t);
            float cy = (yb + yt) * 0.5f, b = (yt - yb) * 0.5f;
            float phi = -Mathf.PI * 0.5f + 2f * Mathf.PI * k / Ring;
            float c = Mathf.Cos(phi), sn = Mathf.Sin(phi);
            float n = sn > 0f ? NTop : NBot;
            float x = a * Mathf.Sign(c) * Mathf.Pow(Mathf.Abs(c), 2f / n);
            float y = cy + b * Mathf.Sign(sn) * Mathf.Pow(Mathf.Abs(sn), 2f / n);
            float up = Mathf.Max(0f, (y - cy) / Mathf.Max(0.01f, b));
            x *= 1f - s.tumble * up * up;
            return new Vector3(x, y, t * L);
        }

        /// <summary>Halbe Breite der Karosserie bei t in Hoehe y (0, wenn y ausserhalb liegt).</summary>
        public float SideX(float t, float y)
        {
            float yb = BottomY(t), yt = TopY(t), a = HalfW(t);
            float cy = (yb + yt) * 0.5f, b = (yt - yb) * 0.5f;
            float v = (y - cy) / Mathf.Max(0.01f, b);
            if (Mathf.Abs(v) >= 1f) return 0f;
            float n = v > 0f ? NTop : NBot;
            float sn = Mathf.Pow(Mathf.Abs(v), n * 0.5f);
            float c = Mathf.Sqrt(Mathf.Max(0f, 1f - sn * sn));
            float x = a * Mathf.Pow(c, 2f / n);
            float up = Mathf.Max(0f, v);
            return x * (1f - s.tumble * up * up);
        }

        /// <summary>Hoehe der Oberseite bei t an der Stelle x (Motorhaube, Kofferraum).</summary>
        public float TopSurfaceY(float t, float x)
        {
            float yb = BottomY(t), yt = TopY(t), a = HalfW(t);
            float cy = (yb + yt) * 0.5f, b = (yt - yb) * 0.5f;
            float c = Mathf.Clamp01(Mathf.Abs(x) / Mathf.Max(0.01f, a * (1f - s.tumble * 0.6f)));
            float cc = Mathf.Pow(c, NTop * 0.5f);
            float sn = Mathf.Sqrt(Mathf.Max(0f, 1f - cc * cc));
            return cy + b * Mathf.Pow(sn, 2f / NTop);
        }

        /// <summary>z der Front (sign = 1) bzw. des Hecks (sign = -1) an der Stelle (x, y).</summary>
        public float EndZ(float x, float y, int sign)
        {
            float lo = 0.5f - 0.12f, hi = 0.5f;
            if (SideX(lo * sign, y) < Mathf.Abs(x)) return lo * sign * L;
            for (int i = 0; i < 18; i++)
            {
                float m = (lo + hi) * 0.5f;
                if (SideX(m * sign, y) >= Mathf.Abs(x)) lo = m; else hi = m;
            }
            return lo * sign * L;
        }

        /// <summary>Punkt und Normale auf Front/Heck an (x, y).</summary>
        public void EndPoint(float x, float y, int sign, out Vector3 p, out Vector3 n)
        {
            const float e = 0.03f;
            p = new Vector3(x, y, EndZ(x, y, sign));
            Vector3 px = new Vector3(x + e, y, EndZ(x + e, y, sign)) - new Vector3(x - e, y, EndZ(x - e, y, sign));
            Vector3 py = new Vector3(x, y + e, EndZ(x, y + e, sign)) - new Vector3(x, y - e, EndZ(x, y - e, sign));
            n = Vector3.Cross(py, px).normalized * sign;
            if (n.z * sign < 0f) n = -n;
        }

        /// <summary>Punkt und Normale auf der Seite bei (t, y), side = +1 rechts, -1 links.</summary>
        public void SidePoint(float t, float y, int side, out Vector3 p, out Vector3 n)
        {
            const float e = 0.02f;
            float x = SideX(t, y);
            p = new Vector3(side * x, y, t * L);
            float dz = (SideX(t + e / L, y) - SideX(t - e / L, y)) / (2f * e);
            float dy = (SideX(t, y + e) - SideX(t, y - e)) / (2f * e);
            n = new Vector3(side, -dy * side, -dz * side).normalized;
        }

        // ------------------------------------------------------------------ Meshes

        List<float> Stations(float from, float to, float step)
        {
            var list = new List<float>();
            for (float t = from; t < to - 1e-4f; t += step)
            {
                list.Add(t);
                // feiner an den Enden und an den Radlaeufen
                float extra = Mathf.Abs(t) > 0.46f ? 3 : 1;
                for (int axle = 0; axle < 2; axle++)
                    if (Mathf.Abs(t * L - _wheelZ[axle]) < archR + 0.1f) extra = 2;
                for (int i = 1; i < extra; i++) list.Add(t + step * i / extra);
            }
            list.Add(to);
            return list;
        }

        /// <summary>Unterbau mit Folierungs-UVs (ein Submesh: Lack).</summary>
        public Mesh BuildLowerBody()
        {
            var ts = Stations(-0.5f, 0.5f, 0.0125f);
            int nt = ts.Count;
            var grid = new Vector3[nt, Ring + 1];
            for (int i = 0; i < nt; i++)
                for (int k = 0; k <= Ring; k++) grid[i, k] = SectionPoint(ts[i], k % Ring);
            var normals = GridNormals(grid, nt, Ring + 1, true);

            var mb = new MeshBuilder();
            for (int i = 0; i < nt - 1; i++)
                for (int k = 0; k < Ring; k++)
                    mb.Quad(0, grid[i, k], grid[i + 1, k], grid[i + 1, k + 1], grid[i, k + 1],
                            normals[i, k], normals[i + 1, k], normals[i + 1, k + 1], normals[i, k + 1], this);

            // Deckel vorn und hinten (Faecher), Raender mit den Normalen des Rings (keine Luecken in der Outline)
            for (int end = 0; end < 2; end++)
            {
                int i = end == 0 ? nt - 1 : 0;
                Vector3 center = Vector3.zero;
                for (int k = 0; k < Ring; k++) center += grid[i, k];
                center /= Ring;
                Vector3 cn = end == 0 ? Vector3.forward : Vector3.back;
                for (int k = 0; k < Ring; k++)
                {
                    if (end == 0) mb.Tri(0, center, grid[i, k + 1], grid[i, k], cn, normals[i, k + 1], normals[i, k], this);
                    else mb.Tri(0, center, grid[i, k], grid[i, k + 1], cn, normals[i, k], normals[i, k + 1], this);
                }
            }
            return mb.ToMesh("CarBody");
        }

        /// <summary>Kabine: Submesh 0 Lack (Dach, Saeulen), 1 Glas, 2 dunkle B-Saeule.</summary>
        public Mesh BuildGreenhouse()
        {
            float t0 = s.GlassEndT, t1 = s.CowlT;
            var ts = Stations(t0, t1, 0.008f);
            int nt = ts.Count;
            var grid = new Vector3[nt, GlassRing + 1];
            for (int i = 0; i < nt; i++)
            {
                float t = ts[i];
                float yb = TopY(t) - 0.04f, yt = Mathf.Max(RoofY(t), yb + 0.002f);
                float wb = HalfW(t) * s.glassInset, wt = wb * s.glassTaper;
                for (int k = 0; k <= GlassRing; k++)
                {
                    float phi = Mathf.PI * k / GlassRing;
                    float c = Mathf.Cos(phi), sn = Mathf.Sin(phi);
                    const float n = 4.5f;
                    float h = Mathf.Pow(Mathf.Max(0f, sn), 2f / n); // sin(pi) ist minimal negativ -> sonst NaN
                    float y = yb + (yt - yb) * h;
                    float half = Mathf.Lerp(wb, wt, h);
                    float x = half * Mathf.Sign(c) * Mathf.Pow(Mathf.Abs(c), 2f / n);
                    grid[i, k] = new Vector3(x, y, t * L);
                }
            }
            var normals = GridNormals(grid, nt, GlassRing + 1, false);

            float roofF = s.RoofFrontT, roofR = s.RoofRearT;
            float sideF = s.sideGlassFront != 0f ? s.sideGlassFront : roofF + (t1 - roofF) * 0.5f;
            float sideR = s.sideGlassRear != 0f ? s.sideGlassRear : (s.hatch ? t0 + 0.07f : roofR - 0.035f);
            var mb = new MeshBuilder();
            for (int i = 0; i < nt - 1; i++)
            {
                for (int k = 0; k < GlassRing; k++)
                {
                    Vector3 a = grid[i, k], b = grid[i + 1, k], c = grid[i + 1, k + 1], d = grid[i, k + 1];
                    Vector3 mid = (a + b + c + d) * 0.25f;
                    Vector3 nrm = (normals[i, k] + normals[i + 1, k] + normals[i + 1, k + 1] + normals[i, k + 1]).normalized;
                    float t = mid.z / L;
                    float wt = HalfW(t) * s.glassInset * s.glassTaper;
                    int sub;
                    bool sideRing = k < 4 || k >= GlassRing - 4; // unterer, fast senkrechter Teil des Rings
                    if (!sideRing)
                    {
                        bool roof = t >= roofR - 0.012f && t <= roofF + 0.012f;
                        bool edge = Mathf.Abs(mid.x) > wt * 0.84f;
                        sub = roof || edge ? 0 : 1;
                    }
                    else
                    {
                        bool window = t > sideR && t < sideF;
                        sub = window ? 1 : 0;
                        if (window && s.bPillar != 0f && Mathf.Abs(t - s.bPillar) < 0.011f) sub = 2;
                    }
                    mb.Quad(sub, a, b, c, d, normals[i, k], normals[i + 1, k], normals[i + 1, k + 1], normals[i, k + 1], this);
                }
            }
            return mb.ToMesh("CarCabin", 3);
        }

        /// <summary>Glatte Normalen eines Gitters (Querschnitte x Ringpunkte), nach aussen gerichtet.</summary>
        Vector3[,] GridNormals(Vector3[,] g, int ni, int nk, bool closed)
        {
            var n = new Vector3[ni, nk];
            for (int i = 0; i < ni; i++)
            {
                for (int k = 0; k < nk; k++)
                {
                    Vector3 di = g[Mathf.Min(i + 1, ni - 1), k] - g[Mathf.Max(i - 1, 0), k];
                    int kp = k + 1, km = k - 1;
                    if (closed) { kp = (k + 1) % (nk - 1); km = (k - 1 + nk - 1) % (nk - 1); }
                    else { kp = Mathf.Min(kp, nk - 1); km = Mathf.Max(km, 0); }
                    Vector3 dk = g[i, kp] - g[i, km];
                    Vector3 nrm = Vector3.Cross(dk, di);
                    if (nrm.sqrMagnitude < 1e-10f) nrm = Vector3.Cross(dk, Vector3.forward);
                    nrm.Normalize();
                    // nach aussen: weg von der Mittelachse
                    Vector3 centerDir = new Vector3(g[i, k].x, g[i, k].y - (closed ? (BottomY(g[i, k].z / L) + TopY(g[i, k].z / L)) * 0.5f : TopY(g[i, k].z / L) - 0.2f), 0f);
                    if (Vector3.Dot(nrm, centerDir) < 0f) nrm = -nrm;
                    n[i, k] = nrm;
                }
            }
            return n;
        }

        /// <summary>Folierungs-UV fuer einen Punkt, je nach Flaechenrichtung (Seite oder Draufsicht).</summary>
        public Vector2 LiveryUV(Vector3 p, Vector3 faceNormal)
        {
            float zN = Mathf.Clamp01(p.z / L + 0.5f);
            if (Mathf.Abs(faceNormal.x) > 0.55f)
            {
                float v = Mathf.Clamp01(p.y / H);
                return faceNormal.x < 0f
                    ? new Vector2(1f - zN, BandLeft + Gutter + v * (BandH - 2f * Gutter))
                    : new Vector2(zN, BandRight + Gutter + v * (BandH - 2f * Gutter));
            }
            float lat = Mathf.Clamp01(p.x / W + 0.5f);
            return new Vector2(zN, BandTop + Gutter + lat * (BandH - 2f * Gutter));
        }

        // ------------------------------------------------------------------ Mesh-Bauhelfer

        class MeshBuilder
        {
            readonly List<Vector3> _v = new List<Vector3>();
            readonly List<Vector3> _n = new List<Vector3>();
            readonly List<Vector2> _uv = new List<Vector2>();
            readonly List<int>[] _tris = { new List<int>(), new List<int>(), new List<int>() };

            /// <summary>Viereck mit eigenen Eckpunkten (eigene UV-Projektion), Normalen geglaettet uebernommen.</summary>
            public void Quad(int sub, Vector3 a, Vector3 b, Vector3 c, Vector3 d, Vector3 na, Vector3 nb, Vector3 nc, Vector3 nd, CarBody body)
            {
                Vector3 face = (na + nb + nc + nd).normalized;
                int i0 = _v.Count;
                foreach (var (p, n) in new[] { (a, na), (b, nb), (c, nc), (d, nd) })
                {
                    _v.Add(p);
                    _n.Add(n);
                    _uv.Add(body.LiveryUV(p, face));
                }
                // Wicklung so, dass die Vorderseite zur Normalen zeigt
                Vector3 geo = Vector3.Cross(b - a, d - a);
                if (geo.sqrMagnitude < 1e-12f) geo = Vector3.Cross(c - b, a - b);
                bool flip = Vector3.Dot(geo, face) < 0f; // Unity: Cross(b-a, c-a) zeigt zur Vorderseite
                var t = _tris[sub];
                if (!flip) { t.Add(i0); t.Add(i0 + 1); t.Add(i0 + 2); t.Add(i0); t.Add(i0 + 2); t.Add(i0 + 3); }
                else { t.Add(i0); t.Add(i0 + 2); t.Add(i0 + 1); t.Add(i0); t.Add(i0 + 3); t.Add(i0 + 2); }
            }

            public void Tri(int sub, Vector3 a, Vector3 b, Vector3 c, Vector3 na, Vector3 nb, Vector3 nc, CarBody body)
            {
                Vector3 face = (na + nb + nc).normalized;
                int i0 = _v.Count;
                foreach (var (p, n) in new[] { (a, na), (b, nb), (c, nc) })
                {
                    _v.Add(p);
                    _n.Add(n);
                    _uv.Add(body.LiveryUV(p, face));
                }
                Vector3 geo = Vector3.Cross(b - a, c - a);
                var t = _tris[sub];
                if (Vector3.Dot(geo, face) >= 0f) { t.Add(i0); t.Add(i0 + 1); t.Add(i0 + 2); }
                else { t.Add(i0); t.Add(i0 + 2); t.Add(i0 + 1); }
            }

            public Mesh ToMesh(string name, int subMeshes = 1)
            {
                var m = new Mesh { name = name };
                m.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
                m.SetVertices(_v);
                m.SetNormals(_n);
                m.SetUVs(0, _uv);
                m.subMeshCount = subMeshes;
                for (int i = 0; i < subMeshes; i++) m.SetTriangles(_tris[i], i);
                m.RecalculateBounds();
                return m;
            }
        }
    }
}
