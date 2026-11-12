using System.Collections;
using System.Collections.Generic;
using UnityEngine;

[System.Serializable]
public class RayStep
{
    public OhsyraHand hand = OhsyraHand.Right;
    [Tooltip("World angle in degrees. 0 = right, 90 = up, 180 = left, -90 = down, 45 = up-right, etc.")]
    public float angle;
    public RayStep() { }
    public RayStep(OhsyraHand h, float a) { hand = h; angle = a; }
}

/// <summary>
/// Attacks. Each is one coroutine that sets state, triggers animator, and creates real hitboxes/projectiles.
/// All numeric values are PLACEHOLDERS (timings, damage and sizes were not specified).
/// </summary>
public partial class OhsyraBoss
{
    // =====================================================================
    // Attack 1: Golden Jewelry
    // =====================================================================
    [Header("Golden Jewelry")]
    public float jewelryFadeOutDistance = 5f;        // she drifts this far to her right while fading
    public float jewelryReappearXOffset = 0f;        // from arena centre X ("around the middle")
    public float jewelryYOffset = 0f;                // relative to Loki's Y (her vertical reference)
    public float jewelryTelegraphTime = 0.8f;        // arms swing out (Animator); this is the readable window
    public float jewelryRecovery = 0.6f;
    public Vector2 jewelrySpawnOffset = new Vector2(1.0f, 0.3f);   // x mirrored per direction
    public int jewelryDamage = 10;
    public float jewelrySpeed = 12f;
    public float jewelryLifetime = 2f;
    public Vector2 jewelrySize = new Vector2(0.6f, 0.6f);
    public bool jewelryPierce = false;
    public Color jewelryColor = new Color(1f, 0.84f, 0f);
    public GameObject jewelryProjectilePrefab;       // optional visual

    Vector2 JewelryFadeOutPoint() { return ClampToArena(Pos + Vector2.right * jewelryFadeOutDistance); }

    Vector2 JewelryReappearPos()
    {
        return ClampToArena(new Vector2(arenaCenter.x + jewelryReappearXOffset, TargetPos.y + jewelryYOffset));
    }

    Vector2 JewelrySpawnPoint(Vector2 origin, int dir) { return origin + new Vector2(jewelrySpawnOffset.x * dir, jewelrySpawnOffset.y); }

    IEnumerator JewelryRoutine()
    {
        state = OhsyraState.Repositioning;
        SetFacing(1);
        yield return FadeOutTo(JewelryFadeOutPoint());          // floats right while fading away
        yield return MaterializeAt(JewelryReappearPos());       // reappears mid-arena at Loki's current Y

        state = OhsyraState.Telegraphing;
        Trig(Anim.JewelryTelegraph);                            // right arm -> her right, left arm -> her left
        yield return new WaitForSeconds(jewelryTelegraphTime);

        state = OhsyraState.Attacking;
        Trig(Anim.JewelryRelease);
        SpawnJewelry(1);
        SpawnJewelry(-1);

        state = OhsyraState.Recovery;
        Trig(Anim.Recovery);
        yield return new WaitForSeconds(jewelryRecovery);
    }

    readonly List<Projectile> liveProjectiles = new List<Projectile>();
    [Header("Stagger")]
    public bool clearProjectilesOnStagger = false;   // already-released jewelry normally keeps flying

    void SpawnJewelry(int dir)
    {
        Projectile p = Projectile.Spawn(JewelrySpawnPoint(Pos, dir), Vector2.right * dir, jewelrySpeed, jewelryLifetime,
                                        jewelrySize, jewelryDamage, jewelryPierce, playerLayers, jewelryColor,
                                        jewelryProjectilePrefab, gameObject);
        p.onHit = () => KnockPlayer(LokiController.Instance, dir, jewelryKnockback);
        liveProjectiles.Add(p);
    }

    void ClearProjectilesIfConfigured()
    {
        if (clearProjectilesOnStagger)
            foreach (Projectile p in liveProjectiles) if (p != null) Destroy(p.gameObject);
        liveProjectiles.Clear();
    }

    // =====================================================================
    // Attack 2 — Ab?`b?` Counter Stance
    // =====================================================================
    [Header("Ab?`b?` Counter Stance")]
    public float counterStanceDuration = 1.25f;      // exactly 1.25s counter window
    public float counterHitStop = 0.1f;              // brief hit-stop when counter is triggered
    public int counterSweepDamage = 20;              // actual damage of the counterattack
    public float counterSweepActiveTime = 0.2f;      // active hitbox duration of the counter sweep
    public float counterSweepRecovery = 0.6f;        // recovery after counterattack
    public float counterStanceMissRecovery = 0.35f;  // recovery if player does not hit her
    public BoxHitbox counterStanceArea = new BoxHitbox(new Vector2(0f, 0.2f), new Vector2(2.6f, 3.2f), new Color(0.2f, 0.8f, 1f));
    public BoxHitbox counterSweepHitbox = new BoxHitbox(new Vector2(1.8f, 0f), new Vector2(3.5f, 2.5f), new Color(1f, 0.2f, 0.4f));
    public Color counterStanceGizmoColor = new Color(0.2f, 0.8f, 1f);
    public Color counterSweepGizmoColor = new Color(1f, 0.2f, 0.4f);

    [Header("Ab?`b?` Fan Props / Uneven Cross")]
    public GameObject rightFanProp;                  // optional fan prop right hand
    public GameObject leftFanProp;                   // optional fan prop left hand
    public float unevenRightArmAngle = 35f;          // crossed uneven arm angles
    public float unevenLeftArmAngle = 145f;

    // Runtime counter stance tracking
    public bool IsCounterStanceActive { get; private set; }
    public bool counterTriggered { get; private set; }
    public float counterStanceEndTime { get; private set; }
    Coroutine counterAttackCoroutine;

    IEnumerator FanRoutine()
    {
        state = OhsyraState.Telegraphing;
        FaceTarget();
        int face = facing;

        // 1. Enter stance: take out two ornate Ab?`b?` hand fans
        if (rightFanProp != null) rightFanProp.SetActive(true);
        if (leftFanProp != null) leftFanProp.SetActive(true);

        // 2 & 3. Cross arms in front with UNEVEN heights (right and left asymmetric)
        armAngle[0] = unevenRightArmAngle;
        armAngle[1] = unevenLeftArmAngle;
        PushArmAnim();

        // 4. Graceful floating idle-like stance while holding both fans
        SetBool(Anim.IsFloating, true);
        SetBool(Anim.IsCounterStance, true);
        Trig(Anim.FanCounterStance);
        Trig(Anim.FanAttack);

        IsCounterStanceActive = true;
        counterTriggered = false;
        counterStanceEndTime = Time.time + counterStanceDuration;

        // Counter window: hold for exactly counterStanceDuration
        while (Time.time < counterStanceEndTime && !counterTriggered)
        {
            if (!recoilActive) FloatIdle();
            yield return fixedStep;
        }

        IsCounterStanceActive = false;

        // If counter was triggered by a player hit, wait for the counterattack sequence to complete
        if (counterTriggered)
        {
            while (counterAttackCoroutine != null)
            {
                yield return fixedStep;
            }
        }
        else
        {
            // Stance timed out without being hit: exit gracefully and resume normal combat
            SetBool(Anim.IsCounterStance, false);
            Trig(Anim.Recovery);
            state = OhsyraState.Recovery;
            yield return ReturnArmsToRest();
            yield return new WaitForSeconds(counterStanceMissRecovery);
        }

        // Clean up fan stance visuals
        SetBool(Anim.IsCounterStance, false);
        ResetArms();
    }

    /// <summary>Called by TakeDamage when Loke hits Ohsyra during her active counter stance window.</summary>
    public void TriggerFanCounter(DamageInfo info)
    {
        if (counterTriggered) return; // Ensure counter triggers only once per stance
        counterTriggered = true;
        IsCounterStanceActive = false;
        counterAttackCoroutine = StartCoroutine(CounterAttackRoutine(info));
    }

    IEnumerator CounterAttackRoutine(DamageInfo info)
    {
        state = OhsyraState.Attacking;
        Body.linearVelocity = Vector2.zero;

        // Hit-stop effect (brief freeze / visual impact)
        HitBlinker.Flash(gameObject, 0.06f);
        CameraShake.Shake(0.18f, 0.3f);
        if (counterHitStop > 0f)
        {
            yield return new WaitForSeconds(counterHitStop);
        }

        // Turn towards the incoming attacker
        float dx = info.origin.x - Pos.x;
        if (Mathf.Abs(dx) > 0.1f) SetFacing(dx > 0f ? 1 : -1);
        int face = facing;

        // Sweeping fan counterattack animation
        SetBool(Anim.IsCounterStance, false);
        Trig(Anim.FanCounterAttack);

        // Sweeping counter damage hitbox
        HashSet<object> hits = new HashSet<object>();
        float end = Time.time + counterSweepActiveTime;
        while (Time.time < end)
        {
            Collider2D[] cols = Query(counterSweepHitbox.Center(Pos, face), counterSweepHitbox.size, 0f, counterSweepGizmoColor);
            Collider2D firstHit;
            if (Combat.Damage(cols, counterSweepDamage, Pos, gameObject, hits, out firstHit, false, 12f, 0.25f) > 0 && firstHit != null)
            {
                LokiController lc = firstHit.GetComponentInParent<LokiController>();
                if (lc != null)
                {
                    KnockPlayer(lc, face, 12f);
                }
            }
            yield return fixedStep;
        }

        // Counterattack recovery window
        state = OhsyraState.Recovery;
        Trig(Anim.Recovery);
        yield return ReturnArmsToRest();
        yield return new WaitForSeconds(counterSweepRecovery);
        counterAttackCoroutine = null;
    }
    // =====================================================================
    // Attack 3: Golden Honey Rays
    // =====================================================================
    [Header("Golden Honey Rays: sequence (angle: 0 right, 90 up, 180 left, -90 down, 45/135/... diagonals)")]
    public List<RayStep> raySequence = new List<RayStep>
    {
        new RayStep(OhsyraHand.Right, 0f),
        new RayStep(OhsyraHand.Left, 180f),
        new RayStep(OhsyraHand.Right, 90f),
        new RayStep(OhsyraHand.Left, -90f),
    };

    [Header("Golden Honey Rays: timing")]
    public float raysApproachSmoothTime = 0.5f;      // floating to the arena centre
    public float raysStartDelay = 0.4f;
    public float armRestAngle = -90f;                // arms hang down
    public float rayArmSmoothTime = 0.25f;           // bigger = slower, more graceful arm
    public float rayArmMaxSpeed = 360f;              // degrees per second cap
    public float rayAimTolerance = 2f;               // degrees: counts as "reached direction"
    public float rayFireHold = 0.1f;                 // short beat at the target direction before firing
    public float raysStepPause = 0.25f;              // after firing, before the next arm starts
    public float raysRecovery = 0.8f;
    public float torsoFollowDegrees = 6f;            // subtle torso tilt toward the arm

    [Header("Golden Honey Rays: hitbox / damage")]
    public int rayDamage = 12;
    public float rayLength = 14f;
    public float rayThickness = 0.8f;
    public float rayActiveTime = 0.25f;
    public float raySlowDuration = 0.5f;             // specified: 0.5 s
    [Range(0f, 1f)] public float raySlowMultiplier = 0.5f;   // PLACEHOLDER: strength was not specified
    public Color rayColor = new Color(1f, 0.75f, 0.1f);
    public GameObject rayEffectPrefab;               // optional: 1x1 unit visual, pivot at left-centre. Scaled to length x thickness.

    readonly float[] armAngle = new float[2];
    readonly float[] armVel = new float[2];

    void ResetArms()
    {
        armAngle[0] = armAngle[1] = armRestAngle;
        armVel[0] = armVel[1] = 0f;
        PushArmAnim();
    }

    Vector2 HandPos(OhsyraHand h)
    {
        Transform t = h == OhsyraHand.Right ? rightHand : leftHand;
        if (t != null) return t.position;
        return Pos + new Vector2(h == OhsyraHand.Right ? 0.6f : -0.6f, 0.5f);   // fallback if no hand assigned
    }

    // The ray box: used by gameplay AND gizmos.
    void RayBox(Vector2 origin, float angle, out Vector2 center, out Vector2 size)
    {
        float r = angle * Mathf.Deg2Rad;
        center = origin + new Vector2(Mathf.Cos(r), Mathf.Sin(r)) * rayLength * 0.5f;
        size = new Vector2(rayLength, rayThickness);
    }

    void PushArmAnim()
    {
        float rr = armAngle[0] * Mathf.Deg2Rad, lr = armAngle[1] * Mathf.Deg2Rad;
        SetFloat(Anim.RightArmAimX, Mathf.Cos(rr) * facing);
        SetFloat(Anim.RightArmAimY, Mathf.Sin(rr));
        SetFloat(Anim.LeftArmAimX, Mathf.Cos(lr) * facing);
        SetFloat(Anim.LeftArmAimY, Mathf.Sin(lr));
        if (torso != null) torso.localRotation = Quaternion.Euler(0f, 0f, -(Mathf.Cos(rr) + Mathf.Cos(lr)) * torsoFollowDegrees);
    }

    IEnumerator AimArm(int h, float targetAngle)
    {
        armVel[h] = 0f;
        while (Mathf.Abs(Mathf.DeltaAngle(armAngle[h], targetAngle)) > rayAimTolerance)
        {
            armAngle[h] = Mathf.SmoothDampAngle(armAngle[h], targetAngle, ref armVel[h], rayArmSmoothTime, rayArmMaxSpeed);
            PushArmAnim();
            yield return null;
        }
        armAngle[h] = targetAngle;
        PushArmAnim();
    }

    IEnumerator ReturnArmsToRest()
    {
        armVel[0] = armVel[1] = 0f;
        while (Mathf.Abs(Mathf.DeltaAngle(armAngle[0], armRestAngle)) > rayAimTolerance ||
               Mathf.Abs(Mathf.DeltaAngle(armAngle[1], armRestAngle)) > rayAimTolerance)
        {
            for (int h = 0; h < 2; h++)
                armAngle[h] = Mathf.SmoothDampAngle(armAngle[h], armRestAngle, ref armVel[h], rayArmSmoothTime, rayArmMaxSpeed);
            PushArmAnim();
            yield return null;
        }
        ResetArms();
    }

    IEnumerator RaysRoutine()
    {
        state = OhsyraState.Repositioning;
        SetFacing(1);                                           // arm angles are authored in world space
        yield return FloatTo(arenaCenter, raysApproachSmoothTime);   // exact arena centre

        state = OhsyraState.Telegraphing;
        Trig(Anim.RaysStart);
        yield return new WaitForSeconds(raysStartDelay);

        foreach (RayStep s in raySequence)
        {
            state = OhsyraState.Telegraphing;                   // the arm movement IS the telegraph
            yield return AimArm((int)s.hand, s.angle);
            yield return new WaitForSeconds(rayFireHold);

            state = OhsyraState.Attacking;
            Trig(s.hand == OhsyraHand.Right ? Anim.RayFireRight : Anim.RayFireLeft);
            FireRay(s);
            yield return new WaitForSeconds(raysStepPause);
        }

        yield return new WaitForSeconds(rayActiveTime);         // let the last ray finish
        state = OhsyraState.Recovery;
        Trig(Anim.Recovery);
        yield return ReturnArmsToRest();
        yield return new WaitForSeconds(raysRecovery);
        hasDestination = false;
    }

    void FireRay(RayStep s)
    {
        Vector2 origin = HandPos(s.hand);
        if (rayEffectPrefab != null)
        {
            GameObject go = Instantiate(rayEffectPrefab, origin, Quaternion.Euler(0f, 0f, s.angle));
            go.transform.localScale = new Vector3(rayLength, rayThickness, 1f);
            Destroy(go, rayActiveTime);
        }
        StartCoroutine(RayWindow(origin, s.angle));             // each ray has its own independent hit window
    }

    IEnumerator RayWindow(Vector2 origin, float angle)
    {
        HashSet<object> hits = new HashSet<object>();           // one hit per ray
        Vector2 center, size;
        RayBox(origin, angle, out center, out size);
        float end = Time.time + rayActiveTime;
        while (Time.time < end)
        {
            Collider2D first;
            if (Combat.Damage(Query(center, size, angle, rayColor), rayDamage, origin, gameObject, hits, out first) > 0)
            {
                SlowPlayer(first);
                LokiController l = first != null ? first.GetComponentInParent<LokiController>() : null;
                float cx = Mathf.Cos(angle * Mathf.Deg2Rad);
                if (l != null) KnockPlayer(l, Mathf.Abs(cx) > 0.2f ? cx : l.transform.position.x - origin.x, rayKnockback);
            }
            yield return new WaitForFixedUpdate();
        }
    }

    void SlowPlayer(Collider2D c)
    {
        if (c == null) return;
        LokiController l = c.GetComponentInParent<LokiController>();
        if (l != null && !l.Invulnerable) l.ApplySlow(raySlowDuration, raySlowMultiplier);   // ApplySlow ignores re-application while slowed
    }

    // =====================================================================
    // Attack 4: Golden Rush
    // =====================================================================
    [Header("Golden Rush")]
    public float rushRepositionOffset = 2f;          // appear away from Loke before the ground dash
    public float rushTelegraphTime = 0.7f;           // lean forward, draw arm back on the ground (Animator)
    public float rushSpeed = 22f;
    public float rushOvershoot = 6f;                 // distance she keeps gliding beyond Loki
    public bool rushSweepAtEnd = true;               // true: first sweep fires the moment the glide ends, on the side she ends (forward). false: mid-pass, near Loki
    public float rushStrikeLeadDistance = 1.5f;      // mid-pass mode only: sweep starts when she is this close to Loki's snapshot position
    public BoxHitbox rushSweep = new BoxHitbox(new Vector2(1.2f, 0f), new Vector2(2.4f, 2.2f), new Color(1f, 0.5f, 0.1f));
    public int rushDamage = 15;
    public float rushSweepActiveTime = 0.2f;
    public float rushPauseAfter = 0.4f;              // brief pause after gliding past
    [Range(0f, 1f)] public float secondSwipeChance = 0.5f;
    public float secondSwipeDelay = 0.5f;
    public float secondTelegraphTime = 0.6f;         // player's reaction window
    public BoxHitbox secondSweep = new BoxHitbox(new Vector2(1.2f, 0f), new Vector2(2.4f, 2.2f), new Color(1f, 0.3f, 0.3f));
    public int secondSweepDamage = 15;
    public float secondSweepActiveTime = 0.2f;
    public float rushRecovery = 1.2f;                // the punish window
    public Color rushColor = new Color(1f, 0.5f, 0.1f);

    // =====================================================================
    // Knockback on Loke (PLACEHOLDER values; 0 = none). Needs LokiController.ApplyKnockback.
    // =====================================================================
    [Header("Knockback on Loke (placeholders)")]
    public float jewelryKnockback = 6f;              // horizontal speed, along the projectile's travel direction
    public float rayKnockback = 0f;                  // rays already slow; 0 = no knockback. Pushes along the ray's horizontal direction
    public float rushKnockback = 10f;                // first sweep, along her sweep direction
    public float secondSweepKnockback = 10f;
    public float knockbackUpSpeed = 3f;              // small lift so the push reads clearly (0 = purely horizontal)
    public float knockbackDuration = 0.25f;          // horizontal control is suspended and the push fades out over this time

    void KnockPlayer(LokiController l, float dirX, float speed)
    {
        if (l == null || speed <= 0f) return;
        float sgn = dirX < 0f ? -1f : (dirX > 0f ? 1f : -facing);
        l.ApplyKnockback(new Vector2(sgn * speed, knockbackUpSpeed), knockbackDuration, true);
    }

    bool rushPlanned;
    Vector2 rushStart, rushTarget, rushDir, rushEnd;

    // Used by gameplay AND gizmos. The dash path stays at the detected floor height.
    void RushPlan(Vector2 start, Vector2 targetPos, float groundY, out Vector2 dir, out Vector2 end)
    {
        float dx = targetPos.x - start.x;
        if (Mathf.Abs(dx) < 0.1f) dx = facing;
        float sgn = dx >= 0f ? 1f : -1f;
        dir = new Vector2(sgn, 0f);
        float targetX = targetPos.x + sgn * rushOvershoot;
        end = ClampToArena(new Vector2(targetX, groundY));
    }

    IEnumerator RushRoutine()
    {
        state = OhsyraState.Repositioning;
        FaceTarget();
        SetBool(Anim.IsFloating, true);

        // Fade away, then materialize on the floor a short distance from Loke.
        float side = TargetPos.x >= arenaCenter.x ? -1f : 1f;
        float spawnX = Mathf.Clamp(TargetPos.x + side * rushRepositionOffset,
            arenaCenter.x - arenaSize.x * 0.5f, arenaCenter.x + arenaSize.x * 0.5f);
        if (!TryFindGroundY(spawnX, out float groundY))
        {
            Debug.LogWarning("OhsyraBoss: Golden Rush could not find ground beneath its materialization point; skipping Rush.", this);
            yield break;
        }
        Vector2 appearPos = ClampToArena(new Vector2(spawnX, groundY));
        yield return FadeOutTo(Pos);
        Body.linearVelocity = Vector2.zero;
        Teleport(appearPos);
        SetBool(Anim.IsFloating, false);
        yield return MaterializeAt(appearPos);

        // Telegraph from her new ground position before committing to the dash.
        state = OhsyraState.Telegraphing;
        FaceTarget();
        Trig(Anim.RushTelegraph);
        yield return new WaitForSeconds(rushTelegraphTime);

        // Plan horizontal ground dash across to Loke's current position.
        rushStart = Pos;
        rushTarget = new Vector2(TargetPos.x, Pos.y);
        RushPlan(rushStart, rushTarget, groundY, out rushDir, out rushEnd);
        rushPlanned = true;
        int face = rushDir.x >= 0f ? 1 : -1;
        SetFacing(face);

        // Dash across the ground
        state = OhsyraState.Attacking;
        destination = rushEnd;
        hasDestination = true;
        bool struck = false;
        float rushLimit = Time.time + Vector2.Distance(rushStart, rushEnd) / Mathf.Max(0.1f, rushSpeed) * 2f + 0.5f;
        while (Vector2.Distance(Pos, rushEnd) > 0.05f && Time.time < rushLimit)
        {
            Vector2 next = Vector2.MoveTowards(Pos, rushEnd, rushSpeed * Time.fixedDeltaTime);
            MoveTo(next);
            if (!rushSweepAtEnd && !struck && Mathf.Abs(next.x - rushTarget.x) <= rushStrikeLeadDistance)
            {
                struck = true;
                Trig(Anim.RushSweep1);
                StartCoroutine(SweepWindow(rushSweep, face, rushDamage, rushSweepActiveTime, rushColor, rushKnockback));
            }
            yield return fixedStep;
        }
        rushPlanned = false;
        Body.linearVelocity = Vector2.zero; // dash movement stops

        // 5. Sweep attack at the end of the dash (or fallback)
        if (!struck)
        {
            Trig(Anim.RushSweep1);
            yield return SweepWindow(rushSweep, face, rushDamage, rushSweepActiveTime, rushColor, rushKnockback);
        }

        // Brief pause after the first sweep
        yield return new WaitForSeconds(rushPauseAfter);

        // 6. Optional second sweep attack intact
        if (Random.value < secondSwipeChance)
        {
            yield return new WaitForSeconds(secondSwipeDelay);
            state = OhsyraState.Telegraphing;
            SetFacing(-face);
            Trig(Anim.RushSecondTelegraph);
            yield return new WaitForSeconds(secondTelegraphTime);
            state = OhsyraState.Attacking;
            Trig(Anim.RushSweep2);
            yield return SweepWindow(secondSweep, -face, secondSweepDamage, secondSweepActiveTime, secondSweep.color, secondSweepKnockback);
        }

        // 7. Recovery window
        state = OhsyraState.Recovery;
        Trig(Anim.Recovery);
        yield return new WaitForSeconds(rushRecovery);
    }
    // Box follows her position while active. Own hit list per sweep.
    IEnumerator SweepWindow(BoxHitbox hb, int face, int damage, float duration, Color color, float knockback)
    {
        HashSet<object> hits = new HashSet<object>();
        float end = Time.time + duration;
        while (Time.time < end)
        {
            Collider2D[] cols = Query(hb.Center(Pos, face), hb.size, 0f, color);
            Collider2D first;
            if (Combat.Damage(cols, damage, Pos, gameObject, hits, out first) > 0 && first != null)
                KnockPlayer(first.GetComponentInParent<LokiController>(), face, knockback);
            yield return new WaitForFixedUpdate();
        }
    }

    // Marks the gizmo and queries with the very same shape (same pattern as LokiController.QueryBox).
    Collider2D[] Query(Vector2 center, Vector2 size, float angle, Color color)
    {
        Mark(center, size, angle, color);
        return Physics2D.OverlapBoxAll(center, size, angle, playerLayers);
    }
}
