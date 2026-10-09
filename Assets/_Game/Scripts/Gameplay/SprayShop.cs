using UnityEngine;
using UnityEngine.InputSystem;

namespace DriftSkate
{
    public class SprayShop : MonoBehaviour
    {
        public Transform door, register;
        public static SprayShop Instance { get; private set; }
        public const int Capacity = 99;
        public static readonly int[] Quantities = { 1, 6, 12 };
        public static readonly int[] Prices = { 100, 500, 900 };
        bool _open;
        static int _closedFrame = -1;
        public static bool BlocksPause => (Instance != null && Instance._open) || _closedFrame == Time.frameCount;
        int _selected;
        string _message = "Eine Dose reicht fuer ein Graffiti.";
        GUIStyle _heading, _label, _button;

        void OnEnable() => Instance = this;
        void OnDisable()
        {
            Close();
            if (Instance == this) Instance = null;
        }

        bool NearTill(PlayerAvatar player) => player != null && player.Mode == PlayerMode.Skating &&
            player.skater.State == SkaterState.Walking && register != null &&
            Vector3.Distance(player.skater.transform.position + Vector3.up, register.position) < 2.4f;

        public static bool TryInteract(PlayerAvatar player)
        {
            var shop = Instance;
            if (shop == null) return false;
            if (shop._open) return true;
            if (GameInput.Blocked || !shop.NearTill(player)) return false;
            if (GameInput.Pressed(GameInput.Talk))
            {
                shop._open = true;
                GameInput.Blocked = true;
                shop._message = "Willkommen im Can Club. Welche Farbe bekommt deine Stadt heute?";
                shop.GetComponentInChildren<SprayShopKeeper>()?.Greet();
            }
            return true;
        }

        public static bool Purchase(PlayerProfile profile, int product, bool free = false)
        {
            if (profile == null || product < 0 || product >= Quantities.Length) return false;
            int amount = Quantities[product], cost = Prices[product];
            if (profile.sprayCans > Capacity - amount || (!free && profile.money < cost)) return false;
            if (!free) profile.money -= cost;
            profile.sprayCans += amount;
            return true;
        }

        void Buy()
        {
            if (!NearTill(PlayerAvatar.Local)) { Close(); return; }
            if (Purchase(SaveSystem.Profile, _selected, Admin.Free))
            {
                SaveSystem.Save();
                _message = Quantities[_selected] + " Spraydosen gekauft. Danke und viel Spass!";
                SpraySound.Play(.3f, false);
                GetComponentInChildren<SprayShopKeeper>()?.Greet();
            }
            else _message = SaveSystem.Profile.sprayCans > Capacity - Quantities[_selected]
                ? "Rucksack voll (maximal 99 Dosen)." : "Dafuer reicht dein Geld noch nicht.";
        }

        void Close() { if (_open) { _open = false; _closedFrame = Time.frameCount; GameInput.Blocked = false; } }

        void Update()
        {
            var player = PlayerAvatar.Local;
            if (door != null)
            {
                bool near = player != null && player.Mode == PlayerMode.Skating &&
                    Vector3.Distance(player.skater.transform.position, door.position) < 4f;
                door.localRotation = Quaternion.Slerp(door.localRotation, Quaternion.Euler(0, near ? 100f : 0, 0), 1-Mathf.Exp(-6*Time.deltaTime));
            }
            if (!_open) return;
            if (!NearTill(player)) { Close(); return; }
            var keys = Keyboard.current;
            var pad = Gamepad.current;
            if ((keys != null && keys.escapeKey.wasPressedThisFrame) || (pad != null && pad.buttonEast.wasPressedThisFrame)) { Close(); return; }
            if ((keys != null && keys.downArrowKey.wasPressedThisFrame) || (pad != null && pad.dpad.down.wasPressedThisFrame)) _selected = (_selected+1)%3;
            if ((keys != null && keys.upArrowKey.wasPressedThisFrame) || (pad != null && pad.dpad.up.wasPressedThisFrame)) _selected = (_selected+2)%3;
            if ((keys != null && keys.enterKey.wasPressedThisFrame) || (pad != null && pad.buttonSouth.wasPressedThisFrame)) Buy();
        }

        void OnGUI()
        {
            if (!_open) return;
            if (_heading == null)
            {
                _heading = new GUIStyle(GUI.skin.label) { fontSize = 28, fontStyle = FontStyle.Bold };
                _label = new GUIStyle(GUI.skin.label) { fontSize = 17, wordWrap = true };
                _button = new GUIStyle(GUI.skin.button) { fontSize = 18 };
            }
            float scale = Mathf.Min(1f, Screen.width/620f, Screen.height/460f);
            var old = GUI.matrix; GUI.matrix = Matrix4x4.Scale(new Vector3(scale,scale,1));
            float x = (Screen.width/scale-560)/2, y = (Screen.height/scale-410)/2;
            GUI.Box(new Rect(x,y,560,410), GUIContent.none);
            GUI.Label(new Rect(x+24,y+18,510,40), "CAN CLUB / SPRAY SUPPLY", _heading);
            GUI.Label(new Rect(x+24,y+65,510,35), "Geld: " + UIFactory.Money(SaveSystem.Profile.money) + "    Dosen: " + SaveSystem.Profile.sprayCans + "/99", _label);
            for (int i=0;i<3;i++)
            {
                if (GUI.Button(new Rect(x+24,y+112+i*48,512,40), (_selected == i ? ">  " : "") + Quantities[i] + " Dose(n)  /  " + UIFactory.Money(Prices[i]), _button)) _selected=i;
            }
            GUI.Label(new Rect(x+24,y+263,512,55), _message, _label);
            if (GUI.Button(new Rect(x+24,y+330,250,45), "KAUFEN [Enter / A]", _button)) Buy();
            if (GUI.Button(new Rect(x+286,y+330,250,45), "ZURUECK [Esc / B]", _button)) Close();
            GUI.matrix = old;
        }
    }
}
