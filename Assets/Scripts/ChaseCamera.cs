using UnityEngine;

/// <summary>
/// The player's camera. Chase view trails the plane and rolls with it, lagging
/// slightly so turns are readable; cockpit view sits at the pilot's eye.
/// In either, holding the target-view button turns to the current target and
/// the look stick looks around.
/// </summary>
[RequireComponent(typeof(Camera))]
public class ChaseCamera : MonoBehaviour
{
    public PlaneController target;
    public Vector3 offset = new Vector3(0f, 3.5f, -16f);
    [Tooltip("Metres ahead of the plane the camera looks at.")]
    public float lookAhead = 40f;
    public float positionSharpness = 12f;
    public float rotationSharpness = 5f;

    [Header("Target view")]
    [Tooltip("How far behind the plane (along the line to the target) the camera sits.")]
    public float targetViewDistance = 20f;
    public float targetViewHeight = 4.5f;
    public float targetViewRotationSharpness = 8f;

    [Header("Free look (right stick)")]
    [Tooltip("Degrees the camera swings sideways at full stick; 180 looks straight back.")]
    public float lookYawRange = 170f;
    public float lookPitchRange = 55f;
    public float lookSharpness = 10f;

    [Header("Cockpit view")]
    [Tooltip("Pilot's eye position in the plane's local space.")]
    public Vector3 cockpitOffset = new Vector3(0f, 1.05f, 3.1f);
    public float cockpitLookYawRange = 130f;
    public float cockpitLookPitchRange = 60f;
    [Tooltip("Hidden in cockpit view so the pilot isn't looking through their own airframe.")]
    public Renderer[] hideInCockpit;

    [Header("Speed FOV")]
    public float minFov = 55f;
    public float maxFov = 75f;

    /// <summary>Kept across mission restarts so the chosen view sticks.</summary>
    public static bool CockpitView;

    static float shakeAmplitude;
    static float shakeDuration = 1f;
    static float shakeUntil;

    Camera cam;
    WeaponSystem weapons;
    Vector2 look;
    bool hiddenForCockpit;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics()
    {
        CockpitView = false;
        shakeAmplitude = 0f;
        shakeUntil = 0f;
        shakeDuration = 1f;
    }

    /// <summary>Jolt the camera; fades out over the duration. Amplitude is in metres.</summary>
    public static void Shake(float amplitude, float duration)
    {
        shakeAmplitude = amplitude;
        shakeDuration = Mathf.Max(duration, 0.01f);
        shakeUntil = Time.time + duration;
    }

    void Awake()
    {
        cam = GetComponent<Camera>();
    }

    void Start()
    {
        if (target == null) return;
        weapons = target.GetComponent<WeaponSystem>();
        transform.position = target.transform.TransformPoint(CockpitView ? cockpitOffset : offset);
        transform.rotation = target.transform.rotation;
    }

    void LateUpdate()
    {
        if (target == null) return;
        Transform t = target.transform;
        float dt = Time.deltaTime;

        FlightInput.State input = FlightInput.Current;
        if (input.viewPressed && Time.timeScale > 0f) CockpitView = !CockpitView;
        look = Vector2.Lerp(look, input.look, 1f - Mathf.Exp(-lookSharpness * dt));

        // A dead plane's cockpit is no place to be: fall back to the outside view.
        bool cockpit = CockpitView && target.enabled;
        SetCockpitHidden(cockpit);

        Target locked = weapons != null && weapons.TargetViewHeld ? weapons.CurrentTarget : null;

        if (cockpit) CockpitUpdate(t, locked, dt);
        else ChaseUpdate(t, locked, dt);

        if (Time.time < shakeUntil && dt > 0f)
        {
            float fade = (shakeUntil - Time.time) / shakeDuration;
            // Much less travel in the cockpit, where the camera is close to everything.
            transform.position += Random.insideUnitSphere * (shakeAmplitude * fade * (cockpit ? 0.08f : 1f));
        }

        float speedT = Mathf.InverseLerp(target.minSpeed, target.maxSpeed, target.Speed);
        cam.fieldOfView = Mathf.Lerp(minFov, maxFov, speedT);
    }

    void CockpitUpdate(Transform t, Target locked, float dt)
    {
        // Rigidly attached: any lag here reads as the pilot's head sliding around.
        transform.position = t.TransformPoint(cockpitOffset);

        Quaternion desired = locked != null
            ? Quaternion.LookRotation(locked.transform.position - transform.position, t.up)
            : t.rotation * Quaternion.Euler(-look.y * cockpitLookPitchRange, look.x * cockpitLookYawRange, 0f);

        // Only the head turn is smoothed; with no look input this stays locked to the airframe.
        bool turningHead = locked != null || look.sqrMagnitude > 0.0004f;
        transform.rotation = turningHead
            ? Quaternion.Slerp(transform.rotation, desired, 1f - Mathf.Exp(-targetViewRotationSharpness * dt))
            : desired;
    }

    void ChaseUpdate(Transform t, Target locked, float dt)
    {
        Vector3 desiredPosition;
        Vector3 lookPoint;
        Vector3 up;
        float sharpness;

        if (locked != null)
        {
            // Sit on the far side of the plane from the target so both stay in frame.
            // World up keeps the horizon level, so the plane's own attitude is easy to read.
            Vector3 toTarget = (locked.transform.position - t.position).normalized;
            desiredPosition = t.position - toTarget * targetViewDistance + Vector3.up * targetViewHeight;
            lookPoint = locked.transform.position;
            up = Vector3.up;
            sharpness = targetViewRotationSharpness;
        }
        else if (look.sqrMagnitude > 0.01f)
        {
            // Orbit around the plane, looking back at it.
            Quaternion orbit = Quaternion.Euler(-look.y * lookPitchRange, look.x * lookYawRange, 0f);
            desiredPosition = t.position + t.rotation * orbit * offset;
            lookPoint = t.position + t.up * 1.5f;
            up = t.up;
            sharpness = lookSharpness;
        }
        else
        {
            desiredPosition = t.TransformPoint(offset);
            lookPoint = t.position + t.forward * lookAhead;
            up = t.up;
            sharpness = rotationSharpness;
        }

        transform.position = Vector3.Lerp(transform.position, desiredPosition, 1f - Mathf.Exp(-positionSharpness * dt));

        Quaternion desiredRotation = Quaternion.LookRotation(lookPoint - transform.position, up);
        transform.rotation = Quaternion.Slerp(transform.rotation, desiredRotation, 1f - Mathf.Exp(-sharpness * dt));
    }

    void SetCockpitHidden(bool hidden)
    {
        if (hidden == hiddenForCockpit || hideInCockpit == null) return;
        hiddenForCockpit = hidden;
        foreach (Renderer r in hideInCockpit)
            if (r != null) r.shadowCastingMode = hidden
                ? UnityEngine.Rendering.ShadowCastingMode.ShadowsOnly
                : UnityEngine.Rendering.ShadowCastingMode.On;
    }
}
