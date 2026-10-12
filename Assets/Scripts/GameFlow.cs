using UnityEngine;
using UnityEngine.SceneManagement;

public enum MissionPhase
{
    Takeoff,
    Combat,
    ReturnToBase,
    Complete,
    Failed
}

/// <summary>
/// Mission-level rules: the takeoff / combat / return / landing sequence,
/// pausing, restarting, free flight, and what happens when the player dies.
/// </summary>
public class GameFlow : MonoBehaviour
{
    public static GameFlow Instance { get; private set; }
    /// <summary>No enemies: just fly. Kept across restarts.</summary>
    public static bool FreeFlight;

    public Target player;
    [Tooltip("Where the mission ends: land here once every target is destroyed.")]
    public LandingSurface homeBase;
    [Tooltip("Seconds after the player dies before the mission restarts.")]
    public float restartDelay = 4f;

    public MissionPhase Phase { get; private set; }
    public bool Paused { get; private set; }
    /// <summary>Sink rate of the most recent touchdown in m/s, or -1 before any landing.</summary>
    public float LastLandingSink { get; private set; } = -1f;
    /// <summary>Time.time until which the "rearmed" notice should show.</summary>
    public float ServicedNoticeUntil { get; private set; }

    PlaneController plane;
    WeaponSystem weapons;
    bool started;
    bool hadEnemies;
    bool serviced = true;
    float restartAt = -1f;

    void Awake()
    {
        Instance = this;
        Time.timeScale = 1f;
        AudioListener.pause = false;
    }

    void Start()
    {
        if (player == null) return;
        plane = player.GetComponent<PlaneController>();
        weapons = player.GetComponent<WeaponSystem>();

        player.Died += OnPlayerDied;
        player.Damaged += OnPlayerDamaged;
        if (plane != null)
        {
            plane.TouchedDown += OnTouchedDown;
            plane.TookOff += OnTookOff;
        }

        if (FreeFlight)
        {
            for (int i = Target.All.Count - 1; i >= 0; i--)
                if (Target.All[i].team == Team.Enemy) Destroy(Target.All[i].gameObject);
        }
    }

    void OnDestroy()
    {
        Rumble.Stop();
        if (Instance == this) Instance = null;
    }

    void OnPlayerDamaged(Target damaged, float amount)
    {
        Rumble.Pulse(0.9f, 0.9f, 0.4f);
        ChaseCamera.Shake(1.5f, 0.45f);
    }

    void OnTouchedDown(float sink)
    {
        LastLandingSink = sink;
        // Harder landings hit harder.
        float force = Mathf.InverseLerp(1f, 12f, sink);
        Rumble.Pulse(0.5f + force * 0.5f, 0.3f + force * 0.5f, 0.5f);
        ChaseCamera.Shake(0.4f + force * 1.6f, 0.5f);
    }

    void OnTookOff()
    {
        Rumble.Pulse(0.4f, 0.2f, 0.3f);
    }

    void OnPlayerDied(Target dead)
    {
        Phase = MissionPhase.Failed;

        // Leave the wreck where it is: stop it flying and hide it behind the explosion.
        foreach (MonoBehaviour behaviour in new MonoBehaviour[] { dead.GetComponent<PlaneController>(), dead.GetComponent<WeaponSystem>() })
            if (behaviour != null) behaviour.enabled = false;

        Rigidbody rb = dead.GetComponent<Rigidbody>();
        if (rb != null)
        {
            rb.linearVelocity = Vector3.zero;
            rb.isKinematic = true;
        }

        foreach (Renderer r in dead.GetComponentsInChildren<Renderer>()) r.enabled = false;

        Rumble.Pulse(1f, 1f, 0.8f);
        ChaseCamera.Shake(3f, 0.8f);
        restartAt = Time.time + restartDelay;
    }

    void Update()
    {
        if (plane == null) return;

        if (!started)
        {
            // Decided here rather than in Start so the plane has worked out whether it is parked.
            started = true;
            Phase = plane.IsGrounded ? MissionPhase.Takeoff : MissionPhase.Combat;
            hadEnemies = !FreeFlight && Target.CountAlive(Team.Enemy) > 0;
        }

        FlightInput.State input = FlightInput.Current;
        bool ended = Phase == MissionPhase.Complete || Phase == MissionPhase.Failed;

        if (input.pausePressed && Phase != MissionPhase.Failed) SetPaused(!Paused);

        // Restarting needs the pause menu (or a finished mission) so it can't be hit by accident mid-fight.
        if (input.restartPressed && (Paused || ended))
        {
            Restart();
            return;
        }
        if (Paused && input.freeFlightPressed)
        {
            FreeFlight = !FreeFlight;
            Restart();
            return;
        }
        if (Paused) return;

        if (restartAt > 0f && Time.time >= restartAt)
        {
            Restart();
            return;
        }

        bool parked = plane.IsGrounded && plane.Speed < 0.5f;
        switch (Phase)
        {
            case MissionPhase.Takeoff:
                if (!plane.IsGrounded) Phase = MissionPhase.Combat;
                break;
            case MissionPhase.Combat:
                if (hadEnemies && Target.CountAlive(Team.Enemy) == 0) Phase = MissionPhase.ReturnToBase;
                break;
            case MissionPhase.ReturnToBase:
                if (parked) Phase = MissionPhase.Complete;
                break;
        }

        // Landing back at base mid-mission rearms and repairs, like a return-line resupply.
        if (!plane.IsGrounded) serviced = false;
        else if (parked && !serviced && Phase == MissionPhase.Combat && plane.Surface == homeBase)
        {
            serviced = true;
            if (weapons != null) weapons.Rearm();
            player.Repair();
            ServicedNoticeUntil = Time.time + 4f;
        }
    }

    void LateUpdate()
    {
        if (Paused) return;
        Rumble.Tick();
    }

    void SetPaused(bool paused)
    {
        Paused = paused;
        Time.timeScale = paused ? 0f : 1f;
        AudioListener.pause = paused;
        if (paused) Rumble.Stop();
    }

    void Restart()
    {
        Time.timeScale = 1f;
        AudioListener.pause = false;
        Rumble.Stop();
        SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex);
    }
}
