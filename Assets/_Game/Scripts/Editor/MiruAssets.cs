using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace DriftSkate.EditorTools
{
    public static class MiruAssets
    {
        const string ModelPath = "Assets/_Game/Resources/Characters/NPC_Miru.fbx";
        const string Root = "Assets/_Game/Characters/Miru";
        [MenuItem("DriftSkate/Miru/Prefab und Materialien aktualisieren")]
        public static void Build()
        {
            Directory.CreateDirectory(Root + "/Materials");
            AssetDatabase.Refresh();
            var model = AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath);
            if (model == null) throw new InvalidOperationException("Miru FBX missing");
            var clips = AssetDatabase.LoadAllAssetsAtPath(ModelPath).OfType<AnimationClip>().Where(c => !c.name.StartsWith("__preview__")).ToArray();
            var idle = clips.FirstOrDefault(c => c.name == "Idle" || c.name.EndsWith("|Idle"));
            if (idle == null) throw new InvalidOperationException("Miru Idle clip missing: " + string.Join(",", clips.Select(c => c.name)));
            var sitting = clips.FirstOrDefault(c => c.name == "Sitting");
            if (sitting == null || !sitting.isLooping) throw new InvalidOperationException("Miru looping Sitting clip missing");
            var avatar = AssetDatabase.LoadAllAssetsAtPath(ModelPath).OfType<Avatar>().FirstOrDefault();
            if (avatar == null || !avatar.isValid || !avatar.isHuman) throw new InvalidOperationException("Miru Humanoid avatar invalid");
            var controllerPath = Root + "/Miru.controller";
            var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(controllerPath);
            if (controller == null)
            {
                controller = AnimatorController.CreateAnimatorControllerAtPath(controllerPath);
                var state = controller.layers[0].stateMachine.AddState("Idle");
                state.motion = idle;
                controller.layers[0].stateMachine.defaultState = state;
            }
            var machine = controller.layers[0].stateMachine;
            var idleState = machine.states.First(s => s.state.name == "Idle").state;
            idleState.motion = idle;
            var sitState = machine.states.Select(s => s.state).FirstOrDefault(s => s.name == "Sitting") ?? machine.AddState("Sitting");
            sitState.motion = sitting;
            if (!controller.parameters.Any(p => p.name == "IsSitting")) controller.AddParameter("IsSitting",AnimatorControllerParameterType.Bool);
            Connect(idleState,sitState,AnimatorConditionMode.If);
            Connect(sitState,idleState,AnimatorConditionMode.IfNot);
            var prefabPath = Root + "/Miru.prefab";
            // Rebuild our generated prefab after mesh/material-slot changes; keep its GUID.
            {
                var root = new GameObject("Miru");
                try
                {
                    var instance = (GameObject)PrefabUtility.InstantiatePrefab(model);
                    instance.transform.SetParent(root.transform,false);
                    instance.name = "Miru Model";
                    instance.transform.localRotation = Quaternion.Euler(0,180,0);
                    foreach (var renderer in instance.GetComponentsInChildren<SkinnedMeshRenderer>())
                    {
                        var mats = renderer.sharedMaterials;
                        for (int i=0;i<mats.Length;i++) mats[i] = PersistMaterial(mats[i]);
                        renderer.sharedMaterials = mats;
                        var bounds = renderer.localBounds; bounds.Expand(.5f); renderer.localBounds = bounds;
                        if (renderer.bones.Any(b => b == null)) throw new InvalidOperationException("Miru missing skin bone");
                    }
                    var animator = instance.GetComponent<Animator>();
                    if (animator == null) animator = instance.AddComponent<Animator>();
                    animator.avatar = avatar; animator.runtimeAnimatorController = controller; animator.applyRootMotion = false;
                    var collider = root.AddComponent<CapsuleCollider>();
                    collider.height = 1.76f; collider.radius = .24f; collider.center = new Vector3(0,.88f,0);
                    if (PrefabUtility.SaveAsPrefabAsset(root,prefabPath) == null) throw new InvalidOperationException("Miru prefab save failed");
                }
                finally { UnityEngine.Object.DestroyImmediate(root); }
            }
            var editable = PrefabUtility.LoadPrefabContents(prefabPath);
            try { editable.transform.GetChild(0).localRotation = Quaternion.Euler(0,180,0); PrefabUtility.SaveAsPrefabAsset(editable,prefabPath); }
            finally { PrefabUtility.UnloadPrefabContents(editable); }
            AssetDatabase.SaveAssets();
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
            if (prefab == null || prefab.GetComponent<CapsuleCollider>() == null) throw new InvalidOperationException("Miru prefab invalid");
            EnsureRuntimeLink(prefab);
            var sitControllerPath = Root + "/Miru_Sitting.controller";
            var sitController = AssetDatabase.LoadAssetAtPath<AnimatorController>(sitControllerPath) ?? AnimatorController.CreateAnimatorControllerAtPath(sitControllerPath);
            var sitMachine = sitController.layers[0].stateMachine;
            var sitDefault = sitMachine.states.Select(s => s.state).FirstOrDefault(s => s.name == "Sitting") ?? sitMachine.AddState("Sitting");
            sitDefault.motion = sitting; sitMachine.defaultState = sitDefault;
            var seatedInstance = UnityEngine.Object.Instantiate(prefab);
            try
            {
                seatedInstance.name = "Miru_Sitting";
                seatedInstance.GetComponentInChildren<Animator>().runtimeAnimatorController = sitController;
                var capsule = seatedInstance.GetComponent<CapsuleCollider>();
                capsule.height = 1.4f; capsule.center = new Vector3(0,.70f,.12f); capsule.radius = .30f;
                if (PrefabUtility.SaveAsPrefabAsset(seatedInstance,Root + "/Miru_Sitting.prefab") == null) throw new InvalidOperationException("Sitting prefab save failed");
            }
            finally { UnityEngine.Object.DestroyImmediate(seatedInstance); }
            AssetDatabase.SaveAssets();
            Debug.Log("[Miru] Sitting validated: duration="+sitting.length+", looping="+sitting.isLooping+", IsSitting transitions configured");
            ValidateSittingClip(prefab,sitting);
            Debug.Log("[Miru] Valid Humanoid avatar; Idle duration=" + idle.length + "; renderers=" + prefab.GetComponentsInChildren<SkinnedMeshRenderer>().Length + "; prefab=" + prefabPath);
            RenderPreview(prefab,idle);
            RenderPreview(prefab,sitting,true);
        }
        static void Connect(AnimatorState from, AnimatorState to, AnimatorConditionMode mode)
        {
            if (from.transitions.Any(t => t.destinationState == to)) return;
            var transition = from.AddTransition(to);
            transition.hasExitTime = false; transition.hasFixedDuration = true; transition.duration = .35f;
            transition.AddCondition(mode,0,"IsSitting");
        }
        static void ValidateSittingClip(GameObject prefab, AnimationClip clip)
        {
            var instance=UnityEngine.Object.Instantiate(prefab);
            try
            {
                var animator=instance.GetComponentInChildren<Animator>();
                animator.Rebind(); clip.SampleAnimation(animator.gameObject,0);
                var bones=instance.GetComponentsInChildren<Transform>();
                var positions=bones.Select(b=>b.localPosition).ToArray();
                var rotations=bones.Select(b=>b.localRotation).ToArray();
                var left=animator.GetBoneTransform(HumanBodyBones.LeftFoot);
                var right=animator.GetBoneTransform(HumanBodyBones.RightFoot);
                var head=animator.GetBoneTransform(HumanBodyBones.Head);
                var leftStart=left.position; var rightStart=right.position; var headStart=head.rotation;
                float movement=0,footDrift=0;
                for(int i=1;i<=4;i++)
                {
                    clip.SampleAnimation(animator.gameObject,clip.length*i/4f);
                    movement=Mathf.Max(movement,Quaternion.Angle(headStart,head.rotation));
                    footDrift=Mathf.Max(footDrift,Vector3.Distance(leftStart,left.position),Vector3.Distance(rightStart,right.position));
                }
                float seam=0;
                for(int i=0;i<bones.Length;i++)
                {
                    if(Vector3.Distance(positions[i],bones[i].localPosition)>.002f) throw new InvalidOperationException("Sitting position seam: "+bones[i].name);
                    seam=Mathf.Max(seam,Quaternion.Angle(rotations[i],bones[i].localRotation));
                }
                if(seam>.5f || movement<.1f || footDrift>.02f) throw new InvalidOperationException("Sitting motion validation failed");
                Debug.Log($"[Miru] Sitting samples: seam={seam:F4} degrees, head movement={movement:F3} degrees, foot drift={footDrift:F5} m");
            }
            finally { UnityEngine.Object.DestroyImmediate(instance); }
        }
        /// <summary>Resources-Verweis aufs Prefab, damit ParkMiru es im Spiel laden kann.</summary>
        public static void EnsureRuntimeLink(GameObject prefab = null)
        {
            const string linkPath = "Assets/_Game/Resources/Characters/NpcPrefabs.asset";
            if (prefab == null) prefab = AssetDatabase.LoadAssetAtPath<GameObject>(Root + "/Miru.prefab");
            var link = AssetDatabase.LoadAssetAtPath<NpcPrefabs>(linkPath);
            if (link == null) { link = ScriptableObject.CreateInstance<NpcPrefabs>(); AssetDatabase.CreateAsset(link,linkPath); }
            if (link.miru == prefab) return;
            link.miru = prefab; EditorUtility.SetDirty(link); AssetDatabase.SaveAssets();
        }
        static Material PersistMaterial(Material source)
        {
            string name = source.name;
            string path = Root + "/Materials/" + name + ".mat";
            var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            var tag = Regex.Match(name,"__([0-9A-Fa-f]{6})");
            if (!tag.Success || !ColorUtility.TryParseHtmlString("#"+tag.Groups[1].Value,out var color)) throw new InvalidOperationException("Miru material color missing: " + name);
            var shader = Shader.Find("DriftSkate/Toon");
            if (shader == null) throw new InvalidOperationException("Toon shader missing");
            bool isNew = mat == null;
            if (isNew) mat = new Material(shader) { name = name, enableInstancing = true };
            mat.shader = shader;
            mat.SetColor("_BaseColor",color);
            bool flat = name.Contains("_flat") || name.Contains("_Skin__") || name.Contains("_Lips__") || name.Contains("_MouthIn__");
            mat.SetFloat("_OutlineWidth",flat ? 0 : .055f); mat.SetFloat("_OutlineMode",0);
            mat.SetFloat("_Softness",.18f);
            mat.SetFloat("_Threshold",-.05f);
            mat.SetColor("_ShadowTint",new Color(.72f,.69f,.81f));
            mat.SetFloat("_RimStrength",.045f);
            mat.SetFloat("_Emission",name.Contains("_glow") ? 2f : 0);
            mat.SetShaderPassEnabled("SRPDefaultUnlit",!flat);
            if (isNew) AssetDatabase.CreateAsset(mat,path);
            else EditorUtility.SetDirty(mat);
            return mat;
        }
        static void RenderPreview(GameObject prefab, AnimationClip idle, bool seated = false)
        {
            var scene = EditorSceneManager.NewPreviewScene();
            GameObject instance = null;
            RenderTexture target = null;
            Texture2D pixels = null;
            try
            {
                instance = UnityEngine.Object.Instantiate(prefab);
                SceneManager.MoveGameObjectToScene(instance,scene);
                var animator = instance.GetComponentInChildren<Animator>();
                idle.SampleAnimation(animator.gameObject,0);
                var lightObject = new GameObject("Preview light"); SceneManager.MoveGameObjectToScene(lightObject,scene);
                var light = lightObject.AddComponent<Light>(); light.type=LightType.Directional; light.intensity=1.4f;
                lightObject.transform.rotation=Quaternion.Euler(35,145,0);
                var cameraObject = new GameObject("Preview camera"); SceneManager.MoveGameObjectToScene(cameraObject,scene);
                var camera=cameraObject.AddComponent<Camera>(); camera.scene=scene;
                camera.transform.position=new Vector3(1.6f,1.6f,4.4f); camera.transform.LookAt(new Vector3(0,.92f,0));
                if (seated)
                {
                    camera.transform.position=new Vector3(2f,1.55f,3.25f); camera.transform.LookAt(new Vector3(0,.72f,.12f));
                    var seat=GameObject.CreatePrimitive(PrimitiveType.Cube); SceneManager.MoveGameObjectToScene(seat,scene);
                    seat.name="Preview seat (45 cm)"; seat.transform.position=new Vector3(0,.415f,-.10f); seat.transform.localScale=new Vector3(.78f,.07f,.54f);
                    seat.GetComponent<Renderer>().sharedMaterial=AssetDatabase.LoadAssetAtPath<Material>(Root+"/Materials/Miru_JacketTrim__292B39.mat");
                }
                camera.clearFlags=CameraClearFlags.SolidColor; camera.backgroundColor=new Color(.13f,.14f,.18f);
                camera.fieldOfView=29; camera.nearClipPlane=.05f;
                target = new RenderTexture(1200,1500,24) { antiAliasing=4 }; camera.allowMSAA=true; camera.targetTexture=target; camera.Render(); camera.targetTexture=null;
                var old=RenderTexture.active;
                try { RenderTexture.active=target; pixels=new Texture2D(1200,1500,TextureFormat.RGB24,false); pixels.ReadPixels(new Rect(0,0,1200,1500),0,0); pixels.Apply(); }
                finally { RenderTexture.active=old; }
                File.WriteAllBytes(seated ? "Art/Characters/Miru/Miru_sitting_Unity.png" : "Art/Characters/Miru/Miru_Unity.png",pixels.EncodeToPNG());
                if (seated)
                {
                    Directory.CreateDirectory("Logs/MiruSittingFrames");
                    for(int frame=0;frame<48;frame++)
                    {
                        idle.SampleAnimation(animator.gameObject,frame/12f);
                        camera.targetTexture=target; camera.Render(); camera.targetTexture=null;
                        old=RenderTexture.active;
                        try { RenderTexture.active=target; pixels.ReadPixels(new Rect(0,0,1200,1500),0,0); pixels.Apply(); }
                        finally { RenderTexture.active=old; }
                        File.WriteAllBytes($"Logs/MiruSittingFrames/frame_{frame:D3}.png",pixels.EncodeToPNG());
                    }
                    return;
                }
                camera.transform.position=new Vector3(.24f,1.70f,.78f);
                camera.transform.LookAt(new Vector3(0,1.60f,0)); camera.fieldOfView=33;
                camera.targetTexture=target; camera.Render(); camera.targetTexture=null;
                old=RenderTexture.active;
                try { RenderTexture.active=target; pixels.ReadPixels(new Rect(0,0,1200,1500),0,0); pixels.Apply(); }
                finally { RenderTexture.active=old; }
                File.WriteAllBytes("Art/Characters/Miru/Miru_Unity_face.png",pixels.EncodeToPNG());
                Debug.Log("[Miru] Unity Toon preview rendered: Art/Characters/Miru/Miru_Unity.png");
            }
            finally
            {
                if (pixels != null) UnityEngine.Object.DestroyImmediate(pixels);
                if (target != null) { target.Release(); UnityEngine.Object.DestroyImmediate(target); }
                EditorSceneManager.ClosePreviewScene(scene);
            }
        }
    }
}

