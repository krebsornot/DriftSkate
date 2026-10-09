using System;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace DriftSkate.EditorTools
{
    /// <summary>
    /// Fahrtest mit einem Garagen-Modell (Standard: Sylph S15 Drift): in der Stadt Gas geben, lenken, Handbremse.
    /// Prueft, dass das Auto faehrt, die Raeder am Boden sind und nichts durchfaellt. Aufruf ohne -quit:
    /// -executeMethod DriftSkate.EditorTools.CarModelPlayTest.Run [-car sylph15_drift]
    /// </summary>
    public static class CarModelPlayTest
    {
        static double _started;
        static float _t0, _maxSpeed;
        static int _stage;
        static string _car = "sylph15_drift", _oldCar, _dataFolder;
        static bool _oldEnabled;
        static EnterPlayModeOptions _oldOptions;

        public static void Run()
        {
            var args = Environment.GetCommandLineArgs();
            int ai = Array.IndexOf(args, "-car");
            if (ai >= 0 && ai + 1 < args.Length) _car = args[ai + 1];
            // eigener Spielstand, Fynns profile.json bleibt unberuehrt
            SaveSystem.DataFolder = System.IO.Path.GetFullPath("Logs/CarModelTestSave");
            System.IO.Directory.CreateDirectory(SaveSystem.DataFolder);
            SaveSystem.Load();
            if (!SaveSystem.Profile.ownedCars.Contains(_car)) SaveSystem.Profile.ownedCars.Add(_car);
            SaveSystem.Profile.selectedCar = _car;
            SaveSystem.Save();
            EditorSceneManager.OpenScene(ProjectBuilder.CityPath);
            _oldEnabled = EditorSettings.enterPlayModeOptionsEnabled;
            _oldOptions = EditorSettings.enterPlayModeOptions;
            EditorSettings.enterPlayModeOptionsEnabled = true;
            EditorSettings.enterPlayModeOptions = EnterPlayModeOptions.DisableDomainReload;
            _started = EditorApplication.timeSinceStartup;
            EditorApplication.update += Tick;
            EditorApplication.EnterPlaymode();
        }

        static void Quit(int code)
        {
            EditorApplication.update -= Tick;
            EditorSettings.enterPlayModeOptionsEnabled = _oldEnabled;
            EditorSettings.enterPlayModeOptions = _oldOptions;
            AssetDatabase.SaveAssets();
            EditorApplication.Exit(code);
        }

        static void Tick()
        {
            try
            {
                if (EditorApplication.timeSinceStartup - _started > 120) throw new Exception("Zeitlimit");
                if (!EditorApplication.isPlaying) return;
                var a = PlayerAvatar.Local;
                if (a == null || Time.time < 2f) return;
                float t = Time.time;
                var car = a.car;
                switch (_stage)
                {
                    case 0:
                        a.externalControl = true;
                        var model = car.GetComponentInChildren<CarModel>();
                        Debug.Log($"[CarTest] Auto {SaveSystem.Profile.selectedCar}, Modell {(model != null ? model.name : "FEHLT")}, Raeder {car.wheels.Length}");
                        car.Teleport(new Vector3(CityBuilder.RoadCenter(1) + 4f, 0.3f, CityBuilder.RoadCenter(0) + 15f), Quaternion.identity);
                        _stage = 1; _t0 = t;
                        break;
                    case 1:
                        float el = t - _t0;
                        car.input = new VehicleInput { throttle = 1f, steer = el > 4f ? 0.6f : 0f, handbrake = el > 4f && el < 4.6f };
                        _maxSpeed = Mathf.Max(_maxSpeed, car.SpeedKmh);
                        if (car.transform.position.y < -2f) throw new Exception("Auto durch den Boden gefallen");
                        if (el > 6.5f)
                        {
                            var wheel = car.transform.Find("Wheel0");
                            Debug.Log($"[CarTest] max {_maxSpeed:0} km/h, jetzt {car.SpeedKmh:0} km/h, Hoehe {car.transform.position.y:0.00}, Rad0 {(wheel != null ? wheel.position.y.ToString("0.00") : "-")}, aufrecht {car.transform.up.y:0.00}");
                            Camera.main.transform.position = car.transform.position + car.transform.right * 6f + Vector3.up * 2.2f;
                            Camera.main.transform.LookAt(car.transform.position + Vector3.up * 0.6f);
                            SimTests.Capture(Camera.main, "Logs/cars/drive_" + _car + ".png");
                            bool ok = _maxSpeed > 40f && car.transform.up.y > 0.8f;
                            Debug.Log(ok ? "CAR TEST PASS" : "CAR TEST FAIL");
                            EditorApplication.ExitPlaymode();
                            Quit(ok ? 0 : 1);
                        }
                        break;
                }
            }
            catch (Exception e)
            {
                Debug.LogException(e);
                Quit(1);
            }
        }
    }
}
