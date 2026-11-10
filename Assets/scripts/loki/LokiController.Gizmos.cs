using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// All Scene View debug drawing. Gameplay code only calls MarkBox/MarkCircle with the exact shape it queries,
/// so what you see is what is hit. Nothing here touches physics or gameplay state.
/// </summary>
public partial class LokiController
{
    [Header("Gizmos")]
    public bool drawGizmos = true;
    public bool drawPreviews = true;            // faint previews of every attack when Loki is selected
    public float gizmoLinger = 0.12f;           // how long an active hitbox stays drawn after its last query

    [Header("Gizmo colors (attack colors live on each hitbox / spell)")]
    public Color groundColor = new Color(0.6f, 0.4f, 0.2f);
    public Color coyoteColor = new Color(1f, 0.85f, 0.4f);
    public Color jumpColor = new Color(0.6f, 0.8f, 1f);
    public Color dashColor = new Color(0f, 0.6f, 0.5f);
    public Color dashInvulnerabilityColor = new Color(0.4f, 1f, 0.7f);

    struct GizmoShape
    {
        public bool circle;
        public Vector2 center;
        public Vector2 size;       // circle: x = radius
        public float angle;
        public Color color;
        public float expire;
    }

    readonly List<GizmoShape> shapes = new List<GizmoShape>();

    void MarkBox(Vector2 center, Vector2 size, float angle, Color color)
    {
        AddShape(new GizmoShape { center = center, size = size, angle = angle, color = color });
    }

    void MarkCircle(Vector2 center, float radius, Color color)
    {
        AddShape(new GizmoShape { circle = true, center = center, size = new Vector2(radius, radius), color = color });
    }

    void AddShape(GizmoShape s)
    {
        s.expire = Time.time + gizmoLinger;
        shapes.RemoveAll(x => x.expire < Time.time);
        shapes.Add(s);
    }

    // ---------------------------------------------------------------------
    void OnDrawGizmos()
    {
        if (!drawGizmos) return;

        // Active attack hitboxes (only while their window is running)
        foreach (GizmoShape s in shapes)
        {
            if (s.expire < Time.time) continue;
            if (s.circle) Combat.DrawCircle(s.center, s.size.x, s.color, true);
            else Combat.DrawBox(s.center, s.size, s.angle, s.color, true);
        }

        DrawMovementGizmos();

        // Charge-ready areas: shows what the stored charge will do
        if (Application.isPlaying)
        {
            if (sword.ready)
            {
                DrawNailArtPreview(0.9f);
                DrawMagicArtPreview(0.5f);
                DrawCrossStitchPreview(0.5f);
                DrawParryPreview(0.5f);
            }
            if (magic.ready)
            {
                DrawSpellPreview(selectedSpell, true, 0.9f);
                DrawTraversePreview(0.6f);
                DrawAoEPreview(0.5f);
                DrawParryPreview(0.5f);
            }
        }
    }

    void OnDrawGizmosSelected()
    {
        if (!drawGizmos || !drawPreviews) return;
        DrawSwordPreview(0.35f);
        DrawNailArtPreview(0.3f);
        DrawMagicArtPreview(0.3f);
        DrawTraversePreview(0.3f);
        DrawParryPreview(0.3f);
        DrawCrossStitchPreview(0.3f);
        DrawAoEPreview(0.3f);
        foreach (SpellType s in new[] { SpellType.ArcBolt, SpellType.RiftBurst, SpellType.AstralOrbit })
        {
            DrawSpellPreview(s, false, 0.3f);
            DrawSpellPreview(s, true, 0.3f);
        }
    }

    // ---------------------------------------------------------------------
    void DrawMovementGizmos()
    {
        Vector2 p = Pos;
        int d = Dir;

        // Ground detection overlap area (filled while grounded)
        Combat.DrawBox(GroundCheckCenter(), groundCheckSize, 0f, groundColor, grounded);

        // Coyote-time reference: where Loki left the ground
        if (coyoteActive)
        {
            Combat.DrawCircle(lastGroundedPos, 0.25f, coyoteColor, true);
            Gizmos.color = coyoteColor;
            Gizmos.DrawLine(lastGroundedPos, p);
        }

        // Jump detection: expected apex of the ground jump and of the air jump
        float g = Mathf.Abs(Physics2D.gravity.y) * Mathf.Max(0.01f, gravityScale);
        float apex1 = jumpVelocity * jumpVelocity / (2f * g);
        float apex2 = apex1 + (maxAirJumps > 0 ? airJumpVelocity * airJumpVelocity / (2f * g) : 0f);
        Gizmos.color = jumpColor;
        Gizmos.DrawLine(p, p + Vector2.up * apex2);
        Gizmos.DrawLine(p + new Vector2(-0.3f, apex1), p + new Vector2(0.3f, apex1));
        if (maxAirJumps > 0) Gizmos.DrawLine(p + new Vector2(-0.3f, apex2), p + new Vector2(0.3f, apex2));

        // Dash range and dash invulnerability range
        float bodyH = Col.bounds.size.y;
        float len = dashSpeed * dashDuration;
        float inv = dashSpeed * Mathf.Min(dashInvulnerabilityDuration, dashDuration);
        Combat.DrawBox(p + Vector2.right * d * len * 0.5f, new Vector2(len, bodyH), 0f, Combat.A(dashColor, 0.6f));
        Combat.DrawBox(p + Vector2.right * d * inv * 0.5f, new Vector2(inv, bodyH * 0.6f), 0f, dashInvulnerabilityColor, Invulnerable);

        // Actual dash trajectory while dashing
        if (Application.isPlaying && state == PlayerState.Dashing)
        {
            Gizmos.color = dashColor;
            Gizmos.DrawLine(dashStartPos, p);
            Combat.DrawCircle(dashStartPos, 0.15f, dashColor, true);
        }
    }

    // ---------------------------------------------------------------------
    // Previews (use the SAME hitbox data and shape getters as gameplay)
    // ---------------------------------------------------------------------
    void DrawHitboxPreview(BoxHitbox hb, float a, Color? colorOverride = null)
    {
        Color c = Combat.A(colorOverride ?? hb.color, a);
        Combat.DrawBox(hb.Center(Pos, Dir), hb.size, 0f, c);
    }

    void DrawSwordPreview(float a)
    {
        DrawHitboxPreview(swordForward, a);
        DrawHitboxPreview(swordUp, a);
        DrawHitboxPreview(swordDown, a);
    }

    void DrawNailArtPreview(float a) { DrawHitboxPreview(nailArtHitbox, a); }

    void DrawMagicArtPreview(float a)
    {
        DrawHitboxPreview(crescentHitbox, a, magicArtColor);
        Combat.DrawBox(crescentHitbox.Center(Pos, Dir), crescentHitbox.size * crescentSecondScale, 0f, Combat.A(magicArtColor, a * 0.7f));   // weaker 2nd burst
    }

    void DrawTraversePreview(float a)
    {
        DrawHitboxPreview(traverseSlash, a);
        Vector2 spawn = Pos + new Vector2(traverseBeamSpawnOffset.x * Dir, traverseBeamSpawnOffset.y);
        Vector2 end = spawn + Vector2.right * Dir * traverseBeamSpeed * traverseBeamLifetime;
        Color c = Combat.A(traverseSlash.color, a);
        Combat.DrawBox(spawn, traverseBeamSize, 0f, c);
        Combat.DrawBox(end, traverseBeamSize, 0f, c);
        Gizmos.color = c;
        Gizmos.DrawLine(spawn, end);
    }

    void DrawParryPreview(float a) { DrawHitboxPreview(parryArea, a); }

    void DrawCrossStitchPreview(float a)
    {
        Color c = Combat.A(crossStitchColor, a);
        Combat.DrawBox(CrossStitchCenter(), CrossStitchSize(), crossStitchAngle, c);
        Combat.DrawBox(CrossStitchCenter(), CrossStitchSize(), -crossStitchAngle, c);
    }

    void DrawAoEPreview(float a) { Combat.DrawCircle(AoECenter(), aoeBurstRadius, Combat.A(aoeBurstColor, a)); }

    void DrawSpellPreview(SpellType s, bool charged, float a)
    {
        Vector2 p = Pos;
        int d = Dir;
        if (s == SpellType.ArcBolt)
        {
            Vector2 spawn = p + new Vector2(arcBolt.spawnOffset.x * d, arcBolt.spawnOffset.y);
            Vector2 size = charged ? arcBolt.chargedSize : arcBolt.size;
            float range = charged ? arcBolt.chargedSpeed * arcBolt.chargedLifetime : arcBolt.speed * arcBolt.lifetime;
            Color c = Combat.A(arcBolt.color, a);
            Combat.DrawBox(spawn, size, 0f, c);
            Combat.DrawBox(spawn + Vector2.right * d * range, size, 0f, c);
            Gizmos.color = c;
            Gizmos.DrawLine(spawn, spawn + Vector2.right * d * range);
        }
        else if (s == SpellType.RiftBurst)
        {
            int n = charged ? riftBurst.chargedCount : 1;
            for (int i = 0; i < n; i++)
            {
                Vector2 center, size;
                RiftBox(p, i, charged, out center, out size);
                Combat.DrawBox(center, size, 0f, Combat.A(riftBurst.color, a));
            }
        }
        else
        {
            int n = charged ? astralOrbit.chargedBladeCount : astralOrbit.bladeCount;
            float r = charged ? astralOrbit.chargedOrbitRadius : astralOrbit.orbitRadius;
            Vector2 center = p + Vector2.up * astralOrbit.centerHeight;
            Combat.DrawCircle(center, r, Combat.A(astralOrbit.orbitColor, a));
            for (int i = 0; i < n; i++)
            {
                float ang = 360f / n * i * Mathf.Deg2Rad;
                Vector2 dir = new Vector2(Mathf.Cos(ang), Mathf.Sin(ang));
                Vector2 pos = center + dir * r;
                Combat.DrawCircle(pos, astralOrbit.bladeRadius, Combat.A(astralOrbit.orbitColor, a));
                if (charged)
                {
                    Gizmos.color = Combat.A(astralOrbit.releaseColor, a);
                    Gizmos.DrawLine(pos, pos + dir * astralOrbit.releaseSpeed * astralOrbit.releaseLifetime);
                }
            }
        }
    }
}