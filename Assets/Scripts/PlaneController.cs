using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Arcade flight model in the style of PS2-era Ace Combat: the plane always
/// flies along its nose, the player steers with pitch/roll/yaw and trims speed
/// with accelerate/brake. No real aerodynamics.
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

    [Header("Stall")]
    [Tooltip("Below this speed the nose is dragged toward the ground.")]
    public float stallSpeed = 90f;
    public float stallPitchRate = 25f;

    public float Speed { get; private set; }
    public float Altitude => transform.position.y;

    Rigidbody rb;
    Vector3 startPosition;
    Quaternion startRotation;

    // x = pitch, y = yaw, z = roll, each -1..1
    Vector3 rawInput;
    Vector3 smoothInput;
    float throttleInput;

    void Awake()
    {
        rb = GetComponent<Rigidbody>();
        rb.useGravity = false;
        rb.interpolation = RigidbodyInterpolation.Interpolate;
        rb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;

        startPosition = transform.position;
        startRotation = transform.rotation;
        Speed = cruiseSpeed;
    }

    void Update()
    {
        float pitch = 0f, yaw = 0f, roll = 0f, throttle = 0f;

        Keyboard kb = Keyboard.current;
        if (kb != null)
        {
            // Flight-stick convention: pull back (S / Down) raises the nose.
            if (kb.sKey.isPressed || kb.downArrowKey.isPressed) pitch -= 1f;
            if (kb.wKey.isPressed || kb.upArrowKey.isPressed) pitch += 1f;
            if (kb.aKey.isPressed || kb.leftArrowKey.isPressed) roll += 1f;
            if (kb.dKey.isPressed || kb.rightArrowKey.isPressed) roll -= 1f;
            if (kb.qKey.isPressed) yaw -= 1f;
            if (kb.eKey.isPressed) yaw += 1f;
            if (kb.leftShiftKey.isPressed) throttle += 1f;
            if (kb.leftCtrlKey.isPressed) throttle -= 1f;
        }

        Gamepad pad = Gamepad.current;
        if (pad != null)
        {
            Vector2 stick = pad.leftStick.ReadValue();
            pitch += stick.y;
            roll -= stick.x;
            yaw += pad.rightShoulder.ReadValue() - pad.leftShoulder.ReadValue();
            throttle += pad.rightTrigger.ReadValue() - pad.leftTrigger.ReadValue();
        }

        rawInput = new Vector3(Mathf.Clamp(pitch, -1f, 1f), Mathf.Clamp(yaw, -1f, 1f), Mathf.Clamp(roll, -1f, 1f));
        throttleInput = Mathf.Clamp(throttle, -1f, 1f);
    }

    void FixedUpdate()
    {
        float dt = Time.fixedDeltaTime;
        smoothInput = Vector3.Lerp(smoothInput, rawInput, 1f - Mathf.Exp(-controlResponse * dt));

        Vector3 turn = new Vector3(smoothInput.x * pitchRate, smoothInput.y * yawRate, smoothInput.z * rollRate) * dt;
        Quaternion rotation = rb.rotation * Quaternion.Euler(turn);

        if (Speed < stallSpeed)
        {
            // Rotate the nose toward straight down, harder the slower we are.
            float severity = Mathf.InverseLerp(stallSpeed, minSpeed, Speed);
            Quaternion noseDown = Quaternion.LookRotation(Vector3.down, rotation * Vector3.forward);
            rotation = Quaternion.RotateTowards(rotation, noseDown, stallPitchRate * severity * dt);
        }

        if (throttleInput > 0f) Speed += acceleration * throttleInput * dt;
        else if (throttleInput < 0f) Speed += braking * throttleInput * dt;
        else Speed = Mathf.MoveTowards(Speed, cruiseSpeed, cruiseReturn * dt);

        Vector3 forward = rotation * Vector3.forward;
        Speed -= forward.y * gravityEffect * dt;
        Speed = Mathf.Clamp(Speed, minSpeed, maxSpeed);

        rb.MoveRotation(rotation);
        rb.angularVelocity = Vector3.zero;
        rb.linearVelocity = forward * Speed;
    }

    void OnCollisionEnter(Collision collision)
    {
        // Placeholder for a crash: put the plane back where it started.
        Respawn();
    }

    public void Respawn()
    {
        rb.position = startPosition;
        rb.rotation = startRotation;
        transform.SetPositionAndRotation(startPosition, startRotation);
        smoothInput = Vector3.zero;
        Speed = cruiseSpeed;
        rb.angularVelocity = Vector3.zero;
        rb.linearVelocity = transform.forward * Speed;
    }
}
