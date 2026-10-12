using UnityEngine;

/// <summary>
/// Enemy fighter AI. Patrols random waypoints, turns in on the player when
/// they come within detection range and fires missiles once it holds its nose
/// on them, and breaks hard when a missile is closing on it.
/// </summary>
[RequireComponent(typeof(Target))]
public class EnemyPlane : MonoBehaviour
{
    [Header("Flight")]
    public float speed = 160f;
    public float patrolTurnRate = 30f;
    public float attackTurnRate = 40f;
    public float evadeTurnRate = 55f;
    public float maxBank = 70f;
    [Tooltip("Below this height above the ground the plane refuses to descend.")]
    public float groundAvoidAltitude = 300f;
    [Tooltip("Climbs if terrain is closer than this straight ahead (m).")]
    public float terrainLookAhead = 1500f;
    [Tooltip("Visual child that gets rolled; the root itself stays unbanked.")]
    public Transform model;

    [Header("Patrol area")]
    public float areaRadius = 3500f;
    public float minAltitude = 500f;
    public float maxAltitude = 1500f;
    public float waypointReachedDistance = 400f;

    [Header("Attack")]
    public float detectRange = 6000f;
    [Tooltip("Closer than this it turns away to avoid ramming the player.")]
    public float breakOffDistance = 350f;
    public float breakOffTime = 5f;
    public Missile missilePrefab;
    public Transform hardpoint;
    public float fireRange = 2500f;
    public float fireCone = 12f;
    public float lockTime = 2f;
    public float fireCooldown = 12f;

    [Header("Evade")]
    public float evadeMissileDistance = 1800f;

    /// <summary>True while this plane is building a missile lock on the player.</summary>
    public bool IsLockingPlayer => lockTimer > 0f;

    Target self;
    Target player;
    Vector3 waypoint;
    float bank;
    float lockTimer;
    float nextFireTime;
    float breakOffUntil;

    // Shared across all enemies so the player never faces a simultaneous volley.
    const float SharedFireSpacing = 8f;
    static float nextSharedFireTime;

    /// <summary>
    /// Asks permission to launch a missile at the player. Returns false if
    /// another enemy fired too recently; on true the shared timer restarts.
    /// </summary>
    public static bool TryClaimShot()
    {
        if (Time.time < nextSharedFireTime) return false;
        nextSharedFireTime = Time.time + SharedFireSpacing;
        return true;
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics() => nextSharedFireTime = 0f;

    void Start()
    {
        self = GetComponent<Target>();
        PickWaypoint();
        nextFireTime = Time.time + Random.Range(10f, 18f);
    }

    void Update()
    {
        float dt = Time.deltaTime;
        if (dt <= 0f) return;

        if (player == null || player.IsDead) player = Target.FirstAlive(Team.Player);

        Vector3 desired;
        float turnRate;
        Missile threat = IncomingMissile();

        if (threat != null)
        {
            // Break across the missile's path rather than running straight from it.
            Vector3 approach = transform.position - threat.transform.position;
            Vector3 side = Vector3.Cross(approach, Vector3.up).normalized;
            if (Vector3.Dot(side, transform.forward) < 0f) side = -side;
            desired = side;
            turnRate = evadeTurnRate;
            lockTimer = 0f;
        }
        else if (player != null && Time.time >= breakOffUntil
                 && (player.transform.position - transform.position).magnitude < detectRange)
        {
            Vector3 toPlayer = player.transform.position - transform.position;
            if (toPlayer.magnitude < breakOffDistance)
            {
                breakOffUntil = Time.time + breakOffTime;
                PickWaypoint();
            }
            desired = toPlayer;
            turnRate = attackTurnRate;
            UpdateFiring(toPlayer, dt);
        }
        else
        {
            Vector3 toWaypoint = waypoint - transform.position;
            if (toWaypoint.magnitude < waypointReachedDistance)
            {
                PickWaypoint();
                toWaypoint = waypoint - transform.position;
            }
            desired = toWaypoint;
            turnRate = patrolTurnRate;
            lockTimer = 0f;
        }

        desired.Normalize();
        desired = AvoidTerrain(desired);

        Quaternion current = transform.rotation;
        Quaternion next = Quaternion.RotateTowards(current, Quaternion.LookRotation(desired), turnRate * dt);

        float yawRate = Mathf.DeltaAngle(current.eulerAngles.y, next.eulerAngles.y) / dt;
        float targetBank = -Mathf.Clamp(yawRate / turnRate, -1f, 1f) * maxBank;
        bank = Mathf.Lerp(bank, targetBank, 1f - Mathf.Exp(-2f * dt));

        transform.rotation = next;
        transform.position += transform.forward * speed * dt;
        if (model != null) model.localRotation = Quaternion.Euler(0f, 0f, bank);
    }

    /// <summary>Bends the wanted direction upward when the ground is close below or ahead.</summary>
    Vector3 AvoidTerrain(Vector3 desired)
    {
        Vector3 position = transform.position;

        bool lowBelow = Physics.Raycast(position, Vector3.down, out RaycastHit below, groundAvoidAltitude, ~0, QueryTriggerInteraction.Ignore)
                        && IsGround(below.collider);
        bool wallAhead = Physics.Raycast(position, transform.forward, out RaycastHit ahead, terrainLookAhead, ~0, QueryTriggerInteraction.Ignore)
                         && IsGround(ahead.collider);

        if (wallAhead) return new Vector3(desired.x, 0.8f, desired.z).normalized;
        if (lowBelow && desired.y < 0.3f) return new Vector3(desired.x, 0.3f, desired.z).normalized;
        return desired;
    }

    /// <summary>Terrain, sea, buildings, the carrier: anything solid that isn't another combatant.</summary>
    static bool IsGround(Collider collider)
    {
        return collider.GetComponentInParent<Target>() == null;
    }

    void UpdateFiring(Vector3 toPlayer, float dt)
    {
        bool inEnvelope = toPlayer.magnitude <= fireRange && Vector3.Angle(transform.forward, toPlayer) <= fireCone;
        lockTimer = inEnvelope ? lockTimer + dt : 0f;

        if (missilePrefab == null || lockTimer < lockTime || Time.time < nextFireTime) return;
        if (!TryClaimShot()) return;

        Transform rail = hardpoint != null ? hardpoint : transform;
        Missile missile = Instantiate(missilePrefab, rail.position, transform.rotation);
        missile.Launch(player, transform, speed);
        nextFireTime = Time.time + fireCooldown;
        lockTimer = 0f;
    }

    Missile IncomingMissile()
    {
        foreach (Missile m in Missile.Active)
        {
            if (m.Target == self
                && (m.transform.position - transform.position).sqrMagnitude < evadeMissileDistance * evadeMissileDistance)
                return m;
        }
        return null;
    }

    void PickWaypoint()
    {
        Vector2 flat = Random.insideUnitCircle * areaRadius;
        waypoint = new Vector3(flat.x, Random.Range(minAltitude, maxAltitude), flat.y);
    }
}
