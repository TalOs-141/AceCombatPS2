using UnityEngine;

/// <summary>
/// Third-person chase camera that trails the plane and rolls with it,
/// lagging slightly so turns are readable.
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

    [Header("Speed FOV")]
    public float minFov = 55f;
    public float maxFov = 75f;

    Camera cam;

    void Awake()
    {
        cam = GetComponent<Camera>();
    }

    void Start()
    {
        if (target == null) return;
        transform.position = target.transform.TransformPoint(offset);
        transform.rotation = target.transform.rotation;
    }

    void LateUpdate()
    {
        if (target == null) return;
        Transform t = target.transform;
        float dt = Time.deltaTime;

        Vector3 desiredPosition = t.TransformPoint(offset);
        transform.position = Vector3.Lerp(transform.position, desiredPosition, 1f - Mathf.Exp(-positionSharpness * dt));

        Vector3 lookPoint = t.position + t.forward * lookAhead;
        Quaternion desiredRotation = Quaternion.LookRotation(lookPoint - transform.position, t.up);
        transform.rotation = Quaternion.Slerp(transform.rotation, desiredRotation, 1f - Mathf.Exp(-rotationSharpness * dt));

        float speedT = Mathf.InverseLerp(target.minSpeed, target.maxSpeed, target.Speed);
        cam.fieldOfView = Mathf.Lerp(minFov, maxFov, speedT);
    }
}
