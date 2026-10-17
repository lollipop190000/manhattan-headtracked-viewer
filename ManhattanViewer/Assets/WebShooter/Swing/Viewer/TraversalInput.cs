using System;
using Manhattan.Viewer;
using UnityEngine;
using UnityEngine.InputSystem;
namespace WebShooter.Swing
{
    public struct TraversalActions
    {
        public bool active,firePressed,fireHeld,release,jump,toggleFlight;
        public bool launchPressed,launchHeld,launchReleased;
        public float reel,assistDelta,viewportX;
        public Ray aim;
    }
    public interface ITraversalInputSource : IMotionInputSource
    { TraversalActions ReadActions(); }

    public sealed class HumanTraversalInput : ITraversalInputSource
    {
        private readonly IMotionInputSource movement;
        private readonly Func<bool> canRead;
        private bool capturedLast;
        private bool launchHeldLast;
        private float side=.7f;
        public HumanTraversalInput(IMotionInputSource source,Func<bool> active){movement=source;canRead=active;}
        public MotionCommand Sample(float dt)=>movement!=null?movement.Sample(dt):default;
        public TraversalActions ReadActions()
        {
            bool active=canRead();var k=Keyboard.current;var m=Mouse.current;
            if(active && m!=null && Mathf.Abs(m.delta.ReadValue().x)>.5f)side=m.delta.ReadValue().x<0?.3f:.7f;
            bool edge=active && capturedLast;
            var a=new TraversalActions{active=active,firePressed=edge && m!=null && m.leftButton.wasPressedThisFrame && Camera.main!=null,
                fireHeld=active && m!=null && m.leftButton.isPressed,
                jump=edge && k!=null && k.spaceKey.wasPressedThisFrame,toggleFlight=edge && k!=null && k.rKey.wasPressedThisFrame,
                launchPressed=edge && k!=null && k.qKey.isPressed && !launchHeldLast,
                launchHeld=active && k!=null && k.qKey.isPressed,
                launchReleased=edge && launchHeldLast && (k==null || !k.qKey.isPressed),
                reel=active?((m!=null && m.rightButton.isPressed || k!=null && k.eKey.isPressed)?1:0)-(k!=null && k.zKey.isPressed?1:0):0,
                assistDelta=edge && k!=null?((k.rightBracketKey.wasPressedThisFrame?1:0)-(k.leftBracketKey.wasPressedThisFrame?1:0))*.25f:0,
                viewportX=Cursor.lockState==CursorLockMode.Locked?side:m!=null?m.position.ReadValue().x/Mathf.Max(1,Screen.width):.7f,
                aim=Camera.main!=null?Camera.main.ViewportPointToRay(new Vector3(.5f,.5f,0)):default};
            capturedLast=active;launchHeldLast=k!=null && k.qKey.isPressed;return a;
        }
    }
    public sealed class BufferedTraversalInput : ITraversalInputSource
    {
        public Vector2 Movement,LookRate;
        public TraversalActions Actions;
        public MotionCommand Sample(float dt)=>new MotionCommand{localMovement=new Vector3(Movement.x,0,Movement.y),lookDegrees=LookRate*dt};
        public TraversalActions ReadActions()
        {
            var result=Actions;Actions.firePressed=Actions.jump=Actions.toggleFlight=Actions.launchPressed=Actions.launchReleased=false;Actions.assistDelta=0;Actions.release=false;return result;
        }
        public void Clear(){Movement=LookRate=Vector2.zero;Actions=default;}
    }
}
