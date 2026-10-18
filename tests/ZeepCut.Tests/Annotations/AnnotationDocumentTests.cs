using Xunit;
using ZeepCut.Core.Annotations;
using ZeepCut.Core.Geometry;

namespace ZeepCut.Tests.Annotations;

public class AnnotationDocumentTests
{
    private static RectangleAnnotation Rect(int left, int top, int width, int height) =>
        new(new PixelRect(left, top, width, height));

    [Fact]
    public void Add_AppendsAndAssignsCreationOrder()
    {
        var document = new AnnotationDocument();
        var first = Rect(0, 0, 10, 10);
        var second = Rect(10, 10, 10, 10);

        var returned = document.Add(first);
        document.Add(second);

        Assert.Same(first, returned);
        Assert.Equal(2, document.Count);
        Assert.Same(first, document.Items[0]);
        Assert.Same(second, document.Items[1]);
        Assert.Equal(1, first.CreationOrder);
        Assert.Equal(2, second.CreationOrder);
        Assert.False(document.IsEmpty);
    }

    [Fact]
    public void Add_SameInstanceTwice_Throws()
    {
        var document = new AnnotationDocument();
        var annotation = Rect(0, 0, 10, 10);
        document.Add(annotation);

        Assert.Throws<InvalidOperationException>(() => document.Add(annotation));
    }

    [Fact]
    public void Insert_AtBottom_PutsAnnotationUnderExistingOnes()
    {
        var document = new AnnotationDocument();
        var existing = Rect(0, 0, 10, 10);
        document.Add(existing);

        var bottom = Rect(20, 20, 10, 10);
        document.Insert(bottom, 0);

        Assert.Same(bottom, document.Items[0]);
        Assert.Same(existing, document.Items[1]);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(1)]
    public void Insert_InvalidIndex_Throws(int index)
    {
        var document = new AnnotationDocument();

        Assert.Throws<ArgumentOutOfRangeException>(() => document.Insert(Rect(0, 0, 10, 10), index));
    }

    [Fact]
    public void Remove_ExistingAndMissing()
    {
        var document = new AnnotationDocument();
        var annotation = Rect(0, 0, 10, 10);
        document.Add(annotation);

        Assert.True(document.Remove(annotation));
        Assert.False(document.Remove(annotation));
        Assert.Equal(0, document.Count);
        Assert.True(document.IsEmpty);
    }

    [Fact]
    public void RemoveAt_ReturnsRemovedAnnotation()
    {
        var document = new AnnotationDocument();
        var annotation = Rect(0, 0, 10, 10);
        document.Add(annotation);

        var removed = document.RemoveAt(0);

        Assert.Same(annotation, removed);
        Assert.Empty(document.Items);
    }

    [Fact]
    public void Clear_ReturnsRemovedInOrderAndEmptiesDocument()
    {
        var document = new AnnotationDocument();
        var first = Rect(0, 0, 10, 10);
        var second = Rect(10, 10, 10, 10);
        document.Add(first);
        document.Add(second);

        var removed = document.Clear();

        Assert.Equal(new Annotation[] { first, second }, removed.ToArray());
        Assert.True(document.IsEmpty);
    }

    [Fact]
    public void Clear_DoesNotResetCreationOrderSequence()
    {
        var document = new AnnotationDocument();
        document.Add(Rect(0, 0, 10, 10));
        document.Clear();

        var next = Rect(0, 0, 10, 10);
        document.Add(next);

        Assert.Equal(2, next.CreationOrder);
    }

    [Fact]
    public void Insert_ExistingAnnotation_KeepsCreationOrder()
    {
        var document = new AnnotationDocument();
        var first = Rect(0, 0, 10, 10);
        var second = Rect(10, 10, 10, 10);
        document.Add(first);
        document.Add(second);

        document.Remove(first);
        document.Insert(first, 0);

        Assert.Equal(1, first.CreationOrder);
        Assert.Same(first, document.Items[0]);
    }

    [Fact]
    public void HitTest_ReturnsTopmostHit()
    {
        var document = new AnnotationDocument();
        var bottom = Rect(0, 0, 100, 100);
        var top = new EllipseAnnotation(new PixelRect(20, 20, 100, 100));
        document.Add(bottom);
        document.Add(top);

        var hit = document.HitTest(50, 50);

        Assert.Same(top, hit);
    }

    [Fact]
    public void HitTest_SkipsNonHitsAndReturnsNull()
    {
        var document = new AnnotationDocument();
        document.Add(Rect(0, 0, 10, 10));

        Assert.Null(document.HitTest(500, 500));
    }

    [Fact]
    public void HitTest_RespectsTolerance()
    {
        var document = new AnnotationDocument();
        document.Add(Rect(0, 0, 10, 10));

        Assert.Null(document.HitTest(15, 5, 0));
        Assert.NotNull(document.HitTest(15, 5, 5));
    }

    [Fact]
    public void HitTest_AfterBringToFront_ReturnsNewTop()
    {
        var document = new AnnotationDocument();
        var first = Rect(0, 0, 100, 100);
        var second = Rect(0, 0, 100, 100);
        document.Add(first);
        document.Add(second);

        document.BringToFront(first);

        Assert.Same(first, document.HitTest(50, 50));
        Assert.Same(second, document.Items[0]);
    }

    [Fact]
    public void SendToBack_MovesAnnotationToBottom()
    {
        var document = new AnnotationDocument();
        var first = Rect(0, 0, 10, 10);
        var second = Rect(0, 0, 10, 10);
        document.Add(first);
        document.Add(second);

        document.SendToBack(second);

        Assert.Same(second, document.Items[0]);
        Assert.Same(first, document.Items[1]);
    }

    [Fact]
    public void MoveToIndex_Reorders()
    {
        var document = new AnnotationDocument();
        var first = Rect(0, 0, 10, 10);
        var second = Rect(0, 0, 10, 10);
        var third = Rect(0, 0, 10, 10);
        document.Add(first);
        document.Add(second);
        document.Add(third);

        document.MoveToIndex(first, 1);

        Assert.Equal(new Annotation[] { second, first, third }, document.Items.ToArray());
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(3)]
    public void MoveToIndex_InvalidIndex_Throws(int index)
    {
        var document = new AnnotationDocument();
        var annotation = Rect(0, 0, 10, 10);
        document.Add(annotation);

        Assert.Throws<ArgumentOutOfRangeException>(() => document.MoveToIndex(annotation, index));
    }

    [Fact]
    public void MoveToIndex_NotInDocument_Throws()
    {
        var document = new AnnotationDocument();

        Assert.Throws<ArgumentException>(() => document.MoveToIndex(Rect(0, 0, 10, 10), 0));
    }

    [Fact]
    public void MoveToIndex_SameIndex_IsNoOp()
    {
        var document = new AnnotationDocument();
        var annotation = Rect(0, 0, 10, 10);
        document.Add(annotation);

        document.MoveToIndex(annotation, 0);

        Assert.Same(annotation, document.Items[0]);
    }

    [Fact]
    public void Bounds_IsUnionOfAnnotations()
    {
        var document = new AnnotationDocument();
        document.Add(new RectangleAnnotation(new PixelRect(10, 20, 30, 40)));
        document.Add(new EllipseAnnotation(new PixelRect(100, 5, 50, 50)));

        Assert.Equal(new PixelRect(10, 5, 140, 55), document.Bounds);
    }

    [Fact]
    public void Bounds_IgnoresDegenerateBounds()
    {
        var document = new AnnotationDocument();
        document.Add(new LineAnnotation(new PixelPoint(5, 5), new PixelPoint(5, 5)));

        Assert.Equal(PixelRect.Empty, document.Bounds);
    }

    [Fact]
    public void Bounds_EmptyDocument_IsEmpty()
    {
        Assert.Equal(PixelRect.Empty, new AnnotationDocument().Bounds);
    }

    [Fact]
    public void Enumeration_FollowsOrder()
    {
        var document = new AnnotationDocument();
        var first = Rect(0, 0, 10, 10);
        var second = Rect(0, 0, 10, 10);
        document.Add(first);
        document.Add(second);

        Assert.Equal(new Annotation[] { first, second }, document.ToArray());
    }

    [Fact]
    public void IndexOfAndContains_UseReferenceIdentity()
    {
        var document = new AnnotationDocument();
        var annotation = Rect(0, 0, 10, 10);
        var sameValues = Rect(0, 0, 10, 10);

        document.Add(annotation);

        Assert.Equal(0, document.IndexOf(annotation));
        Assert.Equal(-1, document.IndexOf(sameValues));
        Assert.True(document.Contains(annotation));
        Assert.False(document.Contains(sameValues));
    }
}
