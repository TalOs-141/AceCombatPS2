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
    static readonly Color HudBlue = new Color(0.45f, 0.75f, 1f);

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
    PlaneController plane;
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
        plane = weapons.GetComponent<PlaneController>();
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
        DrawHome(cam);
        DrawFlightStatus();
        DrawLandingGuide();
        DrawMessages();
        DrawPause();
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

        foreach (LandingSurface surface in LandingSurface.All)
            Fill(RadarPoint(toRadar, self.position, surface.TouchdownPoint, centre, radius), 7f, HudBlue);

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
        GameFlow flow = GameFlow.Instance;
        MissionPhase phase = flow != null ? flow.Phase : MissionPhase.Combat;
        bool pad = FlightInput.Current.usingGamepad;

        if (phase == MissionPhase.Complete)
        {
            GUI.color = HudGreen;
            GUI.Label(line, "MISSION COMPLETE", bigLabel);
            GUI.Label(hint, $"{LandingGrade(flow.LastLandingSink)} landing ({flow.LastLandingSink:0.0} m/s)   -   {(pad ? "SELECT" : "R")} to fly again", label);
        }
        else if (phase == MissionPhase.Failed || Time.time < messageUntil)
        {
            GUI.color = HudRed;
            GUI.Label(line, message, bigLabel);
        }
        else if (flow != null && Time.time < flow.ServicedNoticeUntil)
        {
            GUI.color = HudGreen;
            GUI.Label(line, "REARMED AND REPAIRED", bigLabel);
        }
        else if (phase == MissionPhase.Takeoff)
        {
            float kmh = plane != null ? plane.takeoffSpeed * 3.6f : 0f;
            GUI.color = HudGreen;
            GUI.Label(line, "CLEARED FOR TAKEOFF", bigLabel);
            if (plane != null && plane.helicopter)
                GUI.Label(hint, pad ? "Hold RT to lift off, LT to descend" : "Hold Shift to lift off, Ctrl to descend", label);
            else
                GUI.Label(hint, pad
                    ? $"Hold RT to launch, pull back at {kmh:0} km/h, then D-pad down to raise the gear"
                    : $"Hold Shift to launch, press S at {kmh:0} km/h, then G to raise the gear", label);
        }
        else if (phase == MissionPhase.ReturnToBase)
        {
            GUI.color = HudGreen;
            GUI.Label(line, "MISSION ACCOMPLISHED", bigLabel);
            GUI.Label(hint, $"Return to the carrier and land   -   gear: {(pad ? "D-pad down" : "G")}", label);
        }

        if (GameFlow.FreeFlight)
        {
            GUI.color = HudBlue;
            GUI.Label(R(0f, 16f, ViewWidth, 24f), "FREE FLIGHT", label);
        }
    }

    static string LandingGrade(float sink)
    {
        if (sink < 0f) return "No";
        if (sink <= 3f) return "Perfect";
        if (sink <= 6f) return "Good";
        if (sink <= 9f) return "Firm";
        return "Hard";
    }

    /// <summary>Gear, high-G and stall status under the speed / altitude readout.</summary>
    void DrawFlightStatus()
    {
        if (plane == null || player == null || player.IsDead) return;
        label.alignment = TextAnchor.UpperLeft;

        if (plane.helicopter)
        {
            // Skids are always down; nothing to report.
        }
        else if (plane.GearPosition > 0f && plane.GearPosition < 1f)
        {
            GUI.color = HudAmber;
            GUI.Label(R(20f, 112f, 300f, 24f), plane.GearDown ? "GEAR  LOWERING" : "GEAR  RAISING", label);
        }
        else if (plane.GearDown)
        {
            // Amber is a reminder that the gear is still hanging out in flight.
            GUI.color = plane.IsGrounded || plane.Speed <= plane.gearMaxSpeed ? HudGreen : HudAmber;
            GUI.Label(R(20f, 112f, 300f, 24f), "GEAR  DOWN", label);
        }
        else if (GearUpOnApproach() && Mathf.Repeat(Time.time, 0.5f) < 0.33f)
        {
            GUI.color = HudRed;
            GUI.Label(R(20f, 112f, 300f, 24f), "GEAR  UP", label);
        }

        if (plane.IsStalling && Mathf.Repeat(Time.time, 0.4f) < 0.26f)
        {
            GUI.color = HudRed;
            GUI.Label(R(20f, 140f, 300f, 24f), "STALL", label);
        }
        else if (plane.HighG)
        {
            GUI.color = HudAmber;
            GUI.Label(R(20f, 140f, 300f, 24f), "HIGH-G", label);
        }

        label.alignment = TextAnchor.UpperCenter;
    }

    /// <summary>Low, descending and close to somewhere to land, but the wheels are still up.</summary>
    bool GearUpOnApproach()
    {
        if (plane.IsGrounded || plane.VerticalSpeed > -1f) return false;
        LandingSurface surface = LandingSurface.Nearest(plane.transform.position);
        if (surface == null) return false;
        Vector3 offset = plane.transform.position - surface.TouchdownPoint;
        return offset.y < 250f && new Vector2(offset.x, offset.z).magnitude < 2500f;
    }

    /// <summary>Marks the home carrier, with an edge pointer when it's time to go back.</summary>
    void DrawHome(Camera cam)
    {
        GameFlow flow = GameFlow.Instance;
        if (flow == null || flow.homeBase == null || plane == null || plane.IsGrounded) return;

        Vector3 home = flow.homeBase.TouchdownPoint;
        float distance = Vector3.Distance(plane.transform.position, home);
        string text = $"{flow.homeBase.displayName}  {distance / 1000f:0.0} km";

        if (ToGui(cam, home, out Vector2 pos))
        {
            Box(pos, 18f, HudBlue, 2f);
            GUI.color = HudBlue;
            GUI.Label(R(pos.x - 100f, pos.y + 14f, 200f, 24f), text, label);
        }
        else if (flow.Phase == MissionPhase.ReturnToBase || plane.GearDown)
        {
            Vector3 local = cam.transform.InverseTransformPoint(home);
            Vector2 dir = new Vector2(local.x, -local.y);
            if (dir.sqrMagnitude < 0.0001f) dir = Vector2.up;
            dir.Normalize();

            Vector2 centre = new Vector2(ViewWidth, ViewHeight) * 0.5f;
            float radius = ViewHeight * 0.32f;
            for (int i = 0; i < 4; i++)
                Fill(centre + dir * (radius - i * 14f), 10f - i * 2f, HudBlue);

            Vector2 textPos = centre + dir * (radius - 80f);
            GUI.color = HudBlue;
            GUI.Label(R(textPos.x - 100f, textPos.y - 12f, 200f, 24f), text, label);
        }
    }

    /// <summary>
    /// Approach aid shown with the gear down near a landing surface: crossed
    /// needles for glide path and line-up, plus sink rate and speed checks.
    /// </summary>
    void DrawLandingGuide()
    {
        if (plane == null || plane.IsGrounded || !plane.GearDown || plane.helicopter || player == null || player.IsDead) return;

        LandingSurface surface = LandingSurface.Nearest(plane.transform.position);
        if (surface == null) return;
        Vector3 rel = Quaternion.Inverse(surface.LandingRotation) * (plane.transform.position - surface.TouchdownPoint);
        float distance = rel.magnitude;
        if (distance > 7000f) return;

        const float size = 130f, half = size * 0.5f;
        Vector2 centre = new Vector2(ViewWidth - 110f, ViewHeight * 0.36f);

        Box(centre, size, HudBlue, 1f);
        GUI.color = new Color(HudBlue.r, HudBlue.g, HudBlue.b, 0.3f);
        GUI.DrawTexture(R(centre.x - half, centre.y - 0.5f, size, 1f), Texture2D.whiteTexture);
        GUI.DrawTexture(R(centre.x - 0.5f, centre.y - half, 1f, size), Texture2D.whiteTexture);

        // Distance still to fly to the touchdown point, measured along the landing direction.
        float toGo = -rel.z;
        string glideText = "PAST TOUCHDOWN POINT";
        Color glideColor = HudAmber;
        if (toGo > 20f)
        {
            const float idealGlide = 3.5f;
            float glideError = Mathf.Atan2(rel.y - plane.gearHeight, toGo) * Mathf.Rad2Deg - idealGlide;
            float lineError = Mathf.Atan2(rel.x, toGo) * Mathf.Rad2Deg;

            // The needles show where the ideal path is: fly toward them to centre them.
            float needleY = centre.y + Mathf.Clamp(glideError / 3f, -1f, 1f) * half;
            float needleX = centre.x - Mathf.Clamp(lineError / 6f, -1f, 1f) * half;
            bool onPath = Mathf.Abs(glideError) < 0.8f && Mathf.Abs(lineError) < 1.5f;
            GUI.color = onPath ? HudGreen : HudAmber;
            GUI.DrawTexture(R(centre.x - half, needleY - 1.5f, size, 3f), Texture2D.whiteTexture);
            GUI.DrawTexture(R(needleX - 1.5f, centre.y - half, 3f, size), Texture2D.whiteTexture);

            if (glideError > 0.8f) glideText = "TOO HIGH";
            else if (glideError < -0.8f) glideText = "TOO LOW";
            else if (lineError > 1.5f) glideText = "DRIFTING RIGHT";
            else if (lineError < -1.5f) glideText = "DRIFTING LEFT";
            else glideText = "ON GLIDE PATH";
            glideColor = onPath ? HudGreen : HudAmber;
        }

        float x = centre.x - 110f, y = centre.y + half + 6f;
        GUI.color = HudBlue;
        GUI.Label(R(x, y, 220f, 24f), $"{surface.displayName}  {distance:0} m", label);
        GUI.color = glideColor;
        GUI.Label(R(x, y + 24f, 220f, 24f), glideText, label);

        float sink = -plane.VerticalSpeed;
        GUI.color = sink > plane.maxSinkRate * 0.8f ? HudRed : HudGreen;
        GUI.Label(R(x, y + 48f, 220f, 24f), $"SINK  {Mathf.Max(sink, 0f):0.0} m/s", label);

        if (plane.Speed > plane.maxLandingSpeed)
        {
            GUI.color = HudRed;
            GUI.Label(R(x, y + 72f, 220f, 24f), "TOO FAST", label);
        }
    }

    void DrawPause()
    {
        GameFlow flow = GameFlow.Instance;
        if (flow == null || !flow.Paused) return;

        GUI.color = new Color(0f, 0f, 0f, 0.74f);
        GUI.DrawTexture(new Rect(0f, 0f, Screen.width, Screen.height), Texture2D.whiteTexture);

        GUI.color = HudGreen;
        GUI.Label(R(0f, 34f, ViewWidth, 44f), "PAUSED", bigLabel);

        // The menu: one highlighted row at a time; left and right change the two settings.
        string vehicle = flow.Vehicles != null ? flow.Vehicles.NameOf(flow.PendingVehicle) : "-";
        string[] rows =
        {
            flow.MenuChanged ? "Resume  (restarts the mission with your changes)" : "Resume",
            $"Vehicle     <  {vehicle}  >",
            $"Free play   <  {(flow.PendingFreeFlight ? "On: no enemies" : "Off")}  >",
            "Restart mission",
        };
        for (int i = 0; i < rows.Length; i++)
        {
            bool selected = i == flow.MenuIndex;
            float y = 92f + i * 34f;
            if (selected)
            {
                GUI.color = new Color(HudGreen.r, HudGreen.g, HudGreen.b, 0.18f);
                GUI.DrawTexture(R(ViewWidth * 0.5f - 300f, y - 3f, 600f, 30f), Texture2D.whiteTexture);
            }
            GUI.color = selected ? Color.white : HudGreen;
            GUI.Label(R(0f, y, ViewWidth, 26f), rows[i], label);
        }
        GUI.color = HudBlue;
        GUI.Label(R(0f, 234f, ViewWidth, 24f), FlightInput.Current.usingGamepad
            ? "D-pad or stick: choose and change     A: select     Start or B: resume"
            : "W / S: choose     A / D: change     Enter: select     Esc: resume", label);

        string[,] controls =
        {
            { "", "KEYBOARD", "CONTROLLER" },
            { "Pitch / roll", "W S / A D", "Left stick" },
            { "Yaw", "Q / E", "LB / RB" },
            { "Accelerate / brake", "Shift / Ctrl", "RT / LT" },
            { "Helicopter: climb / descend", "Shift / Ctrl", "RT / LT" },
            { "High-G turn (jets)", "Hold Shift + Ctrl", "Hold RT + LT" },
            { "Missile", "Space", "B / Circle" },
            { "Gun", "F or left mouse", "A / Cross" },
            { "Switch target / target view", "Tab: tap / hold", "Y: tap / hold" },
            { "Look around", "-", "Right stick" },
            { "Cockpit / chase view", "C", "Click right stick" },
            { "Landing gear (jets)", "G", "D-pad down" },
            { "Restart mission", "R", "Select / Back" },
        };

        label.alignment = TextAnchor.UpperLeft;
        float left = ViewWidth * 0.5f - 350f;
        for (int row = 0; row < controls.GetLength(0); row++)
        {
            float y = 280f + row * 30f;
            GUI.color = row == 0 ? HudBlue : HudGreen;
            GUI.Label(R(left, y, 290f, 26f), controls[row, 0], label);
            GUI.Label(R(left + 300f, y, 210f, 26f), controls[row, 1], label);
            GUI.Label(R(left + 510f, y, 210f, 26f), controls[row, 2], label);
        }
        label.alignment = TextAnchor.UpperCenter;
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
