// Copyright (c) Petabridge, LLC. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using R3;
using Termina.Clipboard;
using Termina.Input;
using Termina.Layout;
using Termina.Rendering;
using Termina.Terminal;

namespace Termina.Tests.Input;

public class MouseInteractionTests
{
    private static readonly Rect SingleLine = new(0, 0, 40, 1);

    private static MouseEvent Down(int col, int chain = 1, ConsoleModifiers mods = 0)
        => new(col, 0, MouseButton.Left, MouseEventKind.Down, mods, chain);

    private static MouseEvent Drag(int col)
        => new(col, 0, MouseButton.Left, MouseEventKind.Drag);

    // --- TextInputNode caret + selection ---

    [Fact]
    public void Click_PositionsCaret_SoTypingInsertsThere()
    {
        var input = new TextInputNode { Text = "abcdef" };
        input.HandleMouse(Down(3), SingleLine);
        input.HandleInput(new ConsoleKeyInfo('X', ConsoleKey.X, false, false, false));
        Assert.Equal("abcXdef", input.Text);
    }

    [Fact]
    public void DoubleClick_SelectsWord()
    {
        var input = new TextInputNode { Text = "hello world" };
        input.HandleMouse(Down(2, chain: 2), SingleLine);
        Assert.Equal("hello", input.SelectedText);
    }

    [Fact]
    public void TripleClick_SelectsAll()
    {
        var input = new TextInputNode { Text = "hello world" };
        input.HandleMouse(Down(2, chain: 3), SingleLine);
        Assert.Equal("hello world", input.SelectedText);
    }

    [Fact]
    public void Drag_SelectsRange()
    {
        var input = new TextInputNode { Text = "hello world" };
        input.HandleMouse(Down(0), SingleLine);
        input.HandleMouse(Drag(5), SingleLine);
        Assert.Equal("hello", input.SelectedText);
    }

    [Fact]
    public void ShiftClick_ExtendsSelectionFromCaret()
    {
        var input = new TextInputNode { Text = "hello world" };
        input.HandleMouse(Down(0), SingleLine);            // caret at 0
        input.HandleMouse(Down(5, mods: ConsoleModifiers.Shift), SingleLine);
        Assert.Equal("hello", input.SelectedText);
    }

    [Fact]
    public void PlainClick_ClearsExistingSelection()
    {
        var input = new TextInputNode { Text = "hello world" };
        input.HandleMouse(Down(2, chain: 2), SingleLine);  // select "hello"
        Assert.True(input.HasSelection);
        input.HandleMouse(Down(7), SingleLine);            // plain click clears it
        Assert.False(input.HasSelection);
    }

    // --- CopyableTextNode ---

    private sealed class StubClipboard : IClipboardService
    {
        public string? LastCopied { get; private set; }
        public bool Copy(string text) { LastCopied = text; return true; }
    }

    private static CopyableTextNode RenderedCopyable(string content, int width = 40)
    {
        var node = new CopyableTextNode(new StubClipboard(), content);
        var terminal = new VirtualTerminal(width, 4);
        var context = new RegionRenderContext(terminal, 0, 0, width, 4);
        node.Render(context, new Rect(0, 0, width, 4)); // establishes the layout width
        return node;
    }

    [Fact]
    public void CopyableText_DoubleClick_SelectsWord()
    {
        var node = RenderedCopyable("alpha beta gamma");
        node.HandleMouse(Down(7, chain: 2), new Rect(0, 0, 40, 1)); // inside "beta"
        Assert.Equal("beta", node.SelectedText);
    }

    [Fact]
    public void CopyableText_Drag_SelectsRange()
    {
        var node = RenderedCopyable("alpha beta gamma");
        node.HandleMouse(Down(0), new Rect(0, 0, 40, 1));
        node.HandleMouse(Drag(5), new Rect(0, 0, 40, 1));
        Assert.Equal("alpha", node.SelectedText);
    }

    // --- HyperlinkNode ---

    [Fact]
    public void Hyperlink_Click_RaisesActivatedWithUrl()
    {
        var link = new HyperlinkNode("Docs", "https://example.com");
        string? activated = null;
        using var _ = link.Activated.Subscribe(u => activated = u);

        var handled = link.HandleMouse(Down(1), new Rect(0, 0, 4, 1));

        Assert.True(handled);
        Assert.Equal("https://example.com", activated);
    }

    [Fact]
    public void Hyperlink_EnterKey_RaisesActivated()
    {
        var link = new HyperlinkNode("Docs", "https://example.com");
        string? activated = null;
        using var _ = link.Activated.Subscribe(u => activated = u);

        link.HandleInput(new ConsoleKeyInfo('\r', ConsoleKey.Enter, false, false, false));

        Assert.Equal("https://example.com", activated);
    }

    [Fact]
    public void Hyperlink_HoverTogglesUnderlineState()
    {
        var link = new HyperlinkNode("Docs", "https://example.com");
        // No exception and idempotent enter/leave.
        link.OnMouseEnter();
        link.OnMouseLeave();
    }
}
