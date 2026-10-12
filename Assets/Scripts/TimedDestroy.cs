using UnityEngine;

/// <summary>
/// Removes a one-shot effect (explosion, impact puff) once it has played out.
/// </summary>
public class TimedDestroy : MonoBehaviour
{
    public float lifetime = 3f;

    void Start()
    {
        Destroy(gameObject, lifetime);
    }
}
