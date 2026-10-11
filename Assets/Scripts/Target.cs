using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Anything the player can select, lock onto and destroy.
/// </summary>
public class Target : MonoBehaviour
{
    public static readonly List<Target> All = new List<Target>();
    public static event Action<Target> AnyDestroyed;

    public string displayName = "MIG-29";
    public float maxHealth = 100f;
    public GameObject explosionPrefab;

    public float Health { get; private set; }
    public bool IsDead { get; private set; }
    public Vector3 Velocity { get; private set; }

    Vector3 lastPosition;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics()
    {
        All.Clear();
        AnyDestroyed = null;
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
        Health -= amount;
        if (Health <= 0f) Die();
    }

    void Die()
    {
        IsDead = true;
        if (explosionPrefab != null) Instantiate(explosionPrefab, transform.position, Quaternion.identity);
        AnyDestroyed?.Invoke(this);
        Destroy(gameObject);
    }
}
