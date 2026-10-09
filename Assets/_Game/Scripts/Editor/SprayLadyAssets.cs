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
    public static class SprayLadyAssets
    {
        const string ModelPath="Assets/_Game/Resources/Characters/NPC_SprayLady.fbx";
        const string Root="Assets/_Game/Characters/SprayShopLady";
        const string Art="Art/Characters/SprayShopLady";
        [MenuItem("DriftSkate/Spray Shop/Shop-Dame einbauen")]
        public static void Build()
        {
            if (!Application.isBatchMode && !EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            Directory.CreateDirectory(Root+"/Materials");
            AssetDatabase.Refresh();
            var model=AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath);
            if(model==null)throw new Exception("Shop lady FBX missing");
            var idle=AssetDatabase.LoadAllAssetsAtPath(ModelPath).OfType<AnimationClip>().FirstOrDefault(c=>c.name=="Idle");
            var avatar=AssetDatabase.LoadAllAssetsAtPath(ModelPath).OfType<Avatar>().FirstOrDefault();
            if(idle==null || !idle.isLooping || avatar==null || !avatar.isValid || !avatar.isHuman)throw new Exception("Invalid shop lady rig/idle");
            string controllerPath=Root+"/SprayShopLady.controller";
            var controller=AssetDatabase.LoadAssetAtPath<AnimatorController>(controllerPath) ?? AnimatorController.CreateAnimatorControllerAtPath(controllerPath);
            var machine=controller.layers[0].stateMachine;
            var state=machine.states.Select(s=>s.state).FirstOrDefault(s=>s.name=="Idle") ?? machine.AddState("Idle");
            state.motion=idle;machine.defaultState=state;
            var root=new GameObject("Spray Shop Lady");
            try
            {
                var instance=(GameObject)PrefabUtility.InstantiatePrefab(model);
                instance.transform.SetParent(root.transform,false);
                instance.transform.localRotation=Quaternion.Euler(0,180,0);
                foreach(var renderer in instance.GetComponentsInChildren<SkinnedMeshRenderer>())
                {
                    renderer.sharedMaterials=renderer.sharedMaterials.Select(Material).ToArray();
                    var bounds=renderer.localBounds;bounds.Expand(.5f);renderer.localBounds=bounds;
                    if(renderer.bones.Any(b=>b==null))throw new Exception("Missing shop lady skin bone");
                }
                var animator=instance.GetComponent<Animator>() ?? instance.AddComponent<Animator>();
                animator.avatar=avatar;animator.runtimeAnimatorController=controller;animator.applyRootMotion=false;
                animator.cullingMode=AnimatorCullingMode.AlwaysAnimate;
                var collider=root.AddComponent<CapsuleCollider>();collider.height=1.8f;collider.radius=.24f;collider.center=Vector3.up*.90f;
                root.AddComponent<SprayShopKeeper>();
                PrefabUtility.SaveAsPrefabAsset(root,Root+"/SprayShopLady.prefab");
            }
            finally { UnityEngine.Object.DestroyImmediate(root); }
            AssetDatabase.SaveAssets();
            var prefab=AssetDatabase.LoadAssetAtPath<GameObject>(Root+"/SprayShopLady.prefab");
            Render(prefab,idle);
            var scene=EditorSceneManager.OpenScene("Assets/_Game/Scenes/City.unity");
            var shop=UnityEngine.Object.FindFirstObjectByType<SprayShop>();
            if(shop==null || shop.register==null)throw new Exception("Spray shop/register missing");
            var old=shop.GetComponentInChildren<SprayShopKeeper>();
            if(old!=null)UnityEngine.Object.DestroyImmediate(old.gameObject);
            var lady=(GameObject)PrefabUtility.InstantiatePrefab(prefab,scene);
            lady.transform.SetParent(shop.transform,false);
            lady.transform.localPosition=new Vector3(1.92f,.065f,-6.42f);
            lady.transform.localRotation=Quaternion.identity;
            if(shop.GetComponentsInChildren<SprayShopKeeper>().Length!=1)throw new Exception("Duplicate shop keeper");
            var anim=lady.GetComponentInChildren<Animator>();idle.SampleAnimation(anim.gameObject,0);
            EditorSceneManager.MarkSceneDirty(scene);EditorSceneManager.SaveScene(scene);
            RenderShop(shop,lady);
            File.WriteAllText(Art+"/unity-validation.txt","PASS: Humanoid avatar, looping Idle, skin bones, collider, prefab, one NPC behind counter in saved City scene.\n");
            Debug.Log("SPRAY LADY PASS: rig, animation, prefab, saved City placement and preview");
        }
        static Material Material(Material source)
        {
            string path=Root+"/Materials/"+source.name+".mat";
            var match=Regex.Match(source.name,"__([0-9A-Fa-f]{6})");
            if(!match.Success)throw new Exception("Missing material colour");
            ColorUtility.TryParseHtmlString("#"+match.Groups[1].Value,out var color);
            var mat=AssetDatabase.LoadAssetAtPath<Material>(path);
            bool create=mat==null;
            if(create)mat=new Material(Shader.Find("DriftSkate/Toon")){name=source.name};
            mat.SetColor("_BaseColor",color);mat.SetFloat("_OutlineWidth",.035f);mat.SetFloat("_OutlineMode",0);
            mat.SetFloat("_Softness",.22f);mat.SetFloat("_Threshold",-.08f);
            mat.SetColor("_ShadowTint",new Color(.75f,.71f,.82f));mat.SetFloat("_RimStrength",.04f);
            mat.SetShaderPassEnabled("SRPDefaultUnlit",!source.name.Contains("_flat"));
            if(create)AssetDatabase.CreateAsset(mat,path);else EditorUtility.SetDirty(mat);
            return mat;
        }
        static void Capture(Camera camera,string path)
        {
            var rt=new RenderTexture(1200,1400,24){antiAliasing=4};var old=RenderTexture.active;
            var image=new Texture2D(1200,1400,TextureFormat.RGB24,false);
            try {camera.targetTexture=rt;camera.Render();RenderTexture.active=rt;image.ReadPixels(new Rect(0,0,1200,1400),0,0);image.Apply();File.WriteAllBytes(path,image.EncodeToPNG());}
            finally {camera.targetTexture=null;RenderTexture.active=old;rt.Release();UnityEngine.Object.DestroyImmediate(rt);UnityEngine.Object.DestroyImmediate(image);}
        }
        static void Render(GameObject prefab,AnimationClip idle)
        {
            var scene=EditorSceneManager.NewPreviewScene();
            try
            {
                var instance=UnityEngine.Object.Instantiate(prefab);SceneManager.MoveGameObjectToScene(instance,scene);
                idle.SampleAnimation(instance.GetComponentInChildren<Animator>().gameObject,0);
                var lamp=new GameObject("Light");SceneManager.MoveGameObjectToScene(lamp,scene);
                var light=lamp.AddComponent<Light>();light.type=LightType.Directional;light.intensity=1.4f;lamp.transform.rotation=Quaternion.Euler(35,145,0);
                var obj=new GameObject("Camera");SceneManager.MoveGameObjectToScene(obj,scene);
                var camera=obj.AddComponent<Camera>();camera.scene=scene;camera.clearFlags=CameraClearFlags.SolidColor;camera.backgroundColor=new Color(.15f,.14f,.19f);camera.fieldOfView=29;
                camera.transform.position=new Vector3(1.6f,1.6f,4.4f);camera.transform.LookAt(new Vector3(0,.93f,0));
                Capture(camera,Art+"/SprayShopLady_Unity.png");
            }
            finally {EditorSceneManager.ClosePreviewScene(scene);}
        }
        static void RenderShop(SprayShop shop,GameObject lady)
        {
            var obj=new GameObject("Shop lady preview camera");
            var lamp=new GameObject("Shop lady preview fill");
            try
            {
                var light=lamp.AddComponent<Light>();light.type=LightType.Point;light.range=6;light.intensity=2;
                lamp.transform.position=shop.transform.TransformPoint(new Vector3(1.7f,2.5f,-4));
                var camera=obj.AddComponent<Camera>();camera.fieldOfView=42;camera.nearClipPlane=.05f;
                camera.transform.position=shop.transform.TransformPoint(new Vector3(.3f,1.95f,-2.8f));
                camera.transform.LookAt(lady.transform.position+Vector3.up*1.18f);
                Capture(camera,Art+"/SprayShopLady_in_shop.png");
            }
            finally {UnityEngine.Object.DestroyImmediate(obj);UnityEngine.Object.DestroyImmediate(lamp);}
        }
    }
}
