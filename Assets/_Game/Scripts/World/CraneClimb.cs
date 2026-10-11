using UnityEngine;
using UnityEngine.SceneManagement;

namespace DriftSkate
{
    /// <summary>
    /// Macht den Baukran der Baustelle besteigbar, ohne Szene oder Prefab zu aendern (beim Laden der Stadt):
    /// - Mast-Leiter (innen im Mast, vom Fundament bis hoch zum Ausleger-Laufsteg), oben steigt man auf den Laufsteg ab,
    ///   der sich mit dem Ausleger dreht. Man kann sie auch vom obersten Podest des Treppenturms aus greifen.
    /// - Leiter am Gegengewicht vom Gegenausleger hoch aufs Jump-Deck.
    /// - Begehbare Decks auf dem drehenden Oberteil (Laufsteg, Gegenausleger, Drehkranz, Jump-Deck, Gegengewicht,
    ///   Windenhaus) als Hitboxen. Das Oberteil wird dafuer ein kinematischer Rigidbody mit MovingGround: wer darauf
    ///   steht, dreht mit.
    /// Masse aus Art/Environment/baukran_baustelle.glb (Modell-Meter, der Kran ist 1,4-fach skaliert).
    /// </summary>
    public static class CraneClimb
    {
        const string RootName = "ClimbSetup";

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Register()
        {
            SceneManager.sceneLoaded -= Loaded;
            SceneManager.sceneLoaded += Loaded;
        }

        static void Loaded(Scene scene, LoadSceneMode mode)
        {
            if (scene.name == "City") Setup();
        }

        // Modell (glTF, rechtshaendig) -> Unity im Kran-Raum: glTFast spiegelt X
        static Vector3 G(float x, float y, float z) => new Vector3(-x, y, z);

        // Oberteil im Modell: um 40,45 Grad gedreht und 42,5 m hoch (Matrix von Crane_Top im GLB)
        const float TopC = 0.7609f, TopS = 0.6489f, TopY = 42.5f;
        static Vector3 T(float x, float y, float z) => G(TopC * x + TopS * z, y + TopY, -TopS * x + TopC * z);
        static Vector3 TDir(float x, float z) => G(TopC * x + TopS * z, 0f, -TopS * x + TopC * z);
        /// <summary>Drehung der Oberteil-Bauteile im Unity-Raum (gespiegelt: andere Richtung).</summary>
        static readonly Quaternion TopRot = Quaternion.Euler(0f, -Mathf.Atan2(TopS, TopC) * Mathf.Rad2Deg, 0f);

        /// <summary>Sprossenabstand im Modell (0,35 m) mal Kran-Skalierung.</summary>
        const float RungSpacing = 0.35f * 1.4f;

        public static bool Setup()
        {
            Transform crane = null;
            foreach (var t in Object.FindObjectsByType<Transform>(FindObjectsSortMode.None))
                if (t.name == "Crane" && t.parent != null && t.parent.name == "Site" && t.parent.parent != null && t.parent.parent.name == "Baustelle")
                {
                    crane = t;
                    break;
                }
            if (crane == null) return false;
            var mast = crane.Find("Mast");
            var top = crane.Find("Top");
            if (mast == null || top == null) { Debug.LogWarning("CraneClimb: Mast oder Oberteil fehlt"); return false; }
            if (top.Find(RootName) != null) return true;

            // Oberteil: kinematischer Rigidbody, damit die Hitboxen sauber mitdrehen; MovingGround nimmt Spieler mit
            var rb = top.GetComponent<Rigidbody>();
            if (rb == null) rb = top.gameObject.AddComponent<Rigidbody>();
            rb.isKinematic = true;
            rb.interpolation = RigidbodyInterpolation.None;
            if (top.GetComponent<MovingGround>() == null) top.gameObject.AddComponent<MovingGround>();
            var decks = new GameObject(RootName).transform;
            decks.SetParent(top, false);

            void Deck(string name, Vector3 min, Vector3 max)
            {
                var go = new GameObject(name);
                go.transform.SetParent(decks, false);
                go.transform.SetLocalPositionAndRotation(T((min.x + max.x) * 0.5f, (min.y + max.y) * 0.5f, (min.z + max.z) * 0.5f), TopRot);
                go.AddComponent<BoxCollider>().size = max - min;
            }
            Deck("Jib_Walkway", new Vector3(1.6f, 2.97f, -0.95f), new Vector3(41.2f, 3.17f, 0.95f));
            Deck("Counter_Jib", new Vector3(-12.5f, 1.2f, -1.2f), new Vector3(-1.6f, 1.5f, 1.2f));
            Deck("Slewing_Unit", new Vector3(-1.6f, 0.18f, -1.6f), new Vector3(1.6f, 1.18f, 1.6f));
            Deck("Jump_Deck", new Vector3(-12.75f, 4.5f, -1.8f), new Vector3(-9.75f, 4.64f, 1.8f));
            Deck("Counterweight", new Vector3(-12.5f, 1.5f, -1.5f), new Vector3(-10f, 4.5f, 1.5f));
            Deck("Winch_House", new Vector3(-6.4f, 1.5f, -0.9f), new Vector3(-4f, 2.8f, 0.9f));

            // Mast-Leiter: Sprossen bei z = 0,75 innen im Mast, man haengt zur Mast-Mitte hin. Oben weiter durch den
            // Turmkopf bis auf Hoehe des Laufstegs, dort ein Schritt auf den Steg (dreht mit dem Ausleger)
            var mastLadder = new GameObject("MastLadder").AddComponent<Ladder>();
            mastLadder.transform.SetParent(mast, false);
            mastLadder.bottom = G(0f, 0.62f, 0.75f);
            mastLadder.top = G(0f, TopY + 3.0f, 0.75f);
            mastLadder.side = Vector3.back;
            mastLadder.rungSpacing = RungSpacing;
            mastLadder.hasTopExit = true;
            mastLadder.exitFrame = top;
            mastLadder.topExit = T(2.1f, 3.2f, 0f);
            mastLadder.hasBottomExit = true;
            mastLadder.bottomExit = G(0f, 0.62f, 1.6f); // aussen vor dem Mast auf dem Fundament, noch in Greifweite
            mastLadder.label = "KRAN";

            // Leiter am Gegengewicht: vom Gegenausleger aufs Jump-Deck
            var cwLadder = new GameObject("CounterweightLadder").AddComponent<Ladder>();
            cwLadder.transform.SetParent(decks, false);
            cwLadder.bottom = T(-9.93f, 1.52f, 0f);
            cwLadder.top = T(-9.93f, 4.5f, 0f);
            cwLadder.side = TDir(1f, 0f);
            cwLadder.rungSpacing = RungSpacing;
            cwLadder.hasTopExit = true;
            cwLadder.topExit = T(-11.2f, 4.68f, 0f);
            cwLadder.hasBottomExit = true;
            cwLadder.bottomExit = T(-8.9f, 1.55f, 0f);
            cwLadder.label = "GEGENGEWICHT";
            return true;
        }
    }
}
