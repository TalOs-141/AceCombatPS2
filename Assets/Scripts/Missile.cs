using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Homing missile. Launched with a target it leads and chases it; launched
/// without one it flies straight. Moves by swept casts so it can't tunnel.
/// </summary>
public class Missile : MonoBehaviour
{
    public static readonly List<Missile> Active = new List<Missile>();

    public float acceleration = 250f;
    public float maxSpeed = 650f;
    public float turnRate = 80f;
    public float lifetime = 9f;
    public float damage = 100f;
    public float proximityRadius = 12f;
    [Tooltip("Target is lost if it gets further than this off the missile's nose (deg).")]
    public float seekerCone = 70f;
    public float castRadius = 1.5f;
    public GameObject explosionPrefab;
    [Tooltip("Detached on impact so the smoke lingers.")]
    public TrailRenderer trail;

    /// <summary>What this missile is currently homing on; null if unguided or it lost track.</summary>
    public Target Target => target;

    Target target;
    Transform owner;
    float speed;
    float age;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics() => Active.Clear();

    void OnEnable() => Active.Add(this);
    void OnDisable() => Active.Remove(this);

    public void Launch(Target lockedTarget, Transform launchedBy, float initialSpeed)
    {
        target = lockedTarget;
        owner = launchedBy;
        speed = initialSpeed;
    }

    void Update()
    {
        float dt = Time.deltaTime;
        age += dt;
        if (age > lifetime)
        {
            Detonate(transform.position);
            return;
        }

        if (target != null && !target.IsDead)
        {
            Vector3 toTarget = target.transform.position - transform.position;
            if (Vector3.Angle(transform.forward, toTarget) > seekerCone)
            {
                target = null;
            }
            else
            {
                float timeToImpact = toTarget.magnitude / Mathf.Max(speed, 1f);
                Vector3 aimPoint = target.transform.position + target.Velocity * timeToImpact;
                Quaternion desired = Quaternion.LookRotation(aimPoint - transform.position);
                transform.rotation = Quaternion.RotateTowards(transform.rotation, desired, turnRate * dt);
            }
        }

        speed = Mathf.MoveTowards(speed, maxSpeed, acceleration * dt);
        float distance = speed * dt;

        if (Physics.SphereCast(transform.position, castRadius, transform.forward, out RaycastHit hit, distance, ~0, QueryTriggerInteraction.Ignore)
            && (owner == null || !hit.transform.IsChildOf(owner)))
        {
            Target hitTarget = hit.collider.GetComponentInParent<Target>();
            if (hitTarget != null) hitTarget.Damage(damage);
            Detonate(hit.point);
            return;
        }

        transform.position += transform.forward * distance;

        if (target != null && !target.IsDead
            && (target.transform.position - transform.position).sqrMagnitude < proximityRadius * proximityRadius)
        {
            target.Damage(damage);
            Detonate(transform.position);
        }
    }

    void Detonate(Vector3 position)
    {
        if (explosionPrefab != null) Instantiate(explosionPrefab, position, Quaternion.identity);
        if (trail != null)
        {
            trail.transform.SetParent(null, true);
            Destroy(trail.gameObject, trail.time);
        }
        Destroy(gameObject);
    }
}
