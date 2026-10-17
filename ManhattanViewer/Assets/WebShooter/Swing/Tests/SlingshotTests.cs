using NUnit.Framework;
namespace WebShooter.Swing.Tests
{
    public sealed class SlingshotTests
    {
        [Test] public void GroundChargeReleaseAndFastMomentum()=>SlingshotChecks.Run((ok,name)=>Assert.That(ok,Is.True,name),(name,value)=>TestContext.WriteLine($"LAUNCH {name}={value:R}"));
        [Test] public void CornerSurfaceMeasurements()=>SlingshotChecks.Corner((ok,name)=>Assert.That(ok,Is.True,name),(name,value)=>TestContext.WriteLine($"CORNER {name}={value:R}"));
        [Test] public void WallAndConstraintSafety()=>SlingshotSafetyChecks.Run((ok,name)=>Assert.That(ok,Is.True,name),(name,value)=>TestContext.WriteLine($"SAFETY {name}={value:R}"));
    }
}
