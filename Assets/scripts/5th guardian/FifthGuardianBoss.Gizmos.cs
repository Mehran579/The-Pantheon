using System.Collections.Generic;
using UnityEngine;

/// <summary>Scene View drawing only. Reads the same helpers/fields as gameplay. Never touches physics or gameplay state.</summary>
public partial class FifthGuardianBoss
{
    [Header("Gizmos")]
    public bool drawGizmos = true;
    public float gizmoLinger = 0.12f;
    public Color arenaColor = new Color(0.7f, 0.7f, 0.7f);
    public Color groundCheckColor = new Color(0.6f, 0.4f, 0.2f);

    struct Shape { public Vector2 center, size; public float angle; public Color color; public float expire; }
    readonly List<Shape> active = new List<Shape>();

    void Mark(Vector2 center, Vector2 size, float angle, Color color)
    {
        active.RemoveAll(x => x.expire < Time.time);
        active.Add(new Shape { center = center, size = size, angle = angle, color = color, expire = Time.time + gizmoLinger });
    }

    void OnDrawGizmos()
    {
        if (!drawGizmos) return;
        Vector2 p = Pos;
        int d = Dir;

        Combat.DrawBox(arenaCenter, arenaSize, 0f, arenaColor);
        Combat.DrawBox(GroundCheckCenter(), groundCheckSize, 0f, groundCheckColor, grounded);

        // Sword combo strikes (faint previews; live strikes are drawn by EnemyAttackHitbox itself)
        for (int i = 0; i < comboStrikes.Count; i++)
        {
            GuardianStrike s = comboStrikes[i];
            Combat.DrawBox(p + new Vector2(s.offset.x * d, s.offset.y), s.size, 0f, Combat.A(swordColor, 0.35f));
        }

        // Tornado spawn points and travel
        for (int i = 0; i < tornadoCount; i++)
        {
            Vector2 sp = TornadoSpawnPoint(p, d, i);
            float edge = d > 0 ? ArenaMaxX : ArenaMinX;
            float travel = Mathf.Min(tornadoLifetime > 0f ? tornadoLifetime * tornadoSpeed : float.MaxValue, Mathf.Abs(edge - sp.x));
            Combat.DrawBox(sp, tornadoSize, 0f, Combat.A(tornadoColor, 0.4f));
            Gizmos.color = Combat.A(tornadoColor, 0.4f);
            Gizmos.DrawLine(sp, sp + Vector2.right * d * travel);
        }

        // Stomp: jump apex, landing impact, shockwave lanes
        Gizmos.color = Combat.A(shockwaveColor, 0.5f);
        Gizmos.DrawLine(p, p + Vector2.up * jumpHeight);
        Gizmos.DrawLine(p + new Vector2(-0.4f, jumpHeight), p + new Vector2(0.4f, jumpHeight));
        Combat.DrawBox(stompImpact.Center(p, d), stompImpact.size, 0f, Combat.A(stompImpact.color, 0.4f));
        for (int dir = -1; dir <= 1; dir += 2)
        {
            if (dir == -1 && !shockwaveBothDirections) continue;
            Vector2 sp = ShockwaveSpawn(p, dir);
            float edge = dir > 0 ? ArenaMaxX : ArenaMinX;
            Combat.DrawBox(sp, shockwaveSize, 0f, Combat.A(shockwaveColor, 0.5f));
            Gizmos.DrawLine(sp, new Vector2(edge, sp.y));
        }

        // Shield state
        if (shieldUp) Combat.DrawCircle(p + Vector2.up * 0.2f, 1.6f, Color.black, true);

        foreach (Shape s in active)
            if (s.expire >= Time.time) Combat.DrawBox(s.center, s.size, s.angle, s.color, true);
    }
}
