using System.Collections;
using System.Net;
using System.Net.Sockets;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using UnityEngine;

namespace DriftSkate
{
    /// <summary>
    /// Startet in der Stadt das Netzwerk: Solo (lokaler Host ohne offene Ports), Online-Host oder Beitreten per IP.
    /// Solo laeuft ueber denselben Code wie Online, damit es nur einen Spielweg gibt.
    /// </summary>
    public class CityBootstrap : MonoBehaviour
    {
        const ushort SoloPort = 17777;

        IEnumerator Start()
        {
            var nm = NetworkManager.Singleton != null ? NetworkManager.Singleton : FindAnyObjectByType<NetworkManager>();
            if (nm == null)
            {
                Debug.LogError("Kein NetworkManager in der Szene.");
                yield break;
            }
            var utp = nm.GetComponent<UnityTransport>();
            nm.OnClientDisconnectCallback += OnDisconnect;
            Admin.ApplyTimeScale();
            SkyLook.ApplySelected();
            // Wetter (nach dem Himmel, es veraendert dessen Material) und Spiegelungen auf nassem Boden
            var weather = new GameObject("Weather");
            weather.AddComponent<WeatherSystem>();
            weather.AddComponent<PlanarReflection>();
            // Stoner-NPCs mit Sofa am Platz (nur Deko, auf jedem Rechner gleich)
            StonerNpc.SpawnHangout();
            // Nix und Moe in der Seitengasse hinter dem Brunnenplatz, dazu der Chill-Effekt der Tueten
            AlleyCrew.Spawn();
            // Miru neben einer Bank im leeren Park gegenueber
            ParkMiru.Spawn();
            Chill.Ensure();

            bool ok;
            switch (GameSession.Mode)
            {
                case SessionMode.Host:
                    // Im automatischen Test nur lokal lauschen (keine Firewall-Abfrage).
                    utp.SetConnectionData("127.0.0.1", GameSession.Port, AutoTest.NetHost ? "127.0.0.1" : "0.0.0.0");
                    ok = nm.StartHost();
                    if (ok) HUD.Instance?.Toast("Online-Host gestartet. Freunde verbinden sich mit " + NetUtil.LocalIPv4(), 6f);
                    break;
                case SessionMode.Client:
                    utp.SetConnectionData(GameSession.Address, GameSession.Port);
                    ok = nm.StartClient();
                    HUD.Instance?.Toast("Verbinde mit " + GameSession.Address + " ...", 4f);
                    break;
                default:
                    // Der automatische Test nimmt einen eigenen Port, damit er neben einem laufenden Spiel startet
                    utp.SetConnectionData("127.0.0.1", AutoTest.Enabled ? (ushort)(SoloPort + 10) : SoloPort, "127.0.0.1");
                    ok = nm.StartHost();
                    break;
            }

            if (!ok)
            {
                Fail("Netzwerk konnte nicht gestartet werden (Port belegt?)");
                yield break;
            }

            if (GameSession.Mode == SessionMode.Client)
            {
                float timeout = 10f;
                while (!nm.IsConnectedClient && timeout > 0f)
                {
                    timeout -= Time.unscaledDeltaTime;
                    yield return null;
                }
                if (!nm.IsConnectedClient) Fail("Keine Verbindung zu " + GameSession.Address);
            }
        }

        void OnDisconnect(ulong clientId)
        {
            var nm = NetworkManager.Singleton;
            if (nm == null || nm.IsServer) return;
            if (clientId == nm.LocalClientId || clientId == NetworkManager.ServerClientId)
                Fail("Verbindung zum Host getrennt");
        }

        static void Fail(string message)
        {
            GameSession.LastError = message;
            Debug.LogWarning(message);
            HUD.LeaveToGarage();
        }

        void OnDestroy()
        {
            var nm = NetworkManager.Singleton;
            if (nm != null) nm.OnClientDisconnectCallback -= OnDisconnect;
        }
    }

    public static class NetUtil
    {
        static string _cached;

        public static string LocalIPv4()
        {
            if (_cached != null) return _cached;
            try
            {
                foreach (var ip in Dns.GetHostEntry(Dns.GetHostName()).AddressList)
                {
                    if (ip.AddressFamily == AddressFamily.InterNetwork && !IPAddress.IsLoopback(ip))
                        return _cached = ip.ToString();
                }
            }
            catch
            {
                // Ohne Netzwerkkarte einfach localhost anzeigen.
            }
            return _cached = "127.0.0.1";
        }
    }
}
