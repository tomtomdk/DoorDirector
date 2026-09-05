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
        private const string DebugSection = "3 - Diagnostics";
        private const string UseDoorRpc = "UseDoor";

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
            if (Player.m_localPlayer && _debugHotkey.Value.IsDown())
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

        private static bool TryGetRule(string prefab, out bool inverted, out float delay)
        {
            inverted = false;
            delay = _defaultDelay.Value;

            if (!_enabled.Value || _ignored.Contains(prefab))
            {
                return false;
            }

            bool configured = _included.Contains(prefab) || _inverted.Contains(prefab) || _delays.ContainsKey(prefab);
            if (_onlyConfigured.Value && !configured)
            {
                return false;
            }

            inverted = _inverted.Contains(prefab);
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

            string prefab = GetPrefabName(door);
            if (!TryGetRule(prefab, out bool inverted, out float delay) || !IsPhysicallyOpen(currentState, inverted))
            {
                return;
            }

            long generation = timerState.Generation;
            long rulesRevision = _rulesRevision;
            bool reopenForward = previousState > 0;
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
                !TryGetRule(prefab, out bool inverted, out _) ||
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
            GameObject hovered = player ? player.GetHoverObject() : null;
            Door door = hovered ? hovered.GetComponentInParent<Door>() : null;
            if (!door && hovered)
            {
                door = hovered.GetComponentInChildren<Door>();
            }

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
