# Fifth Guardian: Setup and Test Guide

Files (next to the Loki/Ohsyra scripts): `FifthGuardianBoss.cs`, `FifthGuardianBoss.Attacks.cs`, `FifthGuardianBoss.Gizmos.cs`.
Reuses `Projectile`, `Combat`, `BoxHitbox`, `IDamageable`, `EnemyAttackHitbox`. No existing file is modified.

## Inspector setup
- Root: `FifthGuardianBoss`, `Rigidbody2D` (Dynamic, **Gravity Scale > 0**, Freeze Rotation Z, Interpolate, Continuous), a non-trigger `Collider2D` on a layer in Loki's `enemyLayers`, `Animator` (optional).
- Physics 2D layer matrix: boss layer must NOT collide with the Player layer (a warning is logged otherwise).
- `Player Layers` = Loki's layer. `Ground Layers` = Ground only (not the boss's own layer). Ground check box must overlap the floor.
- `Arena Center/Size`: bottom edge = floor. Shockwaves/tornadoes despawn at the arena's left/right edge.
- Optional: `shieldProp` (black shield, toggled on/off), prefabs for tornado, tornado telegraph, shockwave.
- Tune everything under the Azure / Vermilion / Black Shield / White Tiger headers. Right-click the component header in Play Mode: *Queue: ...*, or set `Forced Attack`.

## Animator (all optional; set only if present)
Bools: `IsGrounded`, `IsShielding`. Float: `MoveSpeed`.
Triggers: `SwordComboStart`, `SwordStrike` (once per strike), `SwordComboEnd`, `WingsSummon`, `TornadoRelease`, `ShieldRaise`, `ShieldBreak`, `HealComplete`, `StompCrouch`, `StompJump`, `StompSlam`, `StompLand`, `Recovery`, `PhaseTransition`, `Death`.
No animation events are required: the script drives all timing.

## Notes / limits
- Sword strikes use `EnemyAttackHitbox`, so Loki can parry them. Tornadoes, shockwaves and the stomp impact are not parryable.
- Shield: `DamageInfo` has no attack type, so valid slashes are recognised by `info.source == Loki`, `Loki.state == Attacking`, and the damage matching Loki's live `nailArtDamage`, `crescentDamage`, `crescentSecondDamage`, `traverseSlashDamage`, `traverseBeamDamage` (normal `swordDamage` excluded). Changing values in Loki's inspector is picked up automatically. Known weakness: if you tune a slash to the same damage as the normal sword (default `crescentSecondDamage` = `swordDamage` = 10), that hit is not counted. Cleanest long-term fix: add an attack-kind field to `DamageInfo` in Loki's combat (deliberately not done here).
- While shielded, all damage to his health is blocked; non-slash hits do nothing, slashes reduce `shieldHealth`. Default durability 15: Crescent (18) or Traversing slash + beam (16) breaks it, Nail Art (25) breaks it in one hit.
- Phase 2: at `phase2Threshold` (50%) he aborts everything, is invulnerable for `phaseTransitionDuration`, fires `onPhase2Begin`, sets `phase = 2`. No Phase 2 attacks exist; he just idles unless `phase2ReusesPhase1Pool` is on.
- Attacks are armoured against ordinary damage; they are interrupted only by shield break, phase transition and death (all via `AbortActivity`).
