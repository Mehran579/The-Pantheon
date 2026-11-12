using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Hollow Knight / Silksong white flash hit effect.
/// Replaces the SpriteRenderer material with a pure white silhouette shader for duration,
/// then restores original materials.
/// </summary>
public class HitBlinker : MonoBehaviour
{
    static Material solidWhiteMat;

    SpriteRenderer[] renderers;
    Material[] originalMaterials;
    Coroutine blinkRoutine;

    void Awake()
    {
        CacheRenderers();
    }

    void CacheRenderers()
    {
        renderers = GetComponentsInChildren<SpriteRenderer>(true);
        if (renderers != null && renderers.Length > 0)
        {
            originalMaterials = new Material[renderers.Length];
            for (int i = 0; i < renderers.Length; i++)
            {
                originalMaterials[i] = renderers[i].sharedMaterial;
            }
        }
    }

    public static void Flash(GameObject target, float duration = 0.08f)
    {
        if (target == null) return;
        HitBlinker blinker = target.GetComponent<HitBlinker>();
        if (blinker == null) blinker = target.AddComponent<HitBlinker>();
        blinker.Blink(duration);
    }

    public void Blink(float duration = 0.08f)
    {
        if (renderers == null || renderers.Length == 0)
        {
            CacheRenderers();
            if (renderers == null || renderers.Length == 0) return;
        }

        if (blinkRoutine != null)
        {
            StopCoroutine(blinkRoutine);
            RestoreRenderers();
        }

        blinkRoutine = StartCoroutine(BlinkRoutine(duration));
    }

    IEnumerator BlinkRoutine(float duration)
    {
        EnsureMaterial();

        if (solidWhiteMat != null)
        {
            for (int i = 0; i < renderers.Length; i++)
            {
                if (renderers[i] != null)
                {
                    renderers[i].sharedMaterial = solidWhiteMat;
                }
            }
        }

        yield return new WaitForSeconds(duration);

        RestoreRenderers();
        blinkRoutine = null;
    }

    void RestoreRenderers()
    {
        if (renderers == null) return;
        for (int i = 0; i < renderers.Length; i++)
        {
            if (renderers[i] != null && originalMaterials != null && i < originalMaterials.Length)
            {
                renderers[i].sharedMaterial = originalMaterials[i];
            }
        }
    }

    static void EnsureMaterial()
    {
        if (solidWhiteMat != null) return;

        Shader shader = Shader.Find("Hidden/SolidWhite");
        if (shader == null) shader = Shader.Find("GUI/Text Shader");
        if (shader == null) shader = Shader.Find("Sprites/Default");

        if (shader != null)
        {
            solidWhiteMat = new Material(shader) { hideFlags = HideFlags.DontSave };
        }
    }

    void OnDisable()
    {
        if (blinkRoutine != null)
        {
            StopCoroutine(blinkRoutine);
            blinkRoutine = null;
            RestoreRenderers();
        }
    }
}
