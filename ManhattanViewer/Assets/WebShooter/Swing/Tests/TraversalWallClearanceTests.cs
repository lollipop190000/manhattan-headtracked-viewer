using NUnit.Framework;
using UnityEngine;
using Manhattan.Viewer;
namespace WebShooter.Swing.Tests
{
    public sealed class TraversalWallClearanceTests
    {
        private static SwingPhysics Body(Vector3 p)
        {var s=WebSwingMotionInput.TraversalSettings();s.features.SetFlow(true);s.airDrag=0;var b=new SwingPhysics(s);b.Reset(p);return b;}
        private static GameObject Box(float x,float width,float height=100)
        {var g=GameObject.CreatePrimitive(PrimitiveType.Cube);g.layer=30;g.transform.position=new Vector3(x,height*.5f,0);g.transform.localScale=new Vector3(width,height,100);return g;}
        private static BuildingRecord Record(float x,float width,float height=100)
        {return new BuildingRecord{height=height,groundHeight=0,cityBounds=new Bounds(new Vector3(x,height*.5f,0),new Vector3(width,height,100)),footprint=new[]{new Vector2(x-width*.5f,-50),new Vector2(x+width*.5f,-50),new Vector2(x+width*.5f,50),new Vector2(x-width*.5f,50)}};}
        [TestCase(.8f)][TestCase(1.2f)][TestCase(1.8f)]
        public void EmptyRoofGapFallsWithoutPhantomSupport(float gap)
        {
            var left=Box(-gap*.5f-5,10);var right=Box(gap*.5f+5,10);
            try
            {
                Physics.SyncTransforms();
                foreach(bool resident in new[]{false,true})
                {
                    var b=Body(new Vector3(0,104,0));b.Velocity=Vector3.down*20;using var c=new TraversalCollision(null,b);
                    if(resident)c.CitySolids=new CitySolidRecovery(new[]{Record(-gap*.5f-5,10),Record(gap*.5f+5,10)},b);
                    int supports=0;float maxCorrection=0;
                    for(int i=0;i<60;i++){var p=b.Position;b.Step(1f/120);var requested=b.Position;b.Position=c.Constrain(p,requested);maxCorrection=Mathf.Max(maxCorrection,Vector3.Distance(b.Position,requested));if(b.Grounded)supports++;}
                    TestContext.WriteLine($"GAP width={gap:F3} resident={resident} position={b.Position:F6} velocity={b.Velocity:F6} grounded={b.Grounded} supportSteps={supports} maxCorrection={maxCorrection:F6}");
                    Assert.That(b.Position.y,Is.EqualTo(90.95f).Within(.01f));
                    Assert.That(b.Velocity.y,Is.EqualTo(-32f).Within(.01f));
                    Assert.That(supports,Is.Zero);Assert.That(Mathf.Abs(b.Position.x),Is.LessThan(.001f));
                }
            }
            finally{Object.DestroyImmediate(left);Object.DestroyImmediate(right);}
        }
        [Test] public void PassableGapRecoveryDoesNotDependOnCatalogOrder()
        {
            const float gap=1.2f;var left=Box(-gap*.5f-5,10);var right=Box(gap*.5f+5,10);
            try
            {
                Physics.SyncTransforms();
                foreach(bool reverse in new[]{false,true})
                {
                    var b=Body(new Vector3(0,50,0));var records=new[]{Record(-gap*.5f-5,10),Record(gap*.5f+5,10)};
                    if(reverse)System.Array.Reverse(records);using var c=new TraversalCollision(null,b);c.CitySolids=new CitySolidRecovery(records,b);
                    b.Position=c.Constrain(b.Position,b.Position);var normal=c.FindWall(b.Position,Vector3.forward);
                    Assert.That(Mathf.Abs(b.Position.x),Is.LessThan(.001f));Assert.That(b.ContactSequence,Is.Zero);
                    TestContext.WriteLine($"ORDER reverse={reverse} x={b.Position.x:F6} contacts={b.ContactSequence} leftDistance={b.Position.x+gap*.5f:F6} rightDistance={gap*.5f-b.Position.x:F6} selectedNormal={normal}");
                }
            }
            finally{Object.DestroyImmediate(left);Object.DestroyImmediate(right);}
        }
    }
}
