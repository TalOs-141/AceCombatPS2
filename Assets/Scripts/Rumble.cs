using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Gamepad vibration. Call <see cref="Continuous"/> every frame an effect is
/// ongoing (engine, gun, ground roll) and <see cref="Pulse"/> for one-off
/// jolts (hits, touchdown). GameFlow calls <see cref="Tick"/> once per frame.
/// </summary>
public static class Rumble
{
    static float continuousLow, continuousHigh;
    static float pulseLow, pulseHigh, pulseUntil, pulseDuration;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics()
    {
        continuousLow = continuousHigh = pulseLow = pulseHigh = pulseUntil = 0f;
        pulseDuration = 1f;
    }

    /// <param name="low">Heavy, low-frequency motor, 0..1.</param>
    /// <param name="high">Light, high-frequency motor, 0..1.</param>
    public static void Continuous(float low, float high)
    {
        continuousLow = Mathf.Max(continuousLow, low);
        continuousHigh = Mathf.Max(continuousHigh, high);
    }

    public static void Pulse(float low, float high, float duration)
    {
        pulseLow = low;
        pulseHigh = high;
        pulseDuration = Mathf.Max(duration, 0.01f);
        pulseUntil = Time.unscaledTime + duration;
    }

    public static void Tick()
    {
        Gamepad pad = Gamepad.current;
        if (pad != null)
        {
            // Pulses fade out over their duration.
            float fade = Mathf.Clamp01((pulseUntil - Time.unscaledTime) / pulseDuration);
            pad.SetMotorSpeeds(
                Mathf.Clamp01(Mathf.Max(continuousLow, pulseLow * fade)),
                Mathf.Clamp01(Mathf.Max(continuousHigh, pulseHigh * fade)));
        }
        continuousLow = continuousHigh = 0f;
    }

    public static void Stop()
    {
        continuousLow = continuousHigh = pulseUntil = 0f;
        Gamepad pad = Gamepad.current;
        if (pad != null) pad.ResetHaptics();
    }
}
