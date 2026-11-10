using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;

[System.Serializable]
public class EnemyParriedEvent : UnityEvent<LokiController> { }

/// <summary>
/// An enemy attack area. The player's parry detects it through the static All list (no collider needed).
/// Put it on any enemy/boss object and call Activate() / set active from your own scripts.
/// With autoPulse on it fires by itself every pulseInterval, which is handy for testing parries.
/// </summary>
public class EnemyAttackHitbox : MonoBehaviour
{
    public static readonly List<EnemyAttackHitbox> All = new List<EnemyAttackHitbox>();

    public Vector2 offset = new Vector2(-1f, 0f);
    public Vector2 size = new Vector2(1.5f, 1.5f);
    public int damage = 10;
    public bool parryable = true;
    public bool active;
    public Color gizmoColor = Color.grey;

    [Header("Test pulsing")]
    public bool autoPulse = true;
    public float pulseInterval = 2f;
    public float pulseDuration = 0.4f;

    public EnemyParriedEvent onParried;

    bool hitDone;
    bool parried;

    public bool IsParryable { get { return active && parryable && !parried; } }

    public Bounds GetBounds()
    {
        Vector2 c = (Vector2)transform.position + offset;
        return new Bounds(new Vector3(c.x, c.y, 0f), new Vector3(size.x, size.y, 100f));
    }

    void OnEnable() { All.Add(this); }
    void OnDisable() { All.Remove(this); }

    void Start() { if (autoPulse) StartCoroutine(Pulse()); }

    IEnumerator Pulse()
    {
        while (true)
        {
            yield return new WaitForSeconds(pulseInterval);
            Activate();
            yield return new WaitForSeconds(pulseDuration);
            active = false;
        }
    }

    public void Activate()
    {
        active = true;
        hitDone = false;
        parried = false;
    }

    void FixedUpdate()
    {
        if (!active || hitDone || parried) return;
        LokiController p = LokiController.Instance;
        if (p == null || !p.PlayerBounds.Intersects(GetBounds())) return;
        hitDone = true;
        p.TakeDamage(new DamageInfo { damage = damage, origin = transform.position, source = gameObject, enemyAttack = this });
    }

    public void NotifyParried(LokiController player)
    {
        parried = true;
        if (onParried != null) onParried.Invoke(player);
    }

    void OnDrawGizmos()
    {
        Vector2 c = (Vector2)transform.position + offset;
        Combat.DrawBox(c, size, 0f, gizmoColor, active);
    }
}
