# Train — Design Specification

> Status: **v1 implemented — pending in-game verification** (see §11–§12)
> Repo: https://github.com/headclef/Repo-Train
> Author: headclef · Part of the R.E.P.O. mod suite

Train is a companion to **Improve**, but a *separate* mod with a different purpose. Where
Improve is the **wealth axis** (bank your lifetime haul → earn points → *buy* stat levels),
Train is the **mastery axis** (*use* a stat in the field → earn that stat's level through
practice). The two are orthogonal and stack: your final in‑game stat is
`base + Improve allocation + Train level`.

Improve does **not** depend on Train. Train depends on Improve (it reads Improve's level as
the trainable ceiling) and on Character Stats (it surfaces trained levels to the rest of the
suite). The dependency is strictly one‑directional.

---

## 1. Core mechanic

- Every trainable stat has a **lifetime progress counter** (in "use‑points"), persisted
  globally across all saves — exactly like Improve's `LifetimeHaul`.
- Using a stat in the field adds use‑points to that stat's counter.
- A counter crossing a threshold raises that stat's **trained level**.
- **The Improve level is the hard ceiling** for *every* trained stat: a stat's effective
  trained level is `min(level-from-progress, Improve level)`.
- Progress keeps accumulating even while capped, so when the Improve ceiling rises, every
  stat that already met the higher thresholds jumps up immediately.

This double‑gates the late game: a high trained level needs **both** a high Improve level
(grind your haul) **and** a huge amount of practice. Early levels are cheap and fluid.

---

## 2. Threshold formula

`threshold(L)` is the **total** accumulated use‑points needed to *be* at trained level `L`
(a single absolute threshold, not a cumulative sum — same shape as Improve's level curve):

```
threshold(L) = L × factor(L)

factor(L) = 9    for L in 1..3     (3²)
          = 25   for L in 4..10    (5²)
          = 100  for L >= 11       (10²)
```

| Level | Calc | Total use‑points |
|------:|------|-----------------:|
| 1  | 1 × 9   | 9     |
| 2  | 2 × 9   | 18    |
| 3  | 3 × 9   | 27    |
| 4  | 4 × 25  | **100** |
| 5  | 5 × 25  | 125   |
| 6  | 6 × 25  | 150   |
| 7  | 7 × 25  | 175   |
| 8  | 8 × 25  | 200   |
| 9  | 9 × 25  | 225   |
| 10 | 10 × 25 | 250   |
| 11 | 11 × 100 | **1100** |
| 12 | 12 × 100 | 1200  |
| …  | L × 100  | …     |

The tier boundaries are intentional **walls**: L3→L4 jumps 27→100 (≈3.7×), L10→L11 jumps
250→1100 (4.4×). Tier 1 is the smooth on‑ramp; tier 2 is the first real grind; tier 3 is the
"almost nobody gets here" zone.

Thresholds are strictly increasing, so the level is derived by a simple climb:

```csharp
static int Factor(int level) => level <= 3 ? 9 : (level <= 10 ? 25 : 100);
static int Threshold(int level) => level <= 0 ? 0 : level * Factor(level);

// Highest level whose threshold the progress has reached.
static int LevelFromProgress(int progress)
{
    int level = 0;
    while (Threshold(level + 1) <= progress) level++;
    return level;
}

// What the game/consumers actually see, capped by Improve.
static int EffectiveLevel(int progress, int improveLevel)
    => Math.Min(LevelFromProgress(progress), improveLevel);
```

### Worked example (matches the agreed design)

1. Improve level 1.
2. Player tumble‑launches 17 times across a run, then dies. **17 is saved** (counting is
   lifetime, never reset by death — see §5).
3. Trained Launch level = `LevelFromProgress(17)` = 1 (9 ≤ 17 < 18), capped at Improve 1 → **1**.
4. Next run, the first launch makes it 18 → meets `threshold(2)=18`. But Improve is still 1,
   so effective Launch stays **1** (ceiling). Progress (18) is retained.
5. Player reaches Improve level 2.
6. Every stat whose progress already meets `threshold(2)` instantly becomes level 2 — Launch
   included (18 ≥ 18). No extra grind needed; the ceiling simply lifted.

---

## 3. Trainable stats — counting actions & unit conversions

Use‑points are stat‑normalized. Discrete (event) stats earn **1 point per event**. Continuous
stats convert raw activity → points via a per‑stat **unit** (config‑tunable), so the single
9/18/27… table stays meaningful for everyone (otherwise you'd out‑sprint the table in one run).

| Stat (Improve name) | Character Stats key | Counted action | Type | Default unit (1 point =) |
|---|---|---|---|---|
| Tumble Launch | `Launch` | each launch | event | 1 launch |
| Extra Jump | `Extra Jump` | each **air/extra** jump (not the ground jump) | event | 1 jump |
| Throw | `Throw` | each throw | event | 1 throw |
| Grab Range | `Range` | each grab (optionally only beyond a min distance) | event | 1 grab |
| Grab Strength | `Strength` | each grab of a heavy object (or time under load) | event/time | 1 heavy grab |
| Tumble Climb | `Tumble Climb` | each tumble‑climb | event | 1 climb |
| Tumble Wings | `Tumble Wings` | time gliding with wings | time | 2 s glide |
| **Health** | `Health` | damage taken **+** health restored, **any source** | HP | 25 HP cycled |
| **Sprint Speed** | `Speed` | distance while sprinting | metres | 15 m sprinted |
| **Stamina** | `Stamina` | distance walked **+** stamina regained while standing | m + stamina | 15 m walked / 50 stamina |
| **Crouch Rest** | `Crouch Rest` | stamina regained while crouching | stamina | 50 stamina |

Notes:
- **Health** source is deliberately agnostic (Constitution regen, truck heal on return,
  health packs, Medic revive — all count). Cleanest capture: a per‑frame sampler on the local
  player's health field — `+damageDelta` when it drops, `+healDelta` when it rises — so every
  source is counted uniformly without hooking each one.
- **Stamina vs Crouch Rest vs Sprint** partition cleanly with no double counting: standing
  regen → Stamina, crouching regen → Crouch Rest, walking → Stamina, sprinting → Sprint.
- The `~` units are starting points; all are `ConfigEntry`s and will be tuned in playtesting.

### Excluded stats

- **Map Player Count** — no natural in‑field action; not trainable.
- **Death Head Battery** — its only "use" is being dead; rewarding death is off‑theme.
  Excluded.

Both are still present in Improve (point‑buyable there); they simply have no Train track.

---

## 4. Counting — what increments, and how it's stored

A single **`PlayerController.Update` postfix on the local player** (the Berserk pattern) drives
all counting:

- **Discrete events** are incremented from their own Harmony hooks (launch / jump / throw /
  grab / climb fire `Track(stat, 1)`), guarded by §5's "in a level" check.
- **Continuous stats** are sampled each frame in the Update postfix: accumulate raw activity
  (metres, HP, stamina) into an in‑memory fractional buffer per stat; when a buffer crosses one
  unit, flush whole points into the saved counter.

Counters are stored as integers (post‑conversion **use‑points**) in a dedicated save file,
mirroring Improve's `SaveData`:

```
%AppData%LocalLow/semiwork/Repo/REPOModData/Train/save.cfg

[Progress]
Launch       = 0
ExtraJump    = 0
Throw        = 0
GrabRange    = 0
GrabStrength = 0
TumbleClimb  = 0
TumbleWings  = 0
Health       = 0
SprintSpeed  = 0
Stamina      = 0
CrouchRest   = 0
```

The saved counter is the single source of truth; trained levels are always *derived* from it
plus the live Improve ceiling — never stored.

**Flush strategy:** do **not** write the config every frame (BepInEx saves the file on each
`.Value` set). Buffer in memory and flush to the `ConfigEntry`s on a periodic tick (~1 s, like
Improve's watchdog) **and** on scene switch / before a death is finalized, so an in‑level death
never loses progress (the 17‑launches case).

---

## 5. Counting rules

- **In a level only.** Increment only while `SemiFunc.RunIsLevel()` is true. No counting in the
  truck, the shop, between maps, or in menus.
- **Never reset by death or run end.** Counters are lifetime and global. Dying mid‑level keeps
  everything earned so far (flushed before death finalizes).
- **Global across all saves**, identical to Improve.
- A full **Reset Train** action (menu) wipes all counters; nothing else touches them.

---

## 6. Applying trained levels to the game

This is the crux. There are two consumers of a trained level:

1. **Suite mods** that read levels from Character Stats (UI, Armor, Increase Tumble Damage,
   Constitution, Agility) — these benefit automatically the moment the level is in the overlay.
2. **The game's native effect** (actual max health, sprint speed, grab strength, tumble launch
   force, …) — the game derives these from its own `StatsManager.playerUpgrade*` dictionaries.

### Why Train must NOT write to the StatsManager dictionaries

Improve owns those dictionaries via an idempotent reconcile + watchdog (`SaveData.ApplyStats`,
every 0.5 s) and a save‑strip guard. If Train wrote its bonus on top, Improve's reconcile would
see `cur > lastWritten`, fold Train's bonus into *its* base, and the save‑strip would then bake
Train's bonus into the `.es3` file as if it were the true base — re‑applied and **compounded**
every save/quit/relaunch. This is exactly the leak Berserk was built to avoid. So, like Berserk,
**Train never writes to the `playerUpgrade*` dictionaries.**

### The two‑part application (Berserk pattern, generalized)

**Part A — consumer‑visible level (overlay).** Register each trained level as a Character Stats
temporary bonus so every consumer sees it instantly:

```csharp
using static Character_Stats.Character_Stats;
SetTemporaryBonus(steamId, "Launch", trainedLaunchLevel);   // and the rest
```

This overlay is additive at read time, is **never** written to StatsManager, and is invisible
to Improve and to the save file. Unlike Berserk's transient toggle, Train's overlay is
persistent‑but‑reapplied: set/update it on level start and whenever a trained level changes;
clear and re‑apply across scene switches so it never dangles (Berserk's `OnSceneSwitch` →
`ForceDeactivate` discipline).

**Part B — native effect.** Because the overlay is invisible to the game, Train must apply the
real effect itself, for each trainable stat. Preferred mechanism — **reuse the game's own apply
helper via a synchronous, momentary dict inflation** (no leak, no hardcoded formulas):

```
read V = playerUpgradeX[steamId]            // already includes Improve's level
playerUpgradeX[steamId] = V + trainedLevel  // momentary
call the game's UpdateX...RightAway(...)     // game sets the component from the dict
playerUpgradeX[steamId] = V                  // restore immediately
```

Done synchronously inside one method, this is race‑free: Unity is single‑threaded, so Improve's
0.5 s watchdog cannot interleave between the inflate and the restore. The component keeps the
boosted value; the dictionary (and therefore Improve and the save) is untouched.

Fallback for any stat lacking a clean `UpdateX…RightAway` helper — mirror the formula directly
on the live component, as Berserk already does for two of them:

```
Grab Strength:  physGrabber.grabStrength += 0.2 × level
Tumble Launch:  tumble.tumbleLaunch      += level
```

Either way, Train must **reverse** its component deltas precisely when a level drops or on scene
switch (track the exact avatar + deltas applied, Berserk‑style), so it never corrupts a base
value after a respawn.

### Application timing & ordering

- Apply **after** Improve has applied, so the dict already carries Improve's level when Train
  inflates for the helper reuse (yielding `base + Improve + Train`). Train runs its own
  short‑deferred apply + light watchdog (idempotent) like Improve, and re‑applies on
  `StatsManager.PlayerAdd`, after `PunManager.ReceiveSyncData`, and on its periodic tick.
- The Improve ceiling is read **live** each tick via `Improve.SaveData.CurrentLevel()`, so a
  ding in Improve level lifts the cap (and unlocks already‑earned levels) without a relaunch.

---

## 7. Suite integration (what comes for free)

Because trained levels go through the Character Stats overlay, the rest of the suite reacts with
zero extra code:

- **UI** — shows boosted levels; a "Train" line/section can be added later.
- **Increase Tumble Damage** — reads `Launch` → trained Launch raises tumble *damage*.
- **Armor** — reads `Health`/`Strength` → trained levels raise damage reduction.
- **Constitution** — reads `Health` → trained Health raises passive regen.
- **Agility** — reads `Stamina`/`Crouch Rest`/`Speed` → trained levels raise stamina regen.

Train is fully **client‑side** and safe in any lobby — it only ever reads/boosts the local
player and never writes networked state.

---

## 8. Menu — the "Train" tab

The player views progress in a Train view reached from the same menus Improve uses (main /
escape / lobby). To honor the one‑directional dependency (Improve must not reference Train),
Train adds the tab itself, two viable ways:

1. **Sibling button (simplest, robust):** Train registers its own MenuLib button ("Train")
   next to Improve's via `MenuAPI.AddElementToMainMenu/EscapeMenu/LobbyMenu`, opening a Train
   page. No patching of Improve internals.
2. **Embedded tab (literal "second tab"):** Train Harmony‑postfixes Improve's page creation
   (`ImproveMenu.OpenImproveMenu` / `CreateStatsPage`) to inject a "Train" button into Improve's
   panel. Train already hard‑depends on Improve, so patching it is legitimate; Improve stays
   unaware of Train.

Recommended: ship (1) first (clean, decoupled); optionally add (2) for the in‑Improve‑menu feel.

The Train page lists each trainable stat with: current trained level, effective level (after the
Improve cap, flagged when cap‑limited), progress toward the next threshold, and the cap. Plus a
**Reset Train** confirm button (Improve's `MenuAPI.OpenPopup` pattern).

---

## 9. Dependencies, project, build

**Plugin attributes** (`Train.cs`):

```csharp
[BepInPlugin("headclef.Train", "Train", "1.0.0")]
[BepInDependency("headclef.Improve",       BepInDependency.DependencyFlags.HardDependency)]
[BepInDependency("headclef.CharacterStats", BepInDependency.DependencyFlags.HardDependency)]
[BepInDependency("nickklmao.menulib",       BepInDependency.DependencyFlags.HardDependency)]
```

**`manifest.json`** (pin current suite versions — see the version‑consistency rule):

```json
{
    "name": "Train",
    "version_number": "1.0.0",
    "website_url": "https://github.com/headclef/Repo-Train",
    "description": "Train your character stats by using them in the field — capped by your Improve level — for R.E.P.O.",
    "dependencies": [
        "BepInEx-BepInExPack-5.4.2100",
        "nickklmao-MenuLib-2.5.1",
        "headclef-CharacterStats-1.2.0",
        "headclef-Improve-1.1.3"
    ]
}
```

**`Train.csproj`** — same template as the other mods (netstandard2.1, the three NuGet feeds, the
BepInEx/Unity/GameLibs/MenuLib package refs), plus **compile‑only file references** to the built
Character Stats and Improve DLLs (the established cross‑mod pattern — file references + `.slnx`
`BuildDependency`, never `ProjectReference`, to keep Thunderstore output isolated):

```xml
<ItemGroup>
  <Reference Include="Character Stats">
    <HintPath>..\Character Stats\bin\Debug\netstandard2.1\Character Stats.dll</HintPath>
    <Private>false</Private>
  </Reference>
  <Reference Include="Improve">
    <HintPath>..\Improve\bin\Debug\netstandard2.1\Improve.dll</HintPath>
    <Private>false</Private>
  </Reference>
</ItemGroup>
```

Add Train to `Repo.slnx` with `BuildDependency` entries on **Improve** and **Character Stats**
so they build first. Train calls `Improve.SaveData.CurrentLevel()` (public static) for the cap
and `Character_Stats.*` for the overlay.

---

## 10. Config (initial)

| Section | Key | Default | Purpose |
|---|---|---|---|
| General | Enabled | true | Master toggle. |
| Units | Health HP Per Point | 25 | HP cycled per use‑point. |
| Units | Sprint Metres Per Point | 15 | Sprinted metres per point. |
| Units | Walk Metres Per Point | 15 | Walked metres per point (Stamina). |
| Units | Standing Stamina Per Point | 50 | Standing regen per point (Stamina). |
| Units | Crouch Stamina Per Point | 50 | Crouching regen per point (Crouch Rest). |
| Units | Wings Seconds Per Point | 2 | Glide seconds per point. |
| Curve | Tier1 Base / Tier2 Base / Tier3 Base | 9 / 25 / 100 | Threshold factors (advanced). |

The threshold *bases* are exposed for advanced tuning but default to the locked 9 / 25 / 100.

---

## 11. Implementation status (v1)

Done (builds clean against R.E.P.O.GameLibs.Steam, deployed to BepInEx/plugins/headclef-Train):
- [x] `Train.cs` - plugin entry, hard dependencies, config (toggle + unit conversions).
- [x] `SaveData.cs` - progress counters in `REPOModData/Train/save.cfg`, the threshold/level
      math, batched `Flush`, `ResetAll`.
- [x] `TrainApplier.cs` - overlay for all 11 stats + native effect for Strength/Launch.
- [x] `TrainTracker.cs` - per-frame sampler (Health, Sprint, Stamina walk + standing regen,
      Crouch Rest, Extra Jump) + discrete-event entry point.
- [x] `Patches/TrainPatch.cs` - `PlayerController.Update` sampler; `PlayerTumble.TumbleRequest`
      (launch), `PhysGrabber.PhysGrabStarted` (grab -> Range + Strength),
      `PhysGrabber.GrabLinkClimb` (tumble-climb); lifecycle (`PlayerAdd`, `ReceiveSyncData`,
      `OnSceneSwitch`, `ResetProgress`) with a 1 s apply/flush watchdog.
- [x] `TrainMenu.cs` - MenuLib "Train" button beside Improve's + scrollable progress page.
- [x] `manifest.json`, `LICENSE`, `README.md`; added to `Repo.slnx` (BuildDependency on
      Improve + Character Stats).

Confirmed game symbols (via a Mono.Cecil dump of the GameLibs assembly):
`PlayerTumble.TumbleRequest(bool _isTumbling, bool _playerInput)`, `.isTumbling`,
`.tumbleLaunch`; `PhysGrabber.PhysGrabStarted()`, `.GrabLinkClimb(Vector3)`, `.grabStrength`,
`.instance`; `PlayerHealth.health`/`.maxHealth`; `PlayerController.sprinting`/`.Crouching`/
`.moving`/`.EnergyCurrent`/`.JumpExtraCurrent`/`.playerAvatarScript`.

Deferred (no clean hook confirmed - left out of v1, documented):
- [ ] **Throw** counting - no obvious PhysGrabber throw method; needs the in-game throw path.
- [ ] **Tumble Wings** counting - needs a confirmed "gliding active" signal.
- [ ] **Native effects beyond Strength/Launch** - Health/Stamina/Sprint/Jump/Range/Climb
      currently affect only the consumer mods + UI through the overlay, not the game's intrinsic
      values. Adding them needs each stat's per-level apply formula verified in-game.

Packaging follow-up:
- [ ] `icon.png`; extend `pack-dists.ps1` (TS root layout + NX `BepInEx/plugins/headclef-Train/`).

---

## 12. In-game verification checklist

The mechanics are locked; these assumptions were made where the game's behaviour could not be
confirmed without running it, and should be checked during testing:

1. **Tumble Launch hook** - is one player-initiated `TumbleRequest(true, true)` exactly one
   "launch"? (Flagship action - verify the count rises once per launch.)
2. **Grab counting** - `PhysGrabStarted` feeds both Grab Range and Grab Strength 1:1; confirm it
   fires once per grab, then decide whether Strength should be heavy-only.
3. **Tumble Climb** - confirm `GrabLinkClimb` corresponds to the tumble-climb action.
4. **Distance source** - `PlayerController.transform.position` is assumed to be the player's
   world position for Sprint/Stamina distance; verify the metres feel right, then tune units.
5. **Health** - confirm damage and every heal source (Constitution, truck, packs, Medic revive)
   register, and the respawn guard isn't over-counting.
6. **Native effect** - confirm trained Strength/Launch add real grab/launch force on top of
   Improve, and that Berserk + Train compose without leaking into the save (overlay is
   last-writer-wins per key, so Berserk transiently overrides Strength/Launch while active).

Unit values (§3) are tunable from the config; the threshold bases (9/25/100) are constants for v1.
