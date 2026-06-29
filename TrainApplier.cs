using System.Collections.Generic;
using static Character_Stats.Character_Stats;

namespace Train;

/// <summary>
/// Pushes trained levels into the game in two layers, mirroring how Berserk avoids ever
/// touching the StatsManager dictionaries (which Improve owns and strips from the save):
///
///   1. <b>Overlay</b> — every trained level is registered as a Character Stats temporary
///      bonus, so it stacks on top of Improve's real values at read time and every consumer
///      (Armor, Increase Tumble Damage, Constitution, Agility, UI) reflects it instantly.
///      Never written to StatsManager, so it can never be absorbed by Improve or saved.
///
///   2. <b>Native effect</b> — for the stats whose live effect the game does NOT re-derive
///      from the dictionary every frame, we mirror the game's own apply on the live component
///      (Strength → physGrabber.grabStrength, Launch → tumble.tumbleLaunch), tracking exactly
///      what we added so it can be reversed precisely. Other stats' native effects are driven
///      entirely by the overlay through the consumer mods for now.
///
/// Known limitation: the Character Stats overlay stores one value per (player, key), so while
/// Berserk is actively overriding Strength/Launch its value wins for those two keys until the
/// next apply tick re-asserts the trained total. The trained levels for every other key are
/// unaffected.
/// </summary>
internal static class TrainApplier
{
    // Last overlay level we wrote per Character Stats key — lets us skip redundant writes.
    private static readonly Dictionary<string, int> _overlay = new();

    // Native component effect — the exact avatar we boosted and the deltas we added, so the
    // reversal removes precisely what was applied even after a respawn swaps the avatar.
    private static PlayerAvatar? _nativeAvatar;
    private static float _appliedGrabDelta;
    private static int _appliedLaunchDelta;

    /// <summary>
    /// Reconcile the overlay and the native effect with the current trained levels. Idempotent
    /// and self-correcting — safe to call on a timer, on level start, and after a network sync.
    /// </summary>
    internal static void Apply()
    {
        if (!Train.Enabled.Value)
        {
            ClearAll();
            return;
        }

        string? steamId = GetLocalSteamId();
        if (string.IsNullOrEmpty(steamId))
            return;

        // 1) Overlay every trainable stat with its effective (Improve-capped) level.
        foreach (var s in SaveData.Stats)
            SetOverlay(steamId!, s.CsKey, SaveData.EffectiveLevel(s));

        // 2) Native component effect for the local avatar (only while in a level).
        ApplyNative();
    }

    private static void SetOverlay(string steamId, string csKey, int level)
    {
        if (_overlay.TryGetValue(csKey, out int prev) && prev == level)
            return;
        SetTemporaryBonus(steamId, csKey, level); // amount 0 clears the bonus
        _overlay[csKey] = level;
    }

    private static void ApplyNative()
    {
        var avatar = PlayerController.instance != null
            ? PlayerController.instance.playerAvatarScript
            : null;

        // No live, alive avatar in a level → make sure nothing is left applied.
        if (avatar == null || avatar.deadSet || !SemiFunc.RunIsLevel())
        {
            ReverseNative();
            return;
        }

        int strLevel = SaveData.EffectiveLevel(SaveData.ById("GrabStrength")!);
        int launchLevel = SaveData.EffectiveLevel(SaveData.ById("TumbleLaunch")!);
        float grabDelta = 0.2f * strLevel;

        // Only touch the components when something actually changed (avatar swap, or a level
        // crossed a threshold / the Improve cap moved). Reverse the old delta first so we never
        // stack our own bonus on top of itself.
        if (_nativeAvatar != avatar ||
            _appliedGrabDelta != grabDelta ||
            _appliedLaunchDelta != launchLevel)
        {
            ReverseNative();

            if (avatar.physGrabber != null)
                avatar.physGrabber.grabStrength += grabDelta;
            if (avatar.tumble != null)
                avatar.tumble.tumbleLaunch += launchLevel;

            _nativeAvatar = avatar;
            _appliedGrabDelta = grabDelta;
            _appliedLaunchDelta = launchLevel;
        }
    }

    private static void ReverseNative()
    {
        var avatar = _nativeAvatar;
        if (avatar != null)
        {
            if (avatar.physGrabber != null)
                avatar.physGrabber.grabStrength -= _appliedGrabDelta;
            if (avatar.tumble != null)
                avatar.tumble.tumbleLaunch -= _appliedLaunchDelta;
        }

        _nativeAvatar = null;
        _appliedGrabDelta = 0f;
        _appliedLaunchDelta = 0;
    }

    /// <summary>
    /// Reverse the native effect on a scene switch — the avatar is about to be torn down, and
    /// the next level re-applies from scratch. The overlay is intentionally left in place so
    /// trained levels keep showing in the truck and shop.
    /// </summary>
    internal static void ReverseNativeForSceneSwitch() => ReverseNative();

    /// <summary>Tear everything down — reverse the native effect and drop every overlay.</summary>
    internal static void ClearAll()
    {
        ReverseNative();

        string? steamId = GetLocalSteamId();
        if (!string.IsNullOrEmpty(steamId))
            foreach (var s in SaveData.Stats)
                ClearTemporaryBonus(steamId!, s.CsKey);

        _overlay.Clear();
    }
}
