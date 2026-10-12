using UnityEngine;

/// <summary>
/// Predicts a collision along the plane's flight path and flags when the
/// player needs to pull up. The HUD reads this to show the warning.
/// </summary>
[RequireComponent(typeof(PlaneController))]
public class GroundProximityWarning : MonoBehaviour
{
    [Tooltip("Warn when something is this many seconds ahead on the current path.")]
    public float warningTime = 3.5f;
    [Tooltip("Also warn when descending below this height above the ground (m).")]
    public float lowAltitude = 120f;
    [Tooltip("Radius of the swept path; roughly half the wingspan.")]
    public float pathRadius = 5f;

    public bool Warning { get; private set; }
    /// <summary>Seconds until impact on the current path, or infinity if clear.</summary>
    public float TimeToImpact { get; private set; }

    PlaneController plane;

    void Awake()
    {
        plane = GetComponent<PlaneController>();
        TimeToImpact = float.PositiveInfinity;
    }

    void Update()
    {
        TimeToImpact = float.PositiveInfinity;
        Warning = false;
        // No nagging while parked, rolling, or deliberately descending to land with the gear down.
        if (!plane.enabled || plane.IsGrounded || plane.GearDown) return;

        float speed = Mathf.Max(plane.Speed, 1f);
        Vector3 forward = transform.forward;

        // The cast starts inside the plane's own collider, which casts ignore.
        if (Physics.SphereCast(transform.position, pathRadius, forward, out RaycastHit ahead, speed * warningTime, ~0, QueryTriggerInteraction.Ignore)
            && !ahead.transform.IsChildOf(transform))
        {
            TimeToImpact = ahead.distance / speed;
            Warning = true;
        }

        if (forward.y < -0.02f
            && Physics.Raycast(transform.position, Vector3.down, out RaycastHit below, lowAltitude, ~0, QueryTriggerInteraction.Ignore)
            && !below.transform.IsChildOf(transform))
        {
            Warning = true;
        }
    }
}
