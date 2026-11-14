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
        public bool jump, sprint, interact, toggleAssist, toggleView, regenerate, tierUp, tierDown, warp;
        public bool systemView; // held
    }

    public static class DesktopControls
    {
        const float MouseDegreesPerCount = 0.08f, StickDegreesPerSecond = 150f;

        public static Controls Read()
        {
            var c = new Controls();
            var keys = Keyboard.current;
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
                c.regenerate = keys.rKey.wasPressedThisFrame;
                c.tierUp = keys.rightBracketKey.wasPressedThisFrame;
                c.tierDown = keys.leftBracketKey.wasPressedThisFrame;
                c.warp = keys.gKey.wasPressedThisFrame;
                c.systemView = keys.tabKey.isPressed;
            }
            if (Mouse.current != null && Cursor.lockState == CursorLockMode.Locked)
                c.look = Mouse.current.delta.ReadValue() * MouseDegreesPerCount;

            var pad = Gamepad.current;
            if (pad != null)
            {
                c.move += pad.leftStick.ReadValue();
                c.look += pad.rightStick.ReadValue() * (StickDegreesPerSecond * Time.deltaTime);
                c.lift += pad.rightTrigger.ReadValue() - pad.leftTrigger.ReadValue();
                c.roll += Axis(pad.rightShoulder.isPressed, pad.leftShoulder.isPressed);
                c.jump |= pad.buttonSouth.isPressed;
                c.sprint |= pad.leftStickButton.isPressed;
                c.interact |= pad.buttonWest.wasPressedThisFrame;
                c.toggleAssist |= pad.buttonNorth.wasPressedThisFrame;
                c.toggleView |= pad.rightStickButton.wasPressedThisFrame;
                c.warp |= pad.buttonEast.wasPressedThisFrame;
                c.systemView |= pad.selectButton.isPressed;
            }
            return c;
        }

        static float Axis(bool positive, bool negative) => (positive ? 1f : 0f) - (negative ? 1f : 0f);
    }
}
