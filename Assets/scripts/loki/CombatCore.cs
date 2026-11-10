using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>Data passed to every IDamageable. enemyAttack is set when the damage comes from an EnemyAttackHitbox (lets the player parry it).</summary>
public struct DamageInfo
{
    public int damage;
    public Vector2 origin;
    public GameObject source;
    public EnemyAttackHitbox enemyAttack;
}

/// <summary>Implement this on any enemy / boss part. Put it on the same object as the Collider2D or on a parent.</summary>
public interface IDamageable
{
    void TakeDamage(DamageInfo info);
}

/// <summary>
/// Single source of truth for a box hitbox: gameplay and gizmos both read offset/size from here.
/// Offset.x is mirrored by facing.
/// </summary>
[Serializable]
public class BoxHitbox
{
    public Vector2 offset = Vector2.zero;
    public Vector2 size = Vector2.one;
    public Color color = Color.green;

    public BoxHitbox() { }
    public BoxHitbox(Vector2 offset, Vector2 size, Color color)
    {
        this.offset = offset;
        this.size = size;
        this.color = color;
    }

    public Vector2 Center(Vector2 origin, int facing)
    {
        return new Vector2(origin.x + offset.x * facing, origin.y + offset.y);
    }
}

public static class Combat
{
    /// <summary>
    /// Damages every IDamageable found in cols that is not already in hits. Returns the number of NEW targets hit.
    /// hits dedupes per IDamageable (or per collider when perCollider is true, e.g. radial bursts that should hit every boss part).
    /// </summary>
    public static int Damage(Collider2D[] cols, int damage, Vector2 origin, GameObject source,
                             HashSet<object> hits, out Collider2D firstHit, bool perCollider = false)
    {
        firstHit = null;
        int count = 0;
        foreach (Collider2D c in cols)
        {
            if (source != null && c.transform.IsChildOf(source.transform)) continue;
            IDamageable d = c.GetComponentInParent<IDamageable>();
            if (d == null) continue;
            object key = perCollider ? (object)c : d;
            if (!hits.Add(key)) continue;
            d.TakeDamage(new DamageInfo { damage = damage, origin = origin, source = source });
            if (firstHit == null) firstHit = c;
            count++;
        }
        return count;
    }

    public static Color A(Color c, float a) { return new Color(c.r, c.g, c.b, a); }

    public static void DrawBox(Vector2 center, Vector2 size, float angle, Color c, bool fill = false)
    {
        Matrix4x4 old = Gizmos.matrix;
        Gizmos.matrix = Matrix4x4.TRS(center, Quaternion.Euler(0f, 0f, angle), Vector3.one);
        if (fill)
        {
            Gizmos.color = A(c, c.a * 0.3f);
            Gizmos.DrawCube(Vector3.zero, size);
        }
        Gizmos.color = c;
        Gizmos.DrawWireCube(Vector3.zero, size);
        Gizmos.matrix = old;
    }

    public static void DrawCircle(Vector2 center, float radius, Color c, bool fill = false)
    {
        if (fill)
        {
            Gizmos.color = A(c, c.a * 0.3f);
            Gizmos.DrawSphere(center, radius);
        }
        Gizmos.color = c;
        Gizmos.DrawWireSphere(center, radius);
    }
}
