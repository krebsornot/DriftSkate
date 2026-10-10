using System;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace DriftSkate.EditorTools
{
    public static class BusStopNpcTests
    {
        [MenuItem("DriftSkate/Tests/Haltestellen-NPCs pruefen und rendern")]
        public static void Run()
        {
            Directory.CreateDirectory("Logs/bus_stop");
            EditorSceneManager.OpenScene(ProjectBuilder.CityPath);
            SkyLook.ApplySelected();
            var crew=BusStopResidents.Spawn();
            if (crew==null) throw new Exception("Haltestelle konnte nicht aufgestellt werden.");
            if (BusStopResidents.Spawn()!=crew) throw new Exception("Doppelte Haltestelle.");
            foreach (var npc in new[]{crew.bruno,crew.fiete})
            {
                if(npc==null || !npc.Animator.isHuman || !npc.Animator.avatar.isValid || !npc.HasClip("Idle") || !npc.HasClip(npc.Id=="Bruno"?"Drink":"Smoke"))
                    throw new Exception("NPC-Rig oder Animation fehlt.");
                foreach(var r in npc.GetComponentsInChildren<SkinnedMeshRenderer>()) r.forceMatrixRecalculationPerRender=true;
                npc.Preview("Idle",0);
            }
            var cam=Camera.main;
            cam.fieldOfView=45;
            var center=crew.transform.position;
            cam.transform.position=center+new Vector3(5,3.1f,8);
            cam.transform.LookAt(center+Vector3.up*1.1f);
            crew.UpdateProps();
            SimTests.Capture(cam,"Logs/bus_stop/haltestelle.png");
            foreach(var npc in new[]{crew.bruno,crew.fiete})
            {
                foreach(var action in new[]{"Idle",npc.Id=="Bruno"?"Drink":"Smoke"})
                {
                    npc.Preview(action,1.8f);
                    crew.UpdateProps();
                    cam.transform.position=npc.transform.position+new Vector3(1.1f,1.6f,2.7f);
                    cam.transform.LookAt(npc.transform.position+Vector3.up*.9f);
                    SimTests.Capture(cam,$"Logs/bus_stop/{npc.Id}_{action}_unity.png");
                    var feet=npc.Animator.GetBoneTransform(HumanBodyBones.RightFoot);
                    if(feet.position.y<center.y-.08f) throw new Exception(npc.Id+": Fuss im Boden");
                }
                npc.Preview("Idle",0);
            }
            File.WriteAllText("Logs/bus_stop/result.txt","OK: zwei Humanoid-Rigs, Idle/Drink/Smoke, keine doppelte Haltestelle, Fuesse ueber Boden. Standort: "+center);
            UnityEngine.Object.DestroyImmediate(crew.gameObject);
        }

        static double _started;
        static bool _oldEnabled, _drinkSeen, _smokeSeen, _puffSeen;
        static EnterPlayModeOptions _oldOptions;
        /// <summary>Ohne -quit starten: prueft automatischen Aufbau und beide Aktionszyklen im laufenden Spiel.</summary>
        public static void PlayRun()
        {
            EditorSceneManager.OpenScene(ProjectBuilder.CityPath);
            _oldEnabled=EditorSettings.enterPlayModeOptionsEnabled;
            _oldOptions=EditorSettings.enterPlayModeOptions;
            EditorSettings.enterPlayModeOptionsEnabled=true;
            EditorSettings.enterPlayModeOptions=EnterPlayModeOptions.DisableDomainReload;
            _drinkSeen=_smokeSeen=_puffSeen=false;
            _started=EditorApplication.timeSinceStartup;
            EditorApplication.update+=Tick;
            EditorApplication.EnterPlaymode();
        }
        static void Tick()
        {
            try
            {
                if(EditorApplication.timeSinceStartup-_started>110) throw new Exception("Haltestellen-Test: Zeitlimit");
                if(!EditorApplication.isPlaying || Time.time<1) return;
                var crew=UnityEngine.Object.FindAnyObjectByType<BusStopResidents>();
                if(crew==null) throw new Exception("Automatischer Aufbau fehlt");
                var cam=Camera.main;
                if(CameraRig.Instance!=null) CameraRig.Instance.enabled=false;
                cam.transform.position=crew.transform.position+new Vector3(3,2.4f,6);
                cam.transform.LookAt(crew.transform.position+Vector3.up*.95f);
                _drinkSeen|=crew.bruno.CurrentAction=="Drink";
                _smokeSeen|=crew.fiete.CurrentAction=="Smoke";
                foreach(var ps in crew.GetComponentsInChildren<ParticleSystem>()) _puffSeen|=ps.particleCount>0;
                if(Time.time<14) return;
                if(!_drinkSeen || !_smokeSeen || !_puffSeen) throw new Exception("Trinken, Rauchen oder Rauchpartikel fehlen");
                Directory.CreateDirectory("Logs/bus_stop");
                SimTests.Capture(cam,"Logs/bus_stop/play.png");
                File.WriteAllText("Logs/bus_stop/play_result.txt","OK: automatischer Aufbau, Trink- und Rauchanimation, Rauchpartikel im Play-Mode.");
                Quit(0);
            }
            catch(Exception e) { Debug.LogException(e); Quit(1); }
        }
        static void Quit(int code)
        {
            EditorApplication.update-=Tick;
            EditorSettings.enterPlayModeOptionsEnabled=_oldEnabled;
            EditorSettings.enterPlayModeOptions=_oldOptions;
            EditorApplication.ExitPlaymode();
            EditorApplication.Exit(code);
        }
    }
}
