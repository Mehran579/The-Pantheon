using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Blades orbiting a follow target. A single coroutine handles the orbit duration and (if charged) the radial release.
/// Blade positions used for damage are the same ones drawn by the gizmos.
/// </summary>
public class AstralOrbit : MonoBehaviour
{
    AstralOrbitSpell cfg;
    bool charged;
    Transform follow;
    LayerMask targets;
    GameObject radialPrefab;
    GameObject source;

    int count;
    float radius;
    int damage;
    float angle;
    bool orbiting;
    Transform[] visuals;
    readonly Dictionary<object, float> nextHit = new Dictionary<object, float>();

    public void Begin(AstralOrbitSpell c, bool isCharged, Transform center, LayerMask layers,
                      GameObject bladePrefab, GameObject releasePrefab, GameObject src)
    {
        cfg = c;
        charged = isCharged;
        follow = center;
        targets = layers;
        radialPrefab = releasePrefab;
        source = src;
        count = charged ? c.chargedBladeCount : c.bladeCount;
        radius = charged ? c.chargedOrbitRadius : c.orbitRadius;
        damage = charged ? c.chargedDamage : c.damage;

        visuals = new Transform[count];
        if (bladePrefab != null)
        {
            for (int i = 0; i < count; i++)
                visuals[i] = Instantiate(bladePrefab, transform).transform;
        }
        StartCoroutine(Run());
    }

    Vector2 Center { get { return (Vector2)follow.position + Vector2.up * cfg.centerHeight; } }

    Vector2 BladePos(int i)
    {
        float a = (angle + i * 360f / count) * Mathf.Deg2Rad;
        return Center + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * radius;
    }

    IEnumerator Run()
    {
        orbiting = true;
        float end = Time.time + (charged ? cfg.chargedDuration : cfg.duration);
        while (Time.time < end && follow != null)
        {
            angle += cfg.angularSpeed * Time.deltaTime;
            for (int i = 0; i < count; i++)
            {
                Vector2 pos = BladePos(i);
                if (visuals[i] != null) visuals[i].position = pos;
                HitAt(pos);
            }
            yield return null;
        }
        orbiting = false;
        if (charged && follow != null) Release();
        Destroy(gameObject);
    }

    void HitAt(Vector2 pos)
    {
        foreach (Collider2D c in Physics2D.OverlapCircleAll(pos, cfg.bladeRadius, targets))
        {
            if (source != null && c.transform.IsChildOf(source.transform)) continue;
            IDamageable d = c.GetComponentInParent<IDamageable>();
            if (d == null) continue;
            float t;
            if (nextHit.TryGetValue(d, out t) && Time.time < t) continue;
            nextHit[d] = Time.time + cfg.rehitInterval;
            d.TakeDamage(new DamageInfo { damage = damage, origin = pos, source = source });
        }
    }

    // Charged only: blades detach and fly outward; each projectile deals damage on its own.
    void Release()
    {
        Vector2 center = Center;
        for (int i = 0; i < count; i++)
        {
            Vector2 pos = BladePos(i);
            Vector2 dir = (pos - center).normalized;
            Projectile.Spawn(pos, dir, cfg.releaseSpeed, cfg.releaseLifetime, cfg.releaseSize,
                             cfg.releaseDamage, true, targets, cfg.releaseColor, radialPrefab, source);
        }
    }

    void OnDrawGizmos()
    {
        if (!orbiting || follow == null) return;
        Combat.DrawCircle(Center, radius, Combat.A(cfg.orbitColor, 0.35f));
        for (int i = 0; i < count; i++)
        {
            Vector2 pos = BladePos(i);
            Combat.DrawCircle(pos, cfg.bladeRadius, cfg.orbitColor, true);
            if (charged)
            {
                Vector2 dir = (pos - Center).normalized;
                Gizmos.color = Combat.A(cfg.releaseColor, 0.5f);
                Gizmos.DrawLine(pos, pos + dir * cfg.releaseSpeed * cfg.releaseLifetime);
            }
        }
    }
}
