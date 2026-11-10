using UnityEngine;

/// <summary>Test target: logs every hit and refills its health. Needs a Collider2D on an enemy layer.</summary>
[RequireComponent(typeof(Collider2D))]
public class TrainingDummy : MonoBehaviour, IDamageable
{
    public int maxHealth = 1000;
    public int health = 1000;

    public void TakeDamage(DamageInfo info)
    {
        health -= info.damage;
        string src = info.source != null ? info.source.name : "?";
        Debug.Log(name + " took " + info.damage + " damage (hp " + health + "/" + maxHealth + ") from " + src);
        if (health <= 0) health = maxHealth;
    }
}
