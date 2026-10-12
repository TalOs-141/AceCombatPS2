using System;
using UnityEngine;

/// <summary>
/// Arcade flight model in the style of PS2-era Ace Combat: in the air the
/// plane always flies along its nose and the player steers with pitch/roll/yaw
/// and trims speed with accelerate/brake. On a carrier deck or runway it rolls
/// on its landing gear instead. No real aerodynamics.
/// </summary>
[RequireComponent(typeof(Rigidbody))]
public class PlaneController : MonoBehaviour
{
    [Header("Speed (m/s)")]
    public float minSpeed = 70f;
    public float cruiseSpeed = 180f;
    public float maxSpeed = 340f;
    public float acceleration = 60f;
    public float braking = 80f;
    [Tooltip("How quickly speed drifts back to cruise with no throttle input.")]
    public float cruiseReturn = 15f;
    [Tooltip("Speed gained per second in a vertical dive (lost in a vertical climb).")]
    public float gravityEffect = 35f;

    [Header("Turn rates (deg/s)")]
    public float pitchRate = 65f;
    public float rollRate = 170f;
    public float yawRate = 18f;
    [Tooltip("Higher = snappier controls.")]
    public float controlResponse = 6f;

    [Header("Agility")]
    [Tooltip("Turn-rate multiplier at minimum speed; it rises to 1 at cruise speed.")]
    public float lowSpeedAgility = 0.8f;
    [Tooltip("Turn-rate multiplier at maximum speed: fast means wide turns.")]
    public float highSpeedAgility = 0.7f;
    [Tooltip("Speed lost per second while pulling full pitch.")]
    public float turnDrag = 12f;

    [Header("High-G turn (hold accelerate + brake)")]
    public float highGPitchMultiplier = 1.55f;
    [Tooltip("Speed lost per second during a full high-G pull.")]
    public float highGDrag = 55f;

    [Header("Stall")]
    [Tooltip("Below this speed the nose is dragged toward the ground.")]
    public float stallSpeed = 90f;
    public float stallPitchRate = 25f;

    [Header("Landing gear")]
    public float gearTransitionTime = 1.5f;
    [Tooltip("Gear down: drag pulls the plane back to this speed.")]
    public float gearMaxSpeed = 130f;
    [Tooltip("Gear down acts like flaps too: the plane can fly this slowly.")]
    public float gearMinSpeed = 55f;
    [Tooltip("Speed the plane settles at with gear down and no throttle input.")]
    public float gearCruiseSpeed = 85f;
    public float gearDrag = 35f;
    [Tooltip("Distance from the plane's centre down to the bottom of the wheels.")]
    public float gearHeight = 2.4f;
    [Tooltip("Scaled along Y to extend and retract the gear.")]
    public Transform gearRoot;

    [Header("Ground handling")]
    [Tooltip("Speed needed before pulling back lifts the plane off.")]
    public float takeoffSpeed = 65f;
    public float groundAcceleration = 14f;
    public float catapultAcceleration = 38f;
    public float groundBrake = 16f;
    public float arrestDeceleration = 45f;
    public float rollingFriction = 1.5f;
    public float groundSteerRate = 22f;

    [Header("Landing limits")]
    public float maxLandingSpeed = 140f;
    [Tooltip("Hitting the deck faster than this vertically is a crash (m/s).")]
    public float maxSinkRate = 20f;
    public float maxLandingBank = 40f;
    public float maxLandingNoseUp = 35f;
    public float maxLandingNoseDown = 14f;

    [Header("Landing assist")]
    [Tooltip("Just above a deck or runway with the gear down, the plane flares and levels its wings by itself.")]
    public bool landingAssist = true;
    [Tooltip("Height above the surface at which the assist takes over (m).")]
    public float flareHeight = 30f;
    [Tooltip("Sink rate the flare aims for (m/s).")]
    public float flareSinkRate = 4f;

    [Header("Effects")]
    [Tooltip("Stretched along Z while the afterburner is lit.")]
    public Transform afterburner;

    public float Speed { get; private set; }
    public float Altitude => transform.position.y;
    /// <summary>Climb rate in m/s; negative when descending.</summary>
    public float VerticalSpeed { get; private set; }
    public bool IsGrounded { get; private set; }
    public LandingSurface Surface { get; private set; }
    public bool GearDown { get; private set; }
    /// <summary>0 = fully retracted, 1 = fully extended.</summary>
    public float GearPosition { get; private set; }
    public bool HighG { get; private set; }
    public bool IsStalling { get; private set; }
    /// <summary>0..1, how hard the afterburner is burning.</summary>
    public float Afterburner { get; private set; }

    /// <summary>Raised on a successful touchdown with the sink rate in m/s.</summary>
    public event Action<float> TouchedDown;
    public event Action TookOff;

    Rigidbody rb;
    Vector3 startPosition;
    Quaternion startRotation;

    // x = pitch, y = yaw, z = roll, each -1..1
    Vector3 rawInput;
    Vector3 smoothInput;
    float accelerateInput;
    float brakeInput;

    float groundYaw;
    float groundPitch;
    bool arresting;
    // Lets the plane be slower than its normal minimum just after leaving the ground.
    float speedFloor;

    void Awake()
    {
        rb = GetComponent<Rigidbody>();
        rb.useGravity = false;
        rb.interpolation = RigidbodyInterpolation.Interpolate;
        rb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;

        startPosition = transform.position;
        startRotation = transform.rotation;
        Speed = cruiseSpeed;
        speedFloor = minSpeed;
    }

    void Start()
    {
        // Starting parked on a deck or runway: gear down, engines idle.
        if (Physics.Raycast(transform.position, Vector3.down, out RaycastHit hit, gearHeight + 4f, ~0, QueryTriggerInteraction.Ignore)
            && hit.collider.GetComponentInParent<LandingSurface>() is LandingSurface surface)
        {
            IsGrounded = true;
            Surface = surface;
            GearDown = true;
            GearPosition = 1f;
            Speed = 0f;
            groundYaw = transform.eulerAngles.y;
            Vector3 position = transform.position;
            position.y = hit.point.y + gearHeight;
            transform.position = position;
            rb.position = position;
        }
        ApplyVisuals();
    }

    void Update()
    {
        if (Time.timeScale <= 0f) return;

        FlightInput.State input = FlightInput.Current;
        rawInput = new Vector3(input.pitch, input.yaw, input.roll);
        accelerateInput = input.accelerate;
        brakeInput = input.brake;

        // The gear can't be raised while the plane is standing on it.
        if (input.gearPressed && !IsGrounded) GearDown = !GearDown;
        GearPosition = Mathf.MoveTowards(GearPosition, GearDown ? 1f : 0f, Time.deltaTime / gearTransitionTime);

        ApplyVisuals();

        if (IsGrounded) Rumble.Continuous(Mathf.Clamp01(Speed / 90f) * 0.35f, 0f);
        if (HighG) Rumble.Continuous(0.45f, 0.2f);
        else if (IsStalling) Rumble.Continuous(0.3f, 0.3f);
        else if (Afterburner > 0.5f) Rumble.Continuous(0.12f, 0f);
    }

    void ApplyVisuals()
    {
        if (gearRoot != null)
        {
            bool visible = GearPosition > 0.02f;
            if (gearRoot.gameObject.activeSelf != visible) gearRoot.gameObject.SetActive(visible);
            gearRoot.localScale = new Vector3(1f, Mathf.Max(GearPosition, 0.02f), 1f);
        }

        if (afterburner != null)
        {
            bool lit = Afterburner > 0.03f;
            if (afterburner.gameObject.activeSelf != lit) afterburner.gameObject.SetActive(lit);
            // Flicker a little so the flame looks alive.
            float flicker = 0.85f + Mathf.PerlinNoise(Time.time * 30f, 0f) * 0.3f;
            afterburner.localScale = new Vector3(1f, 1f, Mathf.Max(Afterburner * flicker, 0.03f));
        }
    }

    void FixedUpdate()
    {
        float dt = Time.fixedDeltaTime;
        smoothInput = Vector3.Lerp(smoothInput, rawInput, 1f - Mathf.Exp(-controlResponse * dt));

        if (IsGrounded) GroundStep(dt);
        else AirStep(dt);
    }

    void AirStep(float dt)
    {
        // Agility peaks at cruise speed: sluggish when slow, wide turns when fast.
        float agility = Speed <= cruiseSpeed
            ? Mathf.Lerp(lowSpeedAgility, 1f, Mathf.InverseLerp(minSpeed, cruiseSpeed, Speed))
            : Mathf.Lerp(1f, highSpeedAgility, Mathf.InverseLerp(cruiseSpeed, maxSpeed, Speed));

        bool gear = GearPosition > 0.5f;
        float stall = gear ? gearMinSpeed + 8f : stallSpeed;
        float min = gear ? gearMinSpeed : minSpeed;

        HighG = accelerateInput > 0.5f && brakeInput > 0.5f && Speed > stall + 10f;
        float pitchScale = agility * (HighG ? highGPitchMultiplier : 1f);

        Vector3 turn = new Vector3(smoothInput.x * pitchRate * pitchScale, smoothInput.y * yawRate * agility, smoothInput.z * rollRate) * dt;
        Quaternion rotation = rb.rotation * Quaternion.Euler(turn);
        ApplyLandingAssist(ref rotation, dt);

        IsStalling = Speed < stall;
        if (IsStalling)
        {
            // Rotate the nose toward straight down, harder the slower we are.
            float severity = Mathf.InverseLerp(stall, min, Speed);
            Quaternion noseDown = Quaternion.LookRotation(Vector3.down, rotation * Vector3.forward);
            rotation = Quaternion.RotateTowards(rotation, noseDown, stallPitchRate * severity * dt);
        }

        float throttle = accelerateInput - brakeInput;
        if (HighG) Speed -= (highGDrag * Mathf.Abs(smoothInput.x) + 8f) * dt;
        else if (throttle > 0.05f) Speed += acceleration * throttle * dt;
        else if (throttle < -0.05f) Speed += braking * throttle * dt;
        else Speed = Mathf.MoveTowards(Speed, gear ? gearCruiseSpeed : cruiseSpeed, cruiseReturn * dt);

        Speed -= turnDrag * Mathf.Abs(smoothInput.x) * dt;
        if (GearPosition > 0f && Speed > gearMaxSpeed) Speed -= gearDrag * GearPosition * dt;

        Vector3 forward = rotation * Vector3.forward;
        Speed -= forward.y * gravityEffect * dt;

        // A plane that just left the ground slowly is allowed to stay slow (and stall) until it speeds up.
        speedFloor = Mathf.Min(min, Mathf.Max(speedFloor, Speed));
        Speed = Mathf.Clamp(Speed, speedFloor, maxSpeed);

        float burner = !HighG && throttle > 0.6f ? 1f : 0f;
        Afterburner = Mathf.MoveTowards(Afterburner, burner, dt * 4f);
        VerticalSpeed = forward.y * Speed;

        rb.MoveRotation(rotation);
        rb.angularVelocity = Vector3.zero;
        rb.linearVelocity = forward * Speed;

        CheckTouchdown(rotation);
    }

    /// <summary>
    /// The last few metres of a landing are the hard part, so the plane helps:
    /// over a landing surface with the gear down it eases the nose up to a gentle
    /// sink rate and rolls the wings level.
    /// </summary>
    void ApplyLandingAssist(ref Quaternion rotation, float dt)
    {
        if (!landingAssist || GearPosition < 0.95f) return;
        if (!Physics.Raycast(rb.position, Vector3.down, out RaycastHit hit, gearHeight + flareHeight, ~0, QueryTriggerInteraction.Ignore)) return;
        if (hit.collider.GetComponentInParent<LandingSurface>() == null) return;

        Vector3 euler = rotation.eulerAngles;
        float noseDown = Mathf.DeltaAngle(0f, euler.x);
        float bank = Mathf.DeltaAngle(0f, euler.z);

        float flareAngle = Mathf.Asin(Mathf.Clamp01(flareSinkRate / Mathf.Max(Speed, 1f))) * Mathf.Rad2Deg;
        if (noseDown > flareAngle) noseDown = Mathf.MoveTowards(noseDown, flareAngle, 18f * dt);
        bank = Mathf.MoveTowards(bank, 0f, 45f * dt);

        rotation = Quaternion.Euler(noseDown, euler.y, bank);
    }

    /// <summary>With the gear down, the wheels reach a landing surface before the airframe does.</summary>
    void CheckTouchdown(Quaternion rotation)
    {
        if (GearPosition < 0.95f) return;
        // Must actually be descending, so skimming low over the deck after takeoff doesn't re-land.
        float sink = -VerticalSpeed;
        if (sink < 0.5f) return;

        if (!Physics.Raycast(rb.position, Vector3.down, out RaycastHit hit, gearHeight + 0.4f, ~0, QueryTriggerInteraction.Ignore)) return;
        LandingSurface surface = hit.collider.GetComponentInParent<LandingSurface>();
        if (surface == null) return;

        Vector3 euler = rotation.eulerAngles;
        float noseUp = -Mathf.DeltaAngle(0f, euler.x);
        float bank = Mathf.Abs(Mathf.DeltaAngle(0f, euler.z));
        bool safe = Speed <= maxLandingSpeed && sink <= maxSinkRate && bank <= maxLandingBank
                    && noseUp <= maxLandingNoseUp && noseUp >= -maxLandingNoseDown;
        if (!safe)
        {
            Crash();
            return;
        }

        IsGrounded = true;
        Surface = surface;
        arresting = surface.arrested;
        groundYaw = euler.y;
        groundPitch = Mathf.Max(0f, noseUp);
        HighG = false;
        IsStalling = false;
        TouchedDown?.Invoke(sink);
    }

    void GroundStep(float dt)
    {
        float throttle = accelerateInput - brakeInput;
        bool catapult = Surface != null && Surface.catapult;

        if (arresting)
        {
            Speed -= arrestDeceleration * dt;
            if (Speed <= 0f) arresting = false;
        }
        else if (throttle > 0.05f) Speed += (catapult ? catapultAcceleration : groundAcceleration) * throttle * dt;
        else if (throttle < -0.05f) Speed += groundBrake * throttle * dt;
        else Speed -= rollingFriction * dt;
        Speed = Mathf.Clamp(Speed, 0f, maxSpeed);

        // Nose-wheel steering with roll or yaw; none while stationary.
        float steer = Mathf.Clamp(smoothInput.y - smoothInput.z, -1f, 1f);
        groundYaw += steer * groundSteerRate * Mathf.Clamp01(Speed / 15f) * dt;

        bool pulling = smoothInput.x < -0.2f;
        float wantedPitch = pulling && Speed > takeoffSpeed * 0.85f ? 8f : 0f;
        groundPitch = Mathf.MoveTowards(groundPitch, wantedPitch, 20f * dt);

        Quaternion rotation = Quaternion.Euler(-groundPitch, groundYaw, 0f);
        Vector3 forward = Quaternion.Euler(0f, groundYaw, 0f) * Vector3.forward;

        Afterburner = Mathf.MoveTowards(Afterburner, throttle > 0.6f ? 1f : 0f, dt * 4f);
        VerticalSpeed = 0f;

        Vector3 position = rb.position;
        bool onSurface = Physics.Raycast(position, Vector3.down, out RaycastHit hit, gearHeight + 6f, ~0, QueryTriggerInteraction.Ignore)
                         && hit.collider.GetComponentInParent<LandingSurface>() != null;

        if (!onSurface || (pulling && Speed >= takeoffSpeed))
        {
            // Rotated off the deck, or rolled off the end of it.
            IsGrounded = false;
            arresting = false;
            speedFloor = Speed;
            if (onSurface) rotation = Quaternion.Euler(-10f, groundYaw, 0f);
            rb.MoveRotation(rotation);
            rb.angularVelocity = Vector3.zero;
            rb.linearVelocity = rotation * Vector3.forward * Speed;
            if (onSurface) TookOff?.Invoke();
            return;
        }

        Surface = hit.collider.GetComponentInParent<LandingSurface>();
        position.y = hit.point.y + gearHeight;
        rb.position = position;
        rb.MoveRotation(rotation);
        rb.angularVelocity = Vector3.zero;
        rb.linearVelocity = forward * Speed;
    }

    void OnCollisionEnter(Collision collision)
    {
        // Flying into anything is fatal.
        Crash();
    }

    void Crash()
    {
        // Without a Target (e.g. a test scene) just reset.
        if (TryGetComponent(out Target self)) self.Crash();
        else Respawn();
    }

    public void Respawn()
    {
        rb.position = startPosition;
        rb.rotation = startRotation;
        transform.SetPositionAndRotation(startPosition, startRotation);
        smoothInput = Vector3.zero;
        IsGrounded = false;
        Speed = cruiseSpeed;
        speedFloor = minSpeed;
        rb.angularVelocity = Vector3.zero;
        rb.linearVelocity = transform.forward * Speed;
    }
}
