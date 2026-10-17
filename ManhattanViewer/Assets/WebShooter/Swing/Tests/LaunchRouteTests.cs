using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
namespace WebShooter.Swing.Tests
{
    public sealed class LaunchRouteTests
    {
        public static IEnumerable<TestCaseData> Cases()
        {
            foreach(int fps in new[]{30,60,120})foreach(bool ground in new[]{false,true})
            {
                foreach(float half in new[]{15f,22f,30f})foreach(var stage in SlingshotPlayerValidation.Stages())
                    yield return new TestCaseData(stage.x,stage.y,stage.w,ground,stage.z!=0,fps,half);
            }
        }
        [TestCaseSource(nameof(Cases))]
        public void BilateralLaunchIntoContinuousLeftRightRoutes(int before,int after,int sign,bool ground,bool zigzag,int fps,float half)
        {
            using var map=new AirChainReplay.Map(half,grid:zigzag,gridSpacing:after==2?100:180);
            bool passed=true;
            for(int seed=0;seed<3;seed++)
            {
                var tail=new Queue<string>();var transitions=new System.Collections.Generic.List<string>();int lastCycle=-1;
                var r=AirChainReplay.Run(map,before,after,seed,fps,f=>{if(f.cycle!=lastCycle){transitions.Add($"STAGE {f.cycle} p{f.position-map.Center:F2} v{f.velocity:F2}");lastCycle=f.cycle;}tail.Enqueue($"ROUTE t{f.time:F2} cycle{f.cycle} p{f.position-map.Center:F2} v{f.velocity:F2} a{f.anchor-map.Center:F2} L{f.ropeLength:F2}");if(tail.Count>fps)tail.Dequeue();},
                    turnSign:sign,groundLaunch:ground,zigzag:zigzag,modernPilot:true);
                TestContext.WriteLine(JsonUtility.ToJson(r));
                if(!r.passed){passed=false;foreach(var f in transitions)TestContext.WriteLine(f);foreach(var f in tail)TestContext.WriteLine(f);}
            }
            Assert.That(passed,Is.True,"see measured replay tail");
        }
        [TestCase(-1,30)][TestCase(-1,60)][TestCase(-1,120)][TestCase(1,30)][TestCase(1,60)][TestCase(1,120)]
        public void ManhattanLeftRightTurns(int sign,int fps)
        {
            var catalog=Resources.Load<Manhattan.Viewer.CityCatalog>("CityCatalog");Assert.That(catalog,Is.Not.Null);
            using var map=new AirChainReplay.Map(14,catalog);bool passed=true;
            foreach(int n in new[]{0,3,5,10})for(int seed=0;seed<3;seed++)
            {
                var tail=new Queue<string>();
                var r=AirChainReplay.Run(map,n,n==0?1:n,seed,fps,f=>{tail.Enqueue($"CITY_LEFT t{f.time:F3} c{f.cycle} p{f.position-map.Center:F3} v{f.velocity:F3} a{f.anchor-map.Center:F3} L{f.ropeLength:F3} reel{f.reel} {f.state} {f.collider}");if(tail.Count>fps*2)tail.Dequeue();},turnSign:sign,modernPilot:true);
                TestContext.WriteLine(JsonUtility.ToJson(r));if(!r.passed){passed=false;foreach(var frame in tail)TestContext.WriteLine(frame);}
            }
            Assert.That(passed,Is.True,"catalog route telemetry");
        }
    }
}
