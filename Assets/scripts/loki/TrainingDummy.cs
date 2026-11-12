using System.Collections;
using UnityEngine;

/// <summary>Test target: logs every hit, flashes white, and reacts to knockback. Needs a Collider2D on an enemy layer.</summary>
[RequireComponent(typeof(Collider2D))]
public class TrainingDummy : MonoBehaviour, IDamageable
{
    public int maxHealth = 1000;
    public int health = 1000;

    Rigidbody2D rb;
    Coroutine knockRoutine;

    void Awake()
    {
        rb = GetComponent<Rigidbody2D>();
    }

    public void TakeDamage(DamageInfo info)
    {
        health -= info.damage;
        string src = info.source != null ? info.source.name : "?";
        Debug.Log(name + " took " + info.damage + " damage (hp " + health + "/" + maxHealth + ") from " + src);

        // Flash white
        HitBlinker.Flash(gameObject);

        // Camera shake on hit
        CameraShake.Shake(0.12f, 0.15f);

        // Knockback if Rigidbody2D exists
        if (rb != null && info.knockbackSpeed > 0f)
        {
            Vector2 dir = ((Vector2)transform.position - info.origin).normalized;
            if (dir.sqrMagnitude < 0.001f) dir = Vector2.right;
            if (knockRoutine != null) StopCoroutine(knockRoutine);
            knockRoutine = StartCoroutine(KnockRoutine(dir * info.knockbackSpeed, info.knockbackDuration > 0f ? info.knockbackDuration : 0.15f));
        }

        if (health <= 0) health = maxHealth;
    }

    IEnumerator KnockRoutine(Vector2 velocity, float duration)
    {
        float elapsed = 0f;
        while (elapsed < duration)
        {
            elapsed += Time.fixedDeltaTime;
            float t = 1f - Mathf.Clamp01(elapsed / duration);
            rb.linearVelocity = velocity * t;
            yield return new WaitForFixedUpdate();
        }
        rb.linearVelocity = Vector2.zero;
        knockRoutine = null;
    }
}
