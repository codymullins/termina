// Copyright (c) Petabridge, LLC. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using R3;
using Termina.Components.Streaming;
using Termina.Input;
using Termina.Layout;
using Termina.Rendering;
using Termina.Terminal;

namespace Termina.Tests.Layout;

public class StreamingTextNodeSelectionTests
{
    private static readonly Rect Bounds = new(0, 0, 40, 6);

    private static StreamingTextNode Rendered(Action<StreamingTextNode> populate)
    {
        var node = StreamingTextNode.Create();
        populate(node);
        var terminal = new VirtualTerminal(40, 6);
        var context = new RegionRenderContext(terminal, 0, 0, 40, 6);
        node.Render(context, Bounds);
        return node;
    }

    private static MouseEvent Down(int col, int row = 0, int chain = 1) =>
        new(col, row, MouseButton.Left, MouseEventKind.Down, ClickChain: chain);

    private static MouseEvent Drag(int col, int row = 0) =>
        new(col, row, MouseButton.Left, MouseEventKind.Drag);

    private static MouseEvent Up(int col, int row = 0) =>
        new(col, row, MouseButton.Left, MouseEventKind.Up);

    private static MouseEvent Move(int col, int row = 0) =>
        new(col, row, MouseButton.None, MouseEventKind.Move);

    [Fact]
    public void Drag_SelectsRange()
    {
        var node = Rendered(n => { n.Append("hello world"); n.Append("\n"); });
        node.HandleMouse(Down(0), Bounds);
        node.HandleMouse(Drag(5), Bounds);
        Assert.Equal("hello", node.SelectedText);
    }

    [Fact]
    public void DoubleClick_SelectsWord()
    {
        var node = Rendered(n => { n.Append("hello world"); n.Append("\n"); });
        node.HandleMouse(Down(7, chain: 2), Bounds); // inside "world"
        Assert.Equal("world", node.SelectedText);
    }

    [Fact]
    public void TripleClick_SelectsLine()
    {
        var node = Rendered(n => { n.Append("hello world"); n.Append("\n"); });
        node.HandleMouse(Down(3, chain: 3), Bounds);
        Assert.Equal("hello world", node.SelectedText);
    }

    [Fact]
    public void PlainClickOnLink_NoDrag_ActivatesLink()
    {
        string? activated = null;
        var node = Rendered(n =>
        {
            n.Append(new StyledSegment("docs", new TextStyle(Color.Blue)) { Link = "https://x" });
            n.Append("\n");
        });
        using var _ = node.LinkActivated.Subscribe(u => activated = u);

        node.HandleMouse(Down(2), Bounds);
        node.HandleMouse(Up(2), Bounds);

        Assert.Equal("https://x", activated);
    }

    [Fact]
    public void ClickWithSameCellJitterDrag_StillActivatesLink()
    {
        // A real click often includes sub-cell jitter the terminal reports as a same-cell drag.
        // That must not suppress link activation, since it produces no actual selection.
        string? activated = null;
        var node = Rendered(n =>
        {
            n.Append(new StyledSegment("docs", new TextStyle(Color.Blue)) { Link = "https://x" });
            n.Append("\n");
        });
        using var _ = node.LinkActivated.Subscribe(u => activated = u);

        node.HandleMouse(Down(2), Bounds);
        node.HandleMouse(Drag(2), Bounds); // jitter: same cell
        node.HandleMouse(Up(2), Bounds);

        Assert.Equal("https://x", activated);
    }

    [Fact]
    public void DragOverLink_SelectsInsteadOfActivating()
    {
        string? activated = null;
        var node = Rendered(n =>
        {
            n.Append(new StyledSegment("docs", new TextStyle(Color.Blue)) { Link = "https://x" });
            n.Append("\n");
        });
        using var _ = node.LinkActivated.Subscribe(u => activated = u);

        node.HandleMouse(Down(0), Bounds);
        node.HandleMouse(Drag(4), Bounds);
        node.HandleMouse(Up(4), Bounds);

        Assert.Null(activated);
        Assert.Equal("docs", node.SelectedText);
    }

    [Fact]
    public void Move_OverLink_EmitsHoveredUrl()
    {
        string? hovered = "sentinel";
        var node = Rendered(n =>
        {
            n.Append(new StyledSegment("docs", new TextStyle(Color.Blue)) { Link = "https://x" });
            n.Append("\n");
        });
        using var _ = node.HoveredLink.Subscribe(u => hovered = u);

        node.HandleMouse(Move(2), Bounds);
        Assert.Equal("https://x", hovered);

        node.HandleMouse(Move(30), Bounds); // past the link text
        Assert.Null(hovered);
    }

    [Fact]
    public void SelectionCompleted_FiresOnDragUp()
    {
        string? completed = null;
        var node = Rendered(n => { n.Append("hello world"); n.Append("\n"); });
        using var _ = node.SelectionCompleted.Subscribe(t => completed = t);

        node.HandleMouse(Down(0), Bounds);
        node.HandleMouse(Drag(5), Bounds);
        node.HandleMouse(Up(5), Bounds);

        Assert.Equal("hello", completed);
    }

    [Fact]
    public void SoftWrappedSelection_RejoinsWithoutSpuriousNewline()
    {
        // One logical line longer than the 40-col viewport wraps into two visual rows; selecting
        // across the wrap must copy it back as a single line (no '\n').
        var longLine = new string('a', 30) + " " + new string('b', 30);
        var node = Rendered(n => { n.Append(longLine); n.Append("\n"); });

        node.HandleMouse(Down(0, row: 0), Bounds);
        node.HandleMouse(Drag(30, row: 1), Bounds);
        node.HandleMouse(Up(30, row: 1), Bounds);

        Assert.DoesNotContain('\n', node.SelectedText);
        Assert.StartsWith("aaa", node.SelectedText);
    }
}
