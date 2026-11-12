using System.Collections;
using UnityEngine;

/// <summary>
/// Camera Shake controller with singleton access (CameraShake.Instance.Shake(...)).
/// Can be attached to the Main Camera or will dynamically attach to Camera.main if called.
/// </summary>
public class CameraShake : MonoBehaviour
{
    public static CameraShake Instance { get; private set; }

    Vector3 initialLocalPos;
    Coroutine shakeRoutine;

    void Awake()
    {
        if (Instance == null)
        {
            Instance = this;
            initialLocalPos = transform.localPosition;
        }
        else if (Instance != this)
        {
            Destroy(this);
        }
    }

    public static void Shake(float duration = 0.15f, float intensity = 0.2f)
    {
        if (Instance == null)
        {
            Camera cam = Camera.main;
            if (cam != null)
            {
                Instance = cam.gameObject.GetComponent<CameraShake>();
                if (Instance == null)
                {
                    Instance = cam.gameObject.AddComponent<CameraShake>();
                    Instance.initialLocalPos = cam.transform.localPosition;
                }
            }
        }

        if (Instance != null)
        {
            Instance.DoShake(duration, intensity);
        }
    }

    public void DoShake(float duration, float intensity)
    {
        if (shakeRoutine != null)
        {
            StopCoroutine(shakeRoutine);
            transform.localPosition = initialLocalPos;
        }
        shakeRoutine = StartCoroutine(ShakeCoroutine(duration, intensity));
    }

    IEnumerator ShakeCoroutine(float duration, float intensity)
    {
        float elapsed = 0f;
        while (elapsed < duration)
        {
            elapsed += Time.unscaledDeltaTime; // Unscaled so it works during hit stops
            float decay = 1f - Mathf.Clamp01(elapsed / duration);
            Vector2 offset = Random.insideUnitCircle * intensity * decay;
            transform.localPosition = initialLocalPos + new Vector3(offset.x, offset.y, 0f);
            yield return null;
        }

        transform.localPosition = initialLocalPos;
        shakeRoutine = null;
    }
}
