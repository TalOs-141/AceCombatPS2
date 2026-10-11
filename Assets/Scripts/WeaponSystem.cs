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
    /// <summary>0 = no lock, 1 = locked.</summary>
    public float LockProgress { get; private set; }
    public bool IsLocked => LockProgress >= 1f;
    public int MissileAmmo => missileAmmo;

    PlaneController plane;
    float[] railReadyTime;
    float nextGunTime;

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
        bool switchPressed = false, missilePressed = false, gunHeld = false;

        Keyboard kb = Keyboard.current;
        if (kb != null)
        {
            switchPressed |= kb.tabKey.wasPressedThisFrame;
            missilePressed |= kb.spaceKey.wasPressedThisFrame;
            gunHeld |= kb.fKey.isPressed;
        }
        Mouse mouse = Mouse.current;
        if (mouse != null) gunHeld |= mouse.leftButton.isPressed;

        Gamepad pad = Gamepad.current;
        if (pad != null)
        {
            switchPressed |= pad.buttonNorth.wasPressedThisFrame;
            missilePressed |= pad.buttonEast.wasPressedThisFrame;
            gunHeld |= pad.buttonSouth.isPressed;
        }

        if (CurrentTarget == null || CurrentTarget.IsDead) SelectTarget(ClosestToNose());
        else if (switchPressed) SelectTarget(NextTarget());

        UpdateLock();

        if (missilePressed) FireMissile();
        if (gunHeld && Time.time >= nextGunTime) FireGun();
    }

    void SelectTarget(Target target)
    {
        if (target == CurrentTarget) return;
        CurrentTarget = target;
        LockProgress = 0f;
    }

    Target ClosestToNose()
    {
        Target best = null;
        float bestAngle = float.MaxValue;
        foreach (Target t in Target.All)
        {
            if (t.IsDead) continue;
            float angle = Vector3.Angle(transform.forward, t.transform.position - transform.position);
            if (angle < bestAngle)
            {
                bestAngle = angle;
                best = t;
            }
        }
        return best;
    }

    Target NextTarget()
    {
        if (Target.All.Count == 0) return null;
        int index = Target.All.IndexOf(CurrentTarget);
        return Target.All[(index + 1) % Target.All.Count];
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
        LockProgress = canLock ? Mathf.Clamp01(LockProgress + Time.deltaTime / lockTime) : 0f;
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
