using UnityEngine;

/// <summary>
/// Helicopter flight for PlaneController. Where a jet always flies along its
/// nose, a helicopter hovers by default: tilting it moves it, the throttle
/// controls climb and descent, and letting go of everything brings it back to
/// a level hover. It can set down on any solid ground, not just runways.
/// </summary>
public partial class PlaneController
{
    [Header("Helicopter")]
    [Tooltip("Fly as a helicopter instead of a jet. Set by the vehicle selector.")]
    public bool helicopter;
    public float heliMaxSpeed = 75f;
    [Tooltip("How quickly it picks up and sheds speed (m/s per second).")]
    public float heliAcceleration = 24f;
    public float heliClimbRate = 16f;
    [Tooltip("Nose-down angle at full forward stick (deg).")]
    public float heliTilt = 20f;
    public float heliBank = 32f;
    public float heliYawRate = 75f;
    [Tooltip("At speed, banking also turns the helicopter this fast (deg/s).")]
    public float heliBankTurnRate = 38f;
    [Tooltip("Setting down faster than this is a crash (m/s).")]
    public float heliMaxTouchdownSink = 7f;
    public Transform rotor;
    public Transform tailRotor;
    public float rotorSpeed = 1100f;

    Vector3 heliVelocity;
    float heliYaw;

    void HeliStart()
    {
        // Skids, not wheels: always "down".
        GearDown = true;
        GearPosition = 1f;
        Speed = 0f;
        heliVelocity = Vector3.zero;
        heliYaw = transform.eulerAngles.y;
    }

    void HeliUpdate()
    {
        // Rotors idle on the ground and spin up in the air.
        float spin = rotorSpeed * (IsGrounded ? 0.45f : 1f) * Time.deltaTime;
        if (rotor != null) rotor.Rotate(0f, spin, 0f, Space.Self);
        if (tailRotor != null) tailRotor.Rotate(spin * 1.7f, 0f, 0f, Space.Self);
    }

    void HeliStep(float dt)
    {
        float climb = accelerateInput - brakeInput;

        if (IsGrounded)
        {
            Speed = 0f;
            VerticalSpeed = 0f;
            heliVelocity = Vector3.zero;
            rb.MoveRotation(Quaternion.Euler(0f, heliYaw, 0f));
            rb.angularVelocity = Vector3.zero;
            rb.linearVelocity = Vector3.zero;
            if (climb > 0.2f)
            {
                IsGrounded = false;
                heliVelocity = Vector3.up * 3f;
                TookOff?.Invoke();
            }
            return;
        }

        float speedShare = Mathf.Clamp01(new Vector2(heliVelocity.x, heliVelocity.z).magnitude / heliMaxSpeed);

        // Pedals turn it on the spot; once it is moving, banking carries it round the turn as well.
        heliYaw += (smoothInput.y * heliYawRate - smoothInput.z * heliBankTurnRate * speedShare) * dt;
        Quaternion heading = Quaternion.Euler(0f, heliYaw, 0f);
        Quaternion rotation = Quaternion.Euler(smoothInput.x * heliTilt, heliYaw, smoothInput.z * heliBank);

        // Stick forward flies forward; stick sideways slides sideways at low speed.
        Vector3 wanted = heading * new Vector3(-smoothInput.z * heliMaxSpeed * 0.45f * (1f - speedShare), 0f, smoothInput.x * heliMaxSpeed);
        Vector3 horizontal = Vector3.MoveTowards(new Vector3(heliVelocity.x, 0f, heliVelocity.z), wanted, heliAcceleration * dt);
        float vertical = Mathf.MoveTowards(heliVelocity.y, climb * heliClimbRate, 22f * dt);
        heliVelocity = new Vector3(horizontal.x, vertical, horizontal.z);

        Speed = horizontal.magnitude;
        VerticalSpeed = vertical;
        HighG = false;
        IsStalling = false;
        Afterburner = 0f;

        rb.MoveRotation(rotation);
        rb.angularVelocity = Vector3.zero;
        rb.linearVelocity = heliVelocity;

        // Touch down on whatever is underneath, if it is solid and we arrive gently.
        if (vertical <= 0f && Physics.Raycast(rb.position, Vector3.down, out RaycastHit hit, gearHeight + 0.3f, ~0, QueryTriggerInteraction.Ignore)
            && !hit.transform.IsChildOf(transform))
        {
            bool water = hit.collider.gameObject.name == "Ocean";
            if (water || -vertical > heliMaxTouchdownSink || Speed > 14f)
            {
                Crash();
                return;
            }
            IsGrounded = true;
            Surface = hit.collider.GetComponentInParent<LandingSurface>();
            Vector3 position = rb.position;
            position.y = hit.point.y + gearHeight;
            rb.position = position;
            TouchedDown?.Invoke(-vertical);
        }
    }
}
