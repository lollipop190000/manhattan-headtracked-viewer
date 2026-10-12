using Manhattan.Viewer;
using NUnit.Framework;
using UnityEngine;

public sealed class ControllerMotionInputTests
{
    [Test]
    public void ControllerAxesAndButtonsMapToTheKeyboardAxes()
    {
        Assert.That(ControllerMotionInput.Movement(Vector2.up, Vector2.zero, false, false), Is.EqualTo(Vector3.forward));
        Assert.That(ControllerMotionInput.Movement(Vector2.down, Vector2.zero, false, false), Is.EqualTo(Vector3.back));
        Assert.That(ControllerMotionInput.Movement(Vector2.left, Vector2.zero, false, false), Is.EqualTo(Vector3.left));
        Assert.That(ControllerMotionInput.Movement(Vector2.zero, Vector2.right, false, false), Is.EqualTo(Vector3.right), "The hat moves like the stick.");
        Assert.That(ControllerMotionInput.Movement(Vector2.zero, Vector2.zero, true, false), Is.EqualTo(Vector3.down));
        Assert.That(ControllerMotionInput.Movement(Vector2.zero, Vector2.zero, false, true), Is.EqualTo(Vector3.up));
        Assert.That(ControllerMotionInput.Movement(Vector2.zero, Vector2.zero, true, true), Is.EqualTo(Vector3.zero), "Opposite buttons cancel.");
        Assert.That(ControllerMotionInput.Movement(Vector2.one, Vector2.one, false, true).magnitude, Is.EqualTo(1).Within(.0001), "Combined input must not exceed the selected speed.");
        Assert.That(ControllerMotionInput.Movement(new Vector2(0, .5f), Vector2.zero, false, false).z, Is.EqualTo(.5f).Within(.0001), "Analog deflection stays proportional.");
    }

    [Test]
    public void DisabledKeyboardInputAlsoSilencesTheController()
    {
        var host = new GameObject("controller input switch");
        var keyboard = host.AddComponent<KeyboardMotionInput>();
        var controller = host.AddComponent<ControllerMotionInput>(); controller.Keyboard = keyboard;
        keyboard.InputEnabled = false;
        Assert.That(controller.Sample(.016f).localMovement, Is.EqualTo(Vector3.zero));
        Assert.That(controller.Sample(.016f).hasAbsolutePose, Is.False);
        Object.DestroyImmediate(host);
    }
}
