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
        [Test] public void SharedRecoveryDoesNotApplyStartPenetrationTwice()
        {
            var wall=Box(0,10);
            try
            {
                Physics.SyncTransforms();var start=new Vector3(-4.9f,50,0);var b=Body(start);
                var resident=new CitySolidRecovery(new[]{Record(0,10)},b);var residentOnly=resident.Constrain(start,start);
                b.Reset(start);using var c=new TraversalCollision(null,b);c.CitySolids=resident;b.Position=c.Constrain(start,start);
                Assert.That(Vector3.Distance(b.Position,residentOnly),Is.LessThan(.001f));
                Assert.That(c.FindWall(b.Position,Vector3.forward).sqrMagnitude,Is.GreaterThan(.5f));
                var retreat=c.Constrain(b.Position,b.Position+Vector3.left*2);
                Assert.That(retreat.x,Is.EqualTo(b.Position.x-2).Within(.001f));
                TestContext.WriteLine($"OVERLAP startX={start.x:F6} residentOnlyX={residentOnly.x:F6} combinedX={b.Position.x:F6} extraDisplacement={Vector3.Distance(b.Position,residentOnly):F6} wallDetected={c.FindWall(b.Position,Vector3.forward).sqrMagnitude>.5f}");
            }
            finally{Object.DestroyImmediate(wall);}
        }
        [Test] public void SideEntryAndLookingAwayKeepTheActualWall()
        {
            var wall=Box(5,10,300);
            try
            {
                Physics.SyncTransforms();var b=Body(new Vector3(-.801f,100,0));using var c=new TraversalCollision(null,b);
                b.Velocity=Vector3.forward*20;b.WallRunRequested=true;var along=c.FindWall(b.Position,Vector3.forward);
                b.UpdateWall(along,true,1f/120);var sideState=b.State;
                for(int i=0;i<120;i++){b.UpdateWall(c.FindWall(b.Position,Vector3.forward),true,1f/120);var p=b.Position;b.Step(1f/120,Vector3.right);b.Position=c.Constrain(p,b.Position);}
                TestContext.WriteLine($"SIDE detectedNormal={along} initialState={sideState} afterOneSecond={b.State} position={b.Position:F6}");
                b.Reset(new Vector3(-.801f,100,0));b.WallRunRequested=true;b.Velocity=Vector3.right*20;b.Contact(Vector3.left);b.Velocity=Vector3.forward*20;
                var before=b.State;var away=c.FindWall(b.Position,Vector3.left);b.UpdateWall(away,true,1f/120);
                TestContext.WriteLine($"LOOK before={before} awayNormal={away} after={b.State} bodyWallDistance={-b.Position.x:F6}");
                Assert.That(before,Is.EqualTo(TraversalState.WallRunning));Assert.That(away,Is.EqualTo(Vector3.left));Assert.That(sideState,Is.EqualTo(TraversalState.WallRunning));Assert.That(b.State,Is.EqualTo(TraversalState.WallRunning));
                b.Reset(new Vector3(-.321f,100,0));b.Velocity=Vector3.forward*20;b.Attach(b.Position+Vector3.up*50);b.UpdateWall(Vector3.left,true,1f/120);
                Assert.That(b.Attached,Is.True);Assert.That(b.State,Is.EqualTo(TraversalState.Swinging));
                b.Release();b.Velocity=Vector3.down*20;b.UpdateWall(Vector3.left,true,1f/120);Assert.That(b.State,Is.EqualTo(TraversalState.Airborne));
            }
            finally{Object.DestroyImmediate(wall);}
        }
    }
}
