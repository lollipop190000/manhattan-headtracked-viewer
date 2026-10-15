using System;
using System.Collections.Generic;
using UnityEngine;
namespace WebShooter.Swing
{
    public struct TraversalSample
    {
        public float time, dt;
        public Vector3 position, velocity, previousVelocity, contactNormal;
        public TraversalState state, previousState;
        public bool movementInput, collided;
        public string collider;
    }
    public enum SnagKind { Snag, Stuck }
    public struct SnagEvent { public SnagKind kind; public TraversalSample sample; }
    public sealed class SnagDetector
    {
        private struct Point { public float time, distance; }
        private readonly Queue<Point> window = new Queue<Point>(256);
        private Vector3 lastPosition;
        private float path;
        private bool active, stuckLatched;
        public int Snags { get; private set; }
        public int Stucks { get; private set; }
        public event Action<SnagEvent> Detected;
        public void Reset() { window.Clear(); active=stuckLatched=false; path=0; Snags=Stucks=0; }
        public void Observe(TraversalSample s)
        {
            bool apex = s.state==TraversalState.Swinging && !s.collided;
            bool verticalLanding = s.state==TraversalState.Grounded && s.previousVelocity.y < -5f &&
                Vector3.ProjectOnPlane(s.previousVelocity,Vector3.up).magnitude<.5f;
            if(!s.movementInput || apex || verticalLanding)
            { window.Clear(); active=stuckLatched=false; return; }
            if(s.previousVelocity.magnitude>5f && s.velocity.magnitude<.5f)
            { Snags++; Detected?.Invoke(new SnagEvent{kind=SnagKind.Snag,sample=s}); }
            if(!active){active=true;path=0;lastPosition=s.position;}
            path+=Vector3.Distance(lastPosition,s.position);lastPosition=s.position;
            window.Enqueue(new Point{time=s.time,distance=path});
            while(window.Count>1 && window.Peek().time<s.time-2.001f)window.Dequeue();
            var first=window.Peek();float distance=path-first.distance;
            if(distance>=.3f)stuckLatched=false;
            if(s.time-first.time>=1.99f && distance<.3f && !stuckLatched)
            { Stucks++;stuckLatched=true;Detected?.Invoke(new SnagEvent{kind=SnagKind.Stuck,sample=s}); }
        }
    }
}
