using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;

public enum GuardianState { Idle, Telegraphing, Attacking, Recovery, Healing, Stunned, PhaseTransition, Dead }
public enum GuardianAttack { Auto, AzureSwordCombo, VermilionTornadoes, BlackShieldHealing, WhiteTigerStomp }

/// <summary>
/// The Fifth Guardian (Yellow Dragon): grounded humanoid boss. Core: state, ground movement, damage intake,
/// shield rules, phase architecture. One "brain" coroutine runs idle -> attack, so attacks never overlap.
/// Anything that interrupts (shield break, phase change, death) calls AbortActivity(), which stops every coroutine,
/// deactivates hitboxes and removes spawned tornadoes / telegraphs, then restarts the brain explicitly.
/// Attacks live in FifthGuardianBoss.Attacks.cs, Scene View drawing in FifthGuardianBoss.Gizmos.cs.
/// NOTE: every numeric default is a PLACEHOLDER for tuning.
/// </summary>
[RequireComponent(typeof(Rigidbody2D))]
public partial class FifthGuardianBoss : MonoBehaviour, IDamageable
{
    public static class Anim
    {
        public const string MoveSpeed = "MoveSpeed", IsGrounded = "IsGrounded", IsShielding = "IsShielding";
        public const string SwordComboStart = "SwordComboStart", SwordStrike = "SwordStrike", SwordComboEnd = "SwordComboEnd";
        public const string WingsSummon = "WingsSummon", TornadoRelease = "TornadoRelease";
        public const string ShieldRaise = "ShieldRaise", ShieldBreak = "ShieldBreak", HealComplete = "HealComplete";
        public const string StompCrouch = "StompCrouch", StompJump = "StompJump", StompSlam = "StompSlam", StompLand = "StompLand";
        public const string Recovery = "Recovery", PhaseTransition = "PhaseTransition", Death = "Death";
    }

    // =====================================================================
    // References
    // =====================================================================
    [Header("References")]
    public Animator animator;
    public Transform target;                    // optional; defaults to LokiController.Instance
    public LayerMask playerLayers;              // Loki's layer (auto-detected if empty)
    public GameObject shieldProp;               // black shield visual, toggled while the shield is up (optional)

    [Header("Arena (world space)")]
    public Vector2 arenaCenter = Vector2.zero;
    public Vector2 arenaSize = new Vector2(24f, 10f);

    [Header("Health / Phases (PLACEHOLDERS)")]
    public int maxHealth = 1500;
    public int health = 1500;
    [Range(0.05f, 0.95f)] public float phase2Threshold = 0.5f;   // Phase 2 begins at this fraction of max health
    public float phaseTransitionDuration = 2.5f;                 // boss is invulnerable and idle meanwhile
    public bool phase2ReusesPhase1Pool = false;                  // Phase 2 attacks are NOT implemented yet. true = keep using the Phase 1 attacks for testing
    public UnityEvent onPhase2Begin;
    public UnityEvent onDefeated;

    [Header("Brain")]
    public bool autoStart = true;
    public GuardianAttack forcedAttack = GuardianAttack.Auto;   // repeat a single attack for testing
    public bool enableSwordCombo = true;
    public bool enableTornadoes = true;
    public bool enableHealing = true;
    public bool enableStomp = true;
    public float idleBeforeAttack = 1.2f;

    [Header("Ground movement")]
    public float walkSpeed = 4f;
    public float walkAcceleration = 40f;
    public float preferredDistance = 5f;        // distance kept from Loki while idle
    public float distanceTolerance = 1f;
    public LayerMask groundLayers = ~0;
    public Vector2 groundCheckOffset = new Vector2(0f, -0.9f);
    public Vector2 groundCheckSize = new Vector2(0.8f, 0.1f);

    [Header("Runtime (read-only)")]
    public GuardianState state = GuardianState.Idle;
    public int phase = 1;
    public int facing = 1;
    public bool grounded;

    // ---------- internals ----------
    Rigidbody2D body;
    Collider2D col;
    Coroutine brain;
    GuardianAttack queued = GuardianAttack.Auto;
    GuardianAttack lastAttack = GuardianAttack.Auto;
    float desiredVx;
    bool scripted;                               // true while a routine (stomp jump) owns the velocity completely
    float baseGravityScale;
    bool dead;
    bool transitioning;
    HashSet<string> animParams;
    readonly List<string> triggerNames = new List<string>();
    readonly WaitForFixedUpdate fixedStep = new WaitForFixedUpdate();
    readonly Collider2D[] groundHits = new Collider2D[8];

    Rigidbody2D Body { get { if (body == null) body = GetComponent<Rigidbody2D>(); return body; } }
    Collider2D Col { get { if (col == null) col = GetComponent<Collider2D>(); return col; } }
    Vector2 Pos { get { return Application.isPlaying && Body != null ? Body.position : (Vector2)transform.position; } }
    float FeetY { get { return Col != null ? Col.bounds.min.y : Pos.y; } }
    int Dir { get { return Application.isPlaying ? facing : (transform.localScale.x < 0f ? -1 : 1); } }
    float ArenaMinX { get { return arenaCenter.x - arenaSize.x * 0.5f; } }
    float ArenaMaxX { get { return arenaCenter.x + arenaSize.x * 0.5f; } }
    Vector2 GroundCheckCenter() { return Pos + groundCheckOffset; }

    Transform Target
    {
        get
        {
            if (target != null) return target;
            return LokiController.Instance != null ? LokiController.Instance.transform : null;
        }
    }
    Vector2 TargetPos { get { Transform t = Target; return t != null ? (Vector2)t.position : arenaCenter; } }

    // =====================================================================
    // Lifecycle
    // =====================================================================
    void Awake()
    {
        if (playerLayers.value == 0 && LokiController.Instance != null)
        {
            playerLayers = 1 << LokiController.Instance.gameObject.layer;
            Debug.Log("FifthGuardianBoss: playerLayers auto-set. Assign it in the Inspector to suppress this.", this);
        }
        baseGravityScale = Body.gravityScale;
        facing = transform.localScale.x < 0f ? -1 : 1;
        health = maxHealth;
        shieldHealth = shieldDurability;
        if (shieldProp != null) shieldProp.SetActive(false);
        ValidatePhysics();
        CacheAnimatorParams();
        BuildStrikeHitbox();
    }

    void Start() { if (autoStart) brain = StartCoroutine(Brain()); }

    void ValidatePhysics()
    {
        Rigidbody2D rb = Body;
        if (rb.bodyType != RigidbodyType2D.Dynamic) Debug.LogWarning("FifthGuardianBoss: Rigidbody2D should be Dynamic.", this);
        if (rb.gravityScale <= 0f) Debug.LogWarning("FifthGuardianBoss: Gravity Scale must be > 0 (he walks and jumps on the ground).", this);
        if ((rb.constraints & RigidbodyConstraints2D.FreezeRotation) == 0) Debug.LogWarning("FifthGuardianBoss: Freeze Rotation Z should be enabled.", this);
        for (int i = 0; i < 32; i++)
            if ((playerLayers.value & (1 << i)) != 0 && !Physics2D.GetIgnoreLayerCollision(gameObject.layer, i))
                Debug.LogWarning("FifthGuardianBoss: his layer still collides with player layer '" + LayerMask.LayerToName(i) +
                                 "'. Disable it in Physics 2D settings or the stomp can push/trap Loki.", this);
    }

    // =====================================================================
    // Brain
    // =====================================================================
    IEnumerator Brain()
    {
        while (true)
        {
            state = GuardianState.Idle;
            float end = Time.time + idleBeforeAttack;
            while (Time.time < end)
            {
                IdleMove();
                yield return fixedStep;
            }
            desiredVx = 0f;

            GuardianAttack a = NextAttack();
            if (a == GuardianAttack.Auto) { yield return null; continue; }   // nothing available (e.g. Phase 2 placeholder)
            lastAttack = a;

            switch (a)
            {
                case GuardianAttack.AzureSwordCombo: yield return SwordComboRoutine(); break;
                case GuardianAttack.VermilionTornadoes: yield return TornadoRoutine(); break;
                case GuardianAttack.BlackShieldHealing: yield return HealingRoutine(); break;
                case GuardianAttack.WhiteTigerStomp: yield return StompRoutine(); break;
            }
            desiredVx = 0f;
        }
    }

    GuardianAttack NextAttack()
    {
        if (queued != GuardianAttack.Auto) { GuardianAttack q = queued; queued = GuardianAttack.Auto; return q; }
        if (forcedAttack != GuardianAttack.Auto) return forcedAttack;
        if (phase != 1 && !phase2ReusesPhase1Pool) return GuardianAttack.Auto;   // Phase 2 attacks: next task

        List<GuardianAttack> pool = new List<GuardianAttack>();
        if (enableSwordCombo) pool.Add(GuardianAttack.AzureSwordCombo);
        if (enableTornadoes) pool.Add(GuardianAttack.VermilionTornadoes);
        if (enableStomp) pool.Add(GuardianAttack.WhiteTigerStomp);
        if (enableHealing && Time.time >= nextHealTime && health <= maxHealth * healBelowHealthPercent)
            pool.Add(GuardianAttack.BlackShieldHealing);
        if (pool.Count > 1) pool.Remove(lastAttack);
        return pool.Count == 0 ? GuardianAttack.Auto : pool[Random.Range(0, pool.Count)];
    }

    /// <summary>Runs this attack the next time he is idle (never interrupts a running attack).</summary>
    public void QueueAttack(GuardianAttack a) { if (state == GuardianState.Idle || state == GuardianState.Recovery) queued = a; }

    // =====================================================================
    // Movement (velocity-driven, Dynamic Rigidbody2D)
    // =====================================================================
    void IdleMove()
    {
        FaceTarget();
        float dx = TargetPos.x - Pos.x;
        float ad = Mathf.Abs(dx);
        float v = 0f;
        if (ad > preferredDistance + distanceTolerance) v = Mathf.Sign(dx) * walkSpeed;
        else if (ad < preferredDistance - distanceTolerance) v = -Mathf.Sign(dx) * walkSpeed * 0.6f;
        if ((v > 0f && Pos.x >= ArenaMaxX) || (v < 0f && Pos.x <= ArenaMinX)) v = 0f;
        desiredVx = v;
    }

    void FixedUpdate()
    {
        UpdateGrounded();
        SetFloat(Anim.MoveSpeed, Mathf.Abs(Body.linearVelocity.x));
        SetBool(Anim.IsGrounded, grounded);
        if (scripted || dead) return;
        float vx = Mathf.MoveTowards(Body.linearVelocity.x, desiredVx, walkAcceleration * Time.fixedDeltaTime);
        Body.linearVelocity = new Vector2(vx, Body.linearVelocity.y);
    }

    void UpdateGrounded()
    {
        ContactFilter2D filter = new ContactFilter2D();
        filter.SetLayerMask(groundLayers);
        filter.useTriggers = false;
        int n = Physics2D.OverlapBox(GroundCheckCenter(), groundCheckSize, 0f, filter, groundHits);
        bool hit = false;
        for (int i = 0; i < n; i++)
            if (groundHits[i].attachedRigidbody != Body) { hit = true; break; }
        grounded = hit && Body.linearVelocity.y <= 0.1f;
    }

    void FaceTarget()
    {
        float dx = TargetPos.x - Pos.x;
        if (Mathf.Abs(dx) > 0.1f) SetFacing(dx > 0f ? 1 : -1);
    }

    void SetFacing(int f)
    {
        facing = f;
        Vector3 s = transform.localScale;
        s.x = Mathf.Abs(s.x) * f;
        transform.localScale = s;
    }

    // =====================================================================
    // Damage intake: shield rules -> health -> phase / death
    // =====================================================================
    public void TakeDamage(DamageInfo info)
    {
        if (dead || state == GuardianState.PhaseTransition) return;   // invulnerable while transitioning

        if (shieldUp)
        {
            // Shield protects him completely. Only a valid Loki slash hurts the SHIELD (not his health).
            if (IsShieldBreaker(info))
            {
                shieldHealth -= info.damage;
                Debug.Log("Guardian shield took " + info.damage + " (shield " + shieldHealth + "/" + shieldDurability + ")");
                if (shieldHealth <= 0) BreakShield();
            }
            return;
        }

        health -= info.damage;
        Debug.Log("Guardian took " + info.damage + " damage (hp " + health + "/" + maxHealth + ")");

        if (health <= 0) { Die(); return; }
        if (phase == 1 && !transitioning && health <= maxHealth * phase2Threshold) BeginPhaseTransition();
    }

    // =====================================================================
    // Phase transition (architecture only: Phase 2 attacks come in the next task)
    // =====================================================================
    void BeginPhaseTransition()
    {
        transitioning = true;
        AbortActivity();
        state = GuardianState.PhaseTransition;
        StartCoroutine(PhaseTransitionRoutine());
    }

    IEnumerator PhaseTransitionRoutine()
    {
        Trig(Anim.PhaseTransition);
        yield return new WaitForSeconds(phaseTransitionDuration);
        phase = 2;
        transitioning = false;
        Debug.Log("Fifth Guardian: Phase 2 begins (no Phase 2 attacks implemented yet).");
        if (onPhase2Begin != null) onPhase2Begin.Invoke();
        brain = StartCoroutine(Brain());
    }

    // =====================================================================
    // Death
    // =====================================================================
    void Die()
    {
        if (dead) return;
        dead = true;
        AbortActivity();
        state = GuardianState.Dead;
        Trig(Anim.Death);
        Debug.Log("Fifth Guardian defeated!");
        if (onDefeated != null) onDefeated.Invoke();
        StartCoroutine(DeathRoutine());
    }

    IEnumerator DeathRoutine()
    {
        yield return new WaitForSeconds(deathDelay);
        gameObject.SetActive(false);
    }
    public float deathDelay = 2f;

    // =====================================================================
    // Abort: single place that returns him to a clean, safe state
    // =====================================================================
    void AbortActivity()
    {
        StopAllCoroutines();
        brain = null;
        queued = GuardianAttack.Auto;
        desiredVx = 0f;
        scripted = false;
        Body.gravityScale = baseGravityScale;
        Body.linearVelocity = new Vector2(0f, Body.linearVelocity.y);
        if (strikeHitbox != null) strikeHitbox.active = false;      // no hitbox left behind
        ClearTornadoesAndTelegraphs();
        shieldUp = false;
        if (shieldProp != null) shieldProp.SetActive(false);
        SetBool(Anim.IsShielding, false);
        foreach (string n in triggerNames) animator.ResetTrigger(n);
    }

    [ContextMenu("Queue: Azure Sword Combo")] void QC() { QueueAttack(GuardianAttack.AzureSwordCombo); }
    [ContextMenu("Queue: Vermilion Tornadoes")] void QT() { QueueAttack(GuardianAttack.VermilionTornadoes); }
    [ContextMenu("Queue: Black Shield Healing")] void QH() { QueueAttack(GuardianAttack.BlackShieldHealing); }
    [ContextMenu("Queue: White Tiger Stomp")] void QS() { QueueAttack(GuardianAttack.WhiteTigerStomp); }
    [ContextMenu("Debug: Set health to 52%")] void DebugNearPhase() { health = Mathf.RoundToInt(maxHealth * 0.52f); }

    // =====================================================================
    // Animator helpers (parameters are only set if they exist)
    // =====================================================================
    void CacheAnimatorParams()
    {
        if (animator == null || animator.runtimeAnimatorController == null) return;
        animParams = new HashSet<string>();
        foreach (AnimatorControllerParameter p in animator.parameters)
        {
            animParams.Add(p.name);
            if (p.type == AnimatorControllerParameterType.Trigger) triggerNames.Add(p.name);
        }
    }

    bool HasParam(string n) { return animParams != null && animParams.Contains(n); }
    void Trig(string n) { if (HasParam(n)) animator.SetTrigger(n); }
    void SetBool(string n, bool v) { if (HasParam(n)) animator.SetBool(n, v); }
    void SetFloat(string n, float v) { if (HasParam(n)) animator.SetFloat(n, v); }
}
