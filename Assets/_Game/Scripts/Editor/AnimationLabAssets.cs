using System;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace DriftSkate.EditorTools
{
    public static class AnimationLabAssets
    {
        public const string ScenePath = "Assets/_Game/Scenes/AnimationLab.unity";

        [MenuItem("DriftSkate/Animation Lab/Testwelt oeffnen")]
        public static void Open()
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            if (System.IO.File.Exists(ScenePath)) EditorSceneManager.OpenScene(ScenePath);
            else Build();
        }

        public static void Build()
        {
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat;
            RenderSettings.ambientLight = new Color(.68f, .73f, .8f);
            RenderSettings.fog = false;
            var light = new GameObject("Sun").AddComponent<Light>();
            light.type = LightType.Directional;
            light.intensity = 1.3f;
            light.shadows = LightShadows.Soft;
            light.transform.rotation = Quaternion.Euler(45, -35, 0);
            var camera = new GameObject("Main Camera").AddComponent<Camera>();
            camera.tag = "MainCamera";
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(.1f, .15f, .22f);
            camera.fieldOfView = 48;
            camera.transform.position = new Vector3(6, 5, -14);
            camera.transform.LookAt(Vector3.zero);
            camera.gameObject.AddComponent<AudioListener>();
            Box("Test plaza", new Vector3(0, -.3f, 0), new Vector3(40, .6f, 60));
            Box("Gentle bank", new Vector3(-10, .5f, 8), new Vector3(6, .5f, 10)).transform.rotation = Quaternion.Euler(-9, 0, 0);
            Box("Low platform", new Vector3(10, .2f, 7), new Vector3(5, .4f, 8));
            foreach (float x in new[] { -20.5f, 20.5f })
                Box("Side boundary", new Vector3(x, .7f, 0), new Vector3(1, 1.4f, 61));
            foreach (float z in new[] { -30.5f, 30.5f })
                Box("End boundary", new Vector3(0, .7f, z), new Vector3(41, 1.4f, 1));
            for (int z = -25; z <= 25; z += 5)
                Box("Distance marker", new Vector3(0, .006f, z), new Vector3(.15f, .01f, 1), false);
            new GameObject("Animation Lab").AddComponent<AnimationLab>();
            EditorSceneManager.SaveScene(scene, ScenePath);
            AssetDatabase.SaveAssets();
            Validate();
        }

        static GameObject Box(string name, Vector3 position, Vector3 scale, bool collision = true)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = name;
            go.transform.position = position;
            go.transform.localScale = scale;
            if (!collision) UnityEngine.Object.DestroyImmediate(go.GetComponent<Collider>());
            return go;
        }

        public static void Validate()
        {
            var go = new GameObject("Rig validation");
            try
            {
                SkaterBuilder.Build(go.transform, SkaterBuilder.Outfit.FromIds(null), Catalog.Boards[0], null, null);
                var rig = go.GetComponentInChildren<SkaterRig>();
                if (rig == null || !rig.Ready) throw new InvalidOperationException("Animation Lab requires the character skeleton.");
                rig.proceduralLife = 1;
                foreach (float dt in new[] { 1f / 30f, 1f / 60f, 1f / 144f })
                {
                    foreach (float walk in new[] { 0f, .5f, 1f })
                    {
                        rig.walk = walk;
                        for (int i = 0; i < 180; i++)
                        {
                            go.transform.position += go.transform.forward * (i < 90 ? 3f : -1f) * dt;
                            go.transform.Rotate(0, 25f * dt, 0);
                            rig.ApplyPose(dt);
                            foreach (var bone in go.GetComponentsInChildren<Transform>())
                            {
                                Vector3 p = bone.position;
                                if (float.IsNaN(p.x) || float.IsInfinity(p.x) || float.IsNaN(p.y) || float.IsInfinity(p.y) || float.IsNaN(p.z) || float.IsInfinity(p.z))
                                    throw new InvalidOperationException("Invalid pose: " + bone.name);
                            }
                        }
                    }
                }
                Debug.Log("ANIMATION LAB PASS: skeleton, walking/riding/transitions, reverse motion, 30/60/144 Hz.");
            }
            finally { UnityEngine.Object.DestroyImmediate(go); }
        }
    }
}
