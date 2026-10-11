using UnityEngine;

/// <summary>
/// Gun round: flies straight, raycasting each frame's travel.
/// </summary>
public class Bullet : MonoBehaviour
{
    public float speed = 1100f;
    public float lifetime = 1.3f;
    public float damage = 8f;
    [Tooltip("Spawned when a round hits a target.")]
    public GameObject hitEffect;
    [Tooltip("Spawned when a round hits anything else (ground, buildings).")]
    public GameObject missEffect;

    Transform owner;
    float age;

    public void Fire(Transform firedBy)
    {
        owner = firedBy;
    }

    void Update()
    {
        float dt = Time.deltaTime;
        age += dt;
        if (age > lifetime)
        {
            Destroy(gameObject);
            return;
        }

        float distance = speed * dt;
        if (Physics.Raycast(transform.position, transform.forward, out RaycastHit hit, distance, ~0, QueryTriggerInteraction.Ignore)
            && (owner == null || !hit.transform.IsChildOf(owner)))
        {
            Target hitTarget = hit.collider.GetComponentInParent<Target>();
            if (hitTarget != null) hitTarget.Damage(damage);

            GameObject effect = hitTarget != null ? hitEffect : missEffect;
            if (effect != null) Instantiate(effect, hit.point, Quaternion.identity);
            Destroy(gameObject);
            return;
        }

        transform.position += transform.forward * distance;
    }
}
