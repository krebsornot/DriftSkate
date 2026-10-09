using UnityEngine;

namespace DriftSkate
{
    /// <summary>Eine Himmel-Variante: Farben fuer Himmel, Sonne, Wolken, Dunst und Umgebungslicht.</summary>
    public class SkyPreset
    {
        public string id, name;
        public Vector3 sunEuler;
        public Color sunLight;
        public float sunIntensity;
        public Color zenith, mid, horizon, below, glow, sunDisc, cloudLit, cloudShade;
        public float cloudCover = 0.45f, stars = 0.8f, sunSize = 0.055f;
        public float shootingStars = 0f, starHeight = 0.4f;
        /// <summary>0 = Tag/Abend, bis 1 = Schattenseiten nachts dunkel (Laternen und Fenster leuchten weiter).</summary>
        public float darken = 0f;
        public Color ambientSky, ambientEquator, ambientGround;
        public float fogStart = 120f, fogEnd = 680f;

        /// <summary>Richtung zur Sonne (Gegenrichtung des Lichts).</summary>
        public Vector3 SunDirection => -(Quaternion.Euler(sunEuler) * Vector3.forward);
    }

    /// <summary>
    /// Himmel-Varianten der Stadt (Auswahl in der Garage unter OPTIONEN). Die Szene wird mit der ersten Variante gebaut,
    /// beim Laden der Stadt stellt ApplySelected die gewaehlte ein (Licht, Himmel-Material, Nebel, Umgebungslicht).
    /// </summary>
    public static class SkyLook
    {
        public static readonly SkyPreset[] Presets =
        {
            // Abend: tiefe, warme Sonne im West-Suedwesten, Pfirsich-Horizont, lila Schatten
            new SkyPreset
            {
                id = "abend", name = "ABEND",
                sunEuler = new Vector3(17f, 70f, 0f), sunLight = new Color(1f, 0.82f, 0.64f), sunIntensity = 1.25f,
                zenith = Palette.Hex("2C2A6C"), mid = Palette.Hex("8A5CA8"), horizon = Palette.Hex("E8A1B5"), below = Palette.Hex("5C4A78"),
                glow = Palette.Hex("FFB070"), sunDisc = Palette.Hex("FFE2A0"), cloudLit = Palette.Hex("FFCBA8"), cloudShade = Palette.Hex("7F5C9E"),
                ambientSky = new Color(0.46f, 0.42f, 0.76f), ambientEquator = new Color(0.9f, 0.63f, 0.7f), ambientGround = new Color(0.36f, 0.28f, 0.46f),
            },
            // Lila: Sonne knapp ueber dem Horizont, violetter Himmel, pinkes Gluehen, mehr Sterne
            new SkyPreset
            {
                id = "lila", name = "LILA",
                sunEuler = new Vector3(11f, 64f, 0f), sunLight = new Color(0.96f, 0.7f, 0.95f), sunIntensity = 1.15f,
                zenith = Palette.Hex("1C1450"), mid = Palette.Hex("6A3CAA"), horizon = Palette.Hex("C680D8"), below = Palette.Hex("452F6E"),
                glow = Palette.Hex("FF7EC6"), sunDisc = Palette.Hex("FFD6F2"), cloudLit = Palette.Hex("F8A8E2"), cloudShade = Palette.Hex("4C3388"),
                cloudCover = 0.5f, stars = 1.4f, sunSize = 0.065f,
                ambientSky = new Color(0.4f, 0.33f, 0.78f), ambientEquator = new Color(0.76f, 0.5f, 0.86f), ambientGround = new Color(0.3f, 0.22f, 0.46f),
                fogStart = 110f, fogEnd = 650f,
            },
            // Pink: Zuckerwatte-Himmel, rosa Wolken, weich-rosa Licht und helle, weisslich-rosa Sonne
            new SkyPreset
            {
                id = "pink", name = "PINK",
                sunEuler = new Vector3(15f, 76f, 0f), sunLight = new Color(1f, 0.76f, 0.86f), sunIntensity = 1.25f,
                zenith = Palette.Hex("5A2A8E"), mid = Palette.Hex("D9599F"), horizon = Palette.Hex("FFB3CF"), below = Palette.Hex("8A4E86"),
                glow = Palette.Hex("FF8DBE"), sunDisc = Palette.Hex("FFF1C8"), cloudLit = Palette.Hex("FFD6E8"), cloudShade = Palette.Hex("A2448E"),
                cloudCover = 0.55f, stars = 0.4f, sunSize = 0.06f,
                ambientSky = new Color(0.62f, 0.42f, 0.74f), ambientEquator = new Color(1f, 0.66f, 0.8f), ambientGround = new Color(0.42f, 0.27f, 0.45f),
                fogStart = 120f, fogEnd = 690f,
            },
            // Nacht: dunkelblau und dunkellila, Mond statt Sonne, viele Sterne und seltene Sternschnuppen
            new SkyPreset
            {
                id = "nacht", name = "NACHT",
                sunEuler = new Vector3(38f, 230f, 0f), sunLight = new Color(0.55f, 0.62f, 1f), sunIntensity = 0.55f,
                zenith = Palette.Hex("070A24"), mid = Palette.Hex("1A1450"), horizon = Palette.Hex("3A2468"), below = Palette.Hex("120E2C"),
                glow = Palette.Hex("4A3A8C"), sunDisc = Palette.Hex("E8ECFF"), cloudLit = Palette.Hex("4E4A96"), cloudShade = Palette.Hex("1C1646"),
                cloudCover = 0.3f, stars = 1.8f, starHeight = 0.08f, shootingStars = 1f, sunSize = 0.045f, darken = 0.55f,
                ambientSky = new Color(0.16f, 0.17f, 0.38f), ambientEquator = new Color(0.22f, 0.16f, 0.36f), ambientGround = new Color(0.08f, 0.06f, 0.14f),
                fogStart = 90f, fogEnd = 600f,
            },
        };

        /// <summary>Mit dieser Variante wird die City-Szene im Editor gebaut.</summary>
        public static SkyPreset Default => Presets[0];

        public static SkyPreset Find(string id)
        {
            foreach (var p in Presets) if (p.id == id) return p;
            return Default;
        }

        // Kurzformen der Standard-Variante (Szenenbau, Skyline-Dunst, Tests)
        public static Vector3 SunEuler => Default.sunEuler;
        public static Color SunLight => Default.sunLight;
        public static float SunIntensity => Default.sunIntensity;
        public static Color Horizon => Default.horizon;
        /// <summary>Nebel und Skyline-Dunst: gleich dem Horizont, damit ferne Haeuser im Himmel verschwinden.</summary>
        public static Color Haze => Default.horizon;
        public static float FogStart => Default.fogStart;
        public static float FogEnd => Default.fogEnd;
        public static Color AmbientSky => Default.ambientSky;
        public static Color AmbientEquator => Default.ambientEquator;
        public static Color AmbientGround => Default.ambientGround;
        public static Vector3 SunDirection => Default.SunDirection;

        /// <summary>Farben einer Variante in ein Material mit dem Shader DriftSkate/Sky schreiben.</summary>
        public static void SetSkyMaterial(Material mat, SkyPreset p)
        {
            mat.SetColor("_ZenithColor", p.zenith);
            mat.SetColor("_MidColor", p.mid);
            mat.SetColor("_HorizonColor", p.horizon);
            mat.SetColor("_GroundColor", p.below);
            mat.SetColor("_GlowColor", p.glow);
            mat.SetColor("_SunColor", p.sunDisc);
            mat.SetColor("_CloudLit", p.cloudLit);
            mat.SetColor("_CloudShade", p.cloudShade);
            mat.SetFloat("_CloudCover", p.cloudCover);
            mat.SetFloat("_StarStrength", p.stars);
            mat.SetFloat("_SunSize", p.sunSize);
            mat.SetVector("_SunDir", p.SunDirection);
            mat.SetFloat("_ShootingStars", p.shootingStars);
            mat.SetFloat("_StarHeight", p.starHeight);
        }

        /// <summary>Die im Spielstand gewaehlte Variante in der geladenen Stadt einstellen.</summary>
        public static void ApplySelected() => Apply(Find(SaveSystem.Profile != null ? SaveSystem.Profile.sky : null));

        public static void Apply(SkyPreset p)
        {
            Light sun = RenderSettings.sun;
            if (sun == null)
                foreach (var l in Object.FindObjectsByType<Light>(FindObjectsSortMode.None))
                    if (l.type == LightType.Directional) { sun = l; break; }
            if (sun != null)
            {
                sun.transform.rotation = Quaternion.Euler(p.sunEuler);
                sun.color = p.sunLight;
                sun.intensity = p.sunIntensity;
            }
            // Kopie des Himmel-Materials, damit das gespeicherte Asset unveraendert bleibt
            if (RenderSettings.skybox != null && RenderSettings.skybox.shader.name == "DriftSkate/Sky")
            {
                var mat = new Material(RenderSettings.skybox) { name = "Sky_" + p.id };
                SetSkyMaterial(mat, p);
                RenderSettings.skybox = mat;
            }
            RenderSettings.fogColor = p.horizon;
            RenderSettings.fogStartDistance = p.fogStart;
            RenderSettings.fogEndDistance = p.fogEnd;
            RenderSettings.ambientSkyColor = p.ambientSky;
            RenderSettings.ambientEquatorColor = p.ambientEquator;
            RenderSettings.ambientGroundColor = p.ambientGround;
            Shader.SetGlobalFloat("_DS_Darken", p.darken);
            DynamicGI.UpdateEnvironment();
        }
    }
}
