using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;

[System.Serializable]
public class GuardianStrike
{
    [Tooltip("Telegraph/windup before this strike's hitbox turns on.")]
    public float startup = 0.35f;
    [Tooltip("How long this strike's hitbox is dangerous.")]
    public float activeTime = 0.12f;
    [Tooltip("Pause after this strike before the next one begins (ignored after the last strike; use comboRecovery).")]
    public float gapAfter = 0.2f;
    public int damage = 12;
    [Tooltip("Relative to his pivot; x is mirrored by facing.")]
    public Vector2 offset = new Vector2(1.5f, 0.3f);
    public Vector2 size = new Vector2(2.4f, 1.6f);
    [Tooltip("Forward speed while the hitbox is active (0 = stand still).")]
    public float advanceSpeed = 3f;
    [Tooltip("Turn to face Loki before this strike's windup.")]
    public bool faceTargetBefore = true;
}

/// <summary>Attacks. Each is one coroutine; placeholders everywhere (timings, damage, sizes were not specified).</summary>
public partial class FifthGuardianBoss
{
    // =====================================================================
    // Attack A: Azure Sword Combo (uses EnemyAttackHitbox, so Loki can parry each strike)
    // =====================================================================
    [Header("Azure Sword Combo")]
    public List<GuardianStrike> comboStrikes = new List<GuardianStrike>
    {
        new GuardianStrike { startup = 0.45f, damage = 12, advanceSpeed = 3f },
        new GuardianStrike { startup = 0.30f, damage = 12, advanceSpeed = 3f, offset = new Vector2(1.5f, 0.6f), size = new Vector2(2.6f, 1.2f) },
        new GuardianStrike { startup = 0.40f, damage = 18, advanceSpeed = 5f, activeTime = 0.15f, size = new Vector2(3f, 2f), faceTargetBefore = false },
    };
    public float comboRecovery = 1.0f;                       // the punish window after the last strike
    public bool strikesParryable = true;
    public Color swordColor = new Color(0.2f, 0.5f, 1f);

    EnemyAttackHitbox strikeHitbox;

    void BuildStrikeHitbox()
    {
        strikeHitbox = gameObject.AddComponent<EnemyAttackHitbox>();
        strikeHitbox.autoPulse = false;                      // set before Start() so it never self-pulses
        strikeHitbox.active = false;
        strikeHitbox.gizmoColor = swordColor;
    }

    IEnumerator SwordComboRoutine()
    {
        state = GuardianState.Telegraphing;
        Trig(Anim.SwordComboStart);
        for (int i = 0; i < comboStrikes.Count; i++)
        {
            GuardianStrike s = comboStrikes[i];
            desiredVx = 0f;
            if (i == 0 || s.faceTargetBefore) FaceTarget();

            state = GuardianState.Telegraphing;
            Trig(Anim.SwordStrike);
            yield return new WaitForSeconds(s.startup);

            state = GuardianState.Attacking;
            strikeHitbox.offset = new Vector2(s.offset.x * facing, s.offset.y);
            strikeHitbox.size = s.size;
            strikeHitbox.damage = s.damage;
            strikeHitbox.parryable = strikesParryable;
            strikeHitbox.gizmoColor = swordColor;
            strikeHitbox.Activate();                         // hits Loki at most once per strike
            desiredVx = facing * s.advanceSpeed;
            yield return new WaitForSeconds(s.activeTime);
            strikeHitbox.active = false;
            desiredVx = 0f;

            if (i < comboStrikes.Count - 1) yield return new WaitForSeconds(s.gapAfter);
        }
        state = GuardianState.Recovery;
        Trig(Anim.SwordComboEnd);
        yield return new WaitForSeconds(comboRecovery);
    }

    // =====================================================================
    // Attack B: Vermilion Tornadoes (Phase 1: NO earth spikes)
    // =====================================================================
    [Header("Vermilion Tornadoes")]
    public int tornadoCount = 3;
    public float tornadoFirstDistance = 3f;                  // from him, toward Loki
    public float tornadoSpacing = 3.5f;
    public float tornadoTelegraphTime = 0.9f;                // markers show where each will appear
    public float tornadoSpawnInterval = 0.25f;               // delay between consecutive tornadoes after the telegraph
    public float tornadoSpeed = 5f;
    public float tornadoLifetime = 4f;                       // capped by the arena edge so they never outlive the room
    public Vector2 tornadoSize = new Vector2(1.4f, 3f);
    public int tornadoDamage = 12;
    public float tornadoRecovery = 1f;
    public Color tornadoColor = new Color(1f, 0.25f, 0.2f);
    public GameObject tornadoPrefab;                         // optional visual (child of the hitbox)
    public GameObject tornadoTelegraphPrefab;                // optional ground marker

    readonly List<Projectile> liveTornadoes = new List<Projectile>();
    readonly List<GameObject> telegraphObjects = new List<GameObject>();

    Vector2 TornadoSpawnPoint(Vector2 origin, int dir, int i)
    {
        float x = Mathf.Clamp(origin.x + dir * (tornadoFirstDistance + i * tornadoSpacing), ArenaMinX, ArenaMaxX);
        return new Vector2(x, FeetY + tornadoSize.y * 0.5f);
    }

    IEnumerator TornadoRoutine()
    {
        state = GuardianState.Telegraphing;
        FaceTarget();
        int dir = facing;
        Vector2 origin = Pos;
        Trig(Anim.WingsSummon);

        for (int i = 0; i < tornadoCount; i++)
            if (tornadoTelegraphPrefab != null)
            {
                Vector2 p = TornadoSpawnPoint(origin, dir, i);
                telegraphObjects.Add(Instantiate(tornadoTelegraphPrefab, new Vector3(p.x, FeetY, 0f), Quaternion.identity));
            }

        float end = Time.time + tornadoTelegraphTime;
        while (Time.time < end)
        {
            for (int i = 0; i < tornadoCount; i++)
                Mark(TornadoSpawnPoint(origin, dir, i), tornadoSize, 0f, Combat.A(tornadoColor, 0.5f));   // telegraph (gizmo only, not a hitbox)
            yield return fixedStep;
        }
        ClearTelegraphs();

        state = GuardianState.Attacking;
        Trig(Anim.TornadoRelease);
        for (int i = 0; i < tornadoCount; i++)
        {
            SpawnTornado(TornadoSpawnPoint(origin, dir, i), dir);
            if (i < tornadoCount - 1) yield return new WaitForSeconds(tornadoSpawnInterval);
        }

        state = GuardianState.Recovery;
        Trig(Anim.Recovery);
        yield return new WaitForSeconds(tornadoRecovery);
    }

    void SpawnTornado(Vector2 pos, int dir)
    {
        float edge = dir > 0 ? ArenaMaxX : ArenaMinX;
        float untilEdge = (Mathf.Abs(edge - pos.x) + tornadoSize.x) / Mathf.Max(0.1f, tornadoSpeed);
        float life = tornadoLifetime > 0f ? Mathf.Min(tornadoLifetime, untilEdge) : untilEdge;
        // Projectile: pierces, hits Loki once, destroys itself when its lifetime ends (cleanup).
        Projectile p = Projectile.Spawn(pos, Vector2.right * dir, tornadoSpeed, life, tornadoSize, tornadoDamage,
                                        true, playerLayers, tornadoColor, tornadoPrefab, gameObject);
        liveTornadoes.Add(p);
    }

    void ClearTelegraphs()
    {
        foreach (GameObject g in telegraphObjects) if (g != null) Destroy(g);
        telegraphObjects.Clear();
    }

    void ClearTornadoesAndTelegraphs()
    {
        foreach (Projectile p in liveTornadoes) if (p != null) Destroy(p.gameObject);
        liveTornadoes.Clear();
        ClearTelegraphs();
    }

    // =====================================================================
    // Attack C: Black Shield Healing
    // =====================================================================
    [Header("Black Shield Healing")]
    public float healBelowHealthPercent = 0.85f;             // only offered once health is at or below this fraction
    public float healCooldown = 25f;
    public float healDuration = 5f;                          // time he must be left alone to complete the heal
    public int healAmount = 250;                             // clamped to maxHealth
    public int shieldDurability = 15;                        // shield HP. Any single valid slash should break it (see setup notes)
    public float shieldBreakStunTime = 1.5f;                 // staggered window after the shield breaks
    public bool debugAnyDamageBreaksShield = false;          // testing only
    public UnityEvent onShieldBroken;
    public UnityEvent onHealed;

    [Header("Runtime (read-only)")]
    public bool shieldUp;
    public int shieldHealth;
    float nextHealTime;

    IEnumerator HealingRoutine()
    {
        state = GuardianState.Healing;
        desiredVx = 0f;
        FaceTarget();
        nextHealTime = Time.time + healCooldown;
        shieldHealth = shieldDurability;
        shieldUp = true;
        if (shieldProp != null) shieldProp.SetActive(true);
        SetBool(Anim.IsShielding, true);
        Trig(Anim.ShieldRaise);

        yield return new WaitForSeconds(healDuration);        // an interrupt calls AbortActivity() and ends this routine

        health = Mathf.Min(maxHealth, health + healAmount);   // never above max health
        Debug.Log("Guardian healed (hp " + health + "/" + maxHealth + ")");
        shieldUp = false;
        if (shieldProp != null) shieldProp.SetActive(false);
        SetBool(Anim.IsShielding, false);
        Trig(Anim.HealComplete);
        if (onHealed != null) onHealed.Invoke();

        state = GuardianState.Recovery;
        yield return new WaitForSeconds(0.6f);
    }

    /// <summary>
    /// Valid shield breakers are Loki's three slash arts: Nail Art (Great Slash), Magic-infused Crescent (both bursts) and the
    /// Traversing Slash (slash + beam). DamageInfo carries no attack type, so they are recognised by Loki's live damage values
    /// plus Loki being in PlayerState.Attacking. The normal sword (swordDamage) is explicitly excluded.
    /// </summary>
    bool IsShieldBreaker(DamageInfo info)
    {
        if (debugAnyDamageBreaksShield) return true;
        LokiController l = LokiController.Instance;
        if (l == null || info.source != l.gameObject) return false;
        if (l.state != PlayerState.Attacking) return false;
        int d = info.damage;
        if (d == l.swordDamage) return false;                 // ordinary sword hit never breaks it
        return d == l.nailArtDamage || d == l.crescentDamage || d == l.crescentSecondDamage ||
               d == l.traverseSlashDamage || d == l.traverseBeamDamage;
    }

    void BreakShield()
    {
        Debug.Log("Guardian shield broken!");
        AbortActivity();                                      // stops the heal, removes the shield
        state = GuardianState.Stunned;
        Trig(Anim.ShieldBreak);
        if (onShieldBroken != null) onShieldBroken.Invoke();
        StartCoroutine(ShieldBreakRoutine());
    }

    IEnumerator ShieldBreakRoutine()
    {
        yield return new WaitForSeconds(shieldBreakStunTime);
        brain = StartCoroutine(Brain());                      // back to combat
    }

    // =====================================================================
    // Attack D: White Tiger Jump Stomp
    // =====================================================================
    [Header("White Tiger Stomp")]
    public float stompCrouchTime = 0.6f;                     // readable telegraph
    public float jumpHeight = 5f;
    public float jumpRiseTime = 0.45f;                       // time to reach the apex
    [Range(0f, 1f)] public float jumpTrackFactor = 1f;       // how much of the distance to Loki he covers while rising
    public float maxJumpHorizontalSpeed = 14f;
    public float apexHang = 0.15f;
    public float slamSpeed = 35f;
    public float landRecovery = 1.1f;                        // the punish window
    public BoxHitbox stompImpact = new BoxHitbox(new Vector2(0f, -0.4f), new Vector2(3f, 1.2f), Color.white);
    public int stompImpactDamage = 15;
    public float stompImpactActiveTime = 0.12f;
    public bool shockwaveBothDirections = true;
    public float shockwaveSpeed = 18f;
    public Vector2 shockwaveSize = new Vector2(1.2f, 0.9f);
    public float shockwaveSpawnOffsetX = 1.4f;
    public int shockwaveDamage = 12;
    public Color shockwaveColor = Color.white;
    public GameObject shockwavePrefab;                       // optional visual
    public UnityEvent onStompLanded;                         // hook screen shake / dust here

    Vector2 ShockwaveSpawn(Vector2 origin, int dir)
    {
        return new Vector2(origin.x + dir * shockwaveSpawnOffsetX, FeetY + shockwaveSize.y * 0.5f);
    }

    IEnumerator StompRoutine()
    {
        state = GuardianState.Telegraphing;
        desiredVx = 0f;
        FaceTarget();
        Trig(Anim.StompCrouch);
        yield return new WaitForSeconds(stompCrouchTime);

        // ---- jump: v0 = 2H/t, g = 2H/t^2 so he reaches exactly jumpHeight after jumpRiseTime ----
        state = GuardianState.Attacking;
        Trig(Anim.StompJump);
        float t = Mathf.Max(0.05f, jumpRiseTime);
        float v0 = 2f * jumpHeight / t;
        float g = 2f * jumpHeight / (t * t);
        Body.gravityScale = g / Mathf.Max(0.01f, Mathf.Abs(Physics2D.gravity.y));
        float vx = Mathf.Clamp((TargetPos.x - Pos.x) / t * jumpTrackFactor, -maxJumpHorizontalSpeed, maxJumpHorizontalSpeed);
        scripted = true;
        Body.linearVelocity = new Vector2(vx, v0);
        float limit = Time.time + t * 3f + 0.5f;             // safety net if something blocks him
        yield return fixedStep;
        while (Body.linearVelocity.y > 0f && Time.time < limit) yield return fixedStep;

        // ---- apex hang, then slam straight down ----
        Body.gravityScale = 0f;
        Body.linearVelocity = Vector2.zero;
        yield return new WaitForSeconds(apexHang);

        Trig(Anim.StompSlam);
        Body.gravityScale = baseGravityScale;
        limit = Time.time + 3f;
        while (!grounded && Time.time < limit)
        {
            Body.linearVelocity = new Vector2(0f, -slamSpeed);
            yield return fixedStep;
        }

        // ---- landing ----
        Body.linearVelocity = Vector2.zero;
        scripted = false;
        Trig(Anim.StompLand);
        if (onStompLanded != null) onStompLanded.Invoke();
        StartCoroutine(StompImpactWindow());
        SpawnShockwave(1);
        if (shockwaveBothDirections) SpawnShockwave(-1);

        state = GuardianState.Recovery;
        yield return new WaitForSeconds(landRecovery);
    }

    void SpawnShockwave(int dir)
    {
        Vector2 pos = ShockwaveSpawn(Pos, dir);
        float edge = dir > 0 ? ArenaMaxX : ArenaMinX;
        float life = (Mathf.Abs(edge - pos.x) + shockwaveSize.x) / Mathf.Max(0.1f, shockwaveSpeed);   // gone once it leaves the arena
        // Independent of the boss: a self-owned Projectile, not tracked, not cleared by AbortActivity.
        Projectile.Spawn(pos, Vector2.right * dir, shockwaveSpeed, life, shockwaveSize, shockwaveDamage,
                         true, playerLayers, shockwaveColor, shockwavePrefab, gameObject);
    }

    IEnumerator StompImpactWindow()
    {
        HashSet<object> hits = new HashSet<object>();
        float end = Time.time + stompImpactActiveTime;
        while (Time.time < end)
        {
            Collider2D[] cols = Query(stompImpact.Center(Pos, facing), stompImpact.size, 0f, stompImpact.color);
            Combat.Damage(cols, stompImpactDamage, Pos, gameObject, hits, out _);
            yield return new WaitForFixedUpdate();
        }
    }

    // Marks the gizmo and queries with the very same shape (same pattern as the other bosses).
    Collider2D[] Query(Vector2 center, Vector2 size, float angle, Color color)
    {
        Mark(center, size, angle, color);
        return Physics2D.OverlapBoxAll(center, size, angle, playerLayers);
    }
}
