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
        private const string FieldNamePrefix = "PRE_field_";

        private ApplicationLauncherButton _button;

        // The window is open. Each flight scene starts with it closed, like its toolbar button.
        private bool _guiEnabled;

        private bool _gameUiToggle;
        private Rect _windowRect;
        private bool _inputLocked;

        // One of the window's text fields has the keyboard (as of the last window pass).
        private bool _typing;

        private string _globalRangeText = string.Empty;
        private string _spaceRangeText = string.Empty;
        private string _landedRangeText = string.Empty;
        private string _error = string.Empty;

        private GUIStyle _noteStyle;
        private GUIStyle _errorStyle;

        private static readonly CultureInfo Invariant = CultureInfo.InvariantCulture;

        private void Start()
        {
            _windowRect = new Rect(Screen.width - WindowWidth - 40, 100, WindowWidth, 0);
            GameEvents.onHideUI.Add(GameUiDisable);
            GameEvents.onShowUI.Add(GameUiEnable);
            _gameUiToggle = true;
            ResetFields();

            // The launcher may not exist yet when the flight scene starts: add the button when it is ready,
            // and drop it when the launcher goes away.
            GameEvents.onGUIApplicationLauncherReady.Add(AddToolbarButton);
            GameEvents.onGUIApplicationLauncherUnreadifying.Add(OnLauncherUnreadifying);
            if (ApplicationLauncher.Ready) AddToolbarButton();
        }

        private void OnDestroy()
        {
            SetInputLock(false);
            GameEvents.onShowUI.Remove(GameUiEnable);
            GameEvents.onHideUI.Remove(GameUiDisable);
            GameEvents.onGUIApplicationLauncherReady.Remove(AddToolbarButton);
            GameEvents.onGUIApplicationLauncherUnreadifying.Remove(OnLauncherUnreadifying);
            RemoveToolbarButton();
        }

        // ReSharper disable once InconsistentNaming
        private void OnGUI()
        {
            if (!PreSettings.ConfigLoaded) return;

            var visible = _guiEnabled && _gameUiToggle;
            if (visible)
            {
                if (_noteStyle == null) CreateStyles();

                // A click outside the window ends typing in its fields, and so releases the input lock.
                if (_typing && Event.current.type == EventType.MouseDown &&
                    !_windowRect.Contains(Event.current.mousePosition))
                    GUI.FocusControl(null);

                // Height 0: GUILayout grows the window to fit, so it also shrinks when content goes away.
                _windowRect.height = 0;
                _windowRect = GUILayout.Window(GetInstanceID(), _windowRect, GuiWindow, "Physics Range Extender",
                    GUILayout.Width(WindowWidth));
            }
            else
            {
                _typing = false;
            }

            // Keep clicks and typed digits (action groups) from reaching the game behind the window: while the
            // mouse is over it, and while one of its fields has the keyboard, wherever the mouse is.
            if (Event.current.type == EventType.Repaint)
                SetInputLock(visible && (_typing || _windowRect.Contains(Event.current.mousePosition)));
        }

        private void GuiWindow(int windowId)
        {
            if (PreSettings.ModEnabled)
            {
                _globalRangeText = Field("Load range (km)", _globalRangeText);
                _spaceRangeText = Field("Physics range in orbit (km)", _spaceRangeText);
                _landedRangeText = Field("Physics range landed (km)", _landedRangeText);
                GUILayout.Label("Flying and sub-orbital vessels run physics within the load range.", _noteStyle);

                if (_error.Length > 0) GUILayout.Label(_error, _errorStyle);

                var waiting = PhysicsRangeExtender.Instance != null
                    ? PhysicsRangeExtender.Instance.VesselsWaitingForGround
                    : 0;
                if (waiting > 0)
                    GUILayout.Label("Landed vessels waiting for ground: " + waiting, _noteStyle);

                GUILayout.BeginHorizontal();
                if (GUILayout.Button("Apply")) Apply();
                if (GUILayout.Button("Disable Mod")) PRExtensions.SetByUser(false);
                GUILayout.EndHorizontal();
            }
            else
            {
                if (GUILayout.Button("Enable Mod"))
                {
                    ResetFields();
                    PRExtensions.SetByUser(true);
                }
            }

            _typing = GUI.GetNameOfFocusedControl().StartsWith(FieldNamePrefix, StringComparison.Ordinal);
            GUI.DragWindow();
        }

        private static string Field(string label, string text)
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label(label);
            GUILayout.FlexibleSpace();
            GUI.SetNextControlName(FieldNamePrefix + label);
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
                _error = "Physics range in orbit: " + Interval(global) + ".";
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

            if (PhysicsRangeExtender.Instance != null) PhysicsRangeExtender.Instance.ApplyAll();
            PreSettings.SaveConfig();
        }

        private static bool TryParseRange(string text, int global, out float value)
        {
            // No thousands separators and no decimal comma: "1,000" must not become 1.
            return float.TryParse(text.Trim(), NumberStyles.Float, Invariant, out value) &&
                   value >= PreSettings.MinPhysicsRange && value <= global;
        }

        private static string Interval(int global)
        {
            return "from " + PreSettings.MinPhysicsRange.ToString(Invariant) + " to the load range (" + global +
                   "), with a dot for decimals";
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
            if (_button != null || ApplicationLauncher.Instance == null) return;

            var texture = GameDatabase.Instance.GetTexture("PhysicsRangeExtender/Textures/icon", false);
            _button = ApplicationLauncher.Instance.AddModApplication(
                EnableGui, DisableGui, Dummy, Dummy, Dummy, Dummy,
                ApplicationLauncher.AppScenes.FLIGHT | ApplicationLauncher.AppScenes.MAPVIEW, texture);

            // The button is created off; keep the window in step with it.
            if (_guiEnabled) _button.SetTrue(false);
        }

        private void RemoveToolbarButton()
        {
            if (_button == null) return;

            if (ApplicationLauncher.Instance != null) ApplicationLauncher.Instance.RemoveModApplication(_button);
            _button = null;
        }

        private void OnLauncherUnreadifying(GameScenes scene)
        {
            RemoveToolbarButton();
        }

        private void EnableGui()
        {
            _guiEnabled = true;
            Log.Debug("Showing GUI");
        }

        private void DisableGui()
        {
            _guiEnabled = false;
            SetInputLock(false);
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
