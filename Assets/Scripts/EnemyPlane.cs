using UnityEngine;

/// <summary>
/// Simple enemy flight: cruises between random waypoints inside the arena,
/// banking into its turns. It does not fight back yet.
/// </summary>
[RequireComponent(typeof(Target))]
public class EnemyPlane : MonoBehaviour
{
    public float speed = 150f;
    public float turnRate = 30f;
    public float maxBank = 65f;
    [Tooltip("Visual child that gets rolled; the root itself stays unbanked.")]
    public Transform model;

    [Header("Patrol area")]
    public float areaRadius = 3500f;
    public float minAltitude = 500f;
    public float maxAltitude = 1500f;
    public float waypointReachedDistance = 400f;

    Vector3 waypoint;
    float bank;

    void Start()
    {
        PickWaypoint();
    }

    void Update()
    {
        float dt = Time.deltaTime;
        if (dt <= 0f) return;

        Vector3 toWaypoint = waypoint - transform.position;
        if (toWaypoint.magnitude < waypointReachedDistance)
        {
            PickWaypoint();
            toWaypoint = waypoint - transform.position;
        }

        Quaternion current = transform.rotation;
        Quaternion next = Quaternion.RotateTowards(current, Quaternion.LookRotation(toWaypoint), turnRate * dt);

        float yawRate = Mathf.DeltaAngle(current.eulerAngles.y, next.eulerAngles.y) / dt;
        float targetBank = -Mathf.Clamp(yawRate / turnRate, -1f, 1f) * maxBank;
        bank = Mathf.Lerp(bank, targetBank, 1f - Mathf.Exp(-2f * dt));

        transform.rotation = next;
        transform.position += transform.forward * speed * dt;
        if (model != null) model.localRotation = Quaternion.Euler(0f, 0f, bank);
    }

    void PickWaypoint()
    {
        Vector2 flat = Random.insideUnitCircle * areaRadius;
        waypoint = new Vector3(flat.x, Random.Range(minAltitude, maxAltitude), flat.y);
    }
}
