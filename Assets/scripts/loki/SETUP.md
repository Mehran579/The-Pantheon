# Loki Combat System: Setup

Unity 6, New Input System package installed, **Active Input Handling = Input System Package (New)** (Project Settings > Player).

## 1. Files

Put all `.cs` files in one folder (e.g. `Assets/Scripts/Loki/`). Keep the file names as they are.

| File | Purpose |
|---|---|
| `LokiController.cs` | Movement, input, buffering, sword, spells, parry, charge matrix |
| `LokiController.Gizmos.cs` | All Scene View drawing (partial class, same component) |
| `SoulMeter.cs` | Soul numbers: 6 total, 3 spendable |
| `SpellConfigs.cs` | Per-spell parameters + spell-infused sword config |
| `CombatCore.cs` | `IDamageable`, `DamageInfo`, `BoxHitbox`, shared damage/gizmo helpers |
| `Projectile.cs`, `RiftEruption.cs`, `AstralOrbit.cs` | Spawned attack effects (built in code, no prefab needed) |
| `EnemyAttackHitbox.cs`, `TrainingDummy.cs` | Test helpers (also usable as the base for bosses) |

## 2. Hierarchy and components

```
Loki                      <- LokiController, SoulMeter, Rigidbody2D, CapsuleCollider2D
  Visual (optional)       <- SpriteRenderer + Animator (drag the Animator into LokiController.animator)
Ground                    <- BoxCollider2D (not a trigger), on a "Ground" layer
Dummy                     <- TrainingDummy + BoxCollider2D, on an "Enemy" layer
EnemyAttack (optional)    <- EnemyAttackHitbox (child of or next to Dummy)
```

**Layers:** create `Player`, `Ground`, `Enemy`. Loki goes on `Player`.

**Rigidbody2D (Loki):** Body Type Dynamic, Gravity Scale is set by the script (`gravityScale`), Freeze Rotation Z on, Collision Detection Continuous, Interpolate Interpolate.

**CapsuleCollider2D (Loki):** roughly size (0.8, 1.8). The script reads the collider bottom for the ground check and Rift Burst base height, so keep the pivot centred.

**LokiController inspector:**
- `Enemy Layers` = `Enemy` only (never include `Player`).
- `Ground Layers` = `Ground`.
- `Ground Check Offset` = just below the collider bottom, e.g. (0, -0.95) for a 1.8 tall collider. The drawn box must overlap the floor.
- Flip is done through `localScale.x`; put the sprite on the root or a child.

## 3. Input (no asset needed)

The actions are created in code with the New Input System (`InputAction`, press = `performed`, release = `canceled`).

| Action | Binding |
|---|---|
| Move | A/D, Left/Right (W/S, Up/Down only aim the sword up/down) |
| Jump | Space |
| Sword | Left Mouse Button |
| Spell | E |
| Dash | Left Shift |
| Parry | Right Mouse Button |

To use an Input Actions asset later, replace `BuildInput()`; the rest only uses the six `InputAction` fields.

## 4. Optional prefab references (all may stay empty)

| Field | Used by |
|---|---|
| `arcBoltPrefab` | Arc Bolt, spell-infused Arc Bolt |
| `arcBoltChargedPrefab` | Charged Arc Bolt |
| `riftEruptionPrefab` | Every eruption (Rift Burst, charged, spell-infused) |
| `orbitBladePrefab` | One orbiting Astral Orbit blade |
| `radialBladePrefab` | Charged orbit release, spell-infused orbit blades |
| `crescentPrefab` | Magic-infused Great Slash crescent |
| `nailArtEffectPrefab` | Great Slash / infused slash |
| `crossStitchEffectPrefab` | Cross Stitch |
| `aoeBurstEffectPrefab` | Magical AoE Burst |

Projectile/eruption prefabs are parented to the code-built hitbox object, so they move with it. Hitboxes work without any prefab.

## 5. Animator

Parameters are only set if they exist, so you can add them gradually.

- Triggers: `SwordAttack`, `SwordAttackUp`, `SwordAttackDown`, `NailArt`, `MagicInfusedSwordArt`, `SpellInfusedSwordArcBolt`, `SpellInfusedSwordRiftBurst`, `SpellInfusedSwordAstralOrbit`, `ArcBolt`, `ArcBoltCharged`, `RiftBurst`, `RiftBurstCharged`, `AstralOrbit`, `AstralOrbitCharged`, `Parry`, `ParrySuccess`, `ParryFail`, `CrossStitch`, `MagicalAoEBurst`, `Dash`, `Jump`, `DoubleJump`, `SwordChargeStart`, `MagicChargeStart`
- Bools: `IsGrounded`, `IsDashing`, `IsChargingSword`, `IsChargingMagic`, `SwordChargeReady`, `MagicChargeReady`
- Floats: `MoveSpeed`, `VerticalSpeed`

## 6. Gizmos (enable the Gizmos button in the Game/Scene view)

| Attack | Color | Attack | Color |
|---|---|---|---|
| Normal sword | Green | Rift Burst | Orange |
| Nail Art | Yellow | Astral Orbit | Purple |
| Magic-infused Sword Art | Cyan | Orbit radial release | Red |
| Spell-infused sword | Blue | Normal parry area | White |
| Arc Bolt | Magenta | Cross Stitch | Bright cyan |
| Magical AoE Burst | Pink | Enemy attack / parry range | Grey |

- Active hitboxes are drawn filled only while their window is active (plus `gizmoLinger`). Projectiles, eruptions and orbit blades draw their own live boxes.
- A charge that is ready draws what it will do (Nail Art, Cross Stitch, selected spell, AoE radius...).
- Select Loki to see faint previews of every attack (`drawPreviews`).
- Movement gizmos: ground check box, coyote reference point, jump apexes, dash range, dash invulnerability range, live dash path.

## 7. Soul and testing setup

Starting Soul is 3 spendable + 3 reserve. Sword hits give `soulPerSwordHit` (1); parries give `soulOnParry` (0). Right-click the `SoulMeter` header > *Add 1 Soul* in Play Mode to refill. A spell is only castable if `spendable >= cost`; reserve Soul never casts directly (it refills spendable by `reserveTransferPerGain` on each gain).

Test scene: put a `TrainingDummy` on the Enemy layer in front of Loki, and an `EnemyAttackHitbox` (auto pulses every 2 s, grey) overlapping Loki's parry area for parry tests. Console logs every hit.

## 8. How to test each action

| Test | How |
|---|---|
| Move / jump / double jump / coyote | A/D, Space (twice). Walk off a ledge and press Space just after: coyote point shows in gold. |
| Dash | Shift on the ground and in the air (one air dash until landing). Press RMB during a dash: parry is buffered, it does not cancel the dash. |
| Normal sword | Tap LMB (hold W for up, hold S in the air for down). Green box. |
| **None + Sword** | Tap LMB before the charge time. |
| **None + Magic** | Tap E (change `selectedSpell` in the inspector to test each spell). |
| **None + Parry** | RMB just as the grey enemy attack is active. White box, then success event. |
| **Sword + Sword = Nail Art** | Hold LMB ~0.8 s until the yellow preview appears, release LMB. |
| **Magic + Magic = Empowered spell** | Hold E until the preview appears, release E. Arc Bolt pierces, Rift Burst marches 5 eruptions, Astral Orbit releases red blades. |
| **Sword + Magic = Magic-infused Sword Art** | Hold LMB until ready, press E. Cyan slash + long cyan crescent. |
| **Magic + Sword = Spell-infused sword** | Hold E until ready, press LMB. Blue slash: Arc Bolt fires a bolt, Rift Burst erupts at the target, Astral Orbit sprays blades around the target. |
| **Magic + Parry = AoE Burst** | Hold E until ready, press RMB while the enemy attack is active. Pink circle only on a successful parry. |
| **Sword + Parry = Cross Stitch** | Hold LMB until ready, press RMB on a successful parry. Bright cyan X. |
| Parry cancels attacks / spells | Press RMB mid sword swing or mid charged Rift Burst. |
| Buffering | Press LMB / E / Shift / RMB / Space during recovery or a dash: it fires as soon as valid, once, within `inputBufferTime`. |

## 9. Decisions worth knowing

- A charge is "prepared" while its button is held and the charge time has elapsed. Pressing another action then consumes it (see matrix). Pressing the other attack button while a charge is still filling is ignored.
- A charged parry carries the charge from the press; the effect only fires on success. On a failed parry the charge is lost (`keepChargeOnFailedParry` restores it if the button is still held).
- Spell Soul is spent when the spell actually fires (after startup), so a parry-cancelled cast costs nothing.
- Charged versions require `chargedCost` spendable Soul to become ready; otherwise releasing casts the normal version.
