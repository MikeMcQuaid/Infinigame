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
        public bool jump, sprint, interact, scan, travel, toggleAssist, toggleView, tierUp, tierDown, warp, help, menu, confirm, mute, reload, ride, talk;
        // Which weapon to take up, 1 to 5, or 0 to keep the one in hand; and a step to the next or the last.
        public int select, cycle;
        public bool systemView, fire; // held
        // For menus and for talking: step up or down a list, point and click, and what was typed this frame.
        public bool up, down, click, erase;
        public Vector2 pointer;
        public string typed, replaceText;
    }

    public static class DesktopControls
    {
        const float StickDegreesPerSecond = 150f;

        // Degrees turned per count of mouse movement. Adjusted in play with - and =.
        public static float Sensitivity = 0.25f;

        static readonly System.Text.StringBuilder typed = new System.Text.StringBuilder();
        static Keyboard listening;

        public static Controls Read()
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
                c.toggleAssist = keys.zKey.wasPressedThisFrame;
                c.toggleView = keys.vKey.wasPressedThisFrame;
                c.travel = keys.tKey.wasPressedThisFrame;
                c.mute = keys.mKey.wasPressedThisFrame;
                c.select = keys.digit1Key.wasPressedThisFrame ? 1 : keys.digit2Key.wasPressedThisFrame ? 2 : keys.digit3Key.wasPressedThisFrame ? 3 : keys.digit4Key.wasPressedThisFrame ? 4
                    : keys.digit5Key.wasPressedThisFrame ? 5 : 0;
                c.reload = keys.rKey.wasPressedThisFrame;
                c.ride = keys.bKey.wasPressedThisFrame;
                c.tierUp = keys.rightBracketKey.wasPressedThisFrame;
                c.tierDown = keys.leftBracketKey.wasPressedThisFrame;
                c.warp = keys.gKey.wasPressedThisFrame;
                c.systemView = keys.tabKey.isPressed;
                c.menu = keys.escapeKey.wasPressedThisFrame;
                c.confirm = c.talk = keys.enterKey.wasPressedThisFrame && !keys.altKey.isPressed;
                c.help = keys.hKey.wasPressedThisFrame;
                c.up = keys.upArrowKey.wasPressedThisFrame || keys.wKey.wasPressedThisFrame;
                c.down = keys.downArrowKey.wasPressedThisFrame || keys.sKey.wasPressedThisFrame;
                c.erase = keys.backspaceKey.wasPressedThisFrame;
            }
            if (Mouse.current != null && Cursor.lockState != CursorLockMode.Locked)
            {
                c.pointer = Mouse.current.position.ReadValue();
                c.click = Mouse.current.leftButton.wasPressedThisFrame;
            }
            if (Mouse.current != null && Cursor.lockState == CursorLockMode.Locked)
            {
                c.fire = Mouse.current.leftButton.isPressed;
                c.cycle = (int)Mathf.Sign(Mouse.current.scroll.ReadValue().y) * (Mouse.current.scroll.ReadValue().y == 0f ? 0 : 1);
                c.scan = Mouse.current.rightButton.wasPressedThisFrame;
                // acceleration: a quick flick turns further than the same distance moved slowly
                Vector2 delta = Mouse.current.delta.ReadValue();
                c.look = delta * (Sensitivity * (1f + Mathf.Min(delta.magnitude * 0.04f, 2f)));
            }

            var pad = Gamepad.current;
            if (pad != null)
            {
                c.move += pad.leftStick.ReadValue();
                c.look += pad.rightStick.ReadValue() * (StickDegreesPerSecond * Time.deltaTime);
                // triggers fire and scan, as on any pad, so the shoulders climb and descend and roll is left to the keys
                c.lift += Axis(pad.rightShoulder.isPressed, pad.leftShoulder.isPressed);
                c.fire |= pad.rightTrigger.ReadValue() > 0.5f;
                c.scan |= pad.leftTrigger.wasPressedThisFrame;
                c.jump |= pad.buttonSouth.isPressed;
                c.sprint |= pad.leftStickButton.isPressed;
                c.interact |= pad.buttonWest.wasPressedThisFrame;
                c.toggleAssist |= pad.buttonNorth.wasPressedThisFrame;
                c.toggleView |= pad.rightStickButton.wasPressedThisFrame;
                c.warp |= pad.buttonEast.wasPressedThisFrame;
                c.systemView |= pad.selectButton.isPressed;
                c.menu |= pad.startButton.wasPressedThisFrame;
                c.confirm |= pad.buttonSouth.wasPressedThisFrame;
                c.travel |= pad.dpad.right.wasPressedThisFrame;
                c.reload |= pad.buttonWest.wasPressedThisFrame;
                c.ride |= pad.dpad.down.wasPressedThisFrame && pad.leftTrigger.ReadValue() > 0.5f;
                if (pad.buttonNorth.wasPressedThisFrame && pad.leftTrigger.ReadValue() > 0.5f) c.cycle = 1;
                c.help |= pad.dpad.left.wasPressedThisFrame;
                c.up |= pad.dpad.up.wasPressedThisFrame;
                c.down |= pad.dpad.down.wasPressedThisFrame;
            }
            return c;
        }

        static void Typed(char letter) => typed.Append(letter);

        static float Axis(bool positive, bool negative) => (positive ? 1f : 0f) - (negative ? 1f : 0f);
    }
}
