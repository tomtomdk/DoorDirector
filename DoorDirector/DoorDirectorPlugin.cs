using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Runtime.CompilerServices;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;
using ServerSync;
using UnityEngine;

namespace DoorDirector
{
    [BepInPlugin(PluginGuid, PluginName, PluginVersion)]
    [BepInDependency("com.bepis.bepinex.configurationmanager", BepInDependency.DependencyFlags.SoftDependency)]
    public sealed class DoorDirectorPlugin : BaseUnityPlugin
    {
        public const string PluginGuid = "com.tomtom.doordirector";
        public const string PluginName = "DoorDirector";
        public const string PluginVersion = BuildInfo.Version;

        private const string GeneralSection = "1 - General";
        private const string RulesSection = "2 - Prefab Rules";
        private const string PlayerDoorsSection = "3 - Player Doors";
        private const string DebugSection = "4 - Diagnostics";
        private const string UseDoorRpc = "UseDoor";
        private const string ToggleDoorRpc = "DoorDirector_Toggle";
        private const string ToggleResultRpc = "DoorDirector_ToggleResult";
        private const string SetDelayRpc = "DoorDirector_SetDelay";
        private const string SetDelayResultRpc = "DoorDirector_SetDelayResult";
        private const string DoorModeZdoKey = "com.tomtom.doordirector.mode";
        private const string DoorDelayZdoKey = "com.tomtom.doordirector.delay";
        private const float MinimumDelay = 0.1f;
        private const float MaximumDelay = 3600f;
        private const int DoorModeDefault = 0;
        private const int DoorModeEnabled = 1;
        private const int DoorModeDisabled = 2;

        private static readonly ConfigSync ConfigSync = new ConfigSync(PluginGuid)
        {
            DisplayName = PluginName,
            CurrentVersion = PluginVersion,
            MinimumRequiredVersion = PluginVersion,
            ModRequired = true
        };

        private static ConditionalWeakTable<Door, DoorTimerState> _timerStates = new ConditionalWeakTable<Door, DoorTimerState>();
        private static long _rulesRevision;

        private static DoorDirectorPlugin _instance;
        private Harmony _harmony;

        private static ConfigEntry<bool> _enabled;
        private static ConfigEntry<bool> _lockConfiguration;
        private static ConfigEntry<float> _defaultDelay;
        private static ConfigEntry<bool> _onlyConfigured;
        private static ConfigEntry<string> _includedPrefabs;
        private static ConfigEntry<string> _invertedPrefabs;
        private static ConfigEntry<string> _ignoredPrefabs;
        private static ConfigEntry<string> _customDelays;
        private static ConfigEntry<bool> _allowOwnedDoorControls;
        private static ConfigEntry<float> _ownedDoorDelay;
        private static ConfigEntry<KeyboardShortcut> _ownedDoorToggleHotkey;
        private static ConfigEntry<KeyboardShortcut> _ownedDoorSetDelayHotkey;
        private static ConfigEntry<bool> _debugLogging;
        private static ConfigEntry<KeyboardShortcut> _debugHotkey;

        private static HashSet<string> _included = NewPrefabSet();
        private static HashSet<string> _inverted = NewPrefabSet();
        private static HashSet<string> _ignored = NewPrefabSet();
        private static Dictionary<string, float> _delays = NewDelayMap();

        internal static ManualLogSource LogInstance => _instance.Logger;

        private void Awake()
        {
            _instance = this;
            BindConfiguration();
            RefreshRules();

            _harmony = new Harmony(PluginGuid);
            _harmony.PatchAll(typeof(DoorDirectorPlugin).Assembly);

            _ = new Terminal.ConsoleCommand(
                "doordirector_prefab",
                "Print the prefab name of the Door currently under the crosshair.",
                (Terminal.ConsoleEvent)(args => PrintLookedAtPrefab(args.Context)));

            Logger.LogInfo($"{PluginName} {PluginVersion} loaded.");
        }

        private void OnDestroy()
        {
            _harmony?.UnpatchSelf();
            _timerStates = new ConditionalWeakTable<Door, DoorTimerState>();
            _instance = null;
        }

        private void Update()
        {
            Player player = Player.m_localPlayer;
            if (!player)
            {
                return;
            }

            if (TextInput.IsVisible() ||
                Console.IsVisible() ||
                Menu.IsVisible() ||
                InventoryGui.IsVisible() ||
                Minimap.InTextInput() ||
                (Chat.instance && Chat.instance.HasFocus()))
            {
                return;
            }

            if (_ownedDoorToggleHotkey.Value.IsDown())
            {
                ToggleLookedAtOwnedDoor(player);
            }

            if (_ownedDoorSetDelayHotkey.Value.IsDown())
            {
                PromptForLookedAtOwnedDoorDelay(player);
            }

            if (_debugHotkey.Value.IsDown())
            {
                PrintLookedAtPrefab(null);
            }
        }

        private void BindConfiguration()
        {
            _enabled = BindSynced(GeneralSection, "Enabled", true,
                "Enable DoorDirector auto-closing.");

            _lockConfiguration = Config.Bind(GeneralSection, "Lock Configuration", true,
                "Lock synchronized settings for non-admin clients when hosting a server.");
            ConfigSync.AddLockingConfigEntry(_lockConfiguration);

            _defaultDelay = BindSynced(GeneralSection, "Default Auto Close Delay", 5f,
                new ConfigDescription("Seconds before an affected open Door is closed.", new AcceptableValueRange<float>(0.1f, 3600f)));

            _onlyConfigured = BindSynced(RulesSection, "Only Affect Configured Prefabs", false,
                "When enabled, only prefab names in Included Prefabs, Inverted Prefabs, or Custom Delays are affected.");

            _includedPrefabs = BindSynced(RulesSection, "Included Prefabs", string.Empty,
                "Comma-separated prefab names that use normal open/closed semantics.");

            _invertedPrefabs = BindSynced(RulesSection, "Inverted Prefabs", string.Empty,
                "Comma-separated prefab names whose physical open/closed meaning is opposite Valheim's logical Door state.");

            _ignoredPrefabs = BindSynced(RulesSection, "Ignored Prefabs", string.Empty,
                "Comma-separated prefab names that DoorDirector must never affect. Ignored takes precedence over other rules.");

            _customDelays = BindSynced(RulesSection, "Custom Delays", string.Empty,
                "Comma-separated prefab_name=seconds entries. Example: wood_door=3,drawbridge=12");

            _allowOwnedDoorControls = BindSynced(PlayerDoorsSection, "Allow Owned Door Controls", true,
                "Allow players to toggle auto-close and set delays for individual doors and gates they built.");

            _ownedDoorDelay = BindSynced(PlayerDoorsSection, "Owned Door Auto Close Delay", 5f,
                new ConfigDescription("Default seconds before a player-enabled individual door closes.", new AcceptableValueRange<float>(MinimumDelay, MaximumDelay)));

            _ownedDoorToggleHotkey = Config.Bind(PlayerDoorsSection, "Owned Door Toggle Hotkey", new KeyboardShortcut(KeyCode.G, KeyCode.LeftShift),
                "Client-only input binding: toggle auto-close for the targeted door or gate you built.");

            _ownedDoorSetDelayHotkey = Config.Bind(PlayerDoorsSection, "Owned Door Set Delay Hotkey", new KeyboardShortcut(KeyCode.T, KeyCode.LeftShift),
                "Client-only input binding: set the auto-close delay for the targeted door or gate you built.");

            _debugLogging = Config.Bind(DebugSection, "Debug Logging", false,
                "Client-only: log exact Door prefab names and automatic close activity on this machine.");

            _debugHotkey = Config.Bind(DebugSection, "Prefab Debug Hotkey", new KeyboardShortcut(KeyCode.F7),
                "Client-only hotkey that prints the prefab name of the Door under the crosshair.");

            _enabled.SettingChanged += GameplaySettingChanged;
            _defaultDelay.SettingChanged += GameplaySettingChanged;
            _onlyConfigured.SettingChanged += GameplaySettingChanged;
            _includedPrefabs.SettingChanged += GameplaySettingChanged;
            _invertedPrefabs.SettingChanged += GameplaySettingChanged;
            _ignoredPrefabs.SettingChanged += GameplaySettingChanged;
            _customDelays.SettingChanged += GameplaySettingChanged;
            _allowOwnedDoorControls.SettingChanged += GameplaySettingChanged;
            _ownedDoorDelay.SettingChanged += GameplaySettingChanged;
        }

        private ConfigEntry<T> BindSynced<T>(string section, string key, T value, string description)
        {
            return BindSynced(section, key, value, new ConfigDescription(description));
        }

        private ConfigEntry<T> BindSynced<T>(string section, string key, T value, ConfigDescription description)
        {
            ConfigEntry<T> entry = Config.Bind(section, key, value, description);
            ConfigSync.AddConfigEntry(entry);
            return entry;
        }

        private static HashSet<string> NewPrefabSet()
        {
            return new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        }

        private static Dictionary<string, float> NewDelayMap()
        {
            return new Dictionary<string, float>(StringComparer.OrdinalIgnoreCase);
        }

        private static void RefreshRules()
        {
            _included = ParsePrefabList(_includedPrefabs.Value);
            _inverted = ParsePrefabList(_invertedPrefabs.Value);
            _ignored = ParsePrefabList(_ignoredPrefabs.Value);
            _delays = ParseDelays(_customDelays.Value);
            _rulesRevision++;
        }

        private static void GameplaySettingChanged(object sender, EventArgs eventArgs)
        {
            RefreshRules();
        }

        private static HashSet<string> ParsePrefabList(string value)
        {
            HashSet<string> result = NewPrefabSet();
            foreach (string item in (value ?? string.Empty).Split(','))
            {
                string prefab = item.Trim();
                if (prefab.Length > 0)
                {
                    result.Add(prefab);
                }
            }

            return result;
        }

        private static Dictionary<string, float> ParseDelays(string value)
        {
            Dictionary<string, float> result = NewDelayMap();
            foreach (string item in (value ?? string.Empty).Split(','))
            {
                string[] parts = item.Split(new[] { '=' }, 2);
                if (parts.Length != 2)
                {
                    if (item.Trim().Length > 0)
                    {
                        LogInstance.LogWarning($"Invalid Custom Delays entry '{item.Trim()}'. Expected prefab_name=seconds.");
                    }

                    continue;
                }

                string prefab = parts[0].Trim();
                if (prefab.Length == 0 ||
                    !float.TryParse(parts[1].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out float seconds) ||
                    seconds < 0.1f || seconds > 3600f)
                {
                    LogInstance.LogWarning($"Invalid Custom Delays entry '{item.Trim()}'. Seconds must be between 0.1 and 3600.");
                    continue;
                }

                result[prefab] = seconds;
            }

            return result;
        }

        private static string GetPrefabName(Door door)
        {
            if (!door)
            {
                return "<destroyed>";
            }

            ZNetView nview = door.GetComponent<ZNetView>();
            if (nview && nview.IsValid() && ZNetScene.instance)
            {
                GameObject prefab = ZNetScene.instance.GetPrefab(nview.GetZDO().GetPrefab());
                if (prefab)
                {
                    return prefab.name;
                }
            }

            const string cloneSuffix = "(Clone)";
            string objectName = door.gameObject.name;
            return objectName.EndsWith(cloneSuffix, StringComparison.Ordinal)
                ? objectName.Substring(0, objectName.Length - cloneSuffix.Length)
                : objectName;
        }

        private static bool TryGetRule(Door door, string prefab, out bool inverted, out float delay)
        {
            inverted = false;
            delay = _defaultDelay.Value;

            if (!_enabled.Value || _ignored.Contains(prefab))
            {
                return false;
            }

            inverted = _inverted.Contains(prefab);
            if (_allowOwnedDoorControls.Value)
            {
                ZNetView nview = door ? door.GetComponent<ZNetView>() : null;
                if (nview && nview.IsValid())
                {
                    int doorMode = nview.GetZDO().GetInt(DoorModeZdoKey, DoorModeDefault);
                    if (doorMode == DoorModeDisabled)
                    {
                        return false;
                    }

                    if (doorMode == DoorModeEnabled)
                    {
                        delay = GetOwnedDoorDelay(nview.GetZDO());
                        return true;
                    }
                }
            }

            bool configured = _included.Contains(prefab) || _inverted.Contains(prefab) || _delays.ContainsKey(prefab);
            if (_onlyConfigured.Value && !configured)
            {
                return false;
            }

            if (_delays.TryGetValue(prefab, out float customDelay))
            {
                delay = customDelay;
            }

            return true;
        }

        private static int GetLogicalState(Door door)
        {
            if (!door)
            {
                return 0;
            }

            ZNetView nview = door.GetComponent<ZNetView>();
            return nview && nview.IsValid() ? nview.GetZDO().GetInt(ZDOVars.s_state, 0) : 0;
        }

        private static bool IsPhysicallyOpen(int logicalState, bool inverted)
        {
            return inverted ? logicalState == 0 : logicalState != 0;
        }

        private static bool IsValidDelay(float delay)
        {
            return !float.IsNaN(delay) && !float.IsInfinity(delay) && delay >= MinimumDelay && delay <= MaximumDelay;
        }

        private static float GetOwnedDoorDelay(ZDO zdo)
        {
            float fallback = _ownedDoorDelay.Value;
            float delay = zdo.GetFloat(DoorDelayZdoKey, fallback);
            return IsValidDelay(delay) ? delay : fallback;
        }

        private static DoorTimerState AdvanceGeneration(Door door)
        {
            DoorTimerState state = _timerStates.GetValue(door, _ => new DoorTimerState());
            state.Generation++;
            return state;
        }

        private static void HandleDoorStateChanged(Door door, int previousState)
        {
            DoorTimerState timerState = AdvanceGeneration(door);
            int currentState = GetLogicalState(door);
            if (currentState == previousState)
            {
                return;
            }

            ScheduleIfOpen(door, currentState, timerState, previousState > 0);
        }

        private static void ScheduleIfOpen(Door door, int currentState, DoorTimerState timerState, bool reopenForward)
        {
            string prefab = GetPrefabName(door);
            if (!TryGetRule(door, prefab, out bool inverted, out float delay) || !IsPhysicallyOpen(currentState, inverted))
            {
                return;
            }

            long generation = timerState.Generation;
            long rulesRevision = _rulesRevision;
            if (_debugLogging.Value)
            {
                LogInstance.LogInfo($"Scheduling '{prefab}' to close in {delay.ToString(CultureInfo.InvariantCulture)}s (logical state {currentState}, inverted {inverted}).");
            }

            _instance.StartCoroutine(CloseAfterDelay(door, prefab, currentState, generation, rulesRevision, reopenForward, delay));
        }

        private static IEnumerator CloseAfterDelay(Door door, string prefab, int scheduledState, long generation, long rulesRevision, bool reopenForward, float delay)
        {
            yield return new WaitForSeconds(delay);

            if (!door ||
                rulesRevision != _rulesRevision ||
                !_timerStates.TryGetValue(door, out DoorTimerState timerState) ||
                timerState.Generation != generation)
            {
                yield break;
            }

            int currentState = GetLogicalState(door);
            if (currentState != scheduledState ||
                !TryGetRule(door, prefab, out bool inverted, out _) ||
                !IsPhysicallyOpen(currentState, inverted))
            {
                yield break;
            }

            ZNetView nview = door.GetComponent<ZNetView>();
            if (!nview || !nview.IsValid())
            {
                yield break;
            }

            if (_debugLogging.Value)
            {
                LogInstance.LogInfo($"Auto-closing '{prefab}' through Valheim's {UseDoorRpc} RPC.");
            }

            nview.InvokeRPC(UseDoorRpc, reopenForward);
        }

        private static Door GetLookedAtDoor(Player player)
        {
            GameObject hovered = player ? player.GetHoverObject() : null;
            Door door = hovered ? hovered.GetComponentInParent<Door>() : null;
            return !door && hovered ? hovered.GetComponentInChildren<Door>() : door;
        }

        private static Piece GetDoorPiece(Door door)
        {
            if (!door)
            {
                return null;
            }

            Piece piece = door.GetComponentInParent<Piece>();
            return piece ? piece : door.GetComponentInChildren<Piece>();
        }

        private static void ToggleLookedAtOwnedDoor(Player player)
        {
            if (!TryGetOwnedDoorTarget(player, out _, out ZNetView nview))
            {
                return;
            }

            nview.InvokeRPC(ToggleDoorRpc, player.GetPlayerID());
        }

        private static void PromptForLookedAtOwnedDoorDelay(Player player)
        {
            if (!TryGetOwnedDoorTarget(player, out Door door, out ZNetView nview))
            {
                return;
            }

            if (!TextInput.instance)
            {
                ShowHudMessage("DoorDirector: the delay input is not available right now.");
                return;
            }

            float currentDelay = GetOwnedDoorDelay(nview.GetZDO());
            TextInput.instance.RequestText(new DoorDelayTextReceiver(door, player.GetPlayerID(), currentDelay), "DoorDirector delay in seconds", 7);
        }

        private static bool TryGetOwnedDoorTarget(Player player, out Door door, out ZNetView nview)
        {
            door = GetLookedAtDoor(player);
            nview = null;
            if (!door)
            {
                ShowHudMessage("DoorDirector: no Door, gate, or bridge is under the crosshair.");
                return false;
            }

            if (!_enabled.Value || !_allowOwnedDoorControls.Value)
            {
                ShowHudMessage("DoorDirector: individual door controls are disabled by the server.");
                return false;
            }

            string prefab = GetPrefabName(door);
            if (_ignored.Contains(prefab))
            {
                ShowHudMessage($"DoorDirector: '{prefab}' is ignored by the server.");
                return false;
            }

            Piece piece = GetDoorPiece(door);
            if (!piece || !piece.IsCreator())
            {
                ShowHudMessage("DoorDirector: you can only change doors and gates you built.");
                return false;
            }

            nview = door.GetComponent<ZNetView>();
            if (!nview || !nview.IsValid())
            {
                ShowHudMessage("DoorDirector: the targeted door is not network-ready.");
                return false;
            }

            return true;
        }

        private static void RegisterDoorRpcs(Door door)
        {
            ZNetView nview = door ? door.GetComponent<ZNetView>() : null;
            if (!nview || !nview.IsValid())
            {
                return;
            }

            nview.Register<long>(ToggleDoorRpc, (sender, playerId) => HandleToggleDoorRequest(door, sender, playerId));
            nview.Register<int, float>(ToggleResultRpc, (sender, result, delay) => HandleToggleDoorResult(door, result, delay));
            nview.Register<long, float>(SetDelayRpc, (sender, playerId, delay) => HandleSetDoorDelayRequest(door, sender, playerId, delay));
            nview.Register<int, float>(SetDelayResultRpc, (sender, result, delay) => HandleSetDoorDelayResult(door, result, delay));
        }

        private static void HandleToggleDoorRequest(Door door, long sender, long playerId)
        {
            ZNetView nview = door ? door.GetComponent<ZNetView>() : null;
            if (!nview || !nview.IsValid() || !nview.IsOwner())
            {
                return;
            }

            int result;
            float delay = GetOwnedDoorDelay(nview.GetZDO());
            string prefab = GetPrefabName(door);
            Piece piece = GetDoorPiece(door);

            if (!_enabled.Value || !_allowOwnedDoorControls.Value)
            {
                result = ToggleResultDisabledByServer;
            }
            else if (_ignored.Contains(prefab))
            {
                result = ToggleResultIgnored;
            }
            else if (!piece || piece.GetCreator() == 0L || piece.GetCreator() != playerId)
            {
                result = ToggleResultNotCreator;
            }
            else
            {
                bool currentlyEnabled = TryGetRule(door, prefab, out _, out _);
                bool enable = !currentlyEnabled;
                nview.GetZDO().Set(DoorModeZdoKey, enable ? DoorModeEnabled : DoorModeDisabled);

                DoorTimerState timerState = AdvanceGeneration(door);
                if (enable)
                {
                    int currentState = GetLogicalState(door);
                    ScheduleIfOpen(door, currentState, timerState, currentState > 0);
                }

                result = enable ? ToggleResultEnabled : ToggleResultDisabled;
                if (_debugLogging.Value)
                {
                    LogInstance.LogInfo($"Player-owned override for '{prefab}' set to {(enable ? "enabled" : "disabled")}.");
                }
            }

            nview.InvokeRPC(sender, ToggleResultRpc, result, delay);
        }

        private static void HandleSetDoorDelayRequest(Door door, long sender, long playerId, float requestedDelay)
        {
            ZNetView nview = door ? door.GetComponent<ZNetView>() : null;
            if (!nview || !nview.IsValid() || !nview.IsOwner())
            {
                return;
            }

            int result;
            string prefab = GetPrefabName(door);
            Piece piece = GetDoorPiece(door);
            if (!_enabled.Value || !_allowOwnedDoorControls.Value)
            {
                result = ToggleResultDisabledByServer;
            }
            else if (_ignored.Contains(prefab))
            {
                result = ToggleResultIgnored;
            }
            else if (!piece || piece.GetCreator() == 0L || piece.GetCreator() != playerId)
            {
                result = ToggleResultNotCreator;
            }
            else if (!IsValidDelay(requestedDelay))
            {
                result = SetDelayResultInvalid;
            }
            else
            {
                nview.GetZDO().Set(DoorDelayZdoKey, requestedDelay);
                nview.GetZDO().Set(DoorModeZdoKey, DoorModeEnabled);

                DoorTimerState timerState = AdvanceGeneration(door);
                int currentState = GetLogicalState(door);
                ScheduleIfOpen(door, currentState, timerState, currentState > 0);

                result = SetDelayResultSuccess;
                if (_debugLogging.Value)
                {
                    LogInstance.LogInfo($"Player-owned delay for '{prefab}' set to {requestedDelay.ToString(CultureInfo.InvariantCulture)}s.");
                }
            }

            nview.InvokeRPC(sender, SetDelayResultRpc, result, requestedDelay);
        }

        private const int ToggleResultEnabled = 0;
        private const int ToggleResultDisabled = 1;
        private const int ToggleResultNotCreator = 2;
        private const int ToggleResultDisabledByServer = 3;
        private const int ToggleResultIgnored = 4;
        private const int SetDelayResultSuccess = 0;
        private const int SetDelayResultInvalid = 5;

        private static void HandleToggleDoorResult(Door door, int result, float delay)
        {
            string prefab = GetPrefabName(door);
            string message;
            switch (result)
            {
                case ToggleResultEnabled:
                    message = $"DoorDirector: auto-close enabled for '{prefab}' ({delay.ToString(CultureInfo.InvariantCulture)}s).";
                    break;
                case ToggleResultDisabled:
                    message = $"DoorDirector: auto-close disabled for '{prefab}'.";
                    break;
                case ToggleResultNotCreator:
                    message = "DoorDirector: you can only toggle doors and gates you built.";
                    break;
                case ToggleResultIgnored:
                    message = $"DoorDirector: '{prefab}' is ignored by the server.";
                    break;
                default:
                    message = "DoorDirector: individual door controls are disabled by the server.";
                    break;
            }

            ShowHudMessage(message);
        }

        private static void HandleSetDoorDelayResult(Door door, int result, float delay)
        {
            string prefab = GetPrefabName(door);
            string message;
            switch (result)
            {
                case SetDelayResultSuccess:
                    message = $"DoorDirector: auto-close enabled for '{prefab}' at {delay.ToString(CultureInfo.InvariantCulture)}s.";
                    break;
                case ToggleResultNotCreator:
                    message = "DoorDirector: you can only change doors and gates you built.";
                    break;
                case ToggleResultIgnored:
                    message = $"DoorDirector: '{prefab}' is ignored by the server.";
                    break;
                case SetDelayResultInvalid:
                    message = $"DoorDirector: delay must be between {MinimumDelay.ToString(CultureInfo.InvariantCulture)} and {MaximumDelay.ToString(CultureInfo.InvariantCulture)} seconds.";
                    break;
                default:
                    message = "DoorDirector: individual door controls are disabled by the server.";
                    break;
            }

            ShowHudMessage(message);
        }

        private sealed class DoorDelayTextReceiver : TextReceiver
        {
            private readonly Door _door;
            private readonly long _playerId;
            private readonly float _currentDelay;

            public DoorDelayTextReceiver(Door door, long playerId, float currentDelay)
            {
                _door = door;
                _playerId = playerId;
                _currentDelay = currentDelay;
            }

            public string GetText()
            {
                return _currentDelay.ToString(CultureInfo.InvariantCulture);
            }

            public void SetText(string text)
            {
                bool parsed = float.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out float delay) ||
                              float.TryParse(text, NumberStyles.Float, CultureInfo.CurrentCulture, out delay);
                if (!parsed || !IsValidDelay(delay))
                {
                    ShowHudMessage($"DoorDirector: delay must be between {MinimumDelay.ToString(CultureInfo.InvariantCulture)} and {MaximumDelay.ToString(CultureInfo.InvariantCulture)} seconds.");
                    return;
                }

                ZNetView nview = _door ? _door.GetComponent<ZNetView>() : null;
                if (!nview || !nview.IsValid())
                {
                    ShowHudMessage("DoorDirector: the targeted door is no longer available.");
                    return;
                }

                nview.InvokeRPC(SetDelayRpc, _playerId, delay);
            }
        }

        private static void ShowHudMessage(string message)
        {
            LogInstance.LogInfo(message);
            if (MessageHud.instance)
            {
                MessageHud.instance.ShowMessage(MessageHud.MessageType.TopLeft, message, 0, null, false);
            }
        }

        private static void LogInteraction(Door door)
        {
            if (_debugLogging.Value)
            {
                LogInstance.LogInfo($"Door interaction prefab: '{GetPrefabName(door)}'");
            }
        }

        private static void PrintLookedAtPrefab(Terminal terminal)
        {
            string message;
            Player player = Player.m_localPlayer;
            Door door = GetLookedAtDoor(player);

            if (door)
            {
                message = $"DoorDirector prefab: {GetPrefabName(door)}";
            }
            else
            {
                message = "DoorDirector: no Door, gate, or bridge is under the crosshair.";
            }

            LogInstance.LogInfo(message);
            terminal?.AddString(message);
            if (MessageHud.instance)
            {
                MessageHud.instance.ShowMessage(MessageHud.MessageType.TopLeft, message, 0, null, false);
            }
        }

        private sealed class DoorTimerState
        {
            public long Generation;
        }

        [HarmonyPatch(typeof(Door), "Awake")]
        private static class DoorAwakePatch
        {
            private static void Postfix(Door __instance)
            {
                RegisterDoorRpcs(__instance);
            }
        }

        [HarmonyPatch(typeof(Door), nameof(Door.Interact))]
        private static class DoorInteractPatch
        {
            private static void Prefix(Door __instance, bool hold)
            {
                if (!hold)
                {
                    LogInteraction(__instance);
                }
            }
        }

        [HarmonyPatch(typeof(Door), "RPC_UseDoor")]
        private static class DoorUseRpcPatch
        {
            private static void Prefix(Door __instance, out int __state)
            {
                __state = GetLogicalState(__instance);
            }

            private static void Postfix(Door __instance, int __state)
            {
                HandleDoorStateChanged(__instance, __state);
            }
        }
    }
}
