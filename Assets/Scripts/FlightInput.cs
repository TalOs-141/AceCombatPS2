using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// The one place that reads the keyboard, mouse and gamepad. Everything else
/// asks for <see cref="Current"/>, so both devices always behave the same.
/// Read it from Update / LateUpdate / OnGUI only: button presses are per-frame
/// and are not valid inside FixedUpdate.
/// </summary>
public static class FlightInput
{
    public struct State
    {
        /// <summary>+1 = nose down, -1 = nose up (pull back).</summary>
        public float pitch;
        /// <summary>+1 = roll left, -1 = roll right.</summary>
        public float roll;
        /// <summary>+1 = yaw right.</summary>
        public float yaw;
        public float accelerate;
        public float brake;
        /// <summary>Camera look: x = right, y = up.</summary>
        public Vector2 look;
        public bool gunHeld;
        public bool switchHeld;
        public bool missilePressed;
        public bool gearPressed;
        public bool pausePressed;
        public bool restartPressed;
        public bool freeFlightPressed;
        public bool viewPressed;
        // Pause-menu navigation; each is true only on the frame it is pressed.
        public bool menuUp, menuDown, menuLeft, menuRight;
        public bool confirmPressed;
        public bool backPressed;
        /// <summary>True if the gamepad was the last device touched; used for on-screen hints.</summary>
        public bool usingGamepad;
    }

    /// <summary>Flip so that pushing forward raises the nose.</summary>
    public static bool InvertPitch;

    const float TriggerDeadzone = 0.06f;
    // Above 1 makes small stick movements finer without losing full deflection.
    const float StickCurve = 1.6f;

    static State state;
    static int frame = -1;
    static bool lastUsedGamepad;
    static Vector2 lastStickStep;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics()
    {
        frame = -1;
        state = default;
    }

    public static State Current
    {
        get
        {
            if (Time.frameCount != frame)
            {
                frame = Time.frameCount;
                Read();
            }
            return state;
        }
    }

    static void Read()
    {
        var s = new State();
        bool keyboardActive = false, gamepadActive = false;

        Keyboard kb = Keyboard.current;
        if (kb != null)
        {
            // Flight-stick convention: pull back (S / Down) raises the nose.
            if (kb.sKey.isPressed || kb.downArrowKey.isPressed) s.pitch -= 1f;
            if (kb.wKey.isPressed || kb.upArrowKey.isPressed) s.pitch += 1f;
            if (kb.aKey.isPressed || kb.leftArrowKey.isPressed) s.roll += 1f;
            if (kb.dKey.isPressed || kb.rightArrowKey.isPressed) s.roll -= 1f;
            if (kb.qKey.isPressed) s.yaw -= 1f;
            if (kb.eKey.isPressed) s.yaw += 1f;
            if (kb.leftShiftKey.isPressed) s.accelerate = 1f;
            if (kb.leftCtrlKey.isPressed) s.brake = 1f;

            s.gunHeld |= kb.fKey.isPressed;
            s.switchHeld |= kb.tabKey.isPressed;
            s.missilePressed |= kb.spaceKey.wasPressedThisFrame;
            s.gearPressed |= kb.gKey.wasPressedThisFrame;
            s.pausePressed |= kb.escapeKey.wasPressedThisFrame || kb.pKey.wasPressedThisFrame;
            s.restartPressed |= kb.rKey.wasPressedThisFrame;
            s.freeFlightPressed |= kb.vKey.wasPressedThisFrame;
            s.viewPressed |= kb.cKey.wasPressedThisFrame;
            s.menuUp |= kb.wKey.wasPressedThisFrame || kb.upArrowKey.wasPressedThisFrame;
            s.menuDown |= kb.sKey.wasPressedThisFrame || kb.downArrowKey.wasPressedThisFrame;
            s.menuLeft |= kb.aKey.wasPressedThisFrame || kb.leftArrowKey.wasPressedThisFrame;
            s.menuRight |= kb.dKey.wasPressedThisFrame || kb.rightArrowKey.wasPressedThisFrame;
            s.confirmPressed |= kb.enterKey.wasPressedThisFrame || kb.spaceKey.wasPressedThisFrame;
            s.backPressed |= kb.backspaceKey.wasPressedThisFrame;

            keyboardActive = kb.anyKey.isPressed;
        }

        Mouse mouse = Mouse.current;
        if (mouse != null) s.gunHeld |= mouse.leftButton.isPressed;

        Gamepad pad = Gamepad.current;
        if (pad != null)
        {
            Vector2 stick = pad.leftStick.ReadValue();
            s.pitch += Curve(stick.y);
            s.roll -= Curve(stick.x);
            s.yaw += pad.rightShoulder.ReadValue() - pad.leftShoulder.ReadValue();

            float rightTrigger = pad.rightTrigger.ReadValue(), leftTrigger = pad.leftTrigger.ReadValue();
            if (rightTrigger > TriggerDeadzone) s.accelerate = Mathf.Max(s.accelerate, rightTrigger);
            if (leftTrigger > TriggerDeadzone) s.brake = Mathf.Max(s.brake, leftTrigger);

            s.look = pad.rightStick.ReadValue();

            s.gunHeld |= pad.buttonSouth.isPressed;
            s.switchHeld |= pad.buttonNorth.isPressed;
            s.missilePressed |= pad.buttonEast.wasPressedThisFrame;
            s.gearPressed |= pad.dpad.down.wasPressedThisFrame;
            s.pausePressed |= pad.startButton.wasPressedThisFrame;
            s.restartPressed |= pad.selectButton.wasPressedThisFrame;
            s.freeFlightPressed |= pad.buttonWest.wasPressedThisFrame;
            s.viewPressed |= pad.rightStickButton.wasPressedThisFrame;

            // Menus step once per flick of the stick, or per press of the D-pad.
            Vector2 stickStep = new Vector2(Mathf.Abs(stick.x) > 0.6f ? Mathf.Sign(stick.x) : 0f, Mathf.Abs(stick.y) > 0.6f ? Mathf.Sign(stick.y) : 0f);
            s.menuUp |= pad.dpad.up.wasPressedThisFrame || (stickStep.y > 0f && lastStickStep.y <= 0f);
            s.menuDown |= pad.dpad.down.wasPressedThisFrame || (stickStep.y < 0f && lastStickStep.y >= 0f);
            s.menuLeft |= pad.dpad.left.wasPressedThisFrame || (stickStep.x < 0f && lastStickStep.x >= 0f);
            s.menuRight |= pad.dpad.right.wasPressedThisFrame || (stickStep.x > 0f && lastStickStep.x <= 0f);
            lastStickStep = stickStep;
            s.confirmPressed |= pad.buttonSouth.wasPressedThisFrame;
            s.backPressed |= pad.buttonEast.wasPressedThisFrame;

            gamepadActive = stick.sqrMagnitude > 0.04f || s.look.sqrMagnitude > 0.04f
                            || rightTrigger > 0.2f || leftTrigger > 0.2f
                            || pad.buttonSouth.isPressed || pad.buttonNorth.isPressed || pad.buttonEast.isPressed
                            || pad.buttonWest.isPressed || pad.startButton.isPressed || pad.selectButton.isPressed
                            || pad.leftShoulder.isPressed || pad.rightShoulder.isPressed || pad.dpad.ReadValue().sqrMagnitude > 0.1f;
        }

        if (InvertPitch) s.pitch = -s.pitch;
        s.pitch = Mathf.Clamp(s.pitch, -1f, 1f);
        s.roll = Mathf.Clamp(s.roll, -1f, 1f);
        s.yaw = Mathf.Clamp(s.yaw, -1f, 1f);

        if (gamepadActive) lastUsedGamepad = true;
        else if (keyboardActive) lastUsedGamepad = false;
        s.usingGamepad = lastUsedGamepad;

        state = s;
    }

    static float Curve(float value)
    {
        return Mathf.Sign(value) * Mathf.Pow(Mathf.Abs(value), StickCurve);
    }
}
