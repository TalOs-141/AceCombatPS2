using UnityEngine;

/// <summary>
/// Third-person chase camera that trails the plane and rolls with it,
/// lagging slightly so turns are readable. While the player holds the
/// target-view button it instead swings round to keep the plane in the
/// foreground and the current target centred.
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

    [Header("Speed FOV")]
    public float minFov = 55f;
    public float maxFov = 75f;

    Camera cam;
    WeaponSystem weapons;

    void Awake()
    {
        cam = GetComponent<Camera>();
    }

    void Start()
    {
        if (target == null) return;
        weapons = target.GetComponent<WeaponSystem>();
        transform.position = target.transform.TransformPoint(offset);
        transform.rotation = target.transform.rotation;
    }

    void LateUpdate()
    {
        if (target == null) return;
        Transform t = target.transform;
        float dt = Time.deltaTime;

        Vector3 desiredPosition;
        Vector3 lookPoint;
        Vector3 up;
        float sharpness;

        Target locked = weapons != null && weapons.TargetViewHeld ? weapons.CurrentTarget : null;
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

        float speedT = Mathf.InverseLerp(target.minSpeed, target.maxSpeed, target.Speed);
        cam.fieldOfView = Mathf.Lerp(minFov, maxFov, speedT);
    }
}
