// Copyright (c) Petabridge, LLC. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using R3;
using Termina.Input;
using Termina.Rendering;
using Termina.Terminal;

namespace Termina.Layout;

/// <summary>
/// A single-line clickable link. Activates on a left click or, when focused, on Enter, raising
/// <see cref="Activated"/> with the URL. While full mouse tracking is active the host terminal
/// forwards plain clicks to the application, so an explicit modifier is not required to follow an
/// app-managed link.
/// </summary>
public sealed class HyperlinkNode : LayoutNode, IHyperlink, IMouseAware, IHoverAware, IFocusable, IInvalidatingNode
{
    private readonly Subject<Unit> _invalidated = new();
    private readonly Subject<string> _activated = new();
    private bool _hovered;
    private bool _hasFocus;
    private bool _disposed;

    public HyperlinkNode(string text, string url)
    {
        Text = text ?? string.Empty;
        Url = url ?? string.Empty;
        HeightConstraint = new SizeConstraint.Fixed(1);
        WidthConstraint = new SizeConstraint.Auto();
    }

    /// <summary>The visible link text.</summary>
    public string Text { get; private set; }

    /// <inheritdoc />
    public string Url { get; private set; }

    /// <summary>Link text color.</summary>
    public Color Foreground { get; private set; } = Color.BrightBlue;

    /// <summary>Emits the URL whenever the link is activated.</summary>
    public Observable<string> Activated => _activated;

    /// <inheritdoc />
    public Observable<Unit> Invalidated => _invalidated;

    /// <inheritdoc />
    public bool CanFocus => true;

    /// <inheritdoc />
    public bool HasFocus => _hasFocus;

    /// <inheritdoc />
    public int FocusPriority => 5;

    /// <summary>Set the visible text.</summary>
    public HyperlinkNode WithText(string text)
    {
        Text = text ?? string.Empty;
        _invalidated.OnNext(Unit.Default);
        return this;
    }

    /// <summary>Set the target URL.</summary>
    public HyperlinkNode WithUrl(string url)
    {
        Url = url ?? string.Empty;
        return this;
    }

    /// <summary>Set the link text color.</summary>
    public HyperlinkNode WithForeground(Color color)
    {
        Foreground = color;
        return this;
    }

    /// <inheritdoc />
    public override Size Measure(Size available)
    {
        var width = WidthConstraint.Compute(available.Width, TerminalText.GetDisplayWidth(Text), available.Width);
        return new Size(width, 1);
    }

    /// <inheritdoc />
    public override void Render(IRenderContext context, Rect bounds)
    {
        if (!bounds.HasArea)
            return;

        context.RegisterHit(this, bounds, HitTestKind.Link);

        var ctx = context.CreateSubContext(bounds);
        ctx.SetForeground(Foreground);
        // Underline on hover or focus to signal interactivity.
        if (_hovered || _hasFocus)
            ctx.SetDecoration(TextDecoration.Underline);

        var text = TerminalText.GetDisplayWidth(Text) > bounds.Width
            ? TerminalText.TruncateToWidth(Text, bounds.Width)
            : Text;
        ctx.WriteAt(0, 0, text);
        ctx.ResetColors();
    }

    /// <inheritdoc />
    public bool HandleMouse(MouseEvent e, Rect bounds)
    {
        if (e.Kind == MouseEventKind.Down && e.Button == MouseButton.Left)
        {
            Activate();
            return true;
        }
        return false;
    }

    /// <inheritdoc />
    public void OnMouseEnter()
    {
        _hovered = true;
        _invalidated.OnNext(Unit.Default);
    }

    /// <inheritdoc />
    public void OnMouseLeave()
    {
        _hovered = false;
        _invalidated.OnNext(Unit.Default);
    }

    /// <inheritdoc />
    public void OnFocused()
    {
        _hasFocus = true;
        _invalidated.OnNext(Unit.Default);
    }

    /// <inheritdoc />
    public void OnBlurred()
    {
        _hasFocus = false;
        _invalidated.OnNext(Unit.Default);
    }

    /// <inheritdoc />
    public bool HandleInput(ConsoleKeyInfo key)
    {
        if (key.Key == ConsoleKey.Enter)
        {
            Activate();
            return true;
        }
        return false;
    }

    private void Activate()
    {
        if (!string.IsNullOrEmpty(Url))
            _activated.OnNext(Url);
    }

    /// <inheritdoc />
    public override void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        _activated.OnCompleted();
        _activated.Dispose();
        _invalidated.OnCompleted();
        _invalidated.Dispose();
        base.Dispose();
    }
}
