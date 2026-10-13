using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;

namespace Manhattan.Viewer
{
    /// <summary>
    /// Adds a paired HID controller, such as a BLE gamepad, to keyboard motion. Windows and the Input System own the
    /// connection; mouse look and the keyboard keep working.
    /// </summary>
    public sealed class ControllerMotionInput : MonoBehaviour, IMotionInputSource
    {
        private Joystick boundJoystick;
        private Vector2Control hat;
        private ButtonControl upButton;

        public KeyboardMotionInput Keyboard { get; set; }

        /// <summary>Stick or hat x/y is strafe/forward, like A/D and W/S. Down and up are Q and E.</summary>
        public static Vector3 Movement(Vector2 stick, Vector2 hat, bool down, bool up) =>
            Vector3.ClampMagnitude(new Vector3(stick.x + hat.x, (up ? 1 : 0) - (down ? 1 : 0), stick.y + hat.y), 1);

        public MotionCommand Sample(float deltaTime)
        {
            MotionCommand command = Keyboard != null ? Keyboard.Sample(deltaTime) : default;
            // Menus, captures and benchmarks disable keyboard input; the controller follows the same switch.
            if (Keyboard != null && !Keyboard.InputEnabled) return command;
            command.localMovement = Vector3.ClampMagnitude(command.localMovement + ReadController(), 1);
            return command;
        }

        private Vector3 ReadController()
        {
            var gamepad = Gamepad.current;
            if (gamepad != null)
                return Movement(gamepad.leftStick.ReadValue(), gamepad.dpad.ReadValue(), gamepad.leftShoulder.isPressed, gamepad.rightShoulder.isPressed);
            // Generic HID gamepads, including ESP32 BLE gamepads, arrive as a Joystick whose button 1 is "trigger".
            var joystick = Joystick.current;
            if (joystick == null) return Vector3.zero;
            if (joystick != boundJoystick)
            {
                boundJoystick = joystick;
                hat = joystick.TryGetChildControl<Vector2Control>("hat") ?? joystick.hatswitch;
                upButton = joystick.TryGetChildControl<ButtonControl>("button2");
            }
            return Movement(joystick.stick.ReadValue(), hat != null ? hat.ReadValue() : Vector2.zero,
                joystick.trigger.isPressed, upButton != null && upButton.isPressed);
        }
    }
}
