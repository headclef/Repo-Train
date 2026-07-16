using System;
using System.Collections.Generic;

namespace Train;

/// <summary>
/// Makes trained levels FELT in the game, in two layers that mirror how the game itself
/// applies upgrades:
///
///   1. <b>Persistent layer (via Improve)</b> — Train postfixes Improve's
///      <c>GetAllocationForStat</c> (see <c>TrainImproveBridgePatch</c>) so every Improve
///      reconcile writes <c>base + allocation + trained level</c> into the StatsManager
///      <c>playerUpgrade*</c> dictionaries. The player objects are recreated every level and
///      re-derive their live values from those dictionaries on spawn (PlayerController /
///      PhysGrabber / PlayerAvatar <c>LateStart</c>, PlayerTumble <c>SetupDone</c>,
///      PlayerHealth <c>Fetch</c>), so trained levels land natively for every stat — and
///      Improve's single reconcile/save-strip machinery keeps the dictionaries clean with
///      exactly one writer. Character Stats reads the same dictionaries, so consumer mods
///      (Armor, Increase Tumble Damage, Constitution, Agility, UI) see trained levels too.
///
///   2. <b>Transient layer (this class)</b> — a level earned MID-level is in the dictionaries
///      within Improve's next reconcile, but the game only re-derives live values on spawn.
///      To make the ding felt immediately, the diff between the current effective level and
///      the level the spawn derivation delivered is applied straight to the live components,
///      using the game's own per-level formulas (PunManager's <c>Update…RightAway</c>).
///      Levels never drop mid-level (progress and the Improve cap only grow), so the diff is
///      monotonic — applied once, never reversed. The components die with the scene and the
///      next spawn derives everything from the dictionaries, so the tracking simply resets.
/// </summary>
internal static class TrainApplier
{
    // Instances the current baseline belongs to — when either is recreated (level start,
    // revive), the fresh components already derived the full dictionary values, so the
    // baseline is retaken and the transient tracking starts over from zero.
    private static PlayerController? _pc;
    private static PlayerAvatar? _avatar;

    // Per stat id: the effective level the spawn derivation delivered, and the transient
    // levels this class has applied on top of it since.
    private static readonly Dictionary<string, int> _spawnLevel = new();
    private static readonly Dictionary<string, int> _appliedLevel = new();

    /// <summary>
    /// Take the spawn baseline: the dictionaries (kept current by Improve's reconcile plus our
    /// bridge) delivered the current effective levels to the freshly spawned components, so
    /// only levels earned AFTER this moment need the transient apply.
    /// </summary>
    internal static void BaselineSpawn()
    {
        var pc = PlayerController.instance;
        var avatar = pc != null ? pc.playerAvatarScript : null;
        if (pc == null || avatar == null) { Invalidate(); return; }

        _pc = pc;
        _avatar = avatar;
        _appliedLevel.Clear();
        foreach (var s in SaveData.Stats)
            _spawnLevel[s.Id] = SaveData.EffectiveLevel(s);
    }

    /// <summary>Drop the baseline (scene switch / run reset) — nothing to reverse, the player
    /// components die with the scene and the next spawn re-derives from the dictionaries.</summary>
    internal static void Invalidate()
    {
        _pc = null;
        _avatar = null;
        _spawnLevel.Clear();
        _appliedLevel.Clear();
    }

    /// <summary>
    /// Apply any levels earned since the spawn baseline to the live components. Idempotent —
    /// each earned level is applied exactly once; safe to call on a timer.
    /// </summary>
    internal static void Apply()
    {
        if (!Train.Enabled.Value) return;
        if (!SemiFunc.RunIsLevel()) return;

        // On a co-op CLIENT, Improve's StatEffectApplier (>= 1.1.8) tops the live components up
        // to the full host-unknown shortfall — base + Improve allocation + trained level — in
        // one place, because the host's dictionary sync only carries the client's purchased
        // base. It reads Improve's GetAllocationForStat, which our bridge postfix already grows
        // by the trained level, so it covers Train's levels too. Applying here as well would
        // double every mid-level training ding, so we defer entirely to it and only do our own
        // live application on the host / in single player (where the dictionary derivation
        // delivers the spawn value and this layer just fills in mid-level dings).
        if (!SemiFunc.IsMasterClientOrSingleplayer()) return;

        var pc = PlayerController.instance;
        if (pc == null) return;
        var avatar = pc.playerAvatarScript;
        if (avatar == null || avatar.deadSet) return;

        // A fresh controller/avatar already carries the dictionary values — re-baseline.
        if (_pc != pc || _avatar != avatar)
        {
            BaselineSpawn();
            return;
        }

        foreach (var s in SaveData.Stats)
        {
            if (!_spawnLevel.TryGetValue(s.Id, out int spawn)) continue;

            int want = Math.Max(0, SaveData.EffectiveLevel(s) - spawn);
            int have = _appliedLevel.TryGetValue(s.Id, out int h) ? h : 0;
            int diff = want - have;
            if (diff <= 0) continue; // never reverse mid-level; the next spawn self-corrects

            ApplyLevels(s.Id, diff, pc, avatar);
            _appliedLevel[s.Id] = want;
            Train.Logger.LogInfo($"{s.Display} trained to Lv {SaveData.EffectiveLevel(s)} — applied live (+{diff}).");
        }
    }

    /// <summary>
    /// The game's own per-level effects, mirrored from PunManager's <c>Update…RightAway</c>
    /// methods (dictionary writes excluded — Improve owns those).
    /// </summary>
    private static void ApplyLevels(string statId, int levels, PlayerController pc, PlayerAvatar avatar)
    {
        switch (statId)
        {
            case "Health":
                if (avatar.playerHealth != null)
                {
                    avatar.playerHealth.maxHealth += 20 * levels;
                    avatar.playerHealth.Heal(20 * levels, effect: false);
                    TrainTracker.Invalidate(); // don't count the level-up heal as activity
                }
                break;
            case "Stamina":
                pc.EnergyStart += 10 * levels;
                pc.EnergyCurrent = pc.EnergyStart;
                TrainTracker.Invalidate(); // don't count the level-up refill as regen
                break;
            case "ExtraJump":
                pc.JumpExtra += levels;
                break;
            case "SprintSpeed":
                pc.SprintSpeed += levels;
                pc.SprintSpeedUpgrades += levels;
                pc.playerOriginalSprintSpeed += levels;
                break;
            case "TumbleLaunch":
                if (avatar.tumble != null)
                    avatar.tumble.tumbleLaunch += levels;
                break;
            case "TumbleClimb":
                avatar.upgradeTumbleClimb += levels;
                break;
            case "TumbleWings":
                avatar.upgradeTumbleWings += levels;
                break;
            case "CrouchRest":
                avatar.upgradeCrouchRest += levels;
                break;
            case "GrabStrength":
                if (avatar.physGrabber != null)
                    avatar.physGrabber.grabStrength += 0.2f * levels;
                break;
            case "Throw":
                if (avatar.physGrabber != null)
                    avatar.physGrabber.throwStrength += 0.3f * levels;
                break;
            case "GrabRange":
                if (avatar.physGrabber != null)
                    avatar.physGrabber.grabRange += levels;
                break;
        }
    }
}
