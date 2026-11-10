using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// One vertical eruption: waits for a short delay, then its box hitbox is active for activeTime.
/// Each target is hit once per eruption. The gizmo is drawn only while the hitbox is active.
/// </summary>
public class RiftEruption : MonoBehaviour
{
    Vector2 size;
    float delay;
    float activeTime;
    int damage;
    LayerMask targets;
    Color color;
    GameObject prefab;
    GameObject source;
    bool active;
    readonly HashSet<object> hits = new HashSet<object>();

    public static RiftEruption Spawn(Vector2 center, Vector2 size, float delay, float activeTime, int damage,
                                     LayerMask targets, Color color, GameObject visualPrefab, GameObject source)
    {
        GameObject go = new GameObject("RiftEruption");
        go.transform.position = center;
        RiftEruption e = go.AddComponent<RiftEruption>();
        e.size = size;
        e.delay = delay;
        e.activeTime = activeTime;
        e.damage = damage;
        e.targets = targets;
        e.color = color;
        e.prefab = visualPrefab;
        e.source = source;
        return e;
    }

    void Start() { StartCoroutine(Run()); }

    IEnumerator Run()
    {
        yield return new WaitForSeconds(delay);
        if (prefab != null)
        {
            GameObject v = Instantiate(prefab, transform);
            v.transform.localPosition = Vector3.zero;
        }
        active = true;
        float end = Time.time + activeTime;
        while (Time.time < end)
        {
            Collider2D[] cols = Physics2D.OverlapBoxAll(transform.position, size, 0f, targets);
            Combat.Damage(cols, damage, transform.position, source, hits, out _);
            yield return new WaitForFixedUpdate();
        }
        active = false;
        Destroy(gameObject, 0.1f);
    }

    void OnDrawGizmos()
    {
        if (active) Combat.DrawBox(transform.position, size, 0f, color, true);
    }
}
