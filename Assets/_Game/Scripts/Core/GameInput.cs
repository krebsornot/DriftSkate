using UnityEngine;
using UnityEngine.InputSystem;

namespace DriftSkate
{
    /// <summary>
    /// Alle Eingaben (Tastatur + Gamepad) an einer Stelle. Die Aktionen werden im Code angelegt,
    /// damit kein Input-Asset gepflegt werden muss. Welche Aktion gilt, entscheidet der aktuelle Modus.
    /// </summary>
    public static class GameInput
    {
        static InputActionMap _map;

        public static InputAction Move { get; private set; }
        public static InputAction Throttle { get; private set; }
        public static InputAction Brake { get; private set; }
        public static InputAction Handbrake { get; private set; }
        public static InputAction Clutch { get; private set; }
        public static InputAction ShiftUp { get; private set; }
        public static InputAction ShiftDown { get; private set; }
        public static InputAction Interact { get; private set; }
        public static InputAction Talk { get; private set; }
        public static InputAction Ollie { get; private set; }
        public static InputAction Flip { get; private set; }
        public static InputAction Grab { get; private set; }
        public static InputAction Grind { get; private set; }
        public static InputAction Manual { get; private set; }
        public static InputAction Spray { get; private set; }
        public static InputAction Board { get; private set; }
        public static InputAction UseItem { get; private set; }
        public static InputAction ResetCar { get; private set; }
        public static InputAction LookStick { get; private set; }
        public static InputAction LookMouse { get; private set; }
        public static InputAction CameraView { get; private set; }
        public static InputAction Pause { get; private set; }
        public static InputAction NextSong { get; private set; }
        public static InputAction AdminPanel { get; private set; }

        /// <summary>Gesperrt, solange ein Menue offen ist.</summary>
        public static bool Blocked { get; set; }

        public static void Ensure()
        {
            if (_map != null) return;
            _map = new InputActionMap("Game");

            Move = _map.AddAction("Move", InputActionType.Value);
            Move.expectedControlType = "Vector2";
            Move.AddCompositeBinding("2DVector")
                .With("Up", "<Keyboard>/w").With("Down", "<Keyboard>/s")
                .With("Left", "<Keyboard>/a").With("Right", "<Keyboard>/d");
            Move.AddCompositeBinding("2DVector")
                .With("Up", "<Keyboard>/upArrow").With("Down", "<Keyboard>/downArrow")
                .With("Left", "<Keyboard>/leftArrow").With("Right", "<Keyboard>/rightArrow");
            Move.AddBinding("<Gamepad>/leftStick");

            Throttle = Axis("Throttle", "<Keyboard>/w", "<Keyboard>/upArrow", "<Gamepad>/rightTrigger");
            Brake = Axis("Brake", "<Keyboard>/s", "<Keyboard>/downArrow", "<Gamepad>/leftTrigger");

            Handbrake = Button("Handbrake", "<Keyboard>/space", "<Gamepad>/buttonSouth");
            Clutch = Button("Clutch", "<Keyboard>/leftShift", "<Gamepad>/buttonWest");
            ShiftUp = Button("ShiftUp", "<Keyboard>/e", "<Gamepad>/rightShoulder");
            ShiftDown = Button("ShiftDown", "<Keyboard>/q", "<Gamepad>/leftShoulder");
            Interact = Button("Interact", "<Keyboard>/f", "<Gamepad>/buttonNorth");
            Talk = Button("Talk", "<Keyboard>/e", "<Gamepad>/buttonWest"); // NPCs ansprechen (zu Fuss / auf dem Board)

            Ollie = Button("Ollie", "<Keyboard>/space", "<Gamepad>/buttonSouth");
            Flip = Button("Flip", "<Keyboard>/j", "<Gamepad>/buttonWest");
            Grab = Button("Grab", "<Keyboard>/k", "<Gamepad>/rightShoulder");
            Grind = Button("Grind", "<Keyboard>/l", "<Gamepad>/buttonEast");
            Manual = Button("Manual", "<Keyboard>/leftShift", "<Gamepad>/leftShoulder");
            Spray = Button("Spray", "<Keyboard>/t", "<Gamepad>/dpad/up");
            Board = Button("Board", "<Keyboard>/b", "<Gamepad>/dpad/down"); // absteigen und zu Fuss gehen / wieder aufsteigen
            UseItem = Button("UseItem", "<Keyboard>/g", "<Gamepad>/dpad/left"); // Tuete von Nix rauchen (Chill)

            ResetCar = Button("Reset", "<Keyboard>/r", "<Gamepad>/select");
            Pause = Button("Pause", "<Keyboard>/escape", "<Gamepad>/start");
            NextSong = Button("NextSong", "<Keyboard>/m", "<Gamepad>/dpad/right");
            AdminPanel = Button("AdminPanel", "<Keyboard>/f1");
            CameraView = Button("CameraView", "<Keyboard>/c", "<Gamepad>/rightStickPress"); // Kamera-Perspektive wechseln

            LookStick = _map.AddAction("LookStick", InputActionType.Value, "<Gamepad>/rightStick");
            LookStick.expectedControlType = "Vector2";
            LookMouse = _map.AddAction("LookMouse", InputActionType.Value, "<Mouse>/delta");
            LookMouse.expectedControlType = "Vector2";

            _map.Enable();
        }

        static InputAction Axis(string name, params string[] bindings)
        {
            var action = _map.AddAction(name, InputActionType.Value);
            action.expectedControlType = "Axis";
            foreach (var b in bindings) action.AddBinding(b);
            return action;
        }

        static InputAction Button(string name, params string[] bindings)
        {
            var action = _map.AddAction(name, InputActionType.Button);
            foreach (var b in bindings) action.AddBinding(b);
            return action;
        }

        public static Vector2 MoveValue => Blocked ? Vector2.zero : Move.ReadValue<Vector2>();
        public static float ThrottleValue => Blocked ? 0f : Throttle.ReadValue<float>();
        public static float BrakeValue => Blocked ? 0f : Brake.ReadValue<float>();
        public static bool Held(InputAction a) => !Blocked && a.IsPressed();
        public static bool Pressed(InputAction a) => !Blocked && a.WasPressedThisFrame();
        public static bool Released(InputAction a) => !Blocked && a.WasReleasedThisFrame();

        /// <summary>Kamera-Blick: rechter Stick plus Maus (nur bei gehaltener rechter Maustaste).</summary>
        public static Vector2 LookValue
        {
            get
            {
                if (Blocked) return Vector2.zero;
                Vector2 v = LookStick.ReadValue<Vector2>() * 120f * Time.unscaledDeltaTime;
                var mouse = Mouse.current;
                if (mouse != null && mouse.rightButton.isPressed) v += LookMouse.ReadValue<Vector2>() * 0.15f;
                return v;
            }
        }

        public static bool UsingGamepad => Gamepad.current != null && Gamepad.current.wasUpdatedThisFrame;
    }
}
