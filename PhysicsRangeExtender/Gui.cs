using System;
using System.Globalization;
using KSP.UI.Screens;
using UnityEngine;

namespace PhysicsRangeExtender
{
    [KSPAddon(KSPAddon.Startup.Flight, false)]
    public class Gui : MonoBehaviour
    {
        private const float WindowWidth = 270;
        private const float FieldWidth = 60;
        private const string InputLockId = "PRE_GUI";

        public static Gui Fetch;
        public static bool GuiEnabled;

        private ApplicationLauncherButton button = null;

        private bool _gameUiToggle;
        private Rect _windowRect;
        private bool _inputLocked;

        private string _globalRangeText = string.Empty;
        private string _spaceRangeText = string.Empty;
        private string _landedRangeText = string.Empty;
        private string _error = string.Empty;

        private GUIStyle _noteStyle;
        private GUIStyle _errorStyle;

        private static readonly CultureInfo Invariant = CultureInfo.InvariantCulture;

        private void Awake()
        {
            if (Fetch)
                Destroy(Fetch);

            Fetch = this;
        }

        private void Start()
        {
            _windowRect = new Rect(Screen.width - WindowWidth - 40, 100, WindowWidth, 0);
            AddToolbarButton();
            GameEvents.onHideUI.Add(GameUiDisable);
            GameEvents.onShowUI.Add(GameUiEnable);
            _gameUiToggle = true;
            ResetFields();
        }

        private void OnDestroy()
        {
            SetInputLock(false);
            GameEvents.onShowUI.Remove(GameUiEnable);
            GameEvents.onHideUI.Remove(GameUiDisable);
            ApplicationLauncher.Instance.RemoveModApplication(this.button);
            this.button = null;
        }

        // ReSharper disable once InconsistentNaming
        private void OnGUI()
        {
            if (!PreSettings.ConfigLoaded) return;

            var visible = GuiEnabled && _gameUiToggle;
            if (visible)
            {
                if (_noteStyle == null) CreateStyles();

                // Height 0: GUILayout grows the window to fit, so it also shrinks when content goes away.
                _windowRect.height = 0;
                _windowRect = GUILayout.Window(GetInstanceID(), _windowRect, GuiWindow, "Physics Range Extender",
                    GUILayout.Width(WindowWidth));
            }

            // Keep clicks and typed digits (action groups) from reaching the game behind the window.
            if (Event.current.type == EventType.Repaint)
                SetInputLock(visible && _windowRect.Contains(Event.current.mousePosition));
        }

        private void GuiWindow(int windowId)
        {
            if (PreSettings.ModEnabled)
            {
                _globalRangeText = Field("Load range (km)", _globalRangeText);
                _spaceRangeText = Field("Physics range in space (km)", _spaceRangeText);
                _landedRangeText = Field("Physics range landed (km)", _landedRangeText);
                GUILayout.Label("Flying vessels run physics within the load range.", _noteStyle);

                if (_error.Length > 0) GUILayout.Label(_error, _errorStyle);

                var waiting = PhysicsRangeExtender.Instance != null
                    ? PhysicsRangeExtender.Instance.VesselsWaitingForGround
                    : 0;
                if (waiting > 0)
                    GUILayout.Label("Landed vessels waiting for ground: " + waiting, _noteStyle);

                GUILayout.BeginHorizontal();
                if (GUILayout.Button("Apply")) Apply();
                if (GUILayout.Button("Disable Mod")) PRExtensions.SetModEnabled(false);
                GUILayout.EndHorizontal();
            }
            else
            {
                if (GUILayout.Button("Enable Mod"))
                {
                    ResetFields();
                    PRExtensions.SetModEnabled(true);
                }
            }

            GUI.DragWindow();
        }

        private static string Field(string label, string text)
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label(label);
            GUILayout.FlexibleSpace();
            text = GUILayout.TextField(text, GUILayout.Width(FieldWidth));
            GUILayout.EndHorizontal();
            return text;
        }

        /// <summary>
        ///     Validates the three fields and, if all are valid, saves them and applies them to the scene. If one is
        ///     invalid nothing changes and the window shows the valid interval.
        /// </summary>
        public void Apply()
        {
            _error = string.Empty;

            if (!int.TryParse(_globalRangeText.Trim(), NumberStyles.Integer, Invariant, out var global) ||
                global < PreSettings.MinGlobalRange || global > PreSettings.MaxGlobalRange)
            {
                _error = "Load range: a whole number from " + PreSettings.MinGlobalRange + " to " +
                         PreSettings.MaxGlobalRange + ".";
                return;
            }

            if (!TryParseRange(_spaceRangeText, global, out var space))
            {
                _error = "Physics range in space: " + Interval(global) + ".";
                return;
            }

            if (!TryParseRange(_landedRangeText, global, out var landed))
            {
                _error = "Physics range landed: " + Interval(global) + ".";
                return;
            }

            PreSettings.GlobalRange = global;
            PreSettings.SpacePhysicsRange = space;
            PreSettings.LandedPhysicsRange = landed;
            ResetFields();

            PreSettings.SaveConfig();
            if (PhysicsRangeExtender.Instance != null) PhysicsRangeExtender.Instance.ApplyAll();
        }

        private static bool TryParseRange(string text, int global, out float value)
        {
            // Accept a decimal comma too.
            return float.TryParse(text.Trim().Replace(',', '.'), NumberStyles.Float, Invariant, out value) &&
                   value >= PreSettings.MinPhysicsRange && value <= global;
        }

        private static string Interval(int global)
        {
            return "from " + PreSettings.MinPhysicsRange.ToString(Invariant) + " to the load range (" + global + ")";
        }

        private void ResetFields()
        {
            _globalRangeText = PreSettings.GlobalRange.ToString(Invariant);
            _spaceRangeText = PreSettings.SpacePhysicsRange.ToString(Invariant);
            _landedRangeText = PreSettings.LandedPhysicsRange.ToString(Invariant);
            _error = string.Empty;
        }

        private void CreateStyles()
        {
            _noteStyle = new GUIStyle(GUI.skin.label) { fontSize = 11, wordWrap = true };
            _errorStyle = new GUIStyle(GUI.skin.label)
            {
                wordWrap = true,
                normal = { textColor = new Color(1f, 0.6f, 0.2f) }
            };
        }

        private void SetInputLock(bool locked)
        {
            if (locked == _inputLocked) return;

            if (locked)
                InputLockManager.SetControlLock(ControlTypes.ALLBUTCAMERAS, InputLockId);
            else
                InputLockManager.RemoveControlLock(InputLockId);
            _inputLocked = locked;
        }

        private void AddToolbarButton()
        {
            if (null == this.button)
            {
                Texture buttonTexture = GameDatabase.Instance.GetTexture("PhysicsRangeExtender/Textures/icon", false);
                this.button = ApplicationLauncher.Instance.AddModApplication(
                        EnableGui, DisableGui, Dummy, Dummy, Dummy, Dummy,
                        ApplicationLauncher.AppScenes.ALWAYS, buttonTexture
                    );
            }
        }

        private void EnableGui()
        {
            GuiEnabled = true;
            Log.Debug("Showing GUI");
        }

        private void DisableGui()
        {
            GuiEnabled = false;
            Log.Debug("Hiding GUI");
        }

        private void Dummy()
        {
        }

        private void GameUiEnable()
        {
            _gameUiToggle = true;
        }

        private void GameUiDisable()
        {
            _gameUiToggle = false;
        }
    }
}
