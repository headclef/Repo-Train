using System;
using System.Collections.Generic;
using System.IO;
using BepInEx.Configuration;
using UnityEngine;

#pragma warning disable CS8618

namespace Train;

/// <summary>
/// Persistent, global Train progress — one lifetime use-point counter per trainable stat,
/// stored in its own config file (mirrors Improve's SaveData layout). Trained levels are
/// always DERIVED from progress plus the live Improve ceiling, never stored.
/// </summary>
public static class SaveData
{
    // ── Threshold curve (locked design) ──
    // threshold(L) = L × factor(L), factor stepping up at the tier walls.
    public const int Tier1MaxLevel = 3;   // levels 1..3
    public const int Tier2MaxLevel = 10;  // levels 4..10
    public const int Tier1Factor = 9;     // 3²
    public const int Tier2Factor = 25;    // 5²
    public const int Tier3Factor = 100;   // 10² — levels 11+

    /// <summary>
    /// Metadata for a single trainable stat. The same short key Character Stats uses
    /// (<see cref="CsKey"/>) is what every consumer mod queries; the StatsManager dictionary
    /// field (<see cref="GameField"/>) is where the game stores the real upgrade level.
    /// </summary>
    public sealed class TrainStat
    {
        public readonly string Id;         // save key, e.g. "TumbleLaunch"
        public readonly string CsKey;      // Character Stats key, e.g. "Launch"
        public readonly string GameField;  // StatsManager field, e.g. "playerUpgradeLaunch"
        public readonly string Display;    // UI label, e.g. "Tumble Launch"
        public ConfigEntry<int> Progress;  // lifetime use-points

        public TrainStat(string id, string csKey, string gameField, string display)
        {
            Id = id;
            CsKey = csKey;
            GameField = gameField;
            Display = display;
        }
    }

    // The 11 trainable stats. Map Player Count and Death Head Battery are intentionally absent
    // (no natural in-field action) — they remain point-buyable in Improve only.
    public static readonly TrainStat[] Stats =
    {
        new("TumbleLaunch", "Launch",       "playerUpgradeLaunch",      "Tumble Launch"),
        new("ExtraJump",    "Extra Jump",   "playerUpgradeExtraJump",   "Extra Jump"),
        new("Throw",        "Throw",        "playerUpgradeThrow",       "Throw"),
        new("GrabRange",    "Range",        "playerUpgradeRange",       "Grab Range"),
        new("GrabStrength", "Strength",     "playerUpgradeStrength",    "Grab Strength"),
        new("TumbleClimb",  "Tumble Climb", "playerUpgradeTumbleClimb", "Tumble Climb"),
        new("TumbleWings",  "Tumble Wings", "playerUpgradeTumbleWings", "Tumble Wings"),
        new("Health",       "Health",       "playerUpgradeHealth",      "Health"),
        new("SprintSpeed",  "Speed",        "playerUpgradeSpeed",       "Sprint Speed"),
        new("Stamina",      "Stamina",      "playerUpgradeStamina",     "Stamina"),
        new("CrouchRest",   "Crouch Rest",  "playerUpgradeCrouchRest",  "Crouch Rest"),
    };

    private static readonly Dictionary<string, TrainStat> _byId = new();
    private static ConfigFile _save;

    internal static void Initialize()
    {
        _save = new ConfigFile(
            Path.Combine(Application.persistentDataPath, "REPOModData/Train/save.cfg"),
            false);

        // We mutate progress from a per-frame sampler, so disable write-on-set and instead
        // flush the file ourselves on a timer / scene switch (see Flush). This keeps the
        // save off the hot path while never losing more than the last commit interval.
        _save.SaveOnConfigSet = false;

        foreach (var s in Stats)
        {
            s.Progress = _save.Bind("Progress", s.Id, 0,
                $"Lifetime use-points accumulated for {s.Display}.");
            _byId[s.Id] = s;
        }
    }

    /// <summary>Persist any pending progress to disk. Cheap when nothing changed.</summary>
    public static void Flush()
    {
        try { _save?.Save(); }
        catch { /* disk hiccup — next flush will retry */ }
    }

    public static TrainStat? ById(string id) => _byId.TryGetValue(id, out var s) ? s : null;

    // ── Progress mutation ──

    /// <summary>Add lifetime use-points to a stat (no-op for non-positive amounts).</summary>
    public static void AddProgress(TrainStat stat, int points)
    {
        if (points <= 0) return;
        stat.Progress.Value += points;
    }

    /// <summary>Wipe all trained progress. Trained levels fall back to 0.</summary>
    public static void ResetAll()
    {
        foreach (var s in Stats)
            s.Progress.Value = 0;
        Flush();
        Train.Logger.LogInfo("Train progress reset — all stats wiped.");
    }

    // ── Threshold math ──

    public static int Factor(int level)
    {
        if (level <= 0) return 0;
        if (level <= Tier1MaxLevel) return Tier1Factor;
        if (level <= Tier2MaxLevel) return Tier2Factor;
        return Tier3Factor;
    }

    /// <summary>Total use-points required to BE at the given trained level.</summary>
    public static int Threshold(int level) => level <= 0 ? 0 : level * Factor(level);

    /// <summary>
    /// Highest trained level whose threshold the progress has reached. Thresholds are strictly
    /// increasing (9,18,27,100,…,250,1100,…), so a simple climb is correct.
    /// </summary>
    public static int LevelFromProgress(int progress)
    {
        if (progress <= 0) return 0;
        int level = 0;
        while (Threshold(level + 1) <= progress) level++;
        return level;
    }

    // ── Improve ceiling ──

    /// <summary>The live Improve level — the hard cap on every trained stat.</summary>
    public static int ImproveLevel()
    {
        try { return global::Improve.SaveData.CurrentLevel(); }
        catch { return 0; }
    }

    /// <summary>Level earned purely through use, ignoring the Improve cap.</summary>
    public static int TrainedLevel(TrainStat stat) => LevelFromProgress(stat.Progress.Value);

    /// <summary>Effective level after the Improve ceiling is applied.</summary>
    public static int EffectiveLevel(TrainStat stat) =>
        Math.Min(TrainedLevel(stat), ImproveLevel());

    /// <summary>True when the trained level is being held back by the Improve ceiling.</summary>
    public static bool IsCapped(TrainStat stat) => TrainedLevel(stat) > ImproveLevel();

    /// <summary>Use-points still needed to reach the next trained level.</summary>
    public static int ProgressToNext(TrainStat stat)
    {
        int next = TrainedLevel(stat) + 1;
        return Math.Max(Threshold(next) - stat.Progress.Value, 0);
    }
}
