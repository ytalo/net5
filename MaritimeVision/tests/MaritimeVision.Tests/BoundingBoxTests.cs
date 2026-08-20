using MaritimeVision.Core.Models;
using Xunit;

namespace MaritimeVision.Tests;

public class BoundingBoxTests
{
    [Fact]
    public void IntersectionOverUnion_IsOneForIdenticalBoxes()
    {
        var box = new BoundingBox(10, 20, 30, 40);
        Assert.Equal(1f, box.IntersectionOverUnion(box), 5);
    }

    [Fact]
    public void IntersectionOverUnion_IsZeroForDisjointBoxes()
    {
        var left = new BoundingBox(0, 0, 10, 10);
        var right = new BoundingBox(50, 50, 10, 10);
        Assert.Equal(0f, left.IntersectionOverUnion(right));
    }

    [Fact]
    public void IntersectionOverUnion_MatchesHandComputedValue()
    {
        // Dos cuadrados de 10x10 desplazados 5 px en cada eje: la interseccion es
        // 5x5 = 25 y la union 100 + 100 - 25 = 175.
        var a = new BoundingBox(0, 0, 10, 10);
        var b = new BoundingBox(5, 5, 10, 10);
        Assert.Equal(25f / 175f, a.IntersectionOverUnion(b), 5);
    }

    [Fact]
    public void IntersectionOverUnion_IsSymmetric()
    {
        var a = new BoundingBox(3, 7, 22, 11);
        var b = new BoundingBox(9, 2, 14, 25);
        Assert.Equal(a.IntersectionOverUnion(b), b.IntersectionOverUnion(a), 6);
    }

    [Fact]
    public void BoxesTouchingAtAnEdgeDoNotOverlap()
    {
        var a = new BoundingBox(0, 0, 10, 10);
        var b = new BoundingBox(10, 0, 10, 10);
        Assert.Equal(0f, a.IntersectionOverUnion(b));
    }

    [Fact]
    public void FromCenter_RoundTripsThroughCorners()
    {
        var box = BoundingBox.FromCenter(100, 50, 40, 20);
        Assert.Equal(80f, box.Left, 4);
        Assert.Equal(40f, box.Top, 4);
        Assert.Equal(120f, box.Right, 4);
        Assert.Equal(60f, box.Bottom, 4);
    }

    [Fact]
    public void XyahRoundTripPreservesGeometry()
    {
        var original = new BoundingBox(12, 34, 56, 78);
        var (cx, cy, aspect, height) = original.ToXyah();
        var restored = BoundingBox.FromXyah(cx, cy, aspect, height);

        Assert.Equal(original.X, restored.X, 3);
        Assert.Equal(original.Y, restored.Y, 3);
        Assert.Equal(original.Width, restored.Width, 3);
        Assert.Equal(original.Height, restored.Height, 3);
    }

    [Fact]
    public void ClampTo_KeepsBoxInsideTheFrame()
    {
        var box = new BoundingBox(-20, -10, 100, 60).ClampTo(50, 40);

        Assert.Equal(0f, box.Left);
        Assert.Equal(0f, box.Top);
        Assert.Equal(50f, box.Right);
        Assert.Equal(40f, box.Bottom);
    }

    [Fact]
    public void ClampTo_CollapsesBoxesFullyOutsideTheFrame()
    {
        var box = new BoundingBox(200, 200, 30, 30).ClampTo(100, 100);
        Assert.Equal(0f, box.Area);
    }
}
