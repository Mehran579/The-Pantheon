using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.InputSystem;

public enum PlayerState { Free, Dashing, Attacking, Casting, Parrying }

[System.Serializable]
public class ParryEvent : UnityEvent<EnemyAttackHitbox> { }

[RequireComponent(typeof(Rigidbody2D), typeof(Collider2D), typeof(SoulMeter))]
public partial class LokiController : MonoBehaviour, IDamageable
{
    public static LokiController Instance { get; private set; }

    // =====================================================================
    // Animator parameter names (create these in your Animator Controller)
    // =====================================================================
    public static class Anim
    {
        // Triggers
        public const string SwordAttack = "SwordAttack", SwordAttackUp = "SwordAttackUp", SwordAttackDown = "SwordAttackDown";
        public const string NailArt = "NailArt", MagicInfusedSwordArt = "MagicInfusedSwordArt";
        public const string TraversingSlash = "TraversingSlash", CrescentSecond = "CrescentSecond";
        public const string ArcBolt = "ArcBolt", ArcBoltCharged = "ArcBoltCharged";
        public const string RiftBurst = "RiftBurst", RiftBurstCharged = "RiftBurstCharged";
        public const string AstralOrbit = "AstralOrbit", AstralOrbitCharged = "AstralOrbitCharged";
        public const string Parry = "Parry", ParrySuccess = "ParrySuccess", ParryFail = "ParryFail";
        public const string CrossStitch = "CrossStitch", MagicalAoEBurst = "MagicalAoEBurst";
        public const string Dash = "Dash", Jump = "Jump", DoubleJump = "DoubleJump";
        public const string SwordChargeStart = "SwordChargeStart", MagicChargeStart = "MagicChargeStart";
        // Bools
        public const string IsGrounded = "IsGrounded", IsDashing = "IsDashing";
        public const string IsChargingSword = "IsChargingSword", IsChargingMagic = "IsChargingMagic";
        public const string SwordChargeReady = "SwordChargeReady", MagicChargeReady = "MagicChargeReady";
        // Floats
        public const string MoveSpeed = "MoveSpeed", VerticalSpeed = "VerticalSpeed";
    }

    // =====================================================================
    // Configuration (all public)
    // =====================================================================
    [Header("References")]
    public Animator animator;                  // optional
    public LayerMask enemyLayers;              // layers with IDamageable colliders. Must NOT include the player's layer.

    [Header("Health / Invulnerability")]
    public int health = 100;
    public float hurtInvulnerabilityDuration = 0.8f;     // Hollow Knight / Silksong i-frames after being hit
    public float hurtKnockbackSpeed = 10f;               // Horizontal recoil away from hit
    public float hurtKnockbackUpSpeed = 4f;              // Vertical lift when hit
    public float hurtKnockbackDuration = 0.2f;           // Duration player control is locked

    [Header("Movement")]
    public float moveSpeed = 8f;
    public float groundAcceleration = 90f;
    public float airAcceleration = 60f;
    public float gravityScale = 3.5f;
    public float jumpVelocity = 15f;
    public float airJumpVelocity = 13f;
    public int maxAirJumps = 1;                // 1 = double jump
    [Range(0.1f, 1f)] public float jumpCutMultiplier = 0.5f;   // releasing Jump early multiplies upward speed by this
    public float coyoteTime = 0.12f;
    [Range(0f, 1f)] public float chargeMoveMultiplier = 0.5f;  // speed while holding a charge

    [Header("Ground Check")]
    public LayerMask groundLayers = ~0;        // triggers and the player itself are ignored
    public Vector2 groundCheckOffset = new Vector2(0f, -0.5f);
    public Vector2 groundCheckSize = new Vector2(0.6f, 0.1f);

    [Header("Dash")]
    public float dashSpeed = 22f;
    public float dashDuration = 0.18f;
    public float dashInvulnerabilityDuration = 0.12f;
    public float dashCooldown = 0.35f;
    public int maxAirDashes = 1;

    [Header("Input Buffer")]
    public float inputBufferTime = 0.15f;

    [Header("Normal Sword Attack (single hit, no combo)")]
    public int swordDamage = 10;
    public float swordStartup = 0.04f;
    public float swordActiveTime = 0.12f;
    public float swordRecovery = 0.1f;
    public float swordCooldown = 0.35f;
    [Range(0f, 1f)] public float swordMoveMultiplier = 0.5f;
    public float aimThreshold = 0.5f;          // W/S (Up/Down) aim threshold; Down only counts in the air
    public int soulPerSwordHit = 1;
    public int chargedSlashSoulGain = 1;       // Soul given once when a charged slash (Nail Art / Crescent / Traversing) connects. 3 = fills the spendable pool
    public BoxHitbox swordForward = new BoxHitbox(new Vector2(1.3f, 0f), new Vector2(1.8f, 1.4f), Color.green);
    public BoxHitbox swordUp = new BoxHitbox(new Vector2(0f, 1.5f), new Vector2(1.6f, 1.6f), Color.green);
    public BoxHitbox swordDown = new BoxHitbox(new Vector2(0f, -1.5f), new Vector2(1.6f, 1.6f), Color.green);

    [Header("Pogo (Downward Sword hit in the air)")]
    public float pogoBounceVelocity = 14f;    // upward speed applied on a successful downward hit
    public bool pogoResetsAirJumps = true;    // pogo restores air jumps (true = very generous, false = tighter)

    [Header("Nail Recoil on Attack Hit")]
    public float swordRecoilSpeed = 6f;       // Hollow Knight / Silksong pushback on hitting an enemy with normal slash
    public float swordRecoilDuration = 0.08f; // Short duration for nail recoil
    public float enemyKnockbackSpeed = 8f;    // Knockback applied to hit enemies
    public float enemyKnockbackDuration = 0.15f;

    [Header("Sword Charge / Nail Art (Great Slash)")]
    public float swordChargeTime = 0.8f;       // hold time for a full charge
    public int nailArtDamage = 25;
    public float nailArtStartup = 0.15f;
    public float nailArtActiveTime = 0.15f;
    public float nailArtRecovery = 0.3f;
    [Range(0f, 1f)] public float nailArtMoveMultiplier = 0f;
    public BoxHitbox nailArtHitbox = new BoxHitbox(new Vector2(2.2f, 0f), new Vector2(4.2f, 1.8f), Color.yellow);

    [Header("Magic-infused Sword Art / Crescent Slash (Sword Charge + Magic)")]
    public int magicArtSoulCost = 1;
    public Color magicArtColor = Color.cyan;
    public int crescentDamage = 18;            // melee only: wider/taller than the Great Slash, but less damage (no projectile)
    public BoxHitbox crescentHitbox = new BoxHitbox(new Vector2(1.8f, 0.4f), new Vector2(5f, 3f), Color.cyan);
    public float crescentSecondDelay = 0.25f;  // pause between the first and the second burst
    public int crescentSecondDamage = 10;      // second burst is weaker...
    [Range(0.1f, 1f)] public float crescentSecondScale = 0.7f;   // ...and smaller (multiplies the hitbox size)

    [Header("Magic")]
    public SpellType selectedSpell = SpellType.ArcBolt;
    public float magicChargeTime = 0.8f;
    public float spellAimWindow = 0.2f;        // W/S tapped within this time before E is pressed / released still picks the spell
    [Range(0f, 1f)] public float castMoveMultiplier = 0.3f;
    public ArcBoltSpell arcBolt = new ArcBoltSpell();
    public RiftBurstSpell riftBurst = new RiftBurstSpell();
    public AstralOrbitSpell astralOrbit = new AstralOrbitSpell();

    [Header("Traversing Slash (Magic Charge + Sword): quick slash + piercing sword beam, like Grubberfly's Elegy")]
    public int traverseSoulCost = 1;
    public int traverseSlashDamage = 8;
    public float traverseStartup = 0.05f;
    public float traverseActiveTime = 0.12f;
    public float traverseRecovery = 0.15f;
    [Range(0f, 1f)] public float traverseMoveMultiplier = 0.5f;
    public BoxHitbox traverseSlash = new BoxHitbox(new Vector2(1.3f, 0f), new Vector2(1.8f, 1.4f), new Color(0.4f, 0.8f, 1f));
    public int traverseBeamDamage = 8;
    public float traverseBeamSpeed = 24f;
    public float traverseBeamLifetime = 0.6f;
    public Vector2 traverseBeamSize = new Vector2(2.4f, 2.6f);   // bigger than the slash hitbox; match this to your beam visual
    public Vector2 traverseBeamSpawnOffset = new Vector2(1.2f, 0f);

    [Header("Parry")]
    public float parryStartup = 0.04f;
    public float parryActiveWindow = 0.2f;
    public float parryRecovery = 0.35f;        // used when the parry fails
    public float parrySuccessRecovery = 0.1f;  // used after a plain successful parry
    public float parrySuccessInvulnerability = 0.4f;
    [Range(0f, 1f)] public float parryMoveMultiplier = 0f;
    public int parrySoulMultiplier = 2;        // a successful parry gives soulPerSwordHit x this (2 = double a sword hit)
    public bool keepChargeOnFailedParry = false;
    public BoxHitbox parryArea = new BoxHitbox(new Vector2(0.9f, 0f), new Vector2(2.2f, 2.2f), Color.white);
    public ParryEvent onParrySuccess = new ParryEvent();

    [Header("Cross Stitch (Sword Charge + successful Parry)")]
    public int crossStitchDamage = 30;
    public float crossStitchStartup = 0.1f;
    public float crossStitchActiveTime = 0.15f;
    public float crossStitchRecovery = 0.3f;
    public float crossStitchRange = 4.5f;      // length of each arm of the X
    public float crossStitchThickness = 1f;
    public float crossStitchAngle = 45f;
    public Vector2 crossStitchOffset = new Vector2(2f, 0.2f);
    public Color crossStitchColor = new Color(0.3f, 1f, 0.95f);

    [Header("Magical AoE Burst (Magic Charge + successful Parry)")]
    public int aoeBurstDamage = 20;
    public int aoeBurstSoulCost = 1;
    public float aoeBurstRadius = 4f;
    public Vector2 aoeBurstOffset = Vector2.zero;
    public float aoeBurstStartup = 0.05f;
    public float aoeBurstActiveTime = 0.15f;
    public float aoeBurstRecovery = 0.3f;
    public Color aoeBurstColor = new Color(1f, 0.4f, 0.7f);

    [Header("Optional visual prefabs (all can stay empty)")]
    public GameObject arcBoltPrefab;           // Arc Bolt normal + spell-infused bolt
    public GameObject arcBoltChargedPrefab;    // Charged Arc Bolt
    public GameObject riftEruptionPrefab;      // each vertical eruption (Rift Burst + spell-infused)
    public GameObject orbitBladePrefab;        // one orbiting blade (instantiated per blade)
    public GameObject radialBladePrefab;       // blades flying out (charged orbit release + spell-infused orbit)
    public GameObject crescentPrefab;          // Crescent Slash visual effect
    public GameObject traverseBeamPrefab;      // Traversing Slash sword beam
    public GameObject traverseEffectPrefab;    // slash effect of the Traversing Slash
    public GameObject nailArtEffectPrefab;     // slash effect at start of Nail Art / infused art
    public GameObject crossStitchEffectPrefab; // X slash effect
    public GameObject aoeBurstEffectPrefab;    // radial explosion
    public float effectLifetime = 1f;

    // =====================================================================
    // Runtime state
    // =====================================================================
    [Header("Runtime (read-only for debugging)")]
    public PlayerState state = PlayerState.Free;
    public int facing = 1;
    public bool grounded;
    public int airJumpsLeft;
    public int airDashesLeft;

    Rigidbody2D rb;
    Collider2D col;
    SoulMeter soul;
    Vector2 move;
    Coroutine actionRoutine;
    Coroutine coyoteRoutine;
    float actionMoveMultiplier = 1f;
    bool coyoteActive, dashOnCooldown, swordOnCooldown, parryActive, parrySucceeded;
    int dashDir = 1;
    int invulnCount;
    Vector2 dashStartPos;
    Vector2 lastGroundedPos;
    EnemyAttackHitbox parriedAttack;
    bool slashSoulGiven;
    SpellType lastAimSpell = SpellType.ArcBolt;
    float lastAimTime = -999f;
    AstralOrbit activeOrbit;
    readonly bool[] spellOnCooldown = new bool[3];
    readonly Collider2D[] groundHits = new Collider2D[8];

    class Channel { public bool held, charging, ready; public Coroutine routine; }
    readonly Channel sword = new Channel();
    readonly Channel magic = new Channel();

    enum Act { Jump, Dash, Parry, SwordPress, SpellPress, SwordRelease, SpellRelease }
    static readonly Act[] priority = { Act.Parry, Act.Dash, Act.Jump, Act.SwordRelease, Act.SpellRelease, Act.SpellPress, Act.SwordPress };
    readonly bool[] buffered = new bool[7];
    readonly Coroutine[] bufferRoutines = new Coroutine[7];

    enum Aim { Forward, Up, Down }

    InputAction moveAct, jumpAct, swordAct, spellAct, dashAct, parryAct;
    HashSet<string> animParams;

    // ---------- helpers shared with the gizmo file ----------
    Vector2 Pos { get { return transform.position; } }
    Collider2D Col { get { if (col == null) col = GetComponent<Collider2D>(); return col; } }
    float FeetY { get { return Col.bounds.min.y; } }
    int Dir { get { return Application.isPlaying ? facing : (transform.localScale.x < 0f ? -1 : 1); } }
    Vector2 GroundCheckCenter() { return Pos + groundCheckOffset; }
    public bool Invulnerable { get { return invulnCount > 0; } }
    public Bounds PlayerBounds { get { return Col.bounds; } }

    SpellBase Cfg(SpellType s)
    {
        if (s == SpellType.ArcBolt) return arcBolt;
        if (s == SpellType.RiftBurst) return riftBurst;
        return astralOrbit;
    }

    Bounds ParryBounds()
    {
        Vector2 c = parryArea.Center(Pos, Dir);
        return new Bounds(new Vector3(c.x, c.y, 0f), new Vector3(parryArea.size.x, parryArea.size.y, 100f));
    }

    Vector2 CrossStitchCenter() { return Pos + new Vector2(crossStitchOffset.x * Dir, crossStitchOffset.y); }
    Vector2 CrossStitchSize() { return new Vector2(crossStitchRange, crossStitchThickness); }
    Vector2 AoECenter() { return Pos + aoeBurstOffset; }

    // Eruption box i of Rift Burst (i is always 0 for the normal version). Used by gameplay AND gizmos.
    void RiftBox(Vector2 origin, int i, bool charged, out Vector2 center, out Vector2 size)
    {
        float w = charged ? riftBurst.chargedWidth : riftBurst.width;
        float h = charged ? riftBurst.chargedHeight : riftBurst.height;
        float x = origin.x + Dir * (riftBurst.forwardOffset + (charged ? i * riftBurst.chargedSpacing : 0f));
        float y = FeetY + riftBurst.bottomOffset + h * 0.5f;
        center = new Vector2(x, y);
        size = new Vector2(w, h);
    }

    // =====================================================================
    // Unity lifecycle + input
    // =====================================================================
    void Awake()
    {
        Instance = this;
        rb = GetComponent<Rigidbody2D>();
        col = GetComponent<Collider2D>();
        soul = GetComponent<SoulMeter>();
        rb.gravityScale = gravityScale;
        rb.freezeRotation = true;
        facing = transform.localScale.x < 0f ? -1 : 1;
        airJumpsLeft = maxAirJumps;
        airDashesLeft = maxAirDashes;
        CacheAnimatorParams();
        BuildInput();
    }

    void OnDestroy() { if (Instance == this) Instance = null; }

    void BuildInput()
    {
        moveAct = new InputAction("Move", InputActionType.Value);
        moveAct.AddCompositeBinding("2DVector")
            .With("Up", "<Keyboard>/w").With("Down", "<Keyboard>/s")
            .With("Left", "<Keyboard>/a").With("Right", "<Keyboard>/d");
        moveAct.AddCompositeBinding("2DVector")
            .With("Up", "<Keyboard>/upArrow").With("Down", "<Keyboard>/downArrow")
            .With("Left", "<Keyboard>/leftArrow").With("Right", "<Keyboard>/rightArrow");
        jumpAct = new InputAction("Jump", InputActionType.Button, "<Keyboard>/space");
        swordAct = new InputAction("Sword", InputActionType.Button, "<Mouse>/leftButton");
        spellAct = new InputAction("Spell", InputActionType.Button, "<Keyboard>/e");
        dashAct = new InputAction("Dash", InputActionType.Button, "<Keyboard>/leftShift");
        parryAct = new InputAction("Parry", InputActionType.Button, "<Mouse>/rightButton");
    }

    void OnEnable()
    {
        jumpAct.performed += OnJumpPressed; jumpAct.canceled += OnJumpReleased;
        swordAct.performed += OnSwordPressed; swordAct.canceled += OnSwordReleased;
        spellAct.performed += OnSpellPressed; spellAct.canceled += OnSpellReleased;
        dashAct.performed += OnDashPressed;
        parryAct.performed += OnParryPressed;
        moveAct.Enable(); jumpAct.Enable(); swordAct.Enable(); spellAct.Enable(); dashAct.Enable(); parryAct.Enable();
    }

    void OnDisable()
    {
        jumpAct.performed -= OnJumpPressed; jumpAct.canceled -= OnJumpReleased;
        swordAct.performed -= OnSwordPressed; swordAct.canceled -= OnSwordReleased;
        spellAct.performed -= OnSpellPressed; spellAct.canceled -= OnSpellReleased;
        dashAct.performed -= OnDashPressed;
        parryAct.performed -= OnParryPressed;
        moveAct.Disable(); jumpAct.Disable(); swordAct.Disable(); spellAct.Disable(); dashAct.Disable(); parryAct.Disable();
    }

    void OnJumpPressed(InputAction.CallbackContext c) { if (!TryExecute(Act.Jump)) Buffer(Act.Jump); }
    void OnDashPressed(InputAction.CallbackContext c) { if (!TryExecute(Act.Dash)) Buffer(Act.Dash); }
    void OnParryPressed(InputAction.CallbackContext c) { if (!TryExecute(Act.Parry)) Buffer(Act.Parry); }

    void OnJumpReleased(InputAction.CallbackContext c)
    {
        if (state != PlayerState.Dashing && rb.linearVelocity.y > 0f)
            rb.linearVelocity = new Vector2(rb.linearVelocity.x, rb.linearVelocity.y * jumpCutMultiplier);
    }

    void OnSwordPressed(InputAction.CallbackContext c)
    {
        sword.held = true;
        if (!TryExecute(Act.SwordPress)) Buffer(Act.SwordPress);
    }

    void OnSwordReleased(InputAction.CallbackContext c)
    {
        sword.held = false;
        if (sword.charging && !TryExecute(Act.SwordRelease)) Buffer(Act.SwordRelease);
    }

    void OnSpellPressed(InputAction.CallbackContext c)
    {
        magic.held = true;
        if (!TryExecute(Act.SpellPress)) Buffer(Act.SpellPress);
    }

    void OnSpellReleased(InputAction.CallbackContext c)
    {
        magic.held = false;
        if (magic.charging) selectedSpell = PickSpellFromInput();   // aim at the moment of release decides the spell
        if (magic.charging && !TryExecute(Act.SpellRelease)) Buffer(Act.SpellRelease);
    }

    void Update()
    {
        move = moveAct.ReadValue<Vector2>();
        TrackSpellAim();
        if (magic.charging && magic.held) selectedSpell = PickSpellFromInput();   // live: previews / follows the aim while charging
        if (state == PlayerState.Free) FaceInput();
        ProcessBuffer();
        UpdateAnimator();
    }

    void FixedUpdate()
    {
        UpdateGrounded();
        if (state == PlayerState.Dashing)
        {
            rb.linearVelocity = new Vector2(dashDir * dashSpeed, 0f);
            return;
        }
        if (IsKnockedBack)                                    // knockback: horizontal control suspended while it fades out
        {
            float k = 1f - (Time.time - knockStart) / knockDuration;
            rb.linearVelocity = new Vector2(knockVx * Mathf.Clamp01(k), rb.linearVelocity.y);
            return;
        }
        float target = move.x * moveSpeed * CurrentMoveMultiplier();
        float accel = grounded ? groundAcceleration : airAcceleration;
        float vx = Mathf.MoveTowards(rb.linearVelocity.x, target, accel * Time.fixedDeltaTime);
        rb.linearVelocity = new Vector2(vx, rb.linearVelocity.y);
    }

    // ---------- Status effects applied by enemies (Ohsyra) ----------
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

    float knockVx, knockStart = -999f, knockDuration;
    public bool IsKnockedBack { get { return Time.time < knockStart + knockDuration; } }
    /// Horizontal speed fades to 0 over duration (player horizontal control is suspended meanwhile); y is an optional one-off lift.
    /// Ignored while already knocked back, so hits cannot chain-launch Loki.
    public bool ApplyKnockback(Vector2 velocity, float duration, bool overrideCurrent = false)
    {
        if ((!overrideCurrent && IsKnockedBack) || duration <= 0f) return false;
        knockVx = velocity.x;
        knockStart = Time.time;
        knockDuration = duration;
        rb.linearVelocity = new Vector2(velocity.x, Mathf.Max(rb.linearVelocity.y, velocity.y));
        return true;
    }

    float CurrentMoveMultiplier()
    {
        float m = state == PlayerState.Free ? ((sword.charging || magic.charging) ? chargeMoveMultiplier : 1f) : actionMoveMultiplier;
        return IsSlowed ? m * slowMultiplier : m;
    }

    void FaceInput()
    {
        if (Mathf.Abs(move.x) > 0.1f) SetFacing(move.x > 0f ? 1 : -1);
    }

    void SetFacing(int f)
    {
        facing = f;
        Vector3 s = transform.localScale;
        s.x = Mathf.Abs(s.x) * f;
        transform.localScale = s;
    }

    // =====================================================================
    // Ground / coyote
    // =====================================================================
    void UpdateGrounded()
    {
        ContactFilter2D filter = new ContactFilter2D();
        filter.SetLayerMask(groundLayers);
        filter.useTriggers = false;
        int n = Physics2D.OverlapBox(GroundCheckCenter(), groundCheckSize, 0f, filter, groundHits);
        bool hit = false;
        for (int i = 0; i < n; i++)
            if (groundHits[i].attachedRigidbody != rb) { hit = true; break; }
        hit &= rb.linearVelocity.y <= 0.1f;

        if (hit)
        {
            grounded = true;
            airJumpsLeft = maxAirJumps;
            airDashesLeft = maxAirDashes;
            if (coyoteRoutine != null) { StopCoroutine(coyoteRoutine); coyoteRoutine = null; }
            coyoteActive = false;
            lastGroundedPos = Pos;
        }
        else if (grounded)
        {
            grounded = false;                               // walked off a ledge (jumps clear grounded themselves)
            coyoteRoutine = StartCoroutine(CoyoteRoutine());
        }
    }

    IEnumerator CoyoteRoutine()
    {
        coyoteActive = true;
        yield return new WaitForSeconds(coyoteTime);
        coyoteActive = false;
        coyoteRoutine = null;
    }

    // =====================================================================
    // Input buffer
    // =====================================================================
    void Buffer(Act a)
    {
        int i = (int)a;
        if (bufferRoutines[i] != null) StopCoroutine(bufferRoutines[i]);
        bufferRoutines[i] = StartCoroutine(BufferRoutine(a));
    }

    IEnumerator BufferRoutine(Act a)
    {
        int i = (int)a;
        buffered[i] = true;
        yield return new WaitForSeconds(inputBufferTime);
        buffered[i] = false;
        bufferRoutines[i] = null;
        // A release that never became valid: drop the stale charge so it cannot fire later.
        if (a == Act.SwordRelease && !sword.held) ConsumeChannel(sword);
        if (a == Act.SpellRelease && !magic.held) ConsumeChannel(magic);
    }

    void ClearBuffer(Act a)
    {
        int i = (int)a;
        buffered[i] = false;
        if (bufferRoutines[i] != null) { StopCoroutine(bufferRoutines[i]); bufferRoutines[i] = null; }
    }

    void ProcessBuffer()
    {
        foreach (Act a in priority)
            if (buffered[(int)a] && TryExecute(a)) ClearBuffer(a);   // cleared on execution -> no duplicate attacks
    }

    // Returns true when the input was handled (executed, or deliberately ignored). False = not allowed now, buffer it.
    bool TryExecute(Act a)
    {
        switch (a)
        {
            case Act.Jump: return TryJump();
            case Act.Dash: return TryDash();
            case Act.Parry: return TryParry();
            case Act.SwordPress: return TrySwordPress();
            case Act.SpellPress: return TrySpellPress();
            case Act.SwordRelease: return TryRelease(sword, true);
            case Act.SpellRelease: return TryRelease(magic, false);
        }
        return false;
    }

    // =====================================================================
    // Jump / Dash
    // =====================================================================
    bool TryJump()
    {
        if (state == PlayerState.Dashing) return false;
        if (grounded || coyoteActive)
        {
            if (coyoteRoutine != null) { StopCoroutine(coyoteRoutine); coyoteRoutine = null; }
            coyoteActive = false;
            DoJump(jumpVelocity, Anim.Jump);
            return true;
        }
        if (airJumpsLeft > 0)
        {
            airJumpsLeft--;
            DoJump(airJumpVelocity, Anim.DoubleJump);
            return true;
        }
        return false;
    }

    void DoJump(float v, string trigger)
    {
        rb.linearVelocity = new Vector2(rb.linearVelocity.x, v);
        grounded = false;
        Trig(trigger);
    }

    bool TryDash()
    {
        if (state != PlayerState.Free || dashOnCooldown) return false;
        if (!grounded && airDashesLeft <= 0) return false;
        if (!grounded) airDashesLeft--;
        StartCoroutine(DashRoutine());
        return true;
    }

    IEnumerator DashRoutine()
    {
        state = PlayerState.Dashing;
        dashDir = Mathf.Abs(move.x) > 0.1f ? (move.x > 0f ? 1 : -1) : facing;
        SetFacing(dashDir);
        dashStartPos = Pos;
        Trig(Anim.Dash);
        rb.gravityScale = 0f;
        rb.linearVelocity = new Vector2(dashDir * dashSpeed, 0f);
        StartCoroutine(InvulnerabilityRoutine(Mathf.Min(dashInvulnerabilityDuration, dashDuration)));

        yield return new WaitForSeconds(dashDuration);

        rb.gravityScale = gravityScale;
        rb.linearVelocity = new Vector2(dashDir * moveSpeed, 0f);
        state = PlayerState.Free;
        StartCoroutine(DashCooldownRoutine());
    }

    IEnumerator DashCooldownRoutine()
    {
        dashOnCooldown = true;
        yield return new WaitForSeconds(dashCooldown);
        dashOnCooldown = false;
    }

    IEnumerator InvulnerabilityRoutine(float duration)
    {
        invulnCount++;
        yield return new WaitForSeconds(duration);
        invulnCount--;
    }

    // =====================================================================
    // Action state helpers
    // =====================================================================
    void BeginAction(PlayerState s, float moveMultiplier)
    {
        state = s;
        actionMoveMultiplier = moveMultiplier;
    }

    void EndAction()
    {
        state = PlayerState.Free;
        actionRoutine = null;
        actionMoveMultiplier = 1f;
    }

    void StartAction(IEnumerator routine) { actionRoutine = StartCoroutine(routine); }

    void StopAction()
    {
        if (actionRoutine != null) { StopCoroutine(actionRoutine); actionRoutine = null; }
        parryActive = false;
    }

    // =====================================================================
    // Charging + charge interaction matrix
    // =====================================================================
    void ConsumeChannel(Channel c)
    {
        if (c.routine != null) { StopCoroutine(c.routine); c.routine = null; }
        c.charging = false;
        c.ready = false;
    }

    IEnumerator ChargeRoutine(Channel c, float time, bool isMagic)
    {
        c.charging = true;
        c.ready = false;
        Trig(isMagic ? Anim.MagicChargeStart : Anim.SwordChargeStart);
        yield return new WaitForSeconds(time);
        if (isMagic)
            while (!soul.CanSpend(MinChargedCost())) yield return null;   // empowered needs Soul
        c.ready = true;
        c.routine = null;
    }

    // LMB pressed
    bool TrySwordPress()
    {
        if (state != PlayerState.Free) return false;

        // Magic Charge + Sword = Traversing Slash (quick slash + piercing sword beam)
        if (magic.ready)
        {
            if (soul.CanSpend(traverseSoulCost))
            {
                ConsumeChannel(magic);
                FaceInput();
                StartAction(TraversingSlashRoutine());
                return true;
            }
            ConsumeChannel(magic);          // cannot afford it any more: drop the charge, continue as a normal press
        }
        if (magic.charging) return true;    // still charging magic: ignore
        if (sword.charging) return true;
        if (swordOnCooldown) return false;

        // The swing fires immediately on press. If LMB is still held when the swing ends, the charge starts (see SwordAttackRoutine).
        FaceInput();
        StartAction(SwordAttackRoutine());
        return true;
    }

    void StartSwordCharge() { sword.routine = StartCoroutine(ChargeRoutine(sword, swordChargeTime, false)); }

    // Hollow Knight style: E = Arc Bolt, Up + E = Rift Burst, Down + E = Astral Orbit.
    // Direction counts if it is held OR was tapped within spellAimWindow, and is read when E is pressed and again when it is released.
    void TrackSpellAim()
    {
        if (move.y > aimThreshold) { lastAimSpell = SpellType.RiftBurst; lastAimTime = Time.time; }
        else if (move.y < -aimThreshold) { lastAimSpell = SpellType.AstralOrbit; lastAimTime = Time.time; }
    }

    SpellType PickSpellFromInput()
    {
        float y = moveAct.ReadValue<Vector2>().y;   // live read, so a tap on the same frame as E is not missed
        if (y > aimThreshold) return SpellType.RiftBurst;
        if (y < -aimThreshold) return SpellType.AstralOrbit;
        if (Time.time - lastAimTime <= spellAimWindow) return lastAimSpell;
        return SpellType.ArcBolt;
    }

    // Cheapest empowered cost: the charge becomes ready once ANY spell could be empowered; the real check happens on release.
    int MinChargedCost()
    {
        return Mathf.Min(arcBolt.chargedCost, Mathf.Min(riftBurst.chargedCost, astralOrbit.chargedCost));
    }

    // E pressed
    bool TrySpellPress()
    {
        if (state != PlayerState.Free) return false;

        // Sword Charge + Magic = Magic-infused Sword Art (Great Slash + long-range magical crescent)
        if (sword.ready)
        {
            if (!soul.CanSpend(magicArtSoulCost)) { Debug.Log("Not enough Soul for Magic-infused Sword Art"); return true; }
            ConsumeChannel(sword);
            FaceInput();
            StartAction(CrescentSlashRoutine());
            return true;
        }
        if (sword.charging || magic.charging) return true;

        selectedSpell = PickSpellFromInput();            // E = Arc Bolt, W/Up + E = Rift Burst, S/Down + E = Astral Orbit

        if (spellOnCooldown[(int)selectedSpell]) return false;
        if (!soul.CanSpend(Cfg(selectedSpell).normalCost)) { Debug.Log("Not enough spendable Soul"); return true; }

        FaceInput();
        if (!magic.held)
        {
            StartAction(CastSpellRoutine(selectedSpell, false));
            return true;
        }
        magic.routine = StartCoroutine(ChargeRoutine(magic, magicChargeTime, true));
        return true;
    }

    // Button released while its own channel is charging: Sword+Sword = Nail Art, Magic+Magic = Empowered spell,
    // not fully charged = normal attack / normal spell.
    bool TryRelease(Channel c, bool isSword)
    {
        if (!c.charging) return true;                   // charge was already consumed by another action
        if (state != PlayerState.Free) return false;
        bool wasReady = c.ready;

        if (isSword)
        {
            ConsumeChannel(c);
            if (!wasReady) return true;     // the swing already fired on press: letting go early just cancels the charge
            FaceInput();
            StartAction(NailArtRoutine());
            return true;
        }

        SpellBase b = Cfg(selectedSpell);
        if (spellOnCooldown[(int)selectedSpell]) return false;
        bool charged = wasReady && soul.CanSpend(b.chargedCost);
        ConsumeChannel(c);
        FaceInput();
        if (charged || soul.CanSpend(b.normalCost)) StartAction(CastSpellRoutine(selectedSpell, charged));
        return true;
    }

    // RMB pressed. Parry cancels sword attacks and spells, never a dash, and never itself.
    bool TryParry()
    {
        if (state == PlayerState.Dashing || state == PlayerState.Parrying) return false;
        StopAction();
        FaceInput();

        // Remember which stored charge this parry carries; it is consumed now.
        ChargeKind stored = ChargeKind.None;
        if (sword.ready) stored = ChargeKind.Sword;
        else if (magic.ready) stored = ChargeKind.Magic;
        if (sword.charging) ConsumeChannel(sword);      // not-ready charges are simply cancelled
        if (magic.charging) ConsumeChannel(magic);

        StartAction(ParryRoutine(stored));
        return true;
    }

    enum ChargeKind { None, Sword, Magic }

    // =====================================================================
    // Hit query helpers (mark the gizmo and query with the very same shape)
    // =====================================================================
    Collider2D[] QueryBox(Vector2 center, Vector2 size, float angle, Color color)
    {
        MarkBox(center, size, angle, color);
        return Physics2D.OverlapBoxAll(center, size, angle, enemyLayers);
    }

    Collider2D[] QueryCircle(Vector2 center, float radius, Color color)
    {
        MarkCircle(center, radius, color);
        return Physics2D.OverlapCircleAll(center, radius, enemyLayers);
    }

    void SpawnEffect(GameObject prefab, Vector2 pos, float scale = 1f)
    {
        if (prefab == null) return;
        GameObject go = Instantiate(prefab, pos, Quaternion.identity);
        Vector3 s = go.transform.localScale * scale;
        s.x = Mathf.Abs(s.x) * Dir;
        go.transform.localScale = s;
        Destroy(go, effectLifetime);
    }

    // =====================================================================
    // Normal sword attack (single hit, directional)
    // =====================================================================
    IEnumerator SwordAttackRoutine()
    {
        BeginAction(PlayerState.Attacking, swordMoveMultiplier);
        StartCoroutine(SwordCooldownRoutine());

        Aim aim = Aim.Forward;
        if (move.y > aimThreshold) aim = Aim.Up;
        else if (move.y < -aimThreshold && !grounded) aim = Aim.Down;
        BoxHitbox hb = aim == Aim.Up ? swordUp : (aim == Aim.Down ? swordDown : swordForward);
        Trig(aim == Aim.Up ? Anim.SwordAttackUp : (aim == Aim.Down ? Anim.SwordAttackDown : Anim.SwordAttack));

        yield return new WaitForSeconds(swordStartup);

        HashSet<object> hits = new HashSet<object>();
        bool pogoTriggered = false;
        bool recoilTriggered = false;
        float end = Time.time + swordActiveTime;
        while (Time.time < end)
        {
            Collider2D[] cols = QueryBox(hb.Center(Pos, Dir), hb.size, 0f, hb.color);
            int n = Combat.Damage(cols, swordDamage, Pos, gameObject, hits, out _, false, enemyKnockbackSpeed, enemyKnockbackDuration);
            if (n > 0)
            {
                soul.AddSoul(soulPerSwordHit * n);
                // Pogo: downward attack while airborne bounces Loki upward
                if (aim == Aim.Down && !grounded && !pogoTriggered)
                {
                    pogoTriggered = true;
                    rb.linearVelocity = new Vector2(rb.linearVelocity.x, pogoBounceVelocity);
                    if (pogoResetsAirJumps) airJumpsLeft = maxAirJumps;
                }
                // Horizontal sword attack recoil (Hollow Knight / Silksong nail pushback)
                else if (aim == Aim.Forward && !recoilTriggered)
                {
                    recoilTriggered = true;
                    ApplyKnockback(new Vector2(-Dir * swordRecoilSpeed, 0f), swordRecoilDuration, true);
                }
            }
            yield return new WaitForFixedUpdate();
        }
        yield return new WaitForSeconds(swordRecovery);
        EndAction();
        if (sword.held && !sword.charging && !magic.charging && !magic.ready) StartSwordCharge();   // still holding LMB after the swing
    }

    IEnumerator SwordCooldownRoutine()
    {
        swordOnCooldown = true;
        yield return new WaitForSeconds(swordCooldown);
        swordOnCooldown = false;
    }

    // =====================================================================
    // Nail Art (Great Slash) and Magic-infused Sword Art (same technique + magic crescent)
    // =====================================================================
    IEnumerator NailArtRoutine()
    {
        BeginAction(PlayerState.Attacking, nailArtMoveMultiplier);
        slashSoulGiven = false;
        Trig(Anim.NailArt);
        yield return new WaitForSeconds(nailArtStartup);

        SpawnEffect(nailArtEffectPrefab, nailArtHitbox.Center(Pos, Dir));
        yield return SlashWindow(nailArtHitbox, 1f, nailArtDamage, nailArtHitbox.color, nailArtActiveTime);
        yield return new WaitForSeconds(nailArtRecovery);
        EndAction();
    }

    // Sword Charge + Magic: two delayed bursts. The 2nd is smaller and weaker, so it punishes stunned bosses, not fast movers.
    IEnumerator CrescentSlashRoutine()
    {
        BeginAction(PlayerState.Attacking, nailArtMoveMultiplier);
        slashSoulGiven = false;
        Trig(Anim.MagicInfusedSwordArt);
        yield return new WaitForSeconds(nailArtStartup);

        if (!soul.TrySpend(magicArtSoulCost)) { EndAction(); yield break; }

        SpawnEffect(crescentPrefab, crescentHitbox.Center(Pos, Dir));
        yield return SlashWindow(crescentHitbox, 1f, crescentDamage, magicArtColor, nailArtActiveTime);

        yield return new WaitForSeconds(crescentSecondDelay);

        Trig(Anim.CrescentSecond);
        SpawnEffect(crescentPrefab, crescentHitbox.Center(Pos, Dir), crescentSecondScale);
        yield return SlashWindow(crescentHitbox, crescentSecondScale, crescentSecondDamage, magicArtColor, nailArtActiveTime);

        yield return new WaitForSeconds(nailArtRecovery);
        EndAction();
    }

    // One burst of melee hitbox. Each call has its own hit list, so a later burst can hit the same target again.
    IEnumerator SlashWindow(BoxHitbox hb, float scale, int damage, Color color, float duration)
    {
        HashSet<object> hits = new HashSet<object>();
        float end = Time.time + duration;
        while (Time.time < end)
        {
            Collider2D[] cols = QueryBox(hb.Center(Pos, Dir), hb.size * scale, 0f, color);
            if (Combat.Damage(cols, damage, Pos, gameObject, hits, out _) > 0) OnChargedSlashHit();
            yield return new WaitForFixedUpdate();
        }
    }

    // Soul reward: once per charged slash, on the first hit that connects.
    void OnChargedSlashHit()
    {
        if (slashSoulGiven) return;
        slashSoulGiven = true;
        soul.AddSoul(chargedSlashSoulGain);
    }

    // =====================================================================
    // Traversing Slash (Magic Charge + Sword): a quick slash that sends a thin piercing sword beam across the room
    // =====================================================================
    IEnumerator TraversingSlashRoutine()
    {
        BeginAction(PlayerState.Attacking, traverseMoveMultiplier);
        slashSoulGiven = false;
        Trig(Anim.TraversingSlash);
        yield return new WaitForSeconds(traverseStartup);

        if (!soul.TrySpend(traverseSoulCost)) { EndAction(); yield break; }

        SpawnEffect(traverseEffectPrefab, traverseSlash.Center(Pos, Dir));
        Vector2 spawn = Pos + new Vector2(traverseBeamSpawnOffset.x * Dir, traverseBeamSpawnOffset.y);
        Projectile beam = Projectile.Spawn(spawn, Vector2.right * Dir, traverseBeamSpeed, traverseBeamLifetime, traverseBeamSize,
                                           traverseBeamDamage, true, enemyLayers, traverseSlash.color, traverseBeamPrefab, gameObject);
        beam.onHit = OnChargedSlashHit;                 // the beam connecting also gives the Soul reward

        yield return SlashWindow(traverseSlash, 1f, traverseSlashDamage, traverseSlash.color, traverseActiveTime);
        yield return new WaitForSeconds(traverseRecovery);
        EndAction();
    }

    // =====================================================================
    // Spells
    // =====================================================================
    IEnumerator CastSpellRoutine(SpellType s, bool charged)
    {
        BeginAction(PlayerState.Casting, castMoveMultiplier);
        SpellBase b = Cfg(s);
        Trig(SpellTrigger(s, charged));
        yield return new WaitForSeconds(b.castStartup);

        if (!soul.TrySpend(charged ? b.chargedCost : b.normalCost)) { EndAction(); yield break; }
        StartCoroutine(SpellCooldownRoutine(s, b.cooldown));

        switch (s)
        {
            case SpellType.ArcBolt:
                FireArcBolt(charged);
                break;
            case SpellType.RiftBurst:
                if (charged) yield return RiftChargedSequence();   // nested: stops together with this cast if parried
                else SpawnRiftEruption(Pos, 0, false);
                break;
            case SpellType.AstralOrbit:
                SpawnOrbit(charged);
                break;
        }
        yield return new WaitForSeconds(b.castRecovery);
        EndAction();
    }

    string SpellTrigger(SpellType s, bool charged)
    {
        if (s == SpellType.ArcBolt) return charged ? Anim.ArcBoltCharged : Anim.ArcBolt;
        if (s == SpellType.RiftBurst) return charged ? Anim.RiftBurstCharged : Anim.RiftBurst;
        return charged ? Anim.AstralOrbitCharged : Anim.AstralOrbit;
    }

    IEnumerator SpellCooldownRoutine(SpellType s, float duration)
    {
        spellOnCooldown[(int)s] = true;
        yield return new WaitForSeconds(duration);
        spellOnCooldown[(int)s] = false;
    }

    void FireArcBolt(bool charged)
    {
        Vector2 spawn = Pos + new Vector2(arcBolt.spawnOffset.x * Dir, arcBolt.spawnOffset.y);
        if (charged)
            Projectile.Spawn(spawn, Vector2.right * Dir, arcBolt.chargedSpeed, arcBolt.chargedLifetime, arcBolt.chargedSize,
                             arcBolt.chargedDamage, true, enemyLayers, arcBolt.color, arcBoltChargedPrefab, gameObject);   // piercing
        else
            Projectile.Spawn(spawn, Vector2.right * Dir, arcBolt.speed, arcBolt.lifetime, arcBolt.size,
                             arcBolt.damage, false, enemyLayers, arcBolt.color, arcBoltPrefab, gameObject);
    }

    IEnumerator RiftChargedSequence()
    {
        Vector2 origin = Pos;
        for (int i = 0; i < riftBurst.chargedCount; i++)
        {
            SpawnRiftEruption(origin, i, true);
            yield return new WaitForSeconds(riftBurst.chargedInterval);
        }
    }

    void SpawnRiftEruption(Vector2 origin, int i, bool charged)
    {
        Vector2 center, size;
        RiftBox(origin, i, charged, out center, out size);
        RiftEruption.Spawn(center, size, riftBurst.eruptionDelay, riftBurst.eruptionActiveTime,
                           charged ? riftBurst.chargedDamage : riftBurst.damage, enemyLayers, riftBurst.color,
                           riftEruptionPrefab, gameObject);
    }

    void SpawnOrbit(bool charged)
    {
        if (activeOrbit != null) Destroy(activeOrbit.gameObject);
        GameObject go = new GameObject("AstralOrbit");
        go.transform.position = transform.position;
        activeOrbit = go.AddComponent<AstralOrbit>();
        activeOrbit.Begin(astralOrbit, charged, transform, enemyLayers, orbitBladePrefab, radialBladePrefab, gameObject);
    }

    // =====================================================================
    // Parry (+ Cross Stitch and Magical AoE Burst)
    // =====================================================================
    IEnumerator ParryRoutine(ChargeKind stored)
    {
        BeginAction(PlayerState.Parrying, parryMoveMultiplier);
        Trig(Anim.Parry);
        yield return new WaitForSeconds(parryStartup);

        parryActive = true;
        parrySucceeded = false;
        parriedAttack = null;
        float end = Time.time + parryActiveWindow;
        while (Time.time < end && !parrySucceeded)
        {
            DetectParry();
            yield return new WaitForFixedUpdate();
        }
        parryActive = false;

        if (!parrySucceeded)
        {
            Trig(Anim.ParryFail);
            if (keepChargeOnFailedParry) RestoreCharge(stored);
            yield return new WaitForSeconds(parryRecovery);
            EndAction();
            yield break;
        }

        Trig(Anim.ParrySuccess);
        soul.AddSoul(soulPerSwordHit * parrySoulMultiplier);
        onParrySuccess.Invoke(parriedAttack);
        StartCoroutine(InvulnerabilityRoutine(parrySuccessInvulnerability));

        if (stored == ChargeKind.Magic && soul.TrySpend(aoeBurstSoulCost))
            yield return AoEBurstRoutine();                          // Magic Charge + Parry
        else if (stored == ChargeKind.Sword)
            yield return CrossStitchRoutine();                       // Sword Charge + Parry
        else
            yield return new WaitForSeconds(parrySuccessRecovery);   // normal parry
        EndAction();
    }

    void DetectParry()
    {
        MarkBox(parryArea.Center(Pos, Dir), parryArea.size, 0f, parryArea.color);
        for (int i = EnemyAttackHitbox.All.Count - 1; i >= 0; i--)
            if (TryParryAttack(EnemyAttackHitbox.All[i])) return;
    }

    public bool TryParryAttack(EnemyAttackHitbox atk)
    {
        if (!parryActive || parrySucceeded || atk == null || !atk.IsParryable) return false;
        if (!atk.GetBounds().Intersects(ParryBounds())) return false;
        parrySucceeded = true;
        parriedAttack = atk;
        atk.NotifyParried(this);
        return true;
    }

    void RestoreCharge(ChargeKind stored)
    {
        if (stored == ChargeKind.Sword && sword.held) { sword.charging = true; sword.ready = true; }
        if (stored == ChargeKind.Magic && magic.held) { magic.charging = true; magic.ready = true; }
    }

    IEnumerator CrossStitchRoutine()
    {
        actionMoveMultiplier = 0f;
        Trig(Anim.CrossStitch);
        yield return new WaitForSeconds(crossStitchStartup);
        SpawnEffect(crossStitchEffectPrefab, CrossStitchCenter());

        HashSet<object> hits = new HashSet<object>();
        float end = Time.time + crossStitchActiveTime;
        while (Time.time < end)
        {
            Vector2 c = CrossStitchCenter();
            Vector2 size = CrossStitchSize();
            Combat.Damage(QueryBox(c, size, crossStitchAngle, crossStitchColor), crossStitchDamage, Pos, gameObject, hits, out _);
            Combat.Damage(QueryBox(c, size, -crossStitchAngle, crossStitchColor), crossStitchDamage, Pos, gameObject, hits, out _);
            yield return new WaitForFixedUpdate();
        }
        yield return new WaitForSeconds(crossStitchRecovery);
    }

    IEnumerator AoEBurstRoutine()
    {
        actionMoveMultiplier = 0f;
        Trig(Anim.MagicalAoEBurst);
        yield return new WaitForSeconds(aoeBurstStartup);
        SpawnEffect(aoeBurstEffectPrefab, AoECenter());

        HashSet<object> hits = new HashSet<object>();
        float end = Time.time + aoeBurstActiveTime;
        while (Time.time < end)
        {
            Collider2D[] cols = QueryCircle(AoECenter(), aoeBurstRadius, aoeBurstColor);
            Combat.Damage(cols, aoeBurstDamage, Pos, gameObject, hits, out _, true);   // every collider of a big boss can be hit
            yield return new WaitForFixedUpdate();
        }
        yield return new WaitForSeconds(aoeBurstRecovery);
    }

    // =====================================================================
    // Player damage (parry check first, then invulnerability)
    // =====================================================================
    public void TakeDamage(DamageInfo info)
    {
        if (info.enemyAttack != null && TryParryAttack(info.enemyAttack)) return;
        if (Invulnerable) return;
        health -= info.damage;
        Debug.Log("Loki took " + info.damage + " damage (hp " + health + ")");

        // Hollow Knight / Silksong white flash + camera shake
        HitBlinker.Flash(gameObject);
        CameraShake.Shake(0.15f, 0.25f);

        // Hurt knockback away from hit origin / damage source
        float dirX = Pos.x - info.origin.x;
        if (Mathf.Abs(dirX) < 0.01f) dirX = -facing;
        float sgn = dirX < 0f ? -1f : 1f;
        ApplyKnockback(new Vector2(sgn * hurtKnockbackSpeed, hurtKnockbackUpSpeed), hurtKnockbackDuration, true);

        // Post-damage invulnerability frames
        StartCoroutine(InvulnerabilityRoutine(hurtInvulnerabilityDuration));
    }

    // =====================================================================
    // Animator
    // =====================================================================
    void CacheAnimatorParams()
    {
        if (animator == null || animator.runtimeAnimatorController == null) return;
        animParams = new HashSet<string>();
        foreach (AnimatorControllerParameter p in animator.parameters) animParams.Add(p.name);
    }

    bool HasParam(string n) { return animParams != null && animParams.Contains(n); }
    void Trig(string n) { if (HasParam(n)) animator.SetTrigger(n); }

    void UpdateAnimator()
    {
        if (animParams == null) return;
        if (HasParam(Anim.IsGrounded)) animator.SetBool(Anim.IsGrounded, grounded);
        if (HasParam(Anim.IsDashing)) animator.SetBool(Anim.IsDashing, state == PlayerState.Dashing);
        if (HasParam(Anim.IsChargingSword)) animator.SetBool(Anim.IsChargingSword, sword.charging);
        if (HasParam(Anim.IsChargingMagic)) animator.SetBool(Anim.IsChargingMagic, magic.charging);
        if (HasParam(Anim.SwordChargeReady)) animator.SetBool(Anim.SwordChargeReady, sword.ready);
        if (HasParam(Anim.MagicChargeReady)) animator.SetBool(Anim.MagicChargeReady, magic.ready);
        if (HasParam(Anim.MoveSpeed)) animator.SetFloat(Anim.MoveSpeed, Mathf.Abs(rb.linearVelocity.x));
        if (HasParam(Anim.VerticalSpeed)) animator.SetFloat(Anim.VerticalSpeed, rb.linearVelocity.y);
    }
}
