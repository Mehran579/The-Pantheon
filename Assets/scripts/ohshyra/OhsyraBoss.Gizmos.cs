using System.Collections.Generic;
using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

/// <summary>
/// Scene View drawing only. Reads the same helper functions/fields as gameplay (RayBox, RushPlan,
/// JewelrySpawnPoint, BoxHitbox.Center, ...). Never touches physics or gameplay state.
/// </summary>
public partial class OhsyraBoss
{
    [Header("Gizmo categories")]
    public bool drawGizmos = true;
    public bool gizmoArena = true;
    public bool gizmoMovement = true;
    public bool gizmoJewelry = true;
    public bool gizmoFan = true;
    public bool gizmoRays = true;
    public bool gizmoRush = true;
    public float gizmoLinger = 0.12f;

    [Header("Gizmo colors (attack colors live on each attack)")]
    public Color arenaColor = new Color(0.7f, 0.7f, 0.7f);
    public Color movementColor = Color.white;
    public Color destinationColor = Color.yellow;
    public Color fadeColor = new Color(0.75f, 0.75f, 1f);

    struct Shape { public Vector2 center, size; public float angle; public Color color; public float expire; }
    readonly List<Shape> active = new List<Shape>();
    readonly List<Vector2> trail = new List<Vector2>();

    void Mark(Vector2 center, Vector2 size, float angle, Color color)
    {
        active.RemoveAll(x => x.expire < Time.time);
        active.Add(new Shape { center = center, size = size, angle = angle, color = color, expire = Time.time + gizmoLinger });
    }

    void RecordTrail(Vector2 p)
    {
        if (trail.Count == 0 || (trail[trail.Count - 1] - p).sqrMagnitude > 0.01f)
        {
            trail.Add(p);
            if (trail.Count > 80) trail.RemoveAt(0);
        }
    }

    void OnDrawGizmos()
    {
        if (!drawGizmos) return;
        if (gizmoArena) DrawArena();
        if (gizmoMovement) DrawMovement();
        if (gizmoJewelry) DrawJewelry();
        if (gizmoFan) DrawFan();
        if (gizmoRays) DrawRays();
        if (gizmoRush) DrawRush();

        foreach (Shape s in active)                              // live (currently queried) hitboxes, filled
            if (s.expire >= Time.time) Combat.DrawBox(s.center, s.size, s.angle, s.color, true);
    }

    void Label(Vector2 p, string text)
    {
#if UNITY_EDITOR
        Handles.Label(p, text);
#endif
    }

    void DrawArena()
    {
        Combat.DrawBox(arenaCenter, arenaSize, 0f, arenaColor);
        Gizmos.color = arenaColor;
        Gizmos.DrawLine(arenaCenter + Vector2.left * 0.6f, arenaCenter + Vector2.right * 0.6f);
        Gizmos.DrawLine(arenaCenter + Vector2.down * 0.6f, arenaCenter + Vector2.up * 0.6f);
    }

    void DrawMovement()
    {
        Combat.DrawCircle(Pos, 0.3f, movementColor, true);

        Vector2 idle = IdleDesired();                            // where she wants to float while idle
        Combat.DrawCircle(idle, 0.25f, Combat.A(movementColor, 0.4f));
        Gizmos.color = Combat.A(movementColor, 0.3f);
        Gizmos.DrawLine(Pos, idle);

        Gizmos.color = Combat.A(movementColor, 0.6f);            // recent floating path
        for (int i = 1; i < trail.Count; i++) Gizmos.DrawLine(trail[i - 1], trail[i]);

        if (Application.isPlaying && hasDestination)             // current intended destination
        {
            Combat.DrawCircle(destination, 0.35f, destinationColor);
            Gizmos.color = destinationColor;
            Gizmos.DrawLine(Pos, destination);
        }

        // Fade-out and materialization destinations (Golden Jewelry reposition)
        Vector2 outP = JewelryFadeOutPoint(), inP = JewelryReappearPos();
        Combat.DrawCircle(outP, 0.3f, fadeColor);
        Combat.DrawCircle(inP, 0.3f, fadeColor, true);
        Gizmos.color = Combat.A(fadeColor, 0.6f);
        Gizmos.DrawLine(Pos, outP);
        Gizmos.DrawLine(outP, inP);
        Label(outP, "fade out");
        Label(inP, "materialize");
    }

    void DrawJewelry()
    {
        Vector2 origin = JewelryReappearPos();
        for (int dir = -1; dir <= 1; dir += 2)
        {
            Vector2 spawn = JewelrySpawnPoint(origin, dir);
            Vector2 end = spawn + Vector2.right * dir * jewelrySpeed * jewelryLifetime;
            Combat.DrawCircle(spawn, 0.12f, jewelryColor, true);
            Combat.DrawBox(spawn, jewelrySize, 0f, jewelryColor);
            Combat.DrawBox(end, jewelrySize, 0f, Combat.A(jewelryColor, 0.6f));
            Gizmos.color = jewelryColor;
            Gizmos.DrawLine(spawn, end);
            Gizmos.DrawLine(end, end + new Vector2(-dir * 0.4f, 0.25f));
            Gizmos.DrawLine(end, end + new Vector2(-dir * 0.4f, -0.25f));
        }
    }

    void DrawFan()
    {
        // 1. Counter stance detection area (cyan outline / fill when active)
        bool stanceActive = Application.isPlaying && IsCounterStanceActive;
        Vector2 stanceCenter = counterStanceArea.Center(Pos, facing);
        Combat.DrawBox(stanceCenter, counterStanceArea.size, 0f, counterStanceGizmoColor, stanceActive);

        // 2. Active duration label
        if (stanceActive)
        {
            float remaining = Mathf.Max(0f, counterStanceEndTime - Time.time);
            Label(stanceCenter + Vector2.up * (counterStanceArea.size.y * 0.5f + 0.3f),
                  string.Format("Ab?`b?` Counter Stance: {0:0.00}s", remaining));
        }

        // 3. Sweeping counterattack hitbox and direction (distinct coral/red color)
        Vector2 sweepCenter = counterSweepHitbox.Center(Pos, facing);
        Combat.DrawBox(sweepCenter, counterSweepHitbox.size, 0f, counterSweepGizmoColor, false);

        // Draw sweep direction arrow / vector
        Gizmos.color = counterSweepGizmoColor;
        Vector2 arrowStart = Pos + Vector2.up * 0.5f;
        Vector2 arrowEnd = arrowStart + Vector2.right * facing * (counterSweepHitbox.size.x * 0.7f);
        Gizmos.DrawLine(arrowStart, arrowEnd);
        Gizmos.DrawLine(arrowEnd, arrowEnd + new Vector2(-facing * 0.3f, 0.2f));
        Gizmos.DrawLine(arrowEnd, arrowEnd + new Vector2(-facing * 0.3f, -0.2f));
    }

    void DrawRays()
    {
        for (int i = 0; i < raySequence.Count; i++)
        {
            RayStep s = raySequence[i];
            Vector2 origin = HandPos(s.hand), center, size;
            RayBox(origin, s.angle, out center, out size);
            Color c = Combat.A(rayColor, s.hand == OhsyraHand.Right ? 0.9f : 0.55f);
            Combat.DrawCircle(origin, 0.15f, c, true);
            Combat.DrawBox(center, size, s.angle, c);
            Vector2 d = new Vector2(Mathf.Cos(s.angle * Mathf.Deg2Rad), Mathf.Sin(s.angle * Mathf.Deg2Rad));
            Gizmos.color = c;
            Gizmos.DrawLine(origin, origin + d * rayLength);
            Label(origin + d * (rayLength * 0.5f), (i + 1) + (s.hand == OhsyraHand.Right ? " R " : " L ") + s.angle + "\u00B0");
        }
    }

    void DrawRush()
    {
        Vector2 start, tp, dir, end;
        if (rushPlanned) { start = rushStart; tp = rushTarget; dir = rushDir; end = rushEnd; }
        else { start = Pos; tp = TargetPos; RushPlan(start, tp, Pos.y, out dir, out end); }
        int face = dir.x >= 0f ? 1 : -1;

        Combat.DrawCircle(start, 0.25f, rushColor, true);                  // start
        Combat.DrawCircle(tp, 0.3f, Combat.A(rushColor, 0.7f));            // Loki snapshot / target
        Combat.DrawCircle(end, 0.25f, rushColor, true);                    // end of glide
        Gizmos.color = rushColor;
        Gizmos.DrawLine(start, end);
        Label((start + end) * 0.5f, "rush " + Vector2.Distance(start, end).ToString("0.0") + "u");

        Vector2 s1 = tp - dir * rushStrikeLeadDistance;                    // first sweep begins
        Vector2 s2 = s1 + dir * rushSpeed * rushSweepActiveTime;           // ...and where it ends
        if (rushSweepAtEnd)
            Combat.DrawBox(rushSweep.Center(end, face), rushSweep.size, 0f, rushColor);        // fires where the glide ends
        else
        {
            Combat.DrawBox(rushSweep.Center(s1, face), rushSweep.size, 0f, rushColor);
            Combat.DrawBox(rushSweep.Center(s2, face), rushSweep.size, 0f, Combat.A(rushColor, 0.5f));
        }

        Color c2 = secondSweep.color;                                      // optional opposite sweep, from where she stops
        Combat.DrawBox(secondSweep.Center(end, -face), secondSweep.size, 0f, c2);
        Gizmos.color = c2;
        Vector2 b = end + Vector2.right * -face * 1.5f;
        Gizmos.DrawLine(end, b);
        Gizmos.DrawLine(b, b + new Vector2(face * 0.3f, 0.2f));
        Gizmos.DrawLine(b, b + new Vector2(face * 0.3f, -0.2f));
    }
}
