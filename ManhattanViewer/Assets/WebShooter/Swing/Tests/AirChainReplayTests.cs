using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
namespace WebShooter.Swing.Tests
{
    public sealed class AirChainReplayTests
    {
        public static IEnumerable<TestCaseData> Cases()
        {
            foreach(float half in new[]{15f,22f,30f}) foreach(int fps in new[]{30,60,120})
                foreach(var stage in AirChainReplay.Stages())yield return new TestCaseData(stage.x,stage.y,half,fps);
        }
        public static IEnumerable<TestCaseData> CityCases()
        {
            foreach(int fps in new[]{30,60,120})foreach(var stage in AirChainReplay.Stages())
                yield return new TestCaseData(stage.x,stage.y,fps);
        }
        // Retain all failed-case tails before asserting the complete ON group.
        [TestCaseSource(nameof(CityCases))]
        public void ManhattanAirborneChainsAndRightTurns(int before,int after,int fps)
        {
            var catalog=Resources.Load<Manhattan.Viewer.CityCatalog>("CityCatalog");Assert.That(catalog,Is.Not.Null);
            using var map=new AirChainReplay.Map(14,catalog);
            bool passed=true;
            foreach(bool assisted in new[]{false,true})for(int seed=0;seed<3;seed++)
            {
                var tail=new Queue<string>();
                var result=AirChainReplay.Run(map,before,after,seed,fps,frame=>
                {
                    if(!assisted)return;
                    tail.Enqueue($"CITY_TRACE t{frame.time:F3} c{frame.cycle} p{frame.position-map.Center:F2} v{frame.velocity:F2} a{frame.anchor-map.Center:F2} L{frame.ropeLength:F2} {frame.state} reel{frame.reel} collider{frame.collider}");
                    if(tail.Count>fps)tail.Dequeue();
                },assisted);
                TestContext.WriteLine(JsonUtility.ToJson(result));
                if(assisted && !result.passed)foreach(var frame in tail)TestContext.WriteLine(frame);
                if(assisted && !result.passed)passed=false;
            }
            Assert.That(passed,Is.True,$"Manhattan {before}/{after} fps{fps}; see case telemetry");
        }
        [TestCaseSource(nameof(Cases))]
        public void AirborneChainsAndRightTurns(int before,int after,float half,int fps)
        {
            using var map=new AirChainReplay.Map(half);
            bool passed=true;
            foreach(bool assisted in new[]{false,true})for(int seed=0;seed<5;seed++)
            {
                var r=AirChainReplay.Run(map,before,after,seed,fps,assisted:assisted);
                TestContext.WriteLine(JsonUtility.ToJson(r));
                if(assisted && !r.passed)passed=false;
            }
            Assert.That(passed,Is.True,$"{before}/{after} width{half*2} fps{fps}; see case telemetry");
        }
    }
}
