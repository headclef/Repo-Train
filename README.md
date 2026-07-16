# Train

A [BepInEx](https://github.com/BepInEx/BepInEx) mod for **R.E.P.O.** that lets you level up
your character stats by *using* them in the field — capped by your
[Improve](https://github.com/headclef/Repo-Improve) level.

Where Improve is the **wealth axis** (bank your lifetime haul → earn points → *buy* stat
levels), Train is the **mastery axis**: practise a stat during a run and it levels itself up.
The two stack — your final in‑game stat is `base + Improve allocation + Train level`.

> Train is a companion to Improve, not a replacement. Improve does not need Train; Train reads
> your Improve level as the ceiling for everything it can train.

## What This Mod Does

- Tracks how much you **use** each trainable stat, only while you're in a level.
- Raises that stat's **trained level** when its lifetime usage crosses a threshold.
- Caps every trained level at your current **Improve level** — so practice and wealth advance
  together.
- Persists progress **globally across all saves**, and never loses it on death.
- Applies trained levels through Improve's own stat application, so they land **natively** —
  real max health, sprint speed, grab strength, launch force — and the rest of the suite
  (Armor, Increase Tumble Damage, Constitution, Agility, UI) reacts automatically. A level
  earned mid‑level is felt within about a second.

## How leveling works

Each stat has a lifetime **progress** counter measured in *use‑points*. The total progress
needed to *be* at trained level `L` is a single threshold:

```
threshold(L) = L × factor      factor = 9   for levels 1–3
                                       = 25  for levels 4–10
                                       = 100 for levels 11+
```

| Level | Total use‑points | Level | Total use‑points |
|------:|-----------------:|------:|-----------------:|
| 1 | 9   | 7  | 175  |
| 2 | 18  | 8  | 200  |
| 3 | 27  | 9  | 225  |
| 4 | 100 | 10 | 250  |
| 5 | 125 | 11 | 1100 |
| 6 | 150 | 12 | 1200 |

The tier boundaries are deliberate walls (level 3→4 jumps 27→100; level 10→11 jumps 250→1100):
early levels come fast, late levels are a serious grind. Your effective trained level is
`min(level‑from‑progress, Improve level)`, so progress you bank above the ceiling unlocks the
instant your Improve level rises.

## Trainable stats

Discrete actions earn **1 point each**; continuous stats convert raw activity to points via a
configurable unit, so the same curve is fair for everyone.

| Stat | Counted action |
|---|---|
| Tumble Launch | each launch |
| Extra Jump | each air/extra jump |
| Throw | each throw *(planned — see note below)* |
| Grab Range | each grab |
| Grab Strength | each heavy grab |
| Tumble Climb | each tumble‑climb |
| Tumble Wings | time gliding *(planned — see note below)* |
| Health | damage taken + health restored (any source) |
| Sprint Speed | distance sprinted |
| Stamina | distance walked + standing stamina regen |
| Crouch Rest | stamina regained while crouching |

> **Throw** and **Tumble Wings** don't accumulate progress yet — the stats are listed in the
> Train menu, but their counting arrives in a future version. You can still raise them
> through Improve as usual.
>
> **Map Player Count** and **Death Head Battery** are not trainable (no natural in‑field
> action). They remain point‑buyable in Improve.

Counting happens **only inside a level** — never in the truck, the shop, between maps, or in
menus — and your progress is never reset by dying or finishing a run.

## Requirements

- [BepInEx 5.x](https://github.com/BepInEx/BepInEx) installed for R.E.P.O.
- **[Improve](https://github.com/headclef/Repo-Improve)** — provides the level ceiling and
  carries trained levels into the game.
- **[MenuLib](https://thunderstore.io/c/repo/p/nickklmao/MenuLib/)** — for the Train menu.
- **[Relay](https://github.com/headclef/Repo-Relay)** — arrives automatically with Improve
  1.2.0. Train never talks to it, but it is what lets trained Grab Strength and Tumble
  Launch work while you are a co-op client.

## Installation

1. Install via **Thunderstore** (recommended) — dependencies are pulled in automatically.
2. Or manually: place `Train.dll` into your `BepInEx/plugins` folder (alongside Improve,
   Relay and MenuLib).
3. Launch the game and open the **Train** menu to watch your progress.

### Upgrading to 1.0.7

**No Train setting changes** — every option keeps its name, its place and your value, and your
training progress is untouched. 1.0.7 only follows Improve to 1.2.0, which now requires Relay.

If you play as a **co-op client** and want trained Grab Strength or Tumble Launch to actually
work, there is one switch to set, and it is not in Train — see [Multiplayer](#multiplayer).

## Multiplayer

- Train only ever reads and boosts your own local player — tracking, leveling and saving
  are fully client‑side, and it never affects other players.
- **As the host or in single player**, every trained stat applies in full.
- Safe in any lobby. As a co-op **client**, trained stats apply exactly the way Improve's
  allocations do, because they travel the same road: Health, Sprint, Stamina, Extra Jump,
  Grab Range, Tumble Climb and Crouch Rest are read on your own machine and always work.

### Trained Grab Strength and Tumble Launch, as a client

R.E.P.O. computes those two on the **host**, from the host's copy of your character — which
only knows what you purchased. No client-side mod can change that on its own.
[Relay](https://github.com/headclef/Repo-Relay) carries them there, and it arrives with
Improve 1.2.0.

**Train needs no code and no setting of its own for this.** Improve reports its allocation to
Relay, and Train's trained levels are already folded into that number, so they ride along
without Train knowing Relay exists.

What you do need, **on both your machine and the host's**:

1. Relay installed (it comes with Improve).
2. `BepInEx/config/headclef.Relay.cfg` → `[Multiplayer]` → `Enabled = true`, or the in-game
   mod config menu.

Against a host without it, nothing happens and nothing breaks — those two stats just stay
vanilla for you, exactly as before. Relay ships **off by default**; read its readme first.

## License

This project is licensed under the MIT License — see the [LICENSE](LICENSE) file for details.

---

These mods (all) generated by using AI tools. If there's anything wrong use NexusMods to give a feedback, I use there more than ThunderStore.
