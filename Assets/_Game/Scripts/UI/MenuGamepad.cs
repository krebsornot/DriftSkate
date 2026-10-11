using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace DriftSkate
{
    /// <summary>
    /// Controller (und Pfeiltasten) in allen Menues: sobald man das Pad benutzt, ist immer ein Knopf ausgewaehlt
    /// (Steuerkreuz / linker Stick bewegt die Auswahl, A drueckt, Regler mit links/rechts). Bauen Menues sich nach einem
    /// Klick neu auf, landet die Auswahl wieder an derselben Stelle. Ein Neon-Rahmen zeigt die Auswahl, solange man mit
    /// Pad oder Tasten navigiert (mit der Maus verschwindet er). B ruft den Zurueck-Schritt des obersten Menues auf,
    /// LB / RB wechseln Reiter (Garage). Im laufenden Spiel ist nichts ausgewaehlt, damit A dort keine Knoepfe drueckt.
    /// </summary>
    [DefaultExecutionOrder(-50)]
    public class MenuGamepad : MonoBehaviour
    {
        public static MenuGamepad Instance { get; private set; }
        /// <summary>Gerade mit Pad oder Pfeiltasten unterwegs (Rahmen sichtbar).</summary>
        public static bool UsingNav { get; private set; }

        static readonly List<(Component owner, Func<bool> back)> BackHandlers = new List<(Component, Func<bool>)>();
        static Component _tabOwner;
        static Action<int> _tabSwitch;

        /// <summary>B / Zurueck: handler gibt true zurueck, wenn er etwas getan hat (sonst kommt der naechste dran).</summary>
        public static void OnBack(Component owner, Func<bool> handler) => BackHandlers.Add((owner, handler));
        /// <summary>LB / RB: Reiter wechseln (-1 / +1).</summary>
        public static void OnTabSwitch(Component owner, Action<int> handler) { _tabOwner = owner; _tabSwitch = handler; }

        /// <summary>Auswahl setzen (nur wenn man gerade mit Pad/Tasten navigiert, sonst bleibt die Maus ungestoert).</summary>
        public static void Select(GameObject go)
        {
            if (!UsingNav || go == null || EventSystem.current == null) return;
            EventSystem.current.SetSelectedGameObject(go);
        }

        /// <summary>Erster bedienbarer Knopf unter root (Eingabefelder nur, wenn es nichts anderes gibt).</summary>
        public static GameObject FirstIn(Transform root)
        {
            if (root == null) return null;
            GameObject fallback = null;
            foreach (var s in root.GetComponentsInChildren<Selectable>())
            {
                if (!Usable(s.gameObject)) continue;
                if (s is InputField) { if (fallback == null) fallback = s.gameObject; continue; }
                return s.gameObject;
            }
            return fallback;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Boot()
        {
            if (Instance != null) return;
            var go = new GameObject("MenuGamepad");
            DontDestroyOnLoad(go);
            go.AddComponent<MenuGamepad>();
        }

        Canvas _frameCanvas;
        RectTransform _frame;
        Image[] _edges;
        Canvas _rememberCanvas;
        readonly List<int> _rememberPath = new List<int>();

        void Awake()
        {
            Instance = this;
            BuildFrame();
        }

        /// <summary>Im Menue? In der Garage immer, in der Stadt nur, wenn ein Menue die Steuerung sperrt (Pause, Dialog).</summary>
        static bool MenuContext => SceneManager.GetActiveScene().name != "City" || GameInput.Blocked;

        void Update()
        {
            var es = EventSystem.current;
            BackHandlers.RemoveAll(h => h.owner == null);
            if (es == null) { _frame.gameObject.SetActive(false); return; }
            DetectInputKind();

            var sel = es.currentSelectedGameObject;
            if (!MenuContext)
            {
                if (sel != null) es.SetSelectedGameObject(null);
                _frame.gameObject.SetActive(false);
                return;
            }
            if (UsingNav && !Usable(sel))
            {
                var next = Restore();
                if (next == null) next = Best();
                if (next != sel) es.SetSelectedGameObject(next);
                sel = next;
            }
            if (Usable(sel)) Remember(sel);

            var pad = Gamepad.current;
            if (pad != null)
            {
                if (pad.buttonEast.wasPressedThisFrame)
                    for (int i = BackHandlers.Count - 1; i >= 0; i--)
                    {
                        var h = BackHandlers[i];
                        if (h.owner is Behaviour b && !b.isActiveAndEnabled) continue;
                        if (h.back()) break;
                    }
                if (_tabOwner != null && _tabSwitch != null)
                {
                    if (pad.leftShoulder.wasPressedThisFrame) _tabSwitch(-1);
                    if (pad.rightShoulder.wasPressedThisFrame) _tabSwitch(1);
                }
            }
            UpdateFrame(UsingNav && Usable(sel) ? sel : null);
        }

        void DetectInputKind()
        {
            var pad = Gamepad.current;
            if (pad != null && (pad.dpad.ReadValue().sqrMagnitude > 0.25f || pad.leftStick.ReadValue().sqrMagnitude > 0.25f ||
                                pad.buttonSouth.wasPressedThisFrame || pad.buttonEast.wasPressedThisFrame || pad.leftShoulder.wasPressedThisFrame ||
                                pad.rightShoulder.wasPressedThisFrame || pad.startButton.wasPressedThisFrame))
                UsingNav = true;
            var kb = Keyboard.current;
            if (kb != null && (kb.upArrowKey.wasPressedThisFrame || kb.downArrowKey.wasPressedThisFrame || kb.leftArrowKey.wasPressedThisFrame ||
                               kb.rightArrowKey.wasPressedThisFrame || kb.tabKey.wasPressedThisFrame))
            {
                // In Eingabefeldern bewegen Pfeiltasten den Cursor, das zaehlt nicht
                var cur = EventSystem.current.currentSelectedGameObject;
                if (cur == null || cur.GetComponent<InputField>() == null || !cur.GetComponent<InputField>().isFocused) UsingNav = true;
            }
            var mouse = Mouse.current;
            if (mouse != null && (mouse.delta.ReadValue().sqrMagnitude > 9f || mouse.leftButton.wasPressedThisFrame)) UsingNav = false;
        }

        static bool Usable(GameObject go)
        {
            if (go == null || !go.activeInHierarchy) return false;
            var s = go.GetComponent<Selectable>();
            if (s == null || !s.IsInteractable() || !s.isActiveAndEnabled) return false;
            var c = go.GetComponentInParent<Canvas>();
            return c != null && c.isActiveAndEnabled;
        }

        /// <summary>Bester Startpunkt: im obersten sichtbaren Menue der Knopf ganz oben links (Eingabefelder zuletzt).</summary>
        static GameObject Best()
        {
            Selectable best = null;
            int bestOrder = int.MinValue;
            Vector2 bestPos = default;
            bool bestInput = true;
            foreach (var s in Selectable.allSelectablesArray)
            {
                if (s == null || !Usable(s.gameObject)) continue;
                var canvas = s.GetComponentInParent<Canvas>().rootCanvas;
                int order = canvas.sortingOrder;
                Vector2 p = ((RectTransform)s.transform).position;
                if (p.x < -50f || p.y < -50f || p.x > Screen.width + 50f || p.y > Screen.height + 50f) continue; // ausserhalb (weggescrollt)
                bool input = s is InputField;
                bool better = best == null || order > bestOrder ||
                              (order == bestOrder && (bestInput && !input ||
                               bestInput == input && (p.y > bestPos.y + 20f || Mathf.Abs(p.y - bestPos.y) <= 20f && p.x < bestPos.x)));
                if (!better) continue;
                best = s;
                bestOrder = order;
                bestPos = p;
                bestInput = input;
            }
            return best != null ? best.gameObject : null;
        }

        void Remember(GameObject go)
        {
            var canvas = go.GetComponentInParent<Canvas>();
            if (canvas == null) return;
            canvas = canvas.rootCanvas;
            _rememberCanvas = canvas;
            _rememberPath.Clear();
            for (var t = go.transform; t != null && t != canvas.transform; t = t.parent) _rememberPath.Insert(0, t.GetSiblingIndex());
        }

        /// <summary>Nach einem Neuaufbau: dieselbe Stelle in der Hierarchie (oder den naechsten Knopf darunter) waehlen.</summary>
        GameObject Restore()
        {
            if (_rememberCanvas == null || !_rememberCanvas.isActiveAndEnabled || _rememberPath.Count == 0) return null;
            Transform t = _rememberCanvas.transform;
            foreach (int index in _rememberPath)
            {
                if (index >= t.childCount) break;
                var child = t.GetChild(index);
                if (!child.gameObject.activeInHierarchy) break;
                t = child;
            }
            if (Usable(t.gameObject)) return t.gameObject;
            return t != _rememberCanvas.transform ? FirstIn(t) : null;
        }

        // ------------------------------------------------------------------ Rahmen

        void BuildFrame()
        {
            _frameCanvas = UIFactory.Canvas("MenuGamepadFrame", 900);
            DontDestroyOnLoad(_frameCanvas.gameObject);
            _frame = UIFactory.Rect(_frameCanvas.transform, "Frame", Vector2.zero, Vector2.zero, Vector2.zero, Vector2.zero, Vector2.one);
            _edges = new Image[8];
            for (int i = 0; i < _edges.Length; i++)
            {
                var go = new GameObject("Edge", typeof(RectTransform));
                go.transform.SetParent(_frame, false);
                var img = go.AddComponent<Image>();
                img.raycastTarget = false;
                _edges[i] = img;
                var rt = img.rectTransform;
                switch (i)
                {
                    // vier duenne Kanten
                    case 0: rt.anchorMin = new Vector2(0, 1); rt.anchorMax = new Vector2(1, 1); rt.sizeDelta = new Vector2(0, 3); rt.anchoredPosition = Vector2.zero; break;
                    case 1: rt.anchorMin = new Vector2(0, 0); rt.anchorMax = new Vector2(1, 0); rt.sizeDelta = new Vector2(0, 3); rt.anchoredPosition = Vector2.zero; break;
                    case 2: rt.anchorMin = new Vector2(0, 0); rt.anchorMax = new Vector2(0, 1); rt.sizeDelta = new Vector2(3, 0); rt.anchoredPosition = Vector2.zero; break;
                    case 3: rt.anchorMin = new Vector2(1, 0); rt.anchorMax = new Vector2(1, 1); rt.sizeDelta = new Vector2(3, 0); rt.anchoredPosition = Vector2.zero; break;
                    // dicke Ecken (oben links, unten rechts)
                    case 4: rt.anchorMin = rt.anchorMax = new Vector2(0, 1); rt.pivot = new Vector2(0, 1); rt.sizeDelta = new Vector2(26, 7); break;
                    case 5: rt.anchorMin = rt.anchorMax = new Vector2(0, 1); rt.pivot = new Vector2(0, 1); rt.sizeDelta = new Vector2(7, 26); break;
                    case 6: rt.anchorMin = rt.anchorMax = new Vector2(1, 0); rt.pivot = new Vector2(1, 0); rt.sizeDelta = new Vector2(26, 7); break;
                    case 7: rt.anchorMin = rt.anchorMax = new Vector2(1, 0); rt.pivot = new Vector2(1, 0); rt.sizeDelta = new Vector2(7, 26); break;
                }
            }
            _frame.gameObject.SetActive(false);
        }

        readonly Vector3[] _corners = new Vector3[4];

        void UpdateFrame(GameObject sel)
        {
            if (sel == null) { _frame.gameObject.SetActive(false); return; }
            ((RectTransform)sel.transform).GetWorldCorners(_corners);
            Vector2 min = new Vector2(float.MaxValue, float.MaxValue), max = new Vector2(float.MinValue, float.MinValue);
            foreach (var c in _corners) { min = Vector2.Min(min, c); max = Vector2.Max(max, c); }
            // Bildschirm-Pixel -> Rahmen-Leinwand
            float scale = _frameCanvas.scaleFactor;
            const float pad = 7f;
            _frame.anchoredPosition = min / scale - Vector2.one * pad;
            _frame.sizeDelta = (max - min) / scale + Vector2.one * pad * 2f;
            _frame.gameObject.SetActive(true);
            // Langsam zwischen Pink, Violett und Cyan wechseln
            float t = Mathf.Sin(Time.unscaledTime * 1.2f) * 0.5f + 0.5f;
            Color edge = Color.Lerp(Palette.Hex("FF4FD8"), Palette.Hex("4FF0FF"), t);
            Color corner = Color.Lerp(Palette.Hex("4FF0FF"), Palette.Hex("B58CFF"), t);
            for (int i = 0; i < _edges.Length; i++) _edges[i].color = i < 4 ? edge : corner;
        }
    }
}
