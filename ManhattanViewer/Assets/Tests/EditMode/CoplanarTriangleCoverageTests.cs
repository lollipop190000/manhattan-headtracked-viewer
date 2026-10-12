using Manhattan.Viewer;
using NUnit.Framework;
using UnityEngine;

public sealed class CoplanarTriangleCoverageTests
{
    static Vector3 P(float x,float y,float z=0) => new Vector3(x,y,z);
    static void Quad(CoplanarTriangleCoverage c,float x0,float y0,float x1,float y1,float z=0)
    {
        c.Add(P(x0,y0,z),P(x1,y0,z),P(x0,y1,z));
        c.Add(P(x1,y0,z),P(x1,y1,z),P(x0,y1,z));
    }
    [Test]
    public void DifferentDiagonalAndEdgeSubdivisionsCoverWholeHullTriangle()
    {
        var c=new CoplanarTriangleCoverage();
        Quad(c,0,0,.4f,1);Quad(c,.4f,0,1,1);
        Assert.That(c.Covers(P(0,0),P(1,0),P(1,1)),Is.True);
        Assert.That(c.Covers(P(0,0),P(1,1),P(0,1)),Is.True);
    }
    [Test]
    public void MeasuredHighViewWallIsCoveredByTwoDifferentlySubdividedCoreTriangles()
    {
        var c=new CoplanarTriangleCoverage();
        var a=new Vector3(739.7056274414062f,.22f,3099.30908203125f);
        var b=new Vector3(739.7056274414062f,203.95387268066406f,3099.30908203125f);
        var d=new Vector3(733.9138793945312f,.22f,3102.439697265625f);
        var e=new Vector3(733.9138793945312f,203.95387268066406f,3102.439697265625f);
        c.Add(a,b,e);c.Add(a,e,d);
        Assert.That(c.Covers(new Vector3(736.1729736328125f,.22f,3101.218994140625f),
            new Vector3(736.1729736328125f,203.95399475097656f,3101.218994140625f),
            new Vector3(733.9140014648438f,203.95399475097656f,3102.43994140625f)),Is.True);
    }
    [Test]
    public void DuplicateOverlapDoesNotCountTwiceOrCoverMissingHalf()
    {
        var c=new CoplanarTriangleCoverage();
        for(int i=0;i<5;i++)Quad(c,0,0,.5f,1);
        Assert.That(c.Covers(P(0,0),P(1,0),P(1,1)),Is.False);
    }
    [Test]
    public void InteriorHoleSurvivesEvenWhenAllHullCornersAreCovered()
    {
        var c=new CoplanarTriangleCoverage();
        Quad(c,0,0,1,.3f);Quad(c,0,.7f,1,1);
        Quad(c,0,.3f,.3f,.7f);Quad(c,.7f,.3f,1,.7f);
        Assert.That(c.Covers(P(0,0),P(1,0),P(1,1)),Is.False);
    }
    [TestCase(.0005f,true)]
    [TestCase(.0012f,false)]
    public void PlaneSeparationMustRemainWithinOneMillimetre(float depth,bool covered)
    {
        var c=new CoplanarTriangleCoverage();Quad(c,0,0,1,1,depth);
        Assert.That(c.Covers(P(0,0),P(1,0),P(1,1)),Is.EqualTo(covered));
    }
    [Test]
    public void ReversedWindingCannotClaimTheVisibleFace()
    {
        var c=new CoplanarTriangleCoverage();c.Add(P(0,0),P(1,1),P(1,0));
        Assert.That(c.Covers(P(0,0),P(1,0),P(1,1)),Is.False);
    }
    [Test]
    public void EuclideanCornerToleranceRejectsDiagonalShiftOverOneMillimetre()
    {
        var c=new CoplanarTriangleCoverage();
        Vector3 shift=Vector3.one*-.00049f,opposite=Vector3.one*.00049f;
        c.Add(Vector3.zero+shift,Vector3.right+shift,Vector3.up+shift);
        Assert.That(c.Covers(Vector3.zero+opposite,Vector3.right+opposite,Vector3.up+opposite),Is.False);
    }
}
