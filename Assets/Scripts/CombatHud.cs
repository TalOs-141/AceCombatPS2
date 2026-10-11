using UnityEngine;

/// <summary>
/// Temporary immediate-mode combat HUD: target boxes, lock indicator,
/// off-screen target arrow, gun reticle, radar, weapon and damage status,
/// missile warning and mission messages.
/// </summary>
public class CombatHud : MonoBehaviour
{
    public WeaponSystem weapons;
    public float gunReticleDistance = 900f;
    public float messageDuration = 2.5f;
    [Tooltip("Distance shown at the edge of the radar (m).")]
    public float radarRange = 6000f;

    static readonly Color HudGreen = new Color(0.4f, 1f, 0.4f);
    static readonly Color HudRed = new Color(1f, 0.25f, 0.2f);
    static readonly Color HudAmber = new Color(1f, 0.8f, 0.2f);

    // The HUD is laid out for a 720-pixel-tall screen and scaled to fit.
    const float ReferenceHeight = 720f;
    static float scale = 1f;
    static float ViewWidth => Screen.width / scale;
    static float ViewHeight => Screen.height / scale;

    // NonSerialized: otherwise Unity restores these as blank default styles after a script reload.
    [System.NonSerialized] GUIStyle label;
    [System.NonSerialized] GUIStyle bigLabel;
    Target player;
    MissileWarning missileWarning;
    GroundProximityWarning groundWarning;
    Texture2D radarDisc;
    string message;
    float messageUntil;
    bool hadTargets;

    void Awake()
    {
        if (weapons == null) return;
        player = weapons.GetComponent<Target>();
        missileWarning = weapons.GetComponent<MissileWarning>();
        groundWarning = weapons.GetComponent<GroundProximityWarning>();
    }

    void OnEnable() => Target.AnyDestroyed += OnTargetDestroyed;
    void OnDisable() => Target.AnyDestroyed -= OnTargetDestroyed;

    void OnTargetDestroyed(Target target)
    {
        if (target.team != Team.Player) message = "DESTROYED";
        else message = target.Crashed ? "CRASHED" : "SHOT DOWN";
        messageUntil = Time.time + (target.team == Team.Player ? 60f : messageDuration);
    }

    void OnGUI()
    {
        Camera cam = Camera.main;
        if (cam == null || weapons == null) return;

        if (label == null)
        {
            label = new GUIStyle(GUI.skin.label) { fontSize = 18, fontStyle = FontStyle.Bold, alignment = TextAnchor.UpperCenter };
            bigLabel = new GUIStyle(label) { fontSize = 34 };
        }

        if (Target.CountAlive(Team.Enemy) > 0) hadTargets = true;

        scale = Screen.height / ReferenceHeight;
        label.fontSize = Mathf.RoundToInt(18f * scale);
        bigLabel.fontSize = Mathf.RoundToInt(34f * scale);

        DrawTargets(cam);
        DrawGunReticle(cam);
        DrawWeaponStatus();
        DrawRadar();
        DrawDamage();
        DrawWarnings(cam);
        DrawPullUp();
        DrawMessages();
        GUI.color = Color.white;
    }

    void DrawTargets(Camera cam)
    {
        Target current = weapons.CurrentTarget;

        foreach (Target t in Target.All)
        {
            if (t == current || t.team != Team.Enemy || t.IsDead) continue;
            if (ToGui(cam, t.transform.position, out Vector2 p)) Box(p, 14f, HudGreen, 1f);
        }

        if (current == null) return;

        float distance = Vector3.Distance(weapons.transform.position, current.transform.position);
        Color color = weapons.IsLocked ? HudRed : HudGreen;

        if (ToGui(cam, current.transform.position, out Vector2 pos))
        {
            Box(pos, 26f, color, 2f);

            // Lock ring closes in on the box while the lock builds.
            if (weapons.LockProgress > 0f && !weapons.IsLocked)
                Box(pos, Mathf.Lerp(90f, 30f, weapons.LockProgress), HudRed, 1f);

            GUI.color = color;
            GUI.Label(R(pos.x - 100f, pos.y + 30f, 200f, 24f), $"{current.displayName}  {distance:0} m", label);
            if (weapons.IsLocked)
                GUI.Label(R(pos.x - 100f, pos.y - 56f, 200f, 24f), "LOCK", label);
        }
        else
        {
            // Off screen: point toward it from the centre of the view.
            Vector3 local = cam.transform.InverseTransformPoint(current.transform.position);
            Vector2 dir = new Vector2(local.x, -local.y);
            if (dir.sqrMagnitude < 0.0001f) dir = Vector2.up;
            dir.Normalize();

            Vector2 centre = new Vector2(ViewWidth, ViewHeight) * 0.5f;
            float radius = ViewHeight * 0.38f;
            for (int i = 0; i < 4; i++)
                Fill(centre + dir * (radius - i * 14f), 10f - i * 2f, color);

            Vector2 textPos = centre + dir * (radius - 80f);
            GUI.color = color;
            GUI.Label(R(textPos.x - 100f, textPos.y - 12f, 200f, 24f), $"{distance:0} m", label);
        }
    }

    void DrawGunReticle(Camera cam)
    {
        Vector3 aim = weapons.transform.position + weapons.transform.forward * gunReticleDistance;
        if (!ToGui(cam, aim, out Vector2 p))
        {
            // In target view the nose is often off screen: point toward it so the player knows which way to turn.
            if (weapons.TargetViewHeld) DrawNosePointer(cam, aim);
            return;
        }

        GUI.color = HudGreen;
        GUI.DrawTexture(R(p.x - 12f, p.y - 1f, 8f, 2f), Texture2D.whiteTexture);
        GUI.DrawTexture(R(p.x + 4f, p.y - 1f, 8f, 2f), Texture2D.whiteTexture);
        GUI.DrawTexture(R(p.x - 1f, p.y - 12f, 2f, 8f), Texture2D.whiteTexture);
        GUI.DrawTexture(R(p.x - 1f, p.y + 4f, 2f, 8f), Texture2D.whiteTexture);
    }

    void DrawWeaponStatus()
    {
        float x = ViewWidth - 220f;
        float y = ViewHeight - 90f;

        GUI.color = HudGreen;
        label.alignment = TextAnchor.UpperLeft;
        GUI.Label(R(x, y, 200f, 24f), $"MSL  {weapons.MissileAmmo}", label);
        GUI.Label(R(x, y + 48f, 200f, 24f), $"TGT  {Target.CountAlive(Team.Enemy)}", label);
        label.alignment = TextAnchor.UpperCenter;

        for (int i = 0; i < weapons.RailCount; i++)
        {
            float ready = weapons.RailReadiness(i);
            float barX = x + i * 70f, barY = y + 28f;
            GUI.color = new Color(HudGreen.r, HudGreen.g, HudGreen.b, 0.25f);
            GUI.DrawTexture(R(barX, barY, 60f, 10f), Texture2D.whiteTexture);
            GUI.color = ready >= 1f ? HudGreen : HudRed;
            GUI.DrawTexture(R(barX, barY, 60f * ready, 10f), Texture2D.whiteTexture);
        }
    }

    void DrawRadar()
    {
        const float radius = 80f;
        Vector2 centre = new Vector2(20f + radius, ViewHeight - 20f - radius);

        if (radarDisc == null) radarDisc = BuildDisc(128);
        GUI.color = new Color(HudGreen.r, HudGreen.g, HudGreen.b, 0.9f);
        GUI.DrawTexture(R(centre.x - radius, centre.y - radius, radius * 2f, radius * 2f), radarDisc);

        // Heading-up: the top of the radar is wherever the nose points.
        Transform self = weapons.transform;
        Quaternion toRadar = Quaternion.Inverse(Quaternion.Euler(0f, self.eulerAngles.y, 0f));

        foreach (Target t in Target.All)
        {
            if (t.team != Team.Enemy || t.IsDead) continue;
            Vector2 p = RadarPoint(toRadar, self.position, t.transform.position, centre, radius);
            Color color = t.isGround ? HudAmber : HudRed;
            if (t == weapons.CurrentTarget) Box(p, 13f, Color.white, 1f);
            Fill(p, t.isGround ? 5f : 7f, color);
        }

        foreach (Missile m in Missile.Active)
        {
            if (m.Target != player) continue;
            if (Mathf.Repeat(Time.time, 0.2f) < 0.1f) continue;
            Fill(RadarPoint(toRadar, self.position, m.transform.position, centre, radius), 4f, Color.white);
        }

        // Own aircraft.
        GUI.color = HudGreen;
        GUI.DrawTexture(R(centre.x - 1f, centre.y - 6f, 2f, 10f), Texture2D.whiteTexture);
        GUI.DrawTexture(R(centre.x - 5f, centre.y - 1f, 10f, 2f), Texture2D.whiteTexture);
    }

    Vector2 RadarPoint(Quaternion toRadar, Vector3 origin, Vector3 world, Vector2 centre, float radius)
    {
        Vector3 rel = toRadar * (world - origin);
        Vector2 flat = new Vector2(rel.x, -rel.z) / radarRange;
        // Anything beyond range sits on the rim so its bearing is still readable.
        if (flat.sqrMagnitude > 1f) flat.Normalize();
        return centre + flat * (radius - 4f);
    }

    /// <summary>Translucent disc with a brighter rim and a mid-range ring.</summary>
    static Texture2D BuildDisc(int size)
    {
        var texture = new Texture2D(size, size, TextureFormat.RGBA32, false) { hideFlags = HideFlags.HideAndDontSave };
        float half = size * 0.5f;
        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                float d = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), new Vector2(half, half)) / half;
                float alpha = d > 1f ? 0f : d > 0.96f ? 0.9f : Mathf.Abs(d - 0.5f) < 0.012f ? 0.45f : 0.16f;
                texture.SetPixel(x, y, new Color(1f, 1f, 1f, alpha));
            }
        }
        texture.Apply();
        return texture;
    }

    void DrawDamage()
    {
        if (player == null) return;

        float damage = 1f - player.Health / player.maxHealth;
        GUI.color = damage >= 0.6f ? HudRed : damage > 0f ? HudAmber : HudGreen;
        label.alignment = TextAnchor.UpperLeft;
        GUI.Label(R(20f, ViewHeight - 215f, 240f, 24f), $"DMG  {damage * 100f:0}%", label);
        label.alignment = TextAnchor.UpperCenter;
    }

    void DrawWarnings(Camera cam)
    {
        if (player == null || player.IsDead) return;

        Missile missile = missileWarning != null ? missileWarning.Closest : null;
        Rect line = R(0f, ViewHeight * 0.68f, ViewWidth, 44f);

        if (missile != null)
        {
            float distance = missileWarning.ClosestDistance;
            float urgency = missileWarning.Urgency;

            // Flashes in step with the warning beep, so it speeds up as the missile closes.
            float interval = Mathf.Max(missileWarning.BeepInterval, 0.1f);
            if (Mathf.Repeat(Time.time, interval) < interval * 0.65f)
            {
                GUI.color = HudRed;
                GUI.Label(line, $"MISSILE ALERT  {distance:0} m", bigLabel);
            }

            // Closing bar: fills up as the missile gets nearer.
            float barWidth = 260f;
            float barX = (ViewWidth - barWidth) * 0.5f, barY = ViewHeight * 0.68f + 48f;
            GUI.color = new Color(HudRed.r, HudRed.g, HudRed.b, 0.25f);
            GUI.DrawTexture(R(barX, barY, barWidth, 8f), Texture2D.whiteTexture);
            GUI.color = HudRed;
            GUI.DrawTexture(R(barX, barY, barWidth * urgency, 8f), Texture2D.whiteTexture);

            // Marker pointing at the missile; it grows as the missile closes.
            Vector3 local = cam.transform.InverseTransformPoint(missile.transform.position);
            Vector2 dir = new Vector2(local.x, -local.y);
            if (dir.sqrMagnitude < 0.0001f) dir = Vector2.down;
            dir.Normalize();
            Vector2 centre = new Vector2(ViewWidth, ViewHeight) * 0.5f;
            float radius = ViewHeight * 0.26f;
            float size = Mathf.Lerp(8f, 20f, urgency);
            Fill(centre + dir * radius, size, HudRed);
            Fill(centre + dir * (radius - size), size * 0.6f, HudRed);
        }
        else if (EnemyIsLocking())
        {
            GUI.color = HudAmber;
            GUI.Label(line, "WARNING", bigLabel);
        }
    }

    static bool EnemyIsLocking()
    {
        foreach (Target t in Target.All)
        {
            if (t.team != Team.Enemy || t.IsDead) continue;
            if (t.TryGetComponent(out EnemyPlane enemy) && enemy.IsLockingPlayer) return true;
            if (t.TryGetComponent(out SamSite sam) && sam.IsLockingPlayer) return true;
        }
        return false;
    }

    void DrawNosePointer(Camera cam, Vector3 aim)
    {
        Vector3 local = cam.transform.InverseTransformPoint(aim);
        Vector2 dir = new Vector2(local.x, -local.y);
        if (dir.sqrMagnitude < 0.0001f) dir = Vector2.up;
        dir.Normalize();

        Vector2 centre = new Vector2(ViewWidth, ViewHeight) * 0.5f;
        float radius = ViewHeight * 0.44f;
        for (int i = 0; i < 3; i++)
            Fill(centre + dir * (radius - i * 12f), 8f - i * 2f, HudGreen);

        Vector2 textPos = centre + dir * (radius - 50f);
        GUI.color = HudGreen;
        GUI.Label(R(textPos.x - 60f, textPos.y - 12f, 120f, 24f), "NOSE", label);
    }

    void DrawPullUp()
    {
        if (groundWarning == null || !groundWarning.Warning || player == null || player.IsDead) return;
        if (Mathf.Repeat(Time.time, 0.4f) > 0.26f) return;

        GUI.color = HudRed;
        GUI.Label(R(0f, ViewHeight * 0.56f, ViewWidth, 44f), "PULL UP", bigLabel);
    }

    void DrawMessages()
    {
        Rect line = R(0f, ViewHeight * 0.22f, ViewWidth, 44f);
        Rect hint = R(0f, ViewHeight * 0.22f + 50f, ViewWidth, 24f);
        bool playerDead = player != null && player.IsDead;

        if (!playerDead && hadTargets && Target.CountAlive(Team.Enemy) == 0)
        {
            GUI.color = HudGreen;
            GUI.Label(line, "MISSION ACCOMPLISHED", bigLabel);
            GUI.Label(hint, "Press R to fly again", label);
        }
        else if (Time.time < messageUntil)
        {
            GUI.color = HudRed;
            GUI.Label(line, message, bigLabel);
        }
    }

    /// <summary>Rect in 720p layout units, converted to real pixels.</summary>
    static Rect R(float x, float y, float width, float height)
    {
        return new Rect(x * scale, y * scale, width * scale, height * scale);
    }

    /// <summary>World point to HUD layout units. False if behind the camera or off screen.</summary>
    static bool ToGui(Camera cam, Vector3 world, out Vector2 gui)
    {
        Vector3 s = cam.WorldToScreenPoint(world);
        gui = new Vector2(s.x, Screen.height - s.y) / scale;
        return s.z > 0f && s.x >= 0f && s.x <= Screen.width && s.y >= 0f && s.y <= Screen.height;
    }

    static void Box(Vector2 centre, float size, Color color, float thickness)
    {
        float h = size * 0.5f;
        GUI.color = color;
        GUI.DrawTexture(R(centre.x - h, centre.y - h, size, thickness), Texture2D.whiteTexture);
        GUI.DrawTexture(R(centre.x - h, centre.y + h - thickness, size, thickness), Texture2D.whiteTexture);
        GUI.DrawTexture(R(centre.x - h, centre.y - h, thickness, size), Texture2D.whiteTexture);
        GUI.DrawTexture(R(centre.x + h - thickness, centre.y - h, thickness, size), Texture2D.whiteTexture);
    }

    static void Fill(Vector2 centre, float size, Color color)
    {
        GUI.color = color;
        GUI.DrawTexture(R(centre.x - size * 0.5f, centre.y - size * 0.5f, size, size), Texture2D.whiteTexture);
    }
}
