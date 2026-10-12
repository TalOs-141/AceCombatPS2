using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Marks a collider (and its children) as somewhere the plane can roll, take
/// off from and land on: a carrier deck or a runway.
/// </summary>
public class LandingSurface : MonoBehaviour
{
    public static readonly List<LandingSurface> All = new List<LandingSurface>();

    public string displayName = "RUNWAY";
    [Tooltip("Arrestor wires: a landing plane is stopped within a few dozen metres.")]
    public bool arrested;
    [Tooltip("Catapult: much harder acceleration on the takeoff roll.")]
    public bool catapult;
    [Tooltip("Where to aim to touch down. Its forward axis is the landing direction.")]
    public Transform touchdown;

    public Vector3 TouchdownPoint => touchdown != null ? touchdown.position : transform.position;
    public Quaternion LandingRotation => touchdown != null ? touchdown.rotation : transform.rotation;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics() => All.Clear();

    void OnEnable() => All.Add(this);
    void OnDisable() => All.Remove(this);

    public static LandingSurface Nearest(Vector3 position)
    {
        LandingSurface best = null;
        float bestDistance = float.MaxValue;
        foreach (LandingSurface s in All)
        {
            float distance = (s.TouchdownPoint - position).sqrMagnitude;
            if (distance < bestDistance)
            {
                bestDistance = distance;
                best = s;
            }
        }
        return best;
    }
}
