using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Player weapons: target selection, missile lock-on, missiles and the gun.
/// </summary>
[RequireComponent(typeof(PlaneController))]
public class WeaponSystem : MonoBehaviour
{
    [Header("Lock-on")]
    public float lockRange = 4000f;
    [Tooltip("Target must be within this angle of the nose to lock (deg).")]
    public float lockCone = 20f;
    public float lockTime = 1.2f;
    [Tooltip("Lock time right after pressing the switch-target button.")]
    public float switchLockTime = 0.4f;

    [Tooltip("Releasing the switch button within this time switches target; holding longer gives the target view camera.")]
    public float switchTapTime = 0.25f;

    [Header("Target priority")]
    [Tooltip("Being this many degrees off the centre of the view costs as much as priorityDistance metres of range.")]
    public float priorityAngle = 15f;
    [Tooltip("Being this many metres away costs as much as priorityAngle degrees off-centre.")]
    public float priorityDistance = 2000f;
    [Tooltip("Targets further than this from the centre of the view count as off screen and rank last (deg).")]
    public float priorityViewAngle = 40f;

    [Header("Missiles")]
    public Missile missilePrefab;
    [Tooltip("One launch rail per entry; each reloads separately.")]
    public Transform[] hardpoints;
    public int missileAmmo = 40;
    public float missileReload = 4f;

    [Header("Gun")]
    public Bullet bulletPrefab;
    public Transform gunMuzzle;
    public float gunFireRate = 20f;
    public float gunSpread = 0.25f;

    public Target CurrentTarget { get; private set; }
    /// <summary>True while the switch-target button is being held down for the target view camera.</summary>
    public bool TargetViewHeld { get; private set; }
    /// <summary>0 = no lock, 1 = locked.</summary>
    public float LockProgress { get; private set; }
    public bool IsLocked => LockProgress >= 1f;
    public int MissileAmmo => missileAmmo;

    PlaneController plane;
    float[] railReadyTime;
    float nextGunTime;
    float activeLockTime;
    float switchHeldTime;

    void Awake()
    {
        plane = GetComponent<PlaneController>();
        railReadyTime = new float[hardpoints != null ? hardpoints.Length : 0];
    }

    /// <summary>0 = just fired, 1 = ready.</summary>
    public float RailReadiness(int rail)
    {
        if (rail < 0 || rail >= railReadyTime.Length) return 0f;
        return 1f - Mathf.Clamp01((railReadyTime[rail] - Time.time) / missileReload);
    }

    public int RailCount => railReadyTime.Length;

    void Update()
    {
        bool switchHeld = false, missilePressed = false, gunHeld = false;

        Keyboard kb = Keyboard.current;
        if (kb != null)
        {
            switchHeld |= kb.tabKey.isPressed;
            missilePressed |= kb.spaceKey.wasPressedThisFrame;
            gunHeld |= kb.fKey.isPressed;
        }
        Mouse mouse = Mouse.current;
        if (mouse != null) gunHeld |= mouse.leftButton.isPressed;

        Gamepad pad = Gamepad.current;
        if (pad != null)
        {
            switchHeld |= pad.buttonNorth.isPressed;
            missilePressed |= pad.buttonEast.wasPressedThisFrame;
            gunHeld |= pad.buttonSouth.isPressed;
        }

        // One button, two jobs: a quick tap switches target, a hold swings the camera onto it.
        bool switchPressed = false;
        if (switchHeld)
        {
            switchHeldTime += Time.deltaTime;
        }
        else
        {
            switchPressed = switchHeldTime > 0f && switchHeldTime < switchTapTime;
            switchHeldTime = 0f;
        }
        TargetViewHeld = switchHeld && switchHeldTime >= switchTapTime;

        if (CurrentTarget == null || CurrentTarget.IsDead) SelectTarget(BestTarget(), lockTime);
        else if (switchPressed) SelectTarget(NextTarget(), switchLockTime);

        UpdateLock();

        if (missilePressed) FireMissile();
        if (gunHeld && Time.time >= nextGunTime) FireGun();
    }

    void SelectTarget(Target target, float timeToLock)
    {
        if (target == CurrentTarget) return;
        CurrentTarget = target;
        LockProgress = 0f;
        activeLockTime = timeToLock;
    }

    /// <summary>
    /// How much the player probably wants this target: lower is better.
    /// Blends how far it is from the centre of the camera's view with how far
    /// away it is, and pushes anything outside the view to the back of the queue.
    /// </summary>
    float Priority(Target t)
    {
        Transform view = Camera.main != null ? Camera.main.transform : transform;
        Vector3 toTarget = t.transform.position - view.position;
        float angle = Vector3.Angle(view.forward, toTarget);

        float score = angle / priorityAngle + toTarget.magnitude / priorityDistance;
        if (angle > priorityViewAngle) score += 10f;
        return score;
    }

    Target BestTarget()
    {
        Target best = null;
        float bestScore = float.MaxValue;
        foreach (Target t in Target.All)
        {
            if (t.IsDead || t.team != Team.Enemy) continue;
            float score = Priority(t);
            if (score < bestScore)
            {
                bestScore = score;
                best = t;
            }
        }
        return best;
    }

    Target NextTarget()
    {
        // Step down the priority order: the next target is the one ranked just
        // below the current one, wrapping back to the top-ranked.
        float currentScore = Priority(CurrentTarget);
        Target next = null, best = null;
        float nextScore = float.MaxValue, bestScore = float.MaxValue;

        foreach (Target t in Target.All)
        {
            if (t == CurrentTarget || t.IsDead || t.team != Team.Enemy) continue;
            float score = Priority(t);
            if (score < bestScore)
            {
                bestScore = score;
                best = t;
            }
            if (score > currentScore && score < nextScore)
            {
                nextScore = score;
                next = t;
            }
        }

        if (next != null) return next;
        return best != null ? best : CurrentTarget;
    }

    void UpdateLock()
    {
        if (CurrentTarget == null)
        {
            LockProgress = 0f;
            return;
        }

        Vector3 toTarget = CurrentTarget.transform.position - transform.position;
        bool canLock = toTarget.magnitude <= lockRange && Vector3.Angle(transform.forward, toTarget) <= lockCone;
        if (canLock)
        {
            LockProgress = Mathf.Clamp01(LockProgress + Time.deltaTime / activeLockTime);
        }
        else
        {
            // The quick lock only applies straight after a switch; a lost lock rebuilds at the normal rate.
            LockProgress = 0f;
            activeLockTime = lockTime;
        }
    }

    void FireMissile()
    {
        if (missilePrefab == null || missileAmmo <= 0) return;

        for (int i = 0; i < railReadyTime.Length; i++)
        {
            if (Time.time < railReadyTime[i]) continue;

            Transform rail = hardpoints[i];
            Missile missile = Instantiate(missilePrefab, rail.position, rail.rotation);
            missile.Launch(IsLocked ? CurrentTarget : null, transform, plane.Speed);
            railReadyTime[i] = Time.time + missileReload;
            missileAmmo--;
            return;
        }
    }

    void FireGun()
    {
        if (bulletPrefab == null || gunMuzzle == null) return;
        nextGunTime = Time.time + 1f / gunFireRate;

        Vector2 spread = Random.insideUnitCircle * gunSpread;
        Quaternion rotation = gunMuzzle.rotation * Quaternion.Euler(spread.x, spread.y, 0f);
        Bullet bullet = Instantiate(bulletPrefab, gunMuzzle.position, rotation);
        bullet.Fire(transform);
    }
}
