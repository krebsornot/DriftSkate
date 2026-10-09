using System;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace DriftSkate.EditorTools
{
    public static class SprayShopPlayTest
    {
        static bool oldEnabled, failed;
        static EnterPlayModeOptions oldOptions;
        static double started;
        static float stageAt;
        static int stage;
        public static void Run()
        {
            SaveSystem.DataFolder=Path.GetFullPath("Logs/SprayShopTestSave");
            Directory.CreateDirectory(SaveSystem.DataFolder);
            File.WriteAllText(SaveSystem.ProfilePath,"{\"money\":1000}");
            SaveSystem.Load();
            if(SaveSystem.Profile.sprayCans!=12)throw new Exception("Old save did not receive starter supply");
            File.WriteAllText(SaveSystem.ProfilePath,JsonUtility.ToJson(new PlayerProfile {money=1000,sprayCans=0}));
            SaveSystem.Load();
            if(SaveSystem.Profile.sprayCans!=0)throw new Exception("Empty inventory was refilled on reload");
            File.WriteAllText(SaveSystem.ProfilePath,JsonUtility.ToJson(new PlayerProfile {money=1000,sprayCans=4}));
            EditorSceneManager.OpenScene("Assets/_Game/Scenes/City.unity");
            oldEnabled=EditorSettings.enterPlayModeOptionsEnabled;oldOptions=EditorSettings.enterPlayModeOptions;
            EditorSettings.enterPlayModeOptionsEnabled=true;EditorSettings.enterPlayModeOptions=EnterPlayModeOptions.DisableDomainReload;
            started=EditorApplication.timeSinceStartup;
            Application.logMessageReceived+=Log;
            EditorApplication.update+=Tick;
            EditorApplication.EnterPlaymode();
        }
        static void Log(string text,string trace,LogType type) { if(type==LogType.Exception||type==LogType.Error)failed=true; }
        static void Tick()
        {
            try
            {
                if(EditorApplication.timeSinceStartup-started>100)throw new Exception("Shop play test timeout");
                if(!EditorApplication.isPlaying || Time.time<2 || PlayerAvatar.Local==null)return;
                var player=PlayerAvatar.Local;var sc=player.skater;var shop=SprayShop.Instance;
                if(shop==null)throw new Exception("Shop missing in City");
                player.externalControl=true;sc.inputEnabled=false;
                if(stage==0)
                {
                    var keeper=shop.GetComponentInChildren<SprayShopKeeper>();
                    if(keeper==null || shop.GetComponentsInChildren<SprayShopKeeper>().Length!=1)throw new Exception("Shop keeper missing or duplicated");
                    var animator=keeper.GetComponentInChildren<Animator>();
                    if(animator==null || !animator.isHuman || animator.runtimeAnimatorController==null || !animator.GetCurrentAnimatorStateInfo(0).IsName("Idle"))throw new Exception("Shop keeper idle is not playing");
                    if(keeper.GetComponent<CapsuleCollider>()==null)throw new Exception("Shop keeper collider missing");
                    keeper.Greet();
                    player.PlaceForChallenge(false,shop.transform.TransformPoint(new Vector3(0,.05f,2.5f)),shop.transform.eulerAngles.y+180);
                    sc.InjectBoardToggle();stage=1;stageAt=Time.time;
                }
                else if(stage==1)
                {
                    sc.InjectInput(new Vector2(0,1),false);
                    if(Time.time-stageAt<3) return;
                    if(sc.State!=SkaterState.Walking || shop.transform.InverseTransformPoint(sc.transform.position).z>-.8f)throw new Exception("Could not walk through shop door");
                    sc.InjectInput(Vector2.zero,false);
                    sc.Place(shop.transform.TransformPoint(new Vector3(2.3f,.08f,-4.1f)),shop.transform.eulerAngles.y+180);
                    sc.InjectBoardToggle();stage=2;stageAt=Time.time;
                }
                else if(stage==2 && Time.time-stageAt>.3f)
                {
                    if(sc.State!=SkaterState.Walking)throw new Exception("Not walking at till");
                    long money=SaveSystem.Profile.money;int count=SaveSystem.Profile.sprayCans;
                    shop.SendMessage("Buy");
                    if(SaveSystem.Profile.money!=money-100 || SaveSystem.Profile.sprayCans!=count+1)throw new Exception("Live till purchase failed");
                    var loaded=JsonUtility.FromJson<PlayerProfile>(File.ReadAllText(SaveSystem.ProfilePath));
                    if(loaded.money!=money-100 || loaded.sprayCans!=count+1)throw new Exception("Live purchase not saved");
                    sc.Place(shop.transform.TransformPoint(new Vector3(3.8f,.08f,-2.5f)),shop.transform.eulerAngles.y+90);
                    sc.InjectBoardToggle();stage=3;stageAt=Time.time;
                }
                else if(stage==3 && Time.time-stageAt>.4f)
                {
                    int before=SaveSystem.Profile.sprayCans;
                    if(!player.TrySpray() || SaveSystem.Profile.sprayCans!=before-1)throw new Exception("Spraying did not consume a can");
                    SaveSystem.Profile.sprayCans=0;
                    if(player.TrySpray())throw new Exception("Sprayed with empty inventory");
                    Debug.Log("SPRAY SHOP PLAY PASS: City startup, one animated Humanoid shop keeper with collider, walking through automatic door, live register purchase, saved inventory/money, spray consumption and empty inventory.");
                    Finish(failed?1:0);
                }
            }
            catch(Exception e) { Debug.LogException(e);Finish(1); }
        }
        static void Finish(int code)
        {
            EditorApplication.update-=Tick;
            // Resolve the test's graffiti combo while the HUD still exists, before batch shutdown destroys it.
            if(PlayerAvatar.Local!=null) PlayerAvatar.Local.combo.BankNow();
            EditorSettings.enterPlayModeOptionsEnabled=oldEnabled;EditorSettings.enterPlayModeOptions=oldOptions;
            EditorApplication.Exit(code);
        }
    }
}
