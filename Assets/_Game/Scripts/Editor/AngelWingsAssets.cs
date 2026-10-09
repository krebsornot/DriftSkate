using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

namespace DriftSkate.EditorTools
{
    public class AngelWingsImport : AssetPostprocessor
    {
        void OnPreprocessModel()
        {
            if (!assetPath.EndsWith("/AngelWings/AngelWings.fbx")) return;
            var importer = (ModelImporter)assetImporter;
            importer.globalScale = 1;
            importer.bakeAxisConversion = true;
            importer.importCameras = importer.importLights = false;
            importer.animationType = ModelImporterAnimationType.Generic;
            importer.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
            importer.importAnimation = true;
            importer.optimizeGameObjects = false;
            importer.importNormals = ModelImporterNormals.Import;
        }
        void OnPreprocessAnimation()
        {
            if (!assetPath.EndsWith("/AngelWings/AngelWings.fbx")) return;
            var importer = (ModelImporter)assetImporter;
            var clips = importer.defaultClipAnimations;
            foreach (var clip in clips) { clip.name = "WingFlap"; clip.loopTime = true; }
            importer.clipAnimations = clips;
        }
    }

    public static class AngelWingsAssets
    {
        const string Root = "Assets/_Game/Accessories/AngelWings";
        [MenuItem("DriftSkate/Angel Wings/Prefab erstellen")]
        public static void Build()
        {
            AssetDatabase.Refresh();
            string path = Root + "/AngelWings.fbx";
            var model = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (model == null) throw new Exception("Angel Wings FBX missing.");
            var clip = AssetDatabase.LoadAllAssetsAtPath(path).OfType<AnimationClip>().FirstOrDefault(c => !c.name.StartsWith("__preview__"));
            if (clip == null || !clip.isLooping) throw new Exception("Looping wing animation missing.");
            var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(Root + "/AngelWings.controller")
                ?? AnimatorController.CreateAnimatorControllerAtPath(Root + "/AngelWings.controller");
            var machine = controller.layers[0].stateMachine;
            var state = machine.states.Length > 0 ? machine.states[0].state : machine.AddState("WingFlap");
            state.motion = clip;
            machine.defaultState = state;
            var instance = UnityEngine.Object.Instantiate(model);
            try
            {
                instance.name = "AngelWings";
                foreach (var renderer in instance.GetComponentsInChildren<SkinnedMeshRenderer>())
                {
                    if (renderer.bones.Any(b => b == null)) throw new Exception("Missing wing bone.");
                    var mats = renderer.sharedMaterials;
                    for (int i = 0; i < mats.Length; i++)
                    {
                        string name = mats[i].name;
                        string matPath = Root + "/" + name + ".mat";
                        var mat = AssetDatabase.LoadAssetAtPath<Material>(matPath);
                        if (mat == null)
                        {
                            mat = new Material(Shader.Find("DriftSkate/Toon")) { name = name };
                            AssetDatabase.CreateAsset(mat, matPath);
                        }
                        mat.SetColor("_BaseColor", name.Contains("Lavender") ? new Color(.62f,.58f,.76f) : name.Contains("Pearl") ? new Color(1,.98f,.92f) : new Color(.94f,.9f,.79f));
                        mat.SetColor("_ShadowTint", new Color(.65f,.59f,.80f));
                        mat.SetFloat("_OutlineWidth", .18f);
                        mat.SetFloat("_OutlineMode", 0);
                        mats[i] = mat;
                    }
                    renderer.sharedMaterials = mats;
                    renderer.localBounds = new Bounds(Vector3.zero, new Vector3(5,4,4));
                }
                var animator = instance.GetComponent<Animator>() ?? instance.AddComponent<Animator>();
                animator.runtimeAnimatorController = controller;
                animator.applyRootMotion = false;
                if (animator.avatar == null || !animator.avatar.isValid) throw new Exception("Wing avatar invalid.");
                var skin = instance.GetComponentInChildren<SkinnedMeshRenderer>();
                if (skin == null) throw new Exception("Wing skin missing.");
                var sample = new Mesh();
                clip.SampleAnimation(instance, 0); skin.BakeMesh(sample); var initial = sample.vertices;
                clip.SampleAnimation(instance, clip.length * .25f); skin.BakeMesh(sample); var moved = sample.vertices;
                if (!initial.Where((v,i) => Vector3.Distance(v,moved[i]) > .05f).Any()) throw new Exception("Wing animation does not deform mesh.");
                UnityEngine.Object.DestroyImmediate(sample);
                clip.SampleAnimation(instance, 0);
                PrefabUtility.SaveAsPrefabAsset(instance, Root + "/AngelWings.prefab");
                Debug.Log("ANGEL WINGS UNITY PASS: skinned mesh, valid Generic avatar, looping clip, visible vertex deformation, Toon materials.");
            }
            finally { UnityEngine.Object.DestroyImmediate(instance); }
            AssetDatabase.SaveAssets();
        }

        public static void BuildAndPreview()
        {
            Build();
            var scene = UnityEditor.SceneManagement.EditorSceneManager.NewPreviewScene();
            var wings = UnityEngine.Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(Root + "/AngelWings.prefab"));
            var cameraObject = new GameObject("Wing preview camera");
            var lightObject = new GameObject("Wing preview light");
            var target = new RenderTexture(1200, 850, 24);
            var pixels = new Texture2D(1200, 850, TextureFormat.RGB24, false);
            var old = RenderTexture.active;
            try
            {
                UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(wings, scene);
                UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(cameraObject, scene);
                UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(lightObject, scene);
                var camera = cameraObject.AddComponent<Camera>();
                camera.scene = scene;
                camera.transform.position = new Vector3(.2f, .6f, -6);
                camera.transform.LookAt(new Vector3(0,.05f,0));
                camera.orthographic = true; camera.orthographicSize = 1.45f;
                camera.clearFlags = CameraClearFlags.SolidColor;
                camera.backgroundColor = new Color(.09f,.12f,.18f);
                var light = lightObject.AddComponent<Light>();
                light.type = LightType.Directional; light.intensity = 1.3f;
                light.transform.rotation = Quaternion.Euler(35,-20,0);
                camera.targetTexture = target; camera.Render(); camera.targetTexture = null;
                RenderTexture.active = target;
                pixels.ReadPixels(new Rect(0,0,1200,850),0,0); pixels.Apply();
                File.WriteAllBytes("Art/Accessories/AngelWings/AngelWings_Unity.png",pixels.EncodeToPNG());
            }
            finally
            {
                RenderTexture.active = old;
                UnityEngine.Object.DestroyImmediate(pixels);
                UnityEngine.Object.DestroyImmediate(target);
                UnityEditor.SceneManagement.EditorSceneManager.ClosePreviewScene(scene);
            }
        }
    }
}
