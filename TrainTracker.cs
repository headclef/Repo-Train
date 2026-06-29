using UnityEngine;

namespace Train;

/// <summary>
/// Accrues use-points for each trainable stat from the local player's activity. Continuous
/// stats are sampled every frame (in <see cref="SampleFrame"/>) and converted to points via
/// their configured unit; discrete actions push points through <see cref="AddEvent"/> from
/// their own Harmony hooks. Everything is gated to "in a level" by the callers.
/// </summary>
internal static class TrainTracker
{
    // Sampler baseline — captured on level start / avatar change so the first frame's deltas
    // (spawning at full HP, the initial position) are never counted as activity.
    private static bool _baselined;
    private static int _prevHealth;
    private static float _prevEnergy;
    private static Vector3 _prevPos;
    private static int _prevJumpExtraCurrent;

    // Fractional accumulators for continuous stats, in raw units until they cross one point.
    private static float _healthBuf;
    private static float _sprintBuf;
    private static float _walkBuf;
    private static float _standStaminaBuf;
    private static float _crouchBuf;

    private static SaveData.TrainStat Health => SaveData.ById("Health")!;
    private static SaveData.TrainStat Sprint => SaveData.ById("SprintSpeed")!;
    private static SaveData.TrainStat Stamina => SaveData.ById("Stamina")!;
    private static SaveData.TrainStat CrouchRest => SaveData.ById("CrouchRest")!;
    private static SaveData.TrainStat ExtraJump => SaveData.ById("ExtraJump")!;

    /// <summary>Capture the current player state as the new baseline.</summary>
    internal static void Baseline()
    {
        var pc = PlayerController.instance;
        if (pc == null) { _baselined = false; return; }

        var avatar = pc.playerAvatarScript;
        _prevHealth = (avatar != null && avatar.playerHealth != null) ? avatar.playerHealth.health : 0;
        _prevEnergy = pc.EnergyCurrent;
        _prevPos = pc.transform.position;
        _prevJumpExtraCurrent = pc.JumpExtraCurrent;
        _baselined = true;
    }

    /// <summary>Drop the baseline so the next frame re-captures it (e.g. on a scene switch).</summary>
    internal static void Invalidate() => _baselined = false;

    /// <summary>
    /// Per-frame sampling of continuous stats. Called from PlayerController.Update for the
    /// local player; self-baselines and bails out when not in a level or when dead.
    /// </summary>
    internal static void SampleFrame()
    {
        if (!Train.Enabled.Value) return;
        if (!SemiFunc.RunIsLevel()) { _baselined = false; return; }

        var pc = PlayerController.instance;
        if (pc == null) return;

        var avatar = pc.playerAvatarScript;
        if (avatar == null || avatar.deadSet) { _baselined = false; return; }

        if (!_baselined) { Baseline(); return; }

        // ── Health: count every HP that moves — damage taken AND healing received, any source ──
        if (avatar.playerHealth != null)
        {
            int hp = avatar.playerHealth.health;
            int delta = hp - _prevHealth;
            // Skip implausibly large jumps (a respawn, or max-health changing from an upgrade).
            if (delta != 0 && Mathf.Abs(delta) <= avatar.playerHealth.maxHealth)
                _healthBuf += Mathf.Abs(delta);
            _prevHealth = hp;
        }

        // ── Horizontal movement distance → Sprint while sprinting, else Stamina (walking) ──
        Vector3 pos = pc.transform.position;
        Vector3 flat = pos - _prevPos;
        flat.y = 0f;
        float dist = flat.magnitude;
        _prevPos = pos;
        if (dist > 0.001f && dist < 10f) // ignore teleports and first-frame spikes
        {
            if (pc.sprinting) _sprintBuf += dist;
            else if (pc.moving) _walkBuf += dist;
        }

        // ── Stamina regen → Crouch Rest while crouching, else standing Stamina ──
        float energy = pc.EnergyCurrent;
        float energyDelta = energy - _prevEnergy;
        _prevEnergy = energy;
        if (energyDelta > 0f)
        {
            if (pc.Crouching) _crouchBuf += energyDelta;
            else _standStaminaBuf += energyDelta;
        }

        // ── Extra (air) jump: JumpExtraCurrent drops by one each time an air jump is spent ──
        int jec = pc.JumpExtraCurrent;
        if (jec < _prevJumpExtraCurrent)
            SaveData.AddProgress(ExtraJump, _prevJumpExtraCurrent - jec);
        _prevJumpExtraCurrent = jec;

        // Convert whole accumulated units into points.
        FlushBuffer(ref _healthBuf, Health, Train.HealthHpPerPoint.Value);
        FlushBuffer(ref _sprintBuf, Sprint, Train.SprintMetresPerPoint.Value);
        FlushBuffer(ref _walkBuf, Stamina, Train.WalkMetresPerPoint.Value);
        FlushBuffer(ref _standStaminaBuf, Stamina, Train.StandingStaminaPerPoint.Value);
        FlushBuffer(ref _crouchBuf, CrouchRest, Train.CrouchStaminaPerPoint.Value);
    }

    private static void FlushBuffer(ref float buf, SaveData.TrainStat stat, float unit)
    {
        if (unit <= 0f) { buf = 0f; return; }
        if (buf < unit) return;
        int points = (int)(buf / unit);
        buf -= points * unit;
        SaveData.AddProgress(stat, points);
    }

    /// <summary>Add discrete-event points for a stat — only while in a level.</summary>
    internal static void AddEvent(string statId, int points)
    {
        if (!Train.Enabled.Value) return;
        if (!SemiFunc.RunIsLevel()) return;
        var stat = SaveData.ById(statId);
        if (stat != null)
            SaveData.AddProgress(stat, points);
    }
}
