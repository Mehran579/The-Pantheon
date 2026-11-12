using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public enum OhsyraState { Floating, Repositioning, Telegraphing, Attacking, Recovery, Staggered }
public enum OhsyraAttack { Auto, GoldenJewelry, AbebeFan, GoldenHoneyRays, GoldenRush }
public enum OhsyraHand { Right = 0, Left = 1 }

/// <summary>
/// Ohsyra: floating duelist boss. Core: state, floating movement, fade/materialize, stagger, damage intake.
/// One "brain" coroutine runs everything (idle -> pick attack -> attack routine), so two attacks can never overlap.
/// Stagger calls StopAllCoroutines(), so nothing can continue, then restarts the brain.
/// Attacks live in OhsyraBoss.Attacks.cs, Scene View drawing in OhsyraBoss.Gizmos.cs.
/// NOTE: every numeric default is a PLACEHOLDER for tuning. None were specified in the design.
/// </summary>
[RequireComponent(typeof(Rigidbody2D))]
public partial class OhsyraBoss : MonoBehaviour, IDamageable
{
    // =====================================================================
    // Animator parameter names
    // =====================================================================
    public static class Anim
    {
        public const string IsFloating = "IsFloating", IsStaggered = "IsStaggered";
        public const string MoveSpeed = "MoveSpeed";
        public const string RightArmAimX = "RightArmAimX", RightArmAimY = "RightArmAimY";
        public const string LeftArmAimX = "LeftArmAimX", LeftArmAimY = "LeftArmAimY";
        public const string FadeOut = "FadeOut", Materialize = "Materialize";
        public const string JewelryTelegraph = "JewelryTelegraph", JewelryRelease = "JewelryRelease";
        public const string FanAttack = "FanAttack", FanCounterStance = "FanCounterStance", FanCounterAttack = "FanCounterAttack", IsCounterStance = "IsCounterStance";
        public const string RaysStart = "RaysStart", RayFireRight = "RayFireRight", RayFireLeft = "RayFireLeft";
        public const string RushTelegraph = "RushTelegraph", RushSweep1 = "RushSweep1";
        public const string RushSecondTelegraph = "RushSecondTelegraph", RushSweep2 = "RushSweep2";
        public const string Recovery = "Recovery";
        public const string StaggerStart = "StaggerStart", StaggerEnd = "StaggerEnd";
    }

    // =====================================================================
    // References
    // =====================================================================
    [Header("References")]
    public Animator animator;
    public Transform target;                    // optional; defaults to LokiController.Instance
    public LayerMask playerLayers;              // layer(s) of Loki's collider
    public SpriteRenderer[] sprites;            // faded during fade/materialize; auto-filled from children if empty
    public GameObject mirrorProp;               // small golden hand mirror (visual only, shown while staggered)
    public Transform rightHand;                 // ray origins (also used by the animator rig)
    public Transform leftHand;
    public Transform torso;                     // optional: subtly follows the arm aim during Golden Honey Rays

    [Header("Arena (world space)")]
    public Vector2 arenaCenter = Vector2.zero;
    public Vector2 arenaSize = new Vector2(20f, 10f);

    [Header("Ground detection")]
    public LayerMask groundLayers = ~0;
    public Vector2 groundCheckOffset = new Vector2(0f, -0.9f);
    public Vector2 groundCheckSize = new Vector2(0.8f, 0.1f);
    public float groundSnapDistance = 20f;

    [Header("Health / Stagger (PLACEHOLDERS)")]
    public int maxHealth = 1000;                // not specified in the design. Death behaviour is not implemented.
    public int health = 1000;
    public int staggerThreshold = 100;          // accumulated incoming damage that triggers stagger
    public float staggerDuration = 3f;

    [Header("Brain")]
    public bool autoStart = true;
    public OhsyraAttack forcedAttack = OhsyraAttack.Auto;   // set to one attack to test it on repeat
    public bool enableJewelry = true;
    public bool enableRays = true;
    public bool enableRush = true;
    public bool enableFan = true;               // Ab?b? Counter Stance
    public float idleBeforeAttack = 1.5f;

    [Header("Floating movement")]
    public float preferredDistance = 6f;        // horizontal distance kept from Loki while idle
    public float hoverHeight = 1.5f;            // above Loki's pivot
    public float bobAmplitude = 0.15f;
    public float bobFrequency = 2f;
    public float floatSmoothTime = 0.45f;
    public float floatMaxSpeed = 8f;

    [Header("Fade / Materialize")]
    public float fadeOutDuration = 0.6f;
    public float materializeDuration = 0.5f;

    [Header("Runtime (read-only)")]
    public OhsyraState state = OhsyraState.Floating;
    public int facing = 1;
    public int staggerDamage;

    // ---------- internals ----------
    Coroutine brain;
    OhsyraAttack queued = OhsyraAttack.Auto;
    OhsyraAttack lastAttack = OhsyraAttack.Auto;
    Vector2 floatVel;
    Vector2 destination;
    bool hasDestination;
    HashSet<string> animParams;
    readonly List<string> triggerNames = new List<string>();

    // Ohsyra is a DYNAMIC Rigidbody2D with Gravity Scale 0. All AI movement is velocity-driven at fixed-step rate
    // (MoveTo / recoil), so environment colliders (floor, walls) stop her physically. She is excluded from colliding
    // with Loke through the layer collision matrix (not through triggers). See OHSYRA_SETUP.md.
    [Header("Dynamic body")]
    public float brakeDeceleration = 40f;       // u/s^2: stops drift when no routine is driving her (Dynamic bodies have no automatic stop)
    public float moveTimeout = 5f;              // safety net: FloatTo gives up if obstructed so the AI can never hang

    [Header("Damage recoil")]
    public float recoilSpeed = 4f;              // peak speed of a normal hit's recoil (decays to 0 over recoilDuration)
    public float recoilHeavySpeed = 8f;         // peak speed for a heavy hit
    public int recoilHeavyDamage = 20;          // hits with damage >= this use the heavy recoil
    public float recoilDuration = 0.35f;        // knockback phase: her AI movement is suspended
    public float recoilRecovery = 0.4f;         // control blends back in over this time (smooth resume)
    [Range(0f, 1f)] public float recoilVerticalScale = 0.3f;   // 0 = purely horizontal recoil
    public bool recoilInterruptsAttacks = false;               // false: attacks are armoured (hits still count toward stagger)
    [SerializeField] bool recoilActive;         // runtime: knockback phase running

    Rigidbody2D body;
    Coroutine recoilRoutine;
    float moveControl = 1f;                     // 0..1 how much of the AI's desired velocity is applied (ramps back after recoil)
    bool driven;                                // true when a routine set the velocity this step
    Collider2D[] cols;
    Rigidbody2D Body { get { if (body == null) body = GetComponent<Rigidbody2D>(); return body; } }
    readonly WaitForFixedUpdate fixedStep = new WaitForFixedUpdate();
    readonly Collider2D[] groundHits = new Collider2D[8];

    // Simulation position while playing (transform.position is the interpolated visual one), so movement,
    // hitboxes and physics queries all agree. Outside play mode (gizmos) it falls back to the transform.
    Vector2 Pos { get { return Application.isPlaying && Body != null ? Body.position : (Vector2)transform.position; } }

    Transform Target
    {
        get
        {
            if (target != null) return target;
            return LokiController.Instance != null ? LokiController.Instance.transform : null;
        }
    }

    Vector2 TargetPos { get { Transform t = Target; return t != null ? (Vector2)t.position : arenaCenter; } }

    Vector2 GroundCheckCenter(Vector2 position) { return position + groundCheckOffset; }

    bool TryFindGroundY(float x, out float groundY)
    {
        groundY = Pos.y;
        ContactFilter2D filter = new ContactFilter2D();
        filter.SetLayerMask(groundLayers);
        filter.useTriggers = false;
        Vector2 origin = new Vector2(x, Pos.y + groundSnapDistance * 0.5f);
        Vector2 size = new Vector2(groundCheckSize.x, groundSnapDistance);
        int count = Physics2D.OverlapBox(origin, size, 0f, filter, groundHits);
        Collider2D rootCollider = GetComponent<Collider2D>();
        if (rootCollider == null) return false;

        bool found = false;
        float floorTop = float.NegativeInfinity;
        Bounds ownBounds = rootCollider.bounds;
        for (int i = 0; i < count; i++)
        {
            Collider2D ground = groundHits[i];
            if (ground == null || ground.transform.IsChildOf(transform) || ground.attachedRigidbody == Body) continue;
            Bounds groundBounds = ground.bounds;
            if (ownBounds.min.x > groundBounds.max.x || ownBounds.max.x < groundBounds.min.x) continue;
            if (groundBounds.max.y <= origin.y && groundBounds.max.y > floorTop)
            {
                floorTop = groundBounds.max.y;
                found = true;
            }
        }
        if (!found) return false;
        float colliderOffset = Pos.y - ownBounds.min.y;
        groundY = floorTop + colliderOffset;
        return true;
    }

    // =====================================================================
    // Lifecycle
    // =====================================================================
    void Awake()
    {
        if (sprites == null || sprites.Length == 0) sprites = GetComponentsInChildren<SpriteRenderer>(true);
        if (sprites.Length == 0) Debug.LogWarning("OhsyraBoss: no SpriteRenderers found under her root, so fading will do nothing. Assign them to 'Sprites'.", this);

        // Auto-detect player layers from LokiController if none assigned in Inspector
        if (playerLayers.value == 0 && LokiController.Instance != null)
        {
            playerLayers = 1 << LokiController.Instance.gameObject.layer;
            Debug.Log("OhsyraBoss: playerLayers auto-set to layer '" +
                      LayerMask.LayerToName(LokiController.Instance.gameObject.layer) + "'. Assign it in the Inspector to suppress this.", this);
        }

        ValidatePhysics();
        facing = transform.localScale.x < 0f ? -1 : 1;
        health = maxHealth;
        if (mirrorProp != null) mirrorProp.SetActive(false);
        ResetArms();
        CacheAnimatorParams();
    }

    void Start()
    {
        EnsurePlayerLayers();
        if (autoStart) brain = StartCoroutine(Brain());
    }

    void EnsurePlayerLayers()
    {
        if (playerLayers.value == 0)
        {
            LokiController lc = LokiController.Instance != null ? LokiController.Instance : FindFirstObjectByType<LokiController>();
            if (lc != null)
            {
                playerLayers = 1 << lc.gameObject.layer;
                Debug.Log("OhsyraBoss: playerLayers auto-set to layer '" + LayerMask.LayerToName(lc.gameObject.layer) + "', this will be used for player collision.", this);
            }
        }
    }

    // =====================================================================
    // Brain: idle -> attack -> idle. Only one routine ever runs.
    // =====================================================================
    IEnumerator Brain()
    {
        while (true)
        {
            state = OhsyraState.Floating;
            SetBool(Anim.IsFloating, true);
            float end = Time.time + idleBeforeAttack;
            while (Time.time < end || recoilRoutine != null)      // never start an attack mid-recoil
            {
                if (!recoilActive) FloatIdle();                   // recoil owns her velocity while it runs
                yield return fixedStep;
            }
            SetBool(Anim.IsFloating, false);

            OhsyraAttack a = NextAttack();
            if (a == OhsyraAttack.Auto) { yield return null; continue; }   // nothing enabled
            lastAttack = a;

            switch (a)
            {
                case OhsyraAttack.GoldenJewelry: yield return JewelryRoutine(); break;
                case OhsyraAttack.AbebeFan: yield return FanRoutine(); break;
                case OhsyraAttack.GoldenHoneyRays: yield return RaysRoutine(); break;
                case OhsyraAttack.GoldenRush: yield return RushRoutine(); break;
            }
            hasDestination = false;
        }
    }

    OhsyraAttack NextAttack()
    {
        if (queued != OhsyraAttack.Auto) { OhsyraAttack q = queued; queued = OhsyraAttack.Auto; return q; }
        if (forcedAttack != OhsyraAttack.Auto) return forcedAttack;

        List<OhsyraAttack> pool = new List<OhsyraAttack>();
        if (enableJewelry) pool.Add(OhsyraAttack.GoldenJewelry);
        if (enableRays) pool.Add(OhsyraAttack.GoldenHoneyRays);
        if (enableRush) pool.Add(OhsyraAttack.GoldenRush);
        if (enableFan) pool.Add(OhsyraAttack.AbebeFan);
        if (pool.Count > 1) pool.Remove(lastAttack);      // no immediate repeat
        return pool.Count == 0 ? OhsyraAttack.Auto : pool[Random.Range(0, pool.Count)];
    }

    /// <summary>Runs this attack the next time she is idle (never interrupts a running attack).</summary>
    public void QueueAttack(OhsyraAttack a) { if (state != OhsyraState.Staggered) queued = a; }

    // =====================================================================
    // Floating movement
    // =====================================================================
    Vector2 IdleDesired()
    {
        Vector2 t = TargetPos;
        float side = Pos.x >= t.x ? 1f : -1f;
        return ClampToArena(new Vector2(t.x + side * preferredDistance, t.y + hoverHeight));
    }

    void FloatIdle()
    {
        Vector2 desired = IdleDesired();
        desired.y += Mathf.Sin(Time.time * bobFrequency) * bobAmplitude;
        desired = ClampToArena(desired);
        floatVel = Body.linearVelocity;                           // start from her real velocity (recoil/collisions feed back)
        MoveTo(Vector2.SmoothDamp(Pos, desired, ref floatVel, floatSmoothTime, floatMaxSpeed, Time.fixedDeltaTime));
        FaceTarget();
    }

    IEnumerator FloatTo(Vector2 dest, float smoothTime)
    {
        destination = dest;
        hasDestination = true;
        float limit = Time.time + moveTimeout;
        while (Vector2.Distance(Pos, dest) > 0.1f && Time.time < limit)
        {
            floatVel = Body.linearVelocity;
            MoveTo(Vector2.SmoothDamp(Pos, dest, ref floatVel, smoothTime, floatMaxSpeed, Time.fixedDeltaTime));
            yield return fixedStep;
        }
        floatVel = Vector2.zero;
        Body.linearVelocity = Vector2.zero;
    }

    // Dynamic-body move: sets the velocity that reaches point p in one fixed step. Call once per fixed step.
    // If something blocks her, the next step recomputes from her real position, so there is no wind-up or tunnelling.
    // moveControl < 1 (after recoil) blends this toward her current velocity so control returns smoothly.
    void MoveTo(Vector2 p)
    {
        Vector2 desired = (p - Pos) / Time.fixedDeltaTime;
        Body.linearVelocity = moveControl >= 1f ? desired : Vector2.Lerp(Body.linearVelocity, desired, moveControl);
        driven = true;
        SetFloat(Anim.MoveSpeed, Body.linearVelocity.magnitude);
        RecordTrail(p);
    }

    // Brakes any leftover velocity when no routine drove her this step. Recoil manages its own velocity.
    void FixedUpdate()
    {
        if (!driven && !recoilActive)
            Body.linearVelocity = Vector2.MoveTowards(Body.linearVelocity, Vector2.zero, brakeDeceleration * Time.fixedDeltaTime);
        driven = false;
    }

    // Instant relocation. Only used while she is fully invisible (Materialize), so no interpolation smear is visible.
    void Teleport(Vector2 p)
    {
        Body.position = p;
        Body.linearVelocity = Vector2.zero;
        transform.position = new Vector3(p.x, p.y, transform.position.z);
        SetFloat(Anim.MoveSpeed, 0f);
        RecordTrail(p);
    }

    void ValidatePhysics()
    {
        Rigidbody2D rb = Body;
        if (rb.bodyType != RigidbodyType2D.Dynamic) Debug.LogWarning("OhsyraBoss: Rigidbody2D Body Type should be Dynamic (is " + rb.bodyType + ").", this);
        if (rb.gravityScale != 0f) Debug.LogWarning("OhsyraBoss: Gravity Scale should be 0 (is " + rb.gravityScale + ").", this);
        if ((rb.constraints & RigidbodyConstraints2D.FreezeRotation) == 0) Debug.LogWarning("OhsyraBoss: Freeze Rotation Z should be enabled.", this);
        if (rb.interpolation != RigidbodyInterpolation2D.Interpolate) Debug.LogWarning("OhsyraBoss: Interpolate should be set to Interpolate.", this);
        if (rb.collisionDetectionMode != CollisionDetectionMode2D.Continuous) Debug.LogWarning("OhsyraBoss: Collision Detection should be Continuous.", this);
        for (int i = 0; i < 32; i++)
            if ((playerLayers.value & (1 << i)) != 0 && !Physics2D.GetIgnoreLayerCollision(gameObject.layer, i))
                Debug.LogWarning("OhsyraBoss: her layer still collides with player layer '" + LayerMask.LayerToName(i) +
                                 "'. Disable it in Project Settings > Physics 2D, or Golden Rush will push/block Loke.", this);
    }

    void OnValidate() { cols = null; }

    // Keeps her whole collider (not just the pivot) inside the arena rectangle. Make the arena rectangle's bottom
    // edge the floor surface so she cannot be asked to hover inside the floor.
    public float GroundY
    {
        get
        {
            Vector2 h = arenaSize * 0.5f;
            float floorY = arenaCenter.y - h.y;
            if (cols == null || cols.Length == 0) cols = GetComponentsInChildren<Collider2D>();
            bool any = false;
            Bounds b = new Bounds();
            foreach (Collider2D c in cols)
            {
                if (c == null || !c.enabled) continue;
                if (!any) { b = c.bounds; any = true; } else b.Encapsulate(c.bounds);
            }
            if (any)
            {
                float offsetFromPivot = Pos.y - b.min.y;
                return floorY + Mathf.Max(0.1f, offsetFromPivot);
            }
            return floorY + 1f;
        }
    }

    Vector2 ClampToArena(Vector2 p)
    {
        Vector2 h = arenaSize * 0.5f;
        float left = 0f, right = 0f, down = 0f, up = 0f;
        if (cols == null) cols = GetComponentsInChildren<Collider2D>();
        bool any = false;
        Bounds b = new Bounds();
        foreach (Collider2D c in cols)
        {
            if (c == null || !c.enabled) continue;
            if (!any) { b = c.bounds; any = true; } else b.Encapsulate(c.bounds);
        }
        if (any)
        {
            Vector2 pos = Pos;
            left = pos.x - b.min.x; right = b.max.x - pos.x;
            down = pos.y - b.min.y; up = b.max.y - pos.y;
        }
        return new Vector2(Mathf.Clamp(p.x, arenaCenter.x - h.x + left, arenaCenter.x + h.x - right),
                           Mathf.Clamp(p.y, arenaCenter.y - h.y + down, arenaCenter.y + h.y - up));
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
    // Fade out / materialize (never an instant teleport)
    // =====================================================================
    /// <summary>Floats toward dest while the sprite gradually fades out.</summary>
    IEnumerator FadeOutTo(Vector2 dest)
    {
        destination = dest;
        hasDestination = true;
        Trig(Anim.FadeOut);
        Vector2 from = Pos;
        float t = 0f;
        while (t < 1f)
        {
            t += Time.fixedDeltaTime / Mathf.Max(0.01f, fadeOutDuration);
            float e = Mathf.SmoothStep(0f, 1f, t);
            MoveTo(Vector2.Lerp(from, dest, e));
            SetAlpha(1f - e);
            yield return fixedStep;
        }
        SetAlpha(0f);
    }

    /// <summary>Appears at pos (while invisible) and gradually materializes.</summary>
    IEnumerator MaterializeAt(Vector2 pos)
    {
        destination = pos;
        hasDestination = true;
        Teleport(pos);
        floatVel = Vector2.zero;
        Trig(Anim.Materialize);
        float t = 0f;
        while (t < 1f)
        {
            t += Time.deltaTime / Mathf.Max(0.01f, materializeDuration);
            SetAlpha(Mathf.SmoothStep(0f, 1f, t));
            yield return null;
        }
        SetAlpha(1f);
    }

    void SetAlpha(float a)
    {
        foreach (SpriteRenderer sr in sprites)
        {
            if (sr == null) continue;
            Color c = sr.color; c.a = a; sr.color = c;
        }
    }

    // =====================================================================
    // Damage + Stagger (Mirror Vanity)
    // =====================================================================
    public void TakeDamage(DamageInfo info)
    {
        if (dead) return;

        // Ab?b? Counter Stance trigger check: if hit during counter stance, trigger counterattack
        if (IsCounterStanceActive && !counterTriggered)
        {
            TriggerFanCounter(info);
            return;
        }

        health -= info.damage;
        HitBlinker.Flash(gameObject);
        CameraShake.Shake(0.12f, 0.15f);
        Debug.Log("Ohsyra took " + info.damage + " damage (hp " + health + "/" + maxHealth + ")");

        if (health <= 0 && state != OhsyraState.Staggered)
        {
            Die();
            return;
        }

        if (state == OhsyraState.Staggered) return;            // damage while staggered does not re-stack the counter
        staggerDamage += info.damage;
        if (staggerDamage >= staggerThreshold) { BeginStagger(); return; }   // stagger replaces recoil
        StartRecoil(info);
    }

    // =====================================================================
    // Death (Mirror Vanity defeated)
    // =====================================================================
    bool dead;
    void Die()
    {
        if (dead) return;
        dead = true;
        Debug.Log("Ohsyra defeated!");
        AbortActivity();
        Body.linearVelocity = Vector2.zero;
        // Fade out all sprites, then disable the boss
        StartCoroutine(DeathRoutine());
    }

    IEnumerator DeathRoutine()
    {
        float duration = fadeOutDuration > 0f ? fadeOutDuration : 0.8f;
        float elapsed = 0f;
        Color[] startColors = new Color[sprites.Length];
        for (int i = 0; i < sprites.Length; i++)
            startColors[i] = sprites[i] != null ? sprites[i].color : Color.white;

        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;
            float t = Mathf.Clamp01(elapsed / duration);
            for (int i = 0; i < sprites.Length; i++)
                if (sprites[i] != null)
                    sprites[i].color = new Color(startColors[i].r, startColors[i].g, startColors[i].b, 1f - t);
            yield return null;
        }
        gameObject.SetActive(false);
    }

    // Stops everything she is doing and returns her to a clean, visible, motionless state.
    // Shared by stagger and (optionally) recoil that interrupts an attack.
    void AbortActivity()
    {
        StopAllCoroutines();                                   // brain, attacks, ray windows, fades, recoil...
        brain = null;
        recoilRoutine = null;
        recoilActive = false;
        moveControl = 1f;
        queued = OhsyraAttack.Auto;
        hasDestination = false;
        floatVel = Vector2.zero;
        rushPlanned = false;
        ResetAnimatorTriggers();
        ClearProjectilesIfConfigured();
        SetAlpha(1f);                                          // never left invisible
        ResetArms();
        SetBool(Anim.IsFloating, false);
        Body.linearVelocity = Vector2.zero;
    }

    void BeginStagger()
    {
        AbortActivity();
        state = OhsyraState.Staggered;
        StartCoroutine(StaggerRoutine());
    }

    // =====================================================================
    // Damage recoil: a small, bounded, graceful displacement away from the hit
    // =====================================================================
    void StartRecoil(DamageInfo info)
    {
        if (recoilDuration <= 0f) return;
        bool committed = state == OhsyraState.Repositioning || state == OhsyraState.Telegraphing || state == OhsyraState.Attacking;
        if (committed && !recoilInterruptsAttacks) return;     // armoured attack: no displacement, no interruption

        Vector2 d = Pos - info.origin;
        d.y *= recoilVerticalScale;
        if (d.sqrMagnitude < 0.0001f) d = new Vector2(-facing, 0f);
        d.Normalize();
        float speed = info.damage >= recoilHeavyDamage ? recoilHeavySpeed : recoilSpeed;

        if (committed)
        {
            AbortActivity();                                   // brain restarts when the recoil ends
            Trig(Anim.Recovery);                               // pushes the Animator out of the abandoned attack state
            state = OhsyraState.Recovery;
        }
        else if (recoilRoutine != null) StopCoroutine(recoilRoutine);   // a new hit restarts (never adds to) the recoil

        recoilRoutine = StartCoroutine(RecoilRoutine(d, speed));
    }

    IEnumerator RecoilRoutine(Vector2 dir, float speed)
    {
        float dt = Time.fixedDeltaTime;
        recoilActive = true;
        moveControl = 0f;                                      // AI movement suspended
        for (float t = 0f; t < recoilDuration; t += dt)
        {
            Vector2 v = dir * speed * (1f - t / recoilDuration);               // linear fade to zero
            Vector2 next = ClampToArena(Pos + v * dt);                         // never leaves the arena
            Body.linearVelocity = (next - Pos) / dt;
            yield return fixedStep;
        }
        Body.linearVelocity = Vector2.zero;
        recoilActive = false;

        for (float t = 0f; t < recoilRecovery; t += dt)        // control blends back in (FloatIdle drives from her real velocity)
        {
            moveControl = t / recoilRecovery;
            yield return fixedStep;
        }
        moveControl = 1f;
        recoilRoutine = null;
        if (brain == null) brain = StartCoroutine(Brain());    // only when an attack was interrupted
    }

    IEnumerator StaggerRoutine()
    {
        SetBool(Anim.IsStaggered, true);
        Trig(Anim.StaggerStart);
        if (mirrorProp != null) mirrorProp.SetActive(true);    // visual prop only
        yield return new WaitForSeconds(staggerDuration);
        if (mirrorProp != null) mirrorProp.SetActive(false);
        SetBool(Anim.IsStaggered, false);
        Trig(Anim.StaggerEnd);
        staggerDamage = 0;                                     // reset counter after the stagger
        brain = StartCoroutine(Brain());
    }

    [ContextMenu("Force Stagger")]
    void DebugStagger() { if (Application.isPlaying && state != OhsyraState.Staggered) BeginStagger(); }

    [ContextMenu("Queue: Golden Jewelry")] void QJ() { QueueAttack(OhsyraAttack.GoldenJewelry); }
    [ContextMenu("Queue: Abebe Fan")] void QF() { QueueAttack(OhsyraAttack.AbebeFan); }
    [ContextMenu("Queue: Golden Honey Rays")] void QR() { QueueAttack(OhsyraAttack.GoldenHoneyRays); }
    [ContextMenu("Queue: Golden Rush")] void QU() { QueueAttack(OhsyraAttack.GoldenRush); }

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
    void ResetAnimatorTriggers() { foreach (string n in triggerNames) animator.ResetTrigger(n); }
}
