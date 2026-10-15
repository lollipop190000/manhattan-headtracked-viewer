using NUnit.Framework;
using UnityEngine;
namespace WebShooter.Swing.Tests
{
    public sealed class TraversalInputTests
    {
        [Test] public void BufferedInputConsumesEdgesButKeepsHolds()
        {
            var input=new BufferedTraversalInput{Movement=Vector2.up,LookRate=new Vector2(90,0),Actions=new TraversalActions{active=true,fireHeld=true,firePressed=true,jump=true,reel=1}};
            var first=input.ReadActions();var second=input.ReadActions();
            Assert.That(first.firePressed && first.jump,Is.True);Assert.That(second.firePressed || second.jump,Is.False);
            Assert.That(second.fireHeld,Is.True);Assert.That(second.reel,Is.EqualTo(1));
            Assert.That(input.Sample(1f/60).lookDegrees.x,Is.EqualTo(1.5f));
        }
    }
}
