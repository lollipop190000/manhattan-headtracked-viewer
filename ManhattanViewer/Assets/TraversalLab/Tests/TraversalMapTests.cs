using NUnit.Framework;
using UnityEngine;
namespace Manhattan.TraversalLab.Tests
{
    public sealed class TraversalMapTests
    {
        [Test] public void SeedAndEpisodeReproduceAllGeometryAndEndpoints()
        {
            var a=new TraversalMapLayout(17);var b=new TraversalMapLayout(17);Assert.That(a.Buildings.Count,Is.GreaterThanOrEqualTo(11));
            CollectionAssert.AreEqual(a.Buildings,b.Buildings);
            a.PickEndpoints(8,out var sa,out var ga);b.PickEndpoints(8,out var sb,out var gb);
            Assert.That(sa,Is.EqualTo(sb));Assert.That(ga,Is.EqualTo(gb));Assert.That(sa,Is.Not.EqualTo(ga));
            Assert.That(new TraversalMapLayout(18).Buildings[0],Is.Not.EqualTo(a.Buildings[0]));
        }
    }
}
