using System;
using System.Collections.Generic;
using UnityEngine;

public enum Team
{
    Enemy,
    Player
}

/// <summary>
/// Anything that can be locked onto, damaged and destroyed: enemies and the player.
/// </summary>
public class Target : MonoBehaviour
{
    public static readonly List<Target> All = new List<Target>();
    public static event Action<Target> AnyDestroyed;

    public Team team = Team.Enemy;
    public string displayName = "MIG-29";
    [Tooltip("Ground targets are drawn differently on the radar.")]
    public bool isGround;
    public float maxHealth = 100f;
    public GameObject explosionPrefab;
    [Tooltip("Off for the player, whose death is handled by GameFlow instead.")]
    public bool destroyOnDeath = true;

    public event Action<Target> Died;

    public float Health { get; private set; }
    public bool IsDead { get; private set; }
    /// <summary>True if this died by flying into something rather than being shot.</summary>
    public bool Crashed { get; private set; }
    public Vector3 Velocity { get; private set; }

    Vector3 lastPosition;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics()
    {
        All.Clear();
        AnyDestroyed = null;
    }

    public static int CountAlive(Team team)
    {
        int count = 0;
        foreach (Target t in All)
            if (t.team == team && !t.IsDead) count++;
        return count;
    }

    public static Target FirstAlive(Team team)
    {
        foreach (Target t in All)
            if (t.team == team && !t.IsDead) return t;
        return null;
    }

    void Awake()
    {
        Health = maxHealth;
        lastPosition = transform.position;
    }

    void OnEnable() => All.Add(this);
    void OnDisable() => All.Remove(this);

    void Update()
    {
        if (Time.deltaTime <= 0f) return;
        Velocity = (transform.position - lastPosition) / Time.deltaTime;
        lastPosition = transform.position;
    }

    public void Damage(float amount)
    {
        if (IsDead) return;
        Health = Mathf.Max(0f, Health - amount);
        if (Health <= 0f) Die();
    }

    /// <summary>Instant death from a collision.</summary>
    public void Crash()
    {
        if (IsDead) return;
        Crashed = true;
        Damage(float.MaxValue);
    }

    void Die()
    {
        IsDead = true;
        if (explosionPrefab != null) Instantiate(explosionPrefab, transform.position, Quaternion.identity);
        Died?.Invoke(this);
        AnyDestroyed?.Invoke(this);
        if (destroyOnDeath) Destroy(gameObject);
    }
}
