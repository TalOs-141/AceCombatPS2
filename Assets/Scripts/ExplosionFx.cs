using UnityEngine;

/// <summary>
/// Placeholder explosion: a fireball that swells quickly, then shrinks away.
/// </summary>
public class ExplosionFx : MonoBehaviour
{
    public float size = 60f;
    public float duration = 0.9f;
    [Tooltip("Fraction of the duration spent growing.")]
    [Range(0.05f, 0.95f)] public float growPortion = 0.25f;

    float age;

    void Awake()
    {
        transform.localScale = Vector3.zero;
    }

    void Update()
    {
        age += Time.deltaTime;
        float t = age / duration;
        if (t >= 1f)
        {
            Destroy(gameObject);
            return;
        }

        float scale = t < growPortion
            ? Mathf.SmoothStep(0f, 1f, t / growPortion)
            : 1f - Mathf.SmoothStep(0f, 1f, (t - growPortion) / (1f - growPortion));
        transform.localScale = Vector3.one * (size * scale);
    }
}
