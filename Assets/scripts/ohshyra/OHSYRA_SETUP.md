# Ohsyra: Setup and Test Guide

Files (put next to the Loki scripts): `OhsyraBoss.cs`, `OhsyraBoss.Attacks.cs`, `OhsyraBoss.Gizmos.cs`.
Reuses existing `Projectile`, `Combat.Damage`, `BoxHitbox`, `IDamageable`. Uses no input APIs.

## 0. One required edit to LokiController.cs (slow support)
Loki had no slow system, so Golden Honey Rays needs this small additive patch. Nothing else in the controller changes.

Add anywhere in the class:
```csharp
float slowUntil, slowMultiplier = 1f;
public bool IsSlowed { get { return Time.time < slowUntil; } }
/// Returns false (and does nothing) if already slowed: the slow never re-stacks or refreshes.
public bool ApplySlow(float duration, float multiplier)
{
    if (IsSlowed) return false;
    slowUntil = Time.time + duration;
    slowMultiplier = Mathf.Clamp01(multiplier);
    return true;
}
```
Replace `CurrentMoveMultiplier()`:
```csharp
float CurrentMoveMultiplier()
{
    float m = state == PlayerState.Free ? ((sword.charging || magic.charging) ? chargeMoveMultiplier : 1f) : actionMoveMultiplier;
    return IsSlowed ? m * slowMultiplier : m;
}
```
(Slows horizontal run speed only. Dash and jump are untouched.)

## 1. Hierarchy
```
Ohsyra            <- OhsyraBoss, Rigidbody2D (Kinematic), Collider2D(s) on an "Enemy" layer, Animator
  Visual          <- SpriteRenderer(s) (body, jewelry, Abebe fan: keep the fan visible in every pose)
    Torso         <- optional, assign to `torso`
    HandRight     <- empty Transform at her right palm -> `rightHand` (ray origin)
    HandLeft      <- empty Transform at her left palm  -> `leftHand`
    MirrorProp    <- golden hand mirror, disabled by default -> `mirrorProp`
```
Do NOT put Ohsyra's colliders on a layer in Loki's `enemyLayers`-excluded set: they must be IN `enemyLayers` so Loki can hit her.

Inspector: `Player Layers` = Loki's layer (e.g. Player). `Target` can stay empty (uses `LokiController.Instance`).
Set `Arena Center/Size` to match your room (gizmo shows it). Assign `Sprites` or leave empty (auto from children).
Optional prefabs: `jewelryProjectilePrefab` (visual only), `rayEffectPrefab` (1x1 sprite, pivot left-centre; scaled to ray length x thickness).
Projectiles and rays are code-built hitboxes, so the game works with no prefabs.

## 2. Animator parameters (all optional, set only if present)
- Bools: `IsFloating`, `IsStaggered`
- Float: `MoveSpeed`
- Floats (arm aim, character space, use as 2D Freeform Directional blend trees for the arm layers): `RightArmAimX/Y`, `LeftArmAimX/Y`. The script smooths these, so the arm sweeps gracefully and the ray fires when the aim arrives.
- Triggers: `FadeOut`, `Materialize`, `JewelryTelegraph`, `JewelryRelease`, `FanAttack`, `RaysStart`, `RayFireRight`, `RayFireLeft`, `RushTelegraph`, `RushSweep1`, `RushSecondTelegraph`, `RushSweep2`, `Recovery`, `StaggerStart`, `StaggerEnd`

Suggested states: Float (idle bob) / FloatMove; FadeOut -> (hidden) -> Materialize; JewelryTelegraph (arms out, holds) -> JewelryRelease; RaysStart then arms layer driven by blend tree, RayFire* as short fire poses; RushTelegraph (lean, arm back) -> RushSweep1; RushSecondTelegraph -> RushSweep2; StaggerStart -> MirrorInspect loop (while IsStaggered) -> StaggerEnd; Recovery. Every attack state needs an exit to Float. Triggers are cleared on stagger.

## 3. Behaviour summary
- States: Floating, Repositioning, Telegraphing, Attacking, Recovery, Staggered.
- One brain coroutine runs idle, then attack, so attacks cannot overlap. Stagger calls `StopAllCoroutines()`, resets triggers/arms/alpha, runs the mirror routine, resets the stagger counter, restarts the brain.
- Golden Jewelry: fade out while drifting right, reappear at arena centre X at Loki's current Y, arm telegraph, two opposite `Projectile`s.
- Golden Honey Rays: float to the exact centre, then for each `raySequence` step the arm eases to the angle, then its own ray hitbox (own hit list, own timer). Hit = damage + 0.5 s slow, skipped while Loki is already slowed or invulnerable. The default sequence is R 0, L 180, R 90, L -90. Add 45/135/-45/-135 entries for diagonals.
- Golden Rush: telegraph, snapshot Loki position, glide past, first sweep box (follows her), pause, optional second sweep (`secondSwipeChance`, `secondSwipeDelay`, own telegraph and box), recovery.
- Rays, jewelry and sweeps are not parryable (they are not `EnemyAttackHitbox` and carry no `enemyAttack`).

## 4. Testing each attack independently
Set `Auto Start` on, `Idle Before Attack` ~1 s. Use a `Forced Attack` for repeat testing, or in Play Mode right-click the component header: *Queue: Golden Jewelry / Abebe Fan / Golden Honey Rays / Golden Rush* (runs next time she is idle, never interrupts).
| Attack | Check |
|---|---|
| Jewelry | Move Loki up/down on a platform: she reappears at his Y. Two projectiles fly opposite ways, matching the gizmo boxes. |
| Rays | Hit order and arm timing match the numbered gizmo rays. Take a hit: damage once per ray; slow lasts 0.5 s and is not extended by a second hit during it. |
| Rush | Gizmo start, target, end, strike boxes. Set `Second Swipe Chance` 0 (never) and 1 (always) to verify both branches. Loki gets the telegraph and recovery windows. |
| Fan | Warning logged, animation trigger fires only. Disabled by default. |
| Stagger | Lower `Stagger Threshold` or use the *Force Stagger* menu. |

## 5. Verifying no overlap and no stagger leakage
- Watch `state` in the Inspector (runtime): it only moves along one path, and never shows two attacks at once.
- Trigger stagger mid-jewelry, mid-ray-sequence, mid-rush, mid-fade: arms reset, alpha returns to 1, no further rays/sweeps/projectile spawns, nothing resumes until the mirror ends. Already-released jewelry keeps flying unless `Clear Projectiles On Stagger` is on.
- Damage taken during stagger does not add to the counter. The counter resets to 0 when the stagger ends.

## 6. Open questions (not invented)
1. Abebe fan: activation condition, parry interaction, sequence, hitbox, damage, recovery. Hook is `FanRoutine()`; `fanHitbox` + `fanMechanicsDefined` drive its gizmo.
2. Ray slow strength (only the 0.5 s duration was given). `raySlowMultiplier` 0.5 is a placeholder.
3. All numeric values (health, stagger threshold/duration, damage, speeds, timings, sizes) are placeholders.
4. How attacks are chosen (currently random, no immediate repeat) and whether she dies/does anything at 0 health.
5. Can she be hit while faded out? (Currently yes.)
6. Golden Rush second swipe: currently in place (she turns and sweeps back). Should she glide for it?
