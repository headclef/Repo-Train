using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;
using UnityEngine;

namespace Train;

[BepInPlugin(PluginGuid, PluginName, PluginVersion)]
[BepInDependency("headclef.Improve", BepInDependency.DependencyFlags.HardDependency)]
[BepInDependency("nickklmao.menulib", BepInDependency.DependencyFlags.HardDependency)]
public class Train : BaseUnityPlugin
{
    private const string PluginGuid = "headclef.Train";
    private const string PluginName = "Train";
    private const string PluginVersion = "1.0.4";

    internal static Train Instance { get; private set; } = null!;
    internal new static ManualLogSource Logger => Instance._logger;
    private ManualLogSource _logger => base.Logger;
    internal Harmony? Harmony { get; set; }

    // â”€â”€ Config â”€â”€
    internal static ConfigEntry<bool> Enabled = null!;

    // Continuous-stat unit conversions: how much raw activity equals one use-point. Discrete
    // stats (launches, jumps, throws, grabs, climbs) always earn 1 point per event.
    internal static ConfigEntry<float> HealthHpPerPoint = null!;
    internal static ConfigEntry<float> SprintMetresPerPoint = null!;
    internal static ConfigEntry<float> WalkMetresPerPoint = null!;
    internal static ConfigEntry<float> StandingStaminaPerPoint = null!;
    internal static ConfigEntry<float> CrouchStaminaPerPoint = null!;
    internal static ConfigEntry<float> WingsSecondsPerPoint = null!;

    private void Awake()
    {
        Instance = this;
        this.gameObject.transform.parent = null;
        this.gameObject.hideFlags = HideFlags.HideAndDontSave;

        BindConfiguration();
        SaveData.Initialize();
        TrainMenu.Initialize();

        Harmony ??= new Harmony(Info.Metadata.GUID);
        Harmony.PatchAll();

        Logger.LogInfo($"{Info.Metadata.GUID} v{Info.Metadata.Version} has loaded!");
    }

    private void OnDestroy()
    {
        Harmony?.UnpatchSelf();
    }

    private void BindConfiguration()
    {
        Enabled = Config.Bind("General", "Enabled", true,
            "Master toggle for stat training. When off, nothing is counted or applied.");

        const string units = "Units";
        HealthHpPerPoint = Config.Bind(units, "Health HP Per Point", 25f,
            new ConfigDescription(
                "HP cycled (damage taken + healing received, any source) per Health use-point.",
                new AcceptableValueRange<float>(1f, 1000f)));
        SprintMetresPerPoint = Config.Bind(units, "Sprint Metres Per Point", 15f,
            new ConfigDescription(
                "Metres sprinted per Sprint Speed use-point.",
                new AcceptableValueRange<float>(1f, 1000f)));
        WalkMetresPerPoint = Config.Bind(units, "Walk Metres Per Point", 15f,
            new ConfigDescription(
                "Metres walked (not sprinting) per Stamina use-point.",
                new AcceptableValueRange<float>(1f, 1000f)));
        StandingStaminaPerPoint = Config.Bind(units, "Standing Stamina Per Point", 50f,
            new ConfigDescription(
                "Stamina regained while standing per Stamina use-point.",
                new AcceptableValueRange<float>(1f, 1000f)));
        CrouchStaminaPerPoint = Config.Bind(units, "Crouch Stamina Per Point", 50f,
            new ConfigDescription(
                "Stamina regained while crouching per Crouch Rest use-point.",
                new AcceptableValueRange<float>(1f, 1000f)));
        WingsSecondsPerPoint = Config.Bind(units, "Wings Seconds Per Point", 2f,
            new ConfigDescription(
                "Seconds gliding with Tumble Wings per use-point.",
                new AcceptableValueRange<float>(0.1f, 60f)));
    }
}
