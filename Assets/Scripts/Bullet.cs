using UnityEngine;

/// <summary>
/// Gun round: flies straight, raycasting each frame's travel.
/// </summary>
public class Bullet : MonoBehaviour
{
    public float speed = 1100f;
    public float lifetime = 1.3f;
    public float damage = 8f;

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
            && hit.transform.root != owner)
        {
            Target hitTarget = hit.collider.GetComponentInParent<Target>();
            if (hitTarget != null) hitTarget.Damage(damage);
            Destroy(gameObject);
            return;
        }

        transform.position += transform.forward * distance;
    }
}
