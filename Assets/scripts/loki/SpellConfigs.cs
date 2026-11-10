using System;
using UnityEngine;

public enum SpellType { ArcBolt, RiftBurst, AstralOrbit }

/// <summary>Fields shared by all spells.</summary>
[Serializable]
public class SpellBase
{
    public int normalCost = 1;
    public int chargedCost = 2;
    public float cooldown = 0.35f;
    public float castStartup = 0.1f;
    public float castRecovery = 0.2f;
}

[Serializable]
public class ArcBoltSpell : SpellBase
{
    public Vector2 spawnOffset = new Vector2(0.9f, 0.1f);
    public Color color = Color.magenta;

    [Header("Normal: fast straight bolt")]
    public int damage = 8;
    public float speed = 20f;
    public float lifetime = 1.2f;
    public Vector2 size = new Vector2(0.9f, 0.4f);

    [Header("Charged: larger PIERCING bolt")]
    public int chargedDamage = 18;
    public float chargedSpeed = 18f;
    public float chargedLifetime = 1.4f;
    public Vector2 chargedSize = new Vector2(2.2f, 1.4f);
}

[Serializable]
public class RiftBurstSpell : SpellBase
{
    public Color color = new Color(1f, 0.5f, 0f);
    public float forwardOffset = 1f;       // distance in front of Loki where the first eruption appears
    public float bottomOffset = -0.2f;     // eruption bottom relative to Loki's feet
    public float eruptionDelay = 0.12f;    // telegraph before the hitbox turns on
    public float eruptionActiveTime = 0.3f;

    [Header("Normal: one vertical eruption")]
    public int damage = 10;
    public float width = 1.4f;
    public float height = 5f;

    [Header("Charged: successive eruptions marching forward")]
    public int chargedDamage = 8;          // per eruption (each can hit the same target again)
    public int chargedCount = 5;
    public float chargedInterval = 0.18f;
    public float chargedSpacing = 1.7f;
    public float chargedWidth = 2.4f;
    public float chargedHeight = 6.5f;

    public RiftBurstSpell() { cooldown = 0.6f; }
}

[Serializable]
public class AstralOrbitSpell : SpellBase
{
    public Color orbitColor = new Color(0.6f, 0.2f, 1f);
    public Color releaseColor = Color.red;
    public float centerHeight = 0.3f;       // orbit centre height above Loki's pivot
    public float angularSpeed = 240f;       // degrees per second
    public float bladeRadius = 0.35f;       // hit radius of each blade
    public float rehitInterval = 0.5f;      // seconds before the same target can be hit again by the orbit

    [Header("Normal orbit")]
    public int bladeCount = 3;
    public float orbitRadius = 1.8f;
    public float duration = 4f;
    public int damage = 5;

    [Header("Charged orbit (+ radial release)")]
    public int chargedBladeCount = 6;
    public float chargedOrbitRadius = 3f;
    public float chargedDuration = 3f;
    public int chargedDamage = 7;
    public int releaseDamage = 14;          // independent damage of the outward burst
    public float releaseSpeed = 14f;
    public float releaseLifetime = 0.8f;
    public Vector2 releaseSize = new Vector2(0.9f, 0.35f);

    public AstralOrbitSpell() { cooldown = 1f; }
}