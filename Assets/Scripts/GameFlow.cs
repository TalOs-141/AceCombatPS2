using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;

/// <summary>
/// Mission-level rules: what happens when the player dies, and restarting.
/// </summary>
public class GameFlow : MonoBehaviour
{
    public Target player;
    [Tooltip("Seconds after the player dies before the mission restarts.")]
    public float restartDelay = 4f;

    float restartAt = -1f;

    void OnEnable()
    {
        if (player != null) player.Died += OnPlayerDied;
    }

    void OnDisable()
    {
        if (player != null) player.Died -= OnPlayerDied;
    }

    void OnPlayerDied(Target dead)
    {
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

        restartAt = Time.time + restartDelay;
    }

    void Update()
    {
        Keyboard kb = Keyboard.current;
        Gamepad pad = Gamepad.current;
        bool restartPressed = (kb != null && kb.rKey.wasPressedThisFrame) || (pad != null && pad.startButton.wasPressedThisFrame);

        if (restartPressed || (restartAt > 0f && Time.time >= restartAt))
            SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex);
    }
}
