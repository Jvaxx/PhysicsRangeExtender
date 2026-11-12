using System;
using System.Globalization;
using KSP.UI.Screens;
using UnityEngine;

// ReSharper disable NotAccessedField.Local

namespace PhysicsRangeExtender
{
    [KSPAddon(KSPAddon.Startup.Flight, false)]
    public class Gui : MonoBehaviour
    {
        private const float WindowWidth = 250;
        private const float DraggableHeight = 40;
        private const float LeftIndent = 12;
        private const float ContentTop = 20;
        public static Gui Fetch;
        public static bool GuiEnabled;
        private ApplicationLauncherButton button = null;
        private readonly float _incrButtonWidth = 26;
        private readonly float contentWidth = WindowWidth - 2 * LeftIndent;
        private readonly float entryHeight = 20;
        
        private bool _gameUiToggle;
        private string _guiGlobalRangeForVessels = String.Empty;

        private float _windowHeight = 250;
        private Rect _windowRect;

        private void Awake()
        {
            if (Fetch)
                Destroy(Fetch);

            Fetch = this;
        }

        private void Start()
        {
            _windowRect = new Rect(Screen.width - WindowWidth - 40, 100, WindowWidth, _windowHeight);
            AddToolbarButton();
            GameEvents.onHideUI.Add(GameUiDisable);
            GameEvents.onShowUI.Add(GameUiEnable);
            _gameUiToggle = true;
            _guiGlobalRangeForVessels = PreSettings.GlobalRange.ToString();
        }

        private void OnDestroy()
        {
            GameEvents.onShowUI.Remove(GameUiEnable);
            GameEvents.onHideUI.Remove(GameUiDisable);
            ApplicationLauncher.Instance.RemoveModApplication(this.button);
            this.button = null;
        }

        // ReSharper disable once InconsistentNaming
        private void OnGUI()
        {
            if (!PreSettings.ConfigLoaded) return;
            if (GuiEnabled && _gameUiToggle)
                _windowRect = GUI.Window(320, _windowRect, GuiWindow, "");
        }

        private void GuiWindow(int windowId)
        {
            GUI.DragWindow(new Rect(0, 0, WindowWidth, DraggableHeight));
            float line = 0;
           

            DrawTitle();
            line++;
            if (PreSettings.ModEnabled)
            {
                DrawGlobalVesselRange(line);
                line++;
                DrawSaveButton(line);
                line++;
            }
            DisableMod(line);


            _windowHeight = ContentTop + line * entryHeight + entryHeight + entryHeight;
            _windowRect.height = _windowHeight;
        }

        private void DisableMod(float line)
        {
            var saveRect = new Rect(LeftIndent, ContentTop + line * entryHeight, contentWidth, entryHeight);


            if (PreSettings.ModEnabled)
            {
                if (GUI.Button(saveRect, "Disable Mod"))
                    PRExtensions.SetModEnabled(false);
            }
            else
            {
                if (GUI.Button(saveRect, "Enable Mod"))
                    PRExtensions.SetModEnabled(true);
            }
        }


        private void DrawGlobalVesselRange(float line)
        {
            var leftLabel = new GUIStyle
            {
                alignment = TextAnchor.UpperLeft,
                normal = {textColor = Color.white}
            };

            GUI.Label(new Rect(LeftIndent, ContentTop + line * entryHeight, 60, entryHeight), "Global range:",
                leftLabel);
            float textFieldWidth = 42;
            var fwdFieldRect = new Rect(LeftIndent + contentWidth - textFieldWidth - 3 * _incrButtonWidth,
                  ContentTop + line * entryHeight, textFieldWidth, entryHeight);
            _guiGlobalRangeForVessels = GUI.TextField(fwdFieldRect, _guiGlobalRangeForVessels);
          
        }

        private void DrawSaveButton(float line)
        {
            var saveRect = new Rect(LeftIndent, ContentTop + line * entryHeight, contentWidth / 2, entryHeight);
            if (GUI.Button(saveRect, "Apply new range"))
                Apply();
        }

        public void Apply()
        {
            if (int.TryParse(_guiGlobalRangeForVessels, out var parseGlobalRange))
            {
                PreSettings.GlobalRange = parseGlobalRange;
                _guiGlobalRangeForVessels = PreSettings.GlobalRange.ToString();
            }

            PreSettings.SaveConfig();
            PhysicsRangeExtender.UpdateRanges(true);
        }

        private void DrawTitle()
        {
            var centerLabel = new GUIStyle
            {
                alignment = TextAnchor.UpperCenter,
                normal = {textColor = Color.white}
            };
            var titleStyle = new GUIStyle(centerLabel)
            {
                fontSize = 10,
                alignment = TextAnchor.MiddleCenter
            };
            GUI.Label(new Rect(0, 0, WindowWidth, 20), "Physics Range Extender", titleStyle);
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