using System;
using Manhattan.Viewer;
using NUnit.Framework;
using UnityEngine;

public class CityBuildRegionTests
{
    private CityBuildRegion Region() => new CityBuildRegion { centerX = 0, centerZ = 0, radius = 450, armLength = 1800, halfWidth = 150 };
    [TestCase(0, 0, true)]
    [TestCase(300, 300, true)]
    [TestCase(1000, 0, true)]
    [TestCase(0, -1700, true)]
    [TestCase(1000, 1000, false)]
    [TestCase(2000, 0, false)]
    public void CircleAndCrossUnion(float x, float z, bool expected) => Assert.That(Region().Intersects(new Bounds(new Vector3(x, 0, z), Vector3.one)), Is.EqualTo(expected));
    [Test] public void IntersectingTileIsRetainedEvenWhenItsCenterIsOutside()
        => Assert.That(Region().Intersects(new Bounds(new Vector3(500, 0, 300), new Vector3(300, 100, 300))), Is.True);
    [Test] public void AltitudeDoesNotAffectFootprintSelection()
        => Assert.That(Region().Intersects(new Bounds(new Vector3(0, 900, 0), Vector3.one)), Is.True);
    [Test] public void InvalidDimensionsFailBeforeChangingBuildInputs()
    {
        var r = Region(); r.radius = float.NaN; Assert.Throws<ArgumentException>(() => r.Validate());
        r.radius = 450; r.halfWidth = -1; Assert.Throws<ArgumentException>(() => r.Validate());
    }
}
