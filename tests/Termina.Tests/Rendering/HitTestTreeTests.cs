// Copyright (c) Petabridge, LLC. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using Termina.Layout;
using Termina.Rendering;

namespace Termina.Tests.Rendering;

public class HitTestTreeTests
{
    private sealed class Marker(string name)
    {
        public string Name { get; } = name;
        public override string ToString() => Name;
    }

    [Fact]
    public void HitTest_ReturnsTopMostContainingEntry()
    {
        var tree = new HitTestTree();
        var bottom = new Marker("bottom");
        var top = new Marker("top");
        tree.Register(bottom, new Rect(0, 0, 10, 10), HitTestKind.Component);
        tree.Register(top, new Rect(2, 2, 4, 4), HitTestKind.Component); // drawn later

        // Inside both -> top-most (last drawn) wins.
        Assert.Same(top, tree.HitTest(3, 3)!.Value.Node);
        // Inside only the bottom region.
        Assert.Same(bottom, tree.HitTest(8, 8)!.Value.Node);
        // Outside everything.
        Assert.Null(tree.HitTest(20, 20));
    }

    [Fact]
    public void HitTest_FilteredByKind()
    {
        var tree = new HitTestTree();
        var text = new Marker("text");
        var link = new Marker("link");
        tree.Register(text, new Rect(0, 0, 10, 1), HitTestKind.Text);
        tree.Register(link, new Rect(3, 0, 4, 1), HitTestKind.Link);

        Assert.Same(link, tree.HitTest(4, 0, HitTestKind.Link)!.Value.Node);
        Assert.Same(text, tree.HitTest(0, 0, HitTestKind.Text)!.Value.Node);
        Assert.Null(tree.HitTest(0, 0, HitTestKind.Link)); // no link at column 0
    }

    [Fact]
    public void HitTestPath_ReturnsContainingEntriesTopMostFirst()
    {
        var tree = new HitTestTree();
        var outer = new Marker("outer");
        var inner = new Marker("inner");
        tree.Register(outer, new Rect(0, 0, 10, 10), HitTestKind.Component);
        tree.Register(inner, new Rect(2, 2, 4, 4), HitTestKind.Component);

        var path = tree.HitTestPath(3, 3);
        Assert.Equal(2, path.Count);
        Assert.Same(inner, path[0].Node); // top-most first
        Assert.Same(outer, path[1].Node);
    }

    [Fact]
    public void HitTestPath_StopsAtBackdrop()
    {
        var tree = new HitTestTree();
        var page = new Marker("page");
        var backdrop = new Marker("backdrop");
        var modal = new Marker("modal");
        tree.Register(page, new Rect(0, 0, 20, 20), HitTestKind.Component);
        tree.Register(backdrop, new Rect(0, 0, 20, 20), HitTestKind.Backdrop);
        tree.Register(modal, new Rect(5, 5, 6, 6), HitTestKind.Component);

        // Click on the modal: modal then backdrop, never the page beneath.
        var onModal = tree.HitTestPath(6, 6);
        Assert.Same(modal, onModal[0].Node);
        Assert.Same(backdrop, onModal[1].Node);
        Assert.DoesNotContain(onModal, e => ReferenceEquals(e.Node, page));

        // Click outside the modal but on the backdrop: stops at backdrop.
        var onBackdrop = tree.HitTestPath(1, 1);
        Assert.Same(backdrop, onBackdrop[^1].Node);
        Assert.DoesNotContain(onBackdrop, e => ReferenceEquals(e.Node, page));
    }

    [Fact]
    public void ZeroAreaRegions_AreIgnored()
    {
        var tree = new HitTestTree();
        tree.Register(new Marker("empty"), new Rect(5, 5, 0, 0), HitTestKind.Component);
        Assert.Equal(0, tree.Count);
    }

    [Fact]
    public void Clear_ResetsEntriesAndHoverFlag()
    {
        var tree = new HitTestTree();
        tree.Register(new Marker("x"), new Rect(0, 0, 5, 5), HitTestKind.Component);
        Assert.Equal(1, tree.Count);
        tree.Clear();
        Assert.Equal(0, tree.Count);
        Assert.False(tree.HasHoverTarget);
    }
}
