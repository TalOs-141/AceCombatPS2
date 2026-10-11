using UnityEngine;

/// <summary>
/// Tracks the closest missile homing on the player and sounds a two-tone
/// alarm that pulses faster the nearer it gets. The HUD reads the same data for its on-screen warning.
/// </summary>
[RequireComponent(typeof(Target), typeof(AudioSource))]
public class MissileWarning : MonoBehaviour
{
    [Tooltip("Beeps are slowest at this distance (m) and beyond.")]
    public float farDistance = 2500f;
    [Tooltip("Beeps are fastest at this distance (m) and closer.")]
    public float nearDistance = 250f;
    public float slowBeepInterval = 0.6f;
    public float fastBeepInterval = 0.13f;
    [Tooltip("Pitch of the higher alarm tone (Hz); the lower tone sits a fourth below it.")]
    public float beepFrequency = 620f;
    [Range(0f, 1f)] public float volume = 0.11f;

    /// <summary>Closest missile currently homing on the player, or null.</summary>
    public Missile Closest { get; private set; }
    public float ClosestDistance { get; private set; }
    /// <summary>0 = far away, 1 = about to hit.</summary>
    public float Urgency => Closest == null ? 0f : Mathf.InverseLerp(farDistance, nearDistance, ClosestDistance);
    /// <summary>Seconds between beeps (and HUD flashes) at the current distance.</summary>
    public float BeepInterval => Mathf.Lerp(slowBeepInterval, fastBeepInterval, Urgency);

    Target self;
    AudioSource source;
    AudioClip highTone;
    AudioClip lowTone;
    bool playLowNext;
    float nextBeepTime;

    void Awake()
    {
        self = GetComponent<Target>();
        source = GetComponent<AudioSource>();
        source.playOnAwake = false;
        source.spatialBlend = 0f;
        highTone = BuildTone(beepFrequency);
        lowTone = BuildTone(beepFrequency * 0.75f);
    }

    void Update()
    {
        Closest = null;
        ClosestDistance = float.MaxValue;

        if (!self.IsDead)
        {
            foreach (Missile m in Missile.Active)
            {
                if (m.Target != self) continue;
                float distance = Vector3.Distance(m.transform.position, transform.position);
                if (distance < ClosestDistance)
                {
                    ClosestDistance = distance;
                    Closest = m;
                }
            }
        }

        if (Closest != null && Time.time >= nextBeepTime)
        {
            // Alternating high/low gives the classic warbling alarm.
            source.PlayOneShot(playLowNext ? lowTone : highTone, volume);
            playLowNext = !playLowNext;
            nextBeepTime = Time.time + BeepInterval;
        }
    }

    static AudioClip BuildTone(float frequency)
    {
        const int sampleRate = 44100;
        const float length = 0.11f;
        int count = Mathf.CeilToInt(sampleRate * length);
        var samples = new float[count];
        for (int i = 0; i < count; i++)
        {
            // Short fade in and out so the beep doesn't click.
            float envelope = Mathf.Clamp01(Mathf.Min(i, count - 1 - i) / (sampleRate * 0.005f));
            // A few odd harmonics turn the pure tone into a harsher, buzzier alarm.
            float phase = 2f * Mathf.PI * frequency * i / sampleRate;
            float wave = Mathf.Sin(phase) + Mathf.Sin(3f * phase) / 3f + Mathf.Sin(5f * phase) / 5f;
            samples[i] = wave * 0.7f * envelope;
        }

        AudioClip clip = AudioClip.Create("MissileAlarm", count, 1, sampleRate, false);
        clip.SetData(samples, 0);
        return clip;
    }
}
