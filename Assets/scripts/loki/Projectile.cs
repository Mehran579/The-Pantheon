using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Code-built moving hitbox (no prefab needed). Used for Arc Bolt, Magic crescent, infused bolts and radial blades.
/// Hit detection and gizmo use the exact same box.
/// </summary>
public class Projectile : MonoBehaviour
{
    public Vector2 direction = Vector2.right;
    public float speed = 10f;
    public float lifetime = 1f;
    public Vector2 size = Vector2.one;
    public int damage = 1;
    public bool pierce;
    public LayerMask targets;
    public Color gizmoColor = Color.magenta;
    public System.Action onHit;                // optional: called when the projectile damages something

    GameObject source;
    readonly HashSet<object> hits = new HashSet<object>();

    public static Projectile Spawn(Vector2 pos, Vector2 dir, float speed, float lifetime, Vector2 size, int damage,
                                   bool pierce, LayerMask targets, Color color, GameObject visualPrefab, GameObject source)
    {
        GameObject go = new GameObject("Projectile");
        go.transform.position = pos;
        Projectile p = go.AddComponent<Projectile>();
        p.direction = dir.normalized;
        p.speed = speed;
        p.lifetime = lifetime;
        p.size = size;
        p.damage = damage;
        p.pierce = pierce;
        p.targets = targets;
        p.gizmoColor = color;
        p.source = source;
        if (visualPrefab != null)
        {
            GameObject v = Instantiate(visualPrefab, go.transform);
            v.transform.localPosition = Vector3.zero;
            v.transform.localRotation = Quaternion.Euler(0f, 0f, p.Angle);
        }
        return p;
    }

    float Angle { get { return Mathf.Atan2(direction.y, direction.x) * Mathf.Rad2Deg; } }

    void Start() { Destroy(gameObject, lifetime); }

    void FixedUpdate()
    {
        transform.position += (Vector3)(direction * speed * Time.fixedDeltaTime);
        Collider2D[] cols = Physics2D.OverlapBoxAll(transform.position, size, Angle, targets);
        int n = Combat.Damage(cols, damage, transform.position, source, hits, out _);
        if (n > 0 && onHit != null) onHit();
        if (n > 0 && !pierce) Destroy(gameObject);
    }

    void OnDrawGizmos()
    {
        Combat.DrawBox(transform.position, size, Angle, gizmoColor, true);
    }
}