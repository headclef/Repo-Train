using System.Collections;
using HarmonyLib;
using UnityEngine;

namespace Train.Patches;

// ── Improve bridge: the persistent apply layer ──
// Improve's ApplyStats reconciles every stat as base + GetAllocationForStat(stat) into the
// live StatsManager playerUpgrade* dictionaries (and strips it again before the game saves).
// Adding the trained effective level to that allocation rides the whole pipeline: one writer,
// one reconcile, one save-strip — and the game's own spawn derivation (LateStart/SetupDone/
// Fetch re-read the dictionaries every level, since player objects are recreated per scene)
// turns the trained levels into native, felt effects for every stat.
[HarmonyPatch]
internal static class TrainImproveBridgePatch
{
    [HarmonyPatch(typeof(global::Improve.SaveData), "GetAllocationForStat")]
    [HarmonyPostfix]
    private static void GetAllocationForStat_Postfix(string statName, ref int __result)
    {
        if (!Train.Enabled.Value) return;
        __result += SaveData.EffectiveLevelForGameField(statName);
    }
}

// ── Per-frame sampler: drives continuous-stat counting for the local player ──
[HarmonyPatch]
internal static class TrainSamplerPatch
{
    [HarmonyPatch(typeof(PlayerController), "Update")]
    [HarmonyPostfix]
    private static void PlayerController_Update_Postfix(PlayerController __instance)
    {
        if (__instance != PlayerController.instance) return;
        TrainTracker.SampleFrame();
    }
}

// ── Lifecycle: baseline, periodic apply + flush, and teardown ──
[HarmonyPatch]
internal static class TrainLifecyclePatch
{
    private static Coroutine? _watchdog;
    private static Coroutine? _deferredApply;

    /// <summary>
    /// Local player added to a level — re-baseline the sampler and the transient applier
    /// (after a short defer so Improve's reconcile and the game's spawn derivation settle
    /// first), and start the watchdog.
    /// </summary>
    [HarmonyPatch(typeof(StatsManager), nameof(StatsManager.PlayerAdd))]
    [HarmonyPostfix]
    private static void PlayerAdd_Postfix(string _steamID)
    {
        if (!SemiFunc.RunIsLevel()) return;
        if (PlayerAvatar.instance == null) return;
        if (_steamID != PlayerAvatar.instance.steamID) return;

        TrainTracker.Invalidate();
        ScheduleApply();
        StartWatchdog();
    }

    /// <summary>
    /// Leaving the current scene: persist progress and stand the coroutines down. Nothing to
    /// reverse — the transient component effects die with the scene, and the dictionaries
    /// (which Improve keeps asserted, trained levels included via the bridge) drive the next
    /// spawn's derivation.
    /// </summary>
    [HarmonyPatch(typeof(SemiFunc), nameof(SemiFunc.OnSceneSwitch))]
    [HarmonyPrefix]
    private static void OnSceneSwitch_Prefix()
    {
        SaveData.Flush();
        TrainApplier.Invalidate();
        TrainTracker.Invalidate();
        StopWatchdog();
        StopDeferredApply();
    }

    /// <summary>
    /// The run was reset (team wipe / new game). Trained progress is lifetime and is
    /// deliberately preserved (like Improve's haul) — only the in-scene tracking is dropped.
    /// </summary>
    [HarmonyPatch(typeof(RunManager), nameof(RunManager.ResetProgress))]
    [HarmonyPostfix]
    private static void ResetProgress_Postfix()
    {
        StopWatchdog();
        StopDeferredApply();
        TrainApplier.Invalidate();
        SaveData.Flush();
        TrainTracker.Invalidate();
    }

    private static void ScheduleApply()
    {
        StopDeferredApply();
        _deferredApply = Train.Instance.StartCoroutine(DeferredApply());
    }

    private static void StopDeferredApply()
    {
        if (_deferredApply != null)
        {
            Train.Instance.StopCoroutine(_deferredApply);
            _deferredApply = null;
        }
    }

    private static IEnumerator DeferredApply()
    {
        // Give Improve's deferred reconcile (also 3 frames after PlayerAdd) and the game's
        // spawn derivation time to land the dictionary values on the fresh components, so the
        // spawn baseline reflects what was actually delivered.
        yield return null;
        yield return null;
        yield return null;
        yield return null;

        _deferredApply = null;
        if (!SemiFunc.RunIsLevel()) yield break;

        TrainTracker.Baseline();
        TrainApplier.BaselineSpawn();
    }

    private static void StartWatchdog()
    {
        StopWatchdog();
        _watchdog = Train.Instance.StartCoroutine(WatchdogLoop());
    }

    private static void StopWatchdog()
    {
        if (_watchdog != null)
        {
            Train.Instance.StopCoroutine(_watchdog);
            _watchdog = null;
        }
    }

    private static IEnumerator WatchdogLoop()
    {
        yield return new WaitForSeconds(1f);

        while (SemiFunc.RunIsLevel())
        {
            // Re-assert trained levels (an Improve ding or a freshly crossed threshold lifts
            // them) and persist whatever the sampler accrued this interval.
            TrainApplier.Apply();
            SaveData.Flush();
            yield return new WaitForSeconds(1f);
        }

        _watchdog = null;
    }
}

// ── Discrete-action events ──
[HarmonyPatch]
internal static class TrainEventPatch
{
    /// <summary>A player-initiated tumble — the "tumble launch" action.</summary>
    [HarmonyPatch(typeof(PlayerTumble), nameof(PlayerTumble.TumbleRequest))]
    [HarmonyPostfix]
    private static void TumbleRequest_Postfix(PlayerTumble __instance, bool _isTumbling, bool _playerInput)
    {
        if (!_isTumbling || !_playerInput) return;
        var localTumble = PlayerController.instance != null
            ? PlayerController.instance.playerAvatarScript?.tumble
            : null;
        if (__instance != localTumble) return;
        TrainTracker.AddEvent("TumbleLaunch", 1);
    }

    /// <summary>A grab started — counts toward both Grab Range and Grab Strength.</summary>
    [HarmonyPatch(typeof(PhysGrabber), nameof(PhysGrabber.PhysGrabStarted))]
    [HarmonyPostfix]
    private static void PhysGrabStarted_Postfix(PhysGrabber __instance)
    {
        if (__instance != PhysGrabber.instance) return;
        TrainTracker.AddEvent("GrabRange", 1);
        TrainTracker.AddEvent("GrabStrength", 1);
    }

    /// <summary>A tumble-climb link was made.</summary>
    [HarmonyPatch(typeof(PhysGrabber), nameof(PhysGrabber.GrabLinkClimb))]
    [HarmonyPostfix]
    private static void GrabLinkClimb_Postfix(PhysGrabber __instance)
    {
        if (__instance != PhysGrabber.instance) return;
        TrainTracker.AddEvent("TumbleClimb", 1);
    }
}
