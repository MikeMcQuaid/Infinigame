using UnityEngine;
using UnityEngine.InputSystem;

namespace Infinigame
{
    // One frame of player intent. Touch, VR, network or test input only has to fill this in.
    public struct Controls
    {
        public Vector2 move; // x strafe, y forward
        public Vector2 look; // degrees to turn this frame: x right, y up
        public float lift, roll; // -1 to 1, ship only
        public bool jump, sprint, interact, scan, travel, toggleAssist, toggleView, tierUp, tierDown, warp, help, menu, confirm, mute, reload, ride, talk, voice, cheats;
        // Which weapon to take up, 1 to 5, or 0 to keep the one in hand; and a step to the next or the last.
        public int select, cycle;
        public bool systemView, fire, automaticFire, interfaceActive; // systemView toggles, fire is held
        // For menus and for talking: step up or down a list, point and click, and what was typed this frame.
        public bool up, down, click, erase;
        public Vector2 pointer, mapDrag;
        public float mapPinch;
        public string typed, replaceText;
    }

    public static class DesktopControls
    {
        const float StickDegreesPerSecond = 150f;

        // Degrees turned per count of mouse movement. Adjusted in Settings.
        public static float Sensitivity = 0.25f;

        static readonly System.Text.StringBuilder typed = new System.Text.StringBuilder();
        static Keyboard listening;
        static Core.StickNavigation navigation;
        public static bool UsingPad { get; private set; }
        public static bool KeyboardUsed { get; private set; }

        public static Controls Read() => Read(false, false);

        public static Controls Read(bool flying, bool invertShipPitch)
        {
            var c = new Controls();
            var keys = Keyboard.current;
            if (keys != listening)
            {
                // text comes as characters, not key presses, so that layouts and shift just work
                if (listening != null) listening.onTextInput -= Typed;
                if (keys != null) keys.onTextInput += Typed;
                listening = keys;
            }
            c.typed = typed.ToString();
            typed.Clear();
            if (keys != null)
            {
                c.move = new Vector2(Axis(keys.dKey.isPressed, keys.aKey.isPressed), Axis(keys.wKey.isPressed, keys.sKey.isPressed));
                c.lift = Axis(keys.spaceKey.isPressed, keys.leftCtrlKey.isPressed || keys.cKey.isPressed);
                c.roll = Axis(keys.eKey.isPressed, keys.qKey.isPressed);
                c.jump = keys.spaceKey.isPressed;
                c.sprint = keys.leftShiftKey.isPressed;
                c.interact = keys.fKey.wasPressedThisFrame;
                c.scan = keys.xKey.wasPressedThisFrame;
                c.toggleAssist = keys.zKey.wasPressedThisFrame;
                c.toggleView = keys.vKey.wasPressedThisFrame;
                c.travel = keys.tKey.wasPressedThisFrame;
                c.select = keys.digit1Key.wasPressedThisFrame ? 1 : keys.digit2Key.wasPressedThisFrame ? 2 : keys.digit3Key.wasPressedThisFrame ? 3 : keys.digit4Key.wasPressedThisFrame ? 4
                    : keys.digit5Key.wasPressedThisFrame ? 5 : 0;
                c.reload = keys.rKey.wasPressedThisFrame;
                c.ride = keys.bKey.wasPressedThisFrame;
                c.warp = keys.gKey.wasPressedThisFrame;
                c.systemView = keys.tabKey.wasPressedThisFrame;
                c.voice = keys.yKey.isPressed;
                c.menu = keys.escapeKey.wasPressedThisFrame;
                c.confirm = c.talk = keys.enterKey.wasPressedThisFrame && !keys.altKey.isPressed;
                c.up = keys.upArrowKey.wasPressedThisFrame || keys.wKey.wasPressedThisFrame;
                c.down = keys.downArrowKey.wasPressedThisFrame || keys.sKey.wasPressedThisFrame;
                c.erase = keys.backspaceKey.wasPressedThisFrame;
            }
            if (Mouse.current != null && Cursor.lockState != CursorLockMode.Locked)
            {
                c.pointer = Mouse.current.position.ReadValue();
                if (Mouse.current.leftButton.isPressed) c.mapDrag = Mouse.current.delta.ReadValue() / Mathf.Max(1, Screen.height);
                c.cycle = (int)Mathf.Sign(Mouse.current.scroll.ReadValue().y) * (Mouse.current.scroll.ReadValue().y == 0 ? 0 : 1);
                c.click = Mouse.current.leftButton.wasPressedThisFrame;
            }
            if (Mouse.current != null && Cursor.lockState == CursorLockMode.Locked)
            {
                c.fire = Mouse.current.leftButton.isPressed;
                c.cycle = (int)Mathf.Sign(Mouse.current.scroll.ReadValue().y) * (Mouse.current.scroll.ReadValue().y == 0f ? 0 : 1);

                // acceleration: a quick flick turns further than the same distance moved slowly
                Vector2 delta = Mouse.current.delta.ReadValue();
                c.look = delta * (Sensitivity * (1f + Mathf.Min(delta.magnitude * 0.04f, 2f)));
            }

            var pad = Gamepad.current;
            if (pad != null)
            {
                Vector2 stick = pad.leftStick.ReadValue(), turn = pad.rightStick.ReadValue();
                float shoulder = Axis(pad.rightShoulder.isPressed, pad.leftShoulder.isPressed);
                c.move += new Vector2(flying ? 0 : stick.x, stick.y);
                if (flying) c.roll += stick.x;
                c.lift += shoulder;
                if (flying && invertShipPitch) turn.y = -turn.y;
                c.look += turn * (StickDegreesPerSecond * Time.unscaledDeltaTime);
                c.fire |= pad.rightTrigger.ReadValue() > 0.5f;
                c.scan |= !flying && pad.leftTrigger.wasPressedThisFrame;
                c.jump |= pad.buttonSouth.isPressed;
                c.sprint |= flying ? pad.leftTrigger.isPressed : pad.leftStickButton.isPressed || stick.y >= 0.9f;
                c.interact |= pad.buttonWest.wasPressedThisFrame;
                c.toggleAssist |= pad.buttonNorth.wasPressedThisFrame && pad.leftTrigger.isPressed;
                if (pad.buttonNorth.wasPressedThisFrame && !pad.leftTrigger.isPressed) c.cycle = 1;
                c.toggleView |= pad.rightStickButton.wasPressedThisFrame;
                c.warp |= pad.buttonEast.wasPressedThisFrame;
                c.systemView |= pad.selectButton.wasPressedThisFrame;
                c.menu |= pad.startButton.wasPressedThisFrame;
                c.confirm |= pad.buttonSouth.wasPressedThisFrame;
                c.travel |= pad.dpad.right.wasPressedThisFrame;
                c.reload |= pad.buttonWest.wasPressedThisFrame && pad.leftTrigger.isPressed;
                if (pad.leftTrigger.isPressed) c.interact = false;
                c.ride |= pad.dpad.down.wasPressedThisFrame && pad.leftTrigger.ReadValue() > 0.5f;
                c.voice |= pad.rightStickButton.isPressed && pad.leftTrigger.isPressed;
                if (c.voice) c.toggleView = false;
                c.help |= pad.dpad.left.wasPressedThisFrame && !pad.leftTrigger.isPressed;
                c.talk |= pad.dpad.left.wasPressedThisFrame && pad.leftTrigger.isPressed;
                c.up |= pad.dpad.up.wasPressedThisFrame;
                c.down |= pad.dpad.down.wasPressedThisFrame;
                int step = navigation.Read(pad.leftStick.y.ReadValue(), pad.rightStick.y.ReadValue(), Time.unscaledTime);
                c.up |= step > 0;
                c.down |= step < 0;
                if (pad.leftStick.ReadValue().sqrMagnitude > 0.1f || pad.rightStick.ReadValue().sqrMagnitude > 0.1f || (pad.buttonSouth.wasPressedThisFrame || pad.buttonNorth.wasPressedThisFrame || pad.buttonWest.wasPressedThisFrame || pad.buttonEast.wasPressedThisFrame || pad.startButton.wasPressedThisFrame || pad.selectButton.wasPressedThisFrame || pad.dpad.ReadValue().sqrMagnitude > 0 || pad.rightTrigger.isPressed || pad.leftTrigger.isPressed)) { UsingPad = true; KeyboardUsed = false; }
            }
            if (keys?.anyKey.wasPressedThisFrame == true) { UsingPad = false; KeyboardUsed = true; }
            else if (!DeviceControls.Deck && Mouse.current?.delta.ReadValue().sqrMagnitude > 1) UsingPad = false;
            return c;
        }

        static void Typed(char letter) => typed.Append(letter);

        static float Axis(bool positive, bool negative) => (positive ? 1f : 0f) - (negative ? 1f : 0f);
    }
}
