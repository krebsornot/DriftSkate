using System.IO;
using Unity.Netcode;
using Unity.Netcode.Components;
using Unity.Netcode.Transports.UTP;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace DriftSkate.EditorTools
{
    /// <summary>
    /// Richtet das Projekt ein und erzeugt Prefab und Szenen. Menue: DriftSkate.
    /// Achtung: "Stadt neu generieren" ueberschreibt eigene Aenderungen an der City-Szene.
    /// </summary>
    public static class ProjectBuilder
    {
        public const string Root = "Assets/_Game";
        public const string GaragePath = Root + "/Scenes/Garage.unity";
        public const string CityPath = Root + "/Scenes/City.unity";
        public const string PrefabPath = Root + "/Prefabs/NetworkPlayer.prefab";
        const string ProfilePath = Root + "/Generated/PostFX.asset";

        [MenuItem("DriftSkate/Projekt einrichten (fehlende Teile bauen)")]
        public static void SetupMissing() => Build(false);

        [MenuItem("DriftSkate/Alles neu bauen (ueberschreibt Szenen!)")]
        public static void RebuildEverything()
        {
            if (Application.isBatchMode || EditorUtility.DisplayDialog("Alles neu bauen?", "Garage, Stadt und Spieler-Prefab werden neu erzeugt. Eigene Aenderungen an diesen Szenen gehen verloren.", "Neu bauen", "Abbrechen"))
                Build(true);
        }

        /// <summary>Einstiegspunkt fuer die Kommandozeile (-executeMethod).</summary>
        public static void BatchBuild() => Build(true);

        public const string PlayerBuildPath = "Builds/DriftSkate/DriftSkate.exe";

        [MenuItem("DriftSkate/Windows-Build erstellen")]
        public static void BuildWindowsPlayer()
        {
            // Im Batch-Modus optional woandershin bauen (-buildpath Builds/NetTest/DriftSkate.exe), z. B. wenn das Spiel gerade laeuft
            string path = PlayerBuildPath;
            var args = System.Environment.GetCommandLineArgs();
            int i = System.Array.IndexOf(args, "-buildpath");
            if (i >= 0 && i + 1 < args.Length) path = args[i + 1];
            SetBuildScenes();
            var options = new BuildPlayerOptions
            {
                scenes = new[] { GaragePath, CityPath },
                locationPathName = path,
                target = BuildTarget.StandaloneWindows64,
                options = BuildOptions.None
            };
            var report = BuildPipeline.BuildPlayer(options);
            // Mods-Ordner neben die Exe kopieren (Beispiel-Mods und eigene)
            string modsSource = Path.Combine(Path.GetDirectoryName(Application.dataPath), "Mods");
            string modsTarget = Path.Combine(Path.GetDirectoryName(path), "Mods");
            if (Directory.Exists(modsSource)) CopyDirectory(modsSource, modsTarget);
            Debug.Log($"[DriftSkate] Build: {report.summary.result}, {report.summary.totalSize / (1024 * 1024)} MB, Fehler: {report.summary.totalErrors} -> {path}");
            if (Application.isBatchMode && report.summary.result != UnityEditor.Build.Reporting.BuildResult.Succeeded) EditorApplication.Exit(1);
        }

        static void CopyDirectory(string from, string to)
        {
            Directory.CreateDirectory(to);
            foreach (var f in Directory.GetFiles(from)) File.Copy(f, Path.Combine(to, Path.GetFileName(f)), true);
            foreach (var d in Directory.GetDirectories(from)) CopyDirectory(d, Path.Combine(to, Path.GetFileName(d)));
        }

        [MenuItem("DriftSkate/Stadt neu generieren (ueberschreibt City!)")]
        public static void RebuildCity()
        {
            if (!Application.isBatchMode && !EditorUtility.DisplayDialog("Stadt neu generieren?", "Die City-Szene wird komplett neu erzeugt.", "Neu generieren", "Abbrechen")) return;
            SetupLayers();
            WithHooks(() =>
            {
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath) ?? BuildPlayerPrefab();
                BuildCityScene(prefab);
            });
        }

        static void Build(bool force)
        {
            EnsureFolders();
            SetupLayers();
            SetupProjectSettings();
            WithHooks(() =>
            {
                CreateResources();
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
                if (force || prefab == null) prefab = BuildPlayerPrefab();
                if (force || !File.Exists(GaragePath)) BuildGarageScene();
                if (force || !File.Exists(CityPath)) BuildCityScene(prefab);
            });
            SetBuildScenes();
            if (File.Exists("Assets/Scenes/SampleScene.unity")) AssetDatabase.DeleteAsset("Assets/Scenes/SampleScene.unity");
            if (AssetDatabase.IsValidFolder("Assets/Scenes") && Directory.GetFiles("Assets/Scenes").Length == 0) AssetDatabase.DeleteAsset("Assets/Scenes");
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            if (!Application.isBatchMode) EditorSceneManager.OpenScene(GaragePath);
            Debug.Log("[DriftSkate] Projekt gebaut.");
        }

        // ------------------------------------------------------------------ Hooks: Materialien/Meshes als Assets speichern

        static void WithHooks(System.Action action)
        {
            ToonMaterials.ClearCache();
            SurfaceTextures.ClearCache();
            ToonMaterials.PersistHook = (mat, key) => PersistAsset(mat, $"{Root}/Generated/Materials/Toon_{key}.mat");
            MeshStore.PersistHook = (mesh, key) => PersistAsset(mesh, $"{Root}/Generated/Meshes/{key}.asset");
            TextureStore.PersistHook = PersistTexture;
            try { action(); }
            finally
            {
                ToonMaterials.PersistHook = null;
                MeshStore.PersistHook = null;
                TextureStore.PersistHook = null;
                ToonMaterials.ClearCache();
                SurfaceTextures.ClearCache();
            }
        }

        /// <summary>Prozedurale Textur als PNG speichern: kachelbar, mit Mipmaps; Struktur-Texturen linear, Farben sRGB.</summary>
        static Texture2D PersistTexture(Texture2D tex, string key, bool linear)
        {
            string path = $"{Root}/Generated/Textures/{key}.png";
            File.WriteAllBytes(path, tex.EncodeToPNG());
            Object.DestroyImmediate(tex);
            AssetDatabase.ImportAsset(path);
            var importer = (TextureImporter)AssetImporter.GetAtPath(path);
            bool decal = key.StartsWith("sign_"); // Schriftzug mit Alpha-Cutout, nicht gekachelt
            importer.sRGBTexture = !linear;
            importer.wrapMode = decal ? TextureWrapMode.Clamp : TextureWrapMode.Repeat;
            importer.alphaIsTransparency = decal;
            importer.mipMapsPreserveCoverage = decal;
            importer.alphaTestReferenceValue = 0.5f;
            importer.mipmapEnabled = true;
            importer.filterMode = FilterMode.Trilinear;
            importer.anisoLevel = 8;
            importer.textureCompression = TextureImporterCompression.CompressedHQ;
            importer.SaveAndReimport();
            return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        }

        static T PersistAsset<T>(T obj, string path) where T : Object
        {
            var existing = AssetDatabase.LoadAssetAtPath<T>(path);
            if (existing != null)
            {
                EditorUtility.CopySerialized(obj, existing);
                existing.name = Path.GetFileNameWithoutExtension(path);
                EditorUtility.SetDirty(existing);
                return existing;
            }
            AssetDatabase.CreateAsset(obj, path);
            return obj;
        }

        // ------------------------------------------------------------------ Einstellungen

        static void EnsureFolders()
        {
            foreach (var f in new[] { "Scenes", "Prefabs", "Resources", "Generated", "Generated/Materials", "Generated/Meshes", "Generated/Textures" })
            {
                string path = Root + "/" + f;
                if (!AssetDatabase.IsValidFolder(path))
                {
                    string parent = Path.GetDirectoryName(path).Replace('\\', '/');
                    AssetDatabase.CreateFolder(parent, Path.GetFileName(path));
                }
            }
        }

        static void SetupLayers()
        {
            var tagManager = new SerializedObject(AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/TagManager.asset")[0]);
            var layers = tagManager.FindProperty("layers");
            void Set(int index, string name)
            {
                var p = layers.GetArrayElementAtIndex(index);
                if (string.IsNullOrEmpty(p.stringValue) || p.stringValue == name) p.stringValue = name;
                else Debug.LogWarning($"[DriftSkate] Layer {index} ist schon belegt mit '{p.stringValue}'.");
            }
            Set(8, "Car");
            Set(9, "Skater");
            Set(10, "Rail");
            Set(11, "Detail"); // kleine Deko, wird von der Kamera nur in der Naehe gezeichnet (CameraRig)
            tagManager.ApplyModifiedProperties();
            // Gelaender sind fest: Skater prallen ab wie Autos (gegrindet wird per Knopf, siehe SkaterController)
            Physics.IgnoreLayerCollision(9, 10, false);
        }

        static void SetupProjectSettings()
        {
            PlayerSettings.companyName = "DriftSkate Crew";
            PlayerSettings.productName = "DriftSkate";
            PlayerSettings.runInBackground = true;
            PlayerSettings.defaultScreenWidth = 1920;
            PlayerSettings.defaultScreenHeight = 1080;
            PlayerSettings.fullScreenMode = FullScreenMode.Windowed;
            PlayerSettings.resizableWindow = true;

            foreach (var path in new[] { "Assets/Settings/PC_RPAsset.asset", "Assets/Settings/Mobile_RPAsset.asset" })
            {
                var urp = AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>(path);
                if (urp == null) continue;
                urp.shadowDistance = 130f;
                EditorUtility.SetDirty(urp);
            }
            // SSAO passt nicht zum flachen Toon-Look
            foreach (var path in new[] { "Assets/Settings/PC_Renderer.asset", "Assets/Settings/Mobile_Renderer.asset" })
            {
                var rd = AssetDatabase.LoadAssetAtPath<UniversalRendererData>(path);
                if (rd == null) continue;
                foreach (var f in rd.rendererFeatures)
                    if (f != null && f.GetType().Name.Contains("AmbientOcclusion")) f.SetActive(false);
                EditorUtility.SetDirty(rd);
            }
        }

        static void SetBuildScenes()
        {
            EditorBuildSettings.scenes = new[]
            {
                new EditorBuildSettingsScene(GaragePath, true),
                new EditorBuildSettingsScene(CityPath, true),
            };
        }

        // ------------------------------------------------------------------ Resources (zur Laufzeit geladen)

        static void CreateResources()
        {
            var toonShader = Shader.Find(ToonMaterials.ShaderName);
            var fxShader = Shader.Find("DriftSkate/FX");
            if (toonShader == null || fxShader == null) Debug.LogError("[DriftSkate] Shader nicht gefunden. Wurden sie importiert?");

            PersistAsset(new Material(toonShader) { name = "ToonShaderRef" }, Root + "/Resources/ToonShaderRef.mat");

            var smokeTex = CreateSmokeTexture();
            var smoke = new Material(fxShader) { name = "SmokeMat" };
            smoke.SetTexture("_BaseMap", smokeTex);
            PersistAsset(smoke, Root + "/Resources/SmokeMat.mat");

            var trail = new Material(fxShader) { name = "TrailMat" };
            PersistAsset(trail, Root + "/Resources/TrailMat.mat");
        }

        static Texture2D CreateSmokeTexture()
        {
            string path = Root + "/Generated/Textures/SmokePuff.png";
            const int size = 64;
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
            Vector2 c = new Vector2(size / 2f - 0.5f, size / 2f - 0.5f);
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float d = Vector2.Distance(new Vector2(x, y), c) / (size * 0.5f);
                    float alpha = Mathf.Clamp01((1f - d) * 18f);
                    // Cartoon-Wolke: helle Flaeche, dunklerer Rand, kleine Glanzstelle
                    float shade = d > 0.82f ? 0.72f : 1f;
                    if (Vector2.Distance(new Vector2(x, y), c + new Vector2(-9, 9)) < 7f) shade = 1f;
                    else if (d < 0.82f && (y - c.y) < -(size * 0.18f)) shade = 0.88f;
                    tex.SetPixel(x, y, new Color(shade, shade, shade * 1.02f, alpha));
                }
            }
            tex.Apply();
            File.WriteAllBytes(path, tex.EncodeToPNG());
            AssetDatabase.ImportAsset(path);
            var importer = (TextureImporter)AssetImporter.GetAtPath(path);
            importer.alphaIsTransparency = true;
            importer.wrapMode = TextureWrapMode.Clamp;
            importer.mipmapEnabled = false;
            importer.SaveAndReimport();
            return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        }

        static VolumeProfile GetOrCreateProfile()
        {
            var profile = AssetDatabase.LoadAssetAtPath<VolumeProfile>(ProfilePath);
            if (profile != null) return profile;
            profile = ScriptableObject.CreateInstance<VolumeProfile>();
            AssetDatabase.CreateAsset(profile, ProfilePath);

            var bloom = profile.Add<Bloom>(true);
            bloom.intensity.Override(0.55f);
            bloom.threshold.Override(1.05f);
            bloom.scatter.Override(0.6f);
            var color = profile.Add<ColorAdjustments>(true);
            color.saturation.Override(18f);
            color.contrast.Override(10f);
            color.postExposure.Override(0.05f);
            var tone = profile.Add<Tonemapping>(true);
            tone.mode.Override(TonemappingMode.Neutral);
            var vignette = profile.Add<Vignette>(true);
            vignette.intensity.Override(0.2f);
            foreach (var comp in profile.components) AssetDatabase.AddObjectToAsset(comp, profile);
            EditorUtility.SetDirty(profile);
            AssetDatabase.SaveAssets();
            return profile;
        }

        // ------------------------------------------------------------------ Spieler-Prefab

        static GameObject BuildPlayerPrefab()
        {
            int carLayer = LayerMask.NameToLayer("Car"), skaterLayer = LayerMask.NameToLayer("Skater");

            var root = new GameObject("NetworkPlayer");
            root.AddComponent<NetworkObject>();
            var avatar = root.AddComponent<PlayerAvatar>();
            var combo = root.AddComponent<ComboSystem>();

            var car = new GameObject("Car");
            car.transform.SetParent(root.transform, false);
            car.layer = carLayer;
            var carRb = car.AddComponent<Rigidbody>();
            carRb.mass = 1250f;
            car.AddComponent<BoxCollider>();
            var vc = car.AddComponent<VehicleController>();
            var fx = car.AddComponent<CarEffects>();
            var scorer = car.AddComponent<DriftScorer>();
            ConfigureTransform(car.AddComponent<NetworkTransform>(), false);

            var skater = new GameObject("Skater");
            skater.transform.SetParent(root.transform, false);
            skater.layer = skaterLayer;
            var skRb = skater.AddComponent<Rigidbody>();
            skRb.mass = 75f;
            skRb.freezeRotation = true;
            skRb.useGravity = false;
            var cap = skater.AddComponent<CapsuleCollider>();
            cap.center = new Vector3(0, 0.95f, 0);
            cap.height = 1.5f;
            cap.radius = 0.28f;
            cap.sharedMaterial = PersistAsset(new PhysicsMaterial("SkaterPhysics") { dynamicFriction = 0f, staticFriction = 0f, frictionCombine = PhysicsMaterialCombine.Minimum },
                Root + "/Generated/SkaterPhysics.physicMaterial");
            var sc = skater.AddComponent<SkaterController>();
            ConfigureTransform(skater.AddComponent<NetworkTransform>(), false);

            var align = new GameObject("Align");
            align.transform.SetParent(skater.transform, false);
            var boardPivot = new GameObject("BoardPivot");
            boardPivot.transform.SetParent(align.transform, false);
            boardPivot.transform.localPosition = new Vector3(0, 0.1f, 0);
            ConfigureTransform(boardPivot.AddComponent<NetworkTransform>(), true);

            avatar.car = vc;
            avatar.carEffects = fx;
            avatar.driftScorer = scorer;
            avatar.skater = sc;
            avatar.combo = combo;

            var prefab = PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
            Object.DestroyImmediate(root);
            return prefab;
        }

        static void ConfigureTransform(NetworkTransform nt, bool localSpace)
        {
            nt.AuthorityMode = NetworkTransform.AuthorityModes.Owner;
            nt.InLocalSpace = localSpace;
            nt.SyncScaleX = nt.SyncScaleY = nt.SyncScaleZ = false;
            nt.Interpolate = true;
        }

        // ------------------------------------------------------------------ Szenen

        static Light CreateSun(Vector3 euler, float intensity, Color color)
        {
            var go = new GameObject("Sun");
            var light = go.AddComponent<Light>();
            light.type = LightType.Directional;
            light.intensity = intensity;
            light.color = color;
            light.shadows = LightShadows.Soft;
            light.shadowStrength = 1f;
            go.transform.rotation = Quaternion.Euler(euler);
            return light;
        }

        static Camera CreateCamera(Color background, bool skybox)
        {
            var go = new GameObject("Main Camera") { tag = "MainCamera" };
            var cam = go.AddComponent<Camera>();
            cam.clearFlags = skybox ? CameraClearFlags.Skybox : CameraClearFlags.SolidColor;
            cam.backgroundColor = background;
            cam.nearClipPlane = 0.1f;
            cam.farClipPlane = 1100f;
            cam.fieldOfView = 62f;
            go.AddComponent<AudioListener>();
            var data = cam.GetUniversalAdditionalCameraData();
            data.renderPostProcessing = true;
            data.antialiasing = AntialiasingMode.SubpixelMorphologicalAntiAliasing;
            return cam;
        }

        static void CreateVolume(VolumeProfile profile = null)
        {
            var go = new GameObject("PostFX");
            var vol = go.AddComponent<Volume>();
            vol.isGlobal = true;
            vol.sharedProfile = profile != null ? profile : GetOrCreateProfile();
        }

        /// <summary>
        /// Post-Processing der Stadt am Abend (eigenes Profil, die Garage behaelt ihres): staerkerer Bloom fuer Laternen,
        /// Neon und Fenster, warme Lichter und kuehle lila Schatten (Split-Toning).
        /// </summary>
        static VolumeProfile CreateCityProfile()
        {
            const string path = Root + "/Generated/PostFX_City.asset";
            if (AssetDatabase.LoadAssetAtPath<VolumeProfile>(path) != null) AssetDatabase.DeleteAsset(path);
            var profile = ScriptableObject.CreateInstance<VolumeProfile>();
            AssetDatabase.CreateAsset(profile, path);

            var bloom = profile.Add<Bloom>(true);
            bloom.intensity.Override(0.9f);
            bloom.threshold.Override(0.95f);
            bloom.scatter.Override(0.68f);
            bloom.tint.Override(new Color(1f, 0.86f, 0.78f));
            var color = profile.Add<ColorAdjustments>(true);
            color.saturation.Override(16f);
            color.contrast.Override(12f);
            color.postExposure.Override(0.05f);
            var white = profile.Add<WhiteBalance>(true);
            white.temperature.Override(5f);
            white.tint.Override(3f);
            var split = profile.Add<SplitToning>(true);
            split.shadows.Override(new Color(0.42f, 0.36f, 0.72f));
            split.highlights.Override(new Color(1f, 0.84f, 0.68f));
            split.balance.Override(-10f);
            var tone = profile.Add<Tonemapping>(true);
            tone.mode.Override(TonemappingMode.Neutral);
            // Keine Vignette: der dunkle Rand wirkte auf breiten Monitoren wie ein runder Schatten um den Spieler
            foreach (var comp in profile.components) AssetDatabase.AddObjectToAsset(comp, profile);
            EditorUtility.SetDirty(profile);
            AssetDatabase.SaveAssets();
            return profile;
        }

        /// <summary>Toon-Abendhimmel (Shader DriftSkate/Sky) mit den Farben aus SkyLook.</summary>
        static Material CreateSkybox()
        {
            var mat = new Material(Shader.Find("DriftSkate/Sky")) { name = "Sky" };
            SkyLook.SetSkyMaterial(mat, SkyLook.Default);
            return PersistAsset(mat, Root + "/Generated/Sky.mat");
        }

        static void BuildCityScene(GameObject playerPrefab)
        {
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            // Abendstimmung (Farben in SkyLook): tiefe, warme Sonne im Westen, lange lila Schatten, Dunst wie der Horizont
            var sun = CreateSun(SkyLook.SunEuler, SkyLook.SunIntensity, SkyLook.SunLight);
            RenderSettings.sun = sun;
            RenderSettings.skybox = CreateSkybox();
            RenderSettings.ambientMode = AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = SkyLook.AmbientSky;
            RenderSettings.ambientEquatorColor = SkyLook.AmbientEquator;
            RenderSettings.ambientGroundColor = SkyLook.AmbientGround;
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.Linear;
            RenderSettings.fogColor = SkyLook.Haze;
            RenderSettings.fogStartDistance = SkyLook.FogStart;
            RenderSettings.fogEndDistance = SkyLook.FogEnd;

            var cam = CreateCamera(SkyLook.Horizon, true);
            cam.gameObject.AddComponent<CameraRig>();
            cam.transform.position = new Vector3(0, 40, -120);
            cam.transform.LookAt(Vector3.zero);
            CreateVolume(CreateCityProfile());

            var city = new GameObject("City");
            new CityBuilder().Build(city.transform);

            var nmGo = new GameObject("NetworkManager");
            var nm = nmGo.AddComponent<NetworkManager>();
            var utp = nmGo.AddComponent<UnityTransport>();
            if (nm.NetworkConfig == null) nm.NetworkConfig = new NetworkConfig();
            nm.NetworkConfig.NetworkTransport = utp;
            nm.NetworkConfig.PlayerPrefab = playerPrefab;
            nm.NetworkConfig.EnableSceneManagement = false;
            nm.NetworkConfig.ConnectionApproval = false;

            new GameObject("CityBootstrap").AddComponent<CityBootstrap>();
            new GameObject("HUD").AddComponent<HUD>();

            EditorSceneManager.SaveScene(scene, CityPath);
        }

        static void BuildGarageScene()
        {
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var sun = CreateSun(new Vector3(52f, 150f, 0f), 1.1f, new Color(1f, 0.93f, 0.9f));
            RenderSettings.sun = sun;
            RenderSettings.skybox = null;
            RenderSettings.ambientMode = AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = new Color(0.6f, 0.55f, 0.8f);
            RenderSettings.ambientEquatorColor = new Color(0.55f, 0.45f, 0.6f);
            RenderSettings.ambientGroundColor = new Color(0.3f, 0.25f, 0.35f);
            RenderSettings.fog = false;

            var cam = CreateCamera(Palette.Hex("1E1830"), false);
            cam.gameObject.AddComponent<OrbitCamera>();
            CreateVolume();

            new GameObject("GarageEnvironment").AddComponent<GarageEnvironment>();
            new GameObject("GarageMenu").AddComponent<GarageMenu>();
            EditorSceneManager.SaveScene(scene, GaragePath);
        }
    }
}
