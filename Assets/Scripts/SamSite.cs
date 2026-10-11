using UnityEngine;

/// <summary>
/// Ground missile launcher: fires at the player whenever they are inside its
/// engagement range, sharing the enemies' launch spacing so volleys don't stack.
/// </summary>
[RequireComponent(typeof(Target))]
public class SamSite : MonoBehaviour
{
    public Missile missilePrefab;
    public Transform launchPoint;
    public float range = 3500f;
    [Tooltip("Player must stay in range this long before a launch.")]
    public float lockTime = 3f;
    public float fireCooldown = 14f;
    public float launchSpeed = 120f;

    /// <summary>True while this site is building a missile lock on the player.</summary>
    public bool IsLockingPlayer => lockTimer > 0f;

    Target player;
    float lockTimer;
    float nextFireTime;

    void Start()
    {
        nextFireTime = Time.time + Random.Range(8f, 14f);
    }

    void Update()
    {
        if (player == null || player.IsDead) player = Target.FirstAlive(Team.Player);
        if (player == null)
        {
            lockTimer = 0f;
            return;
        }

        Transform origin = launchPoint != null ? launchPoint : transform;
        Vector3 toPlayer = player.transform.position - origin.position;
        lockTimer = toPlayer.magnitude <= range ? lockTimer + Time.deltaTime : 0f;

        if (missilePrefab == null || lockTimer < lockTime || Time.time < nextFireTime) return;
        if (!EnemyPlane.TryClaimShot()) return;

        Missile missile = Instantiate(missilePrefab, origin.position, Quaternion.LookRotation(toPlayer));
        missile.Launch(player, transform, launchSpeed);
        nextFireTime = Time.time + fireCooldown;
        lockTimer = 0f;
    }
}
