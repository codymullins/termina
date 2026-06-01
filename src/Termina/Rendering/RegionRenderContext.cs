// Copyright (c) Petabridge, LLC. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using Termina.Terminal;

namespace Termina.Rendering;

/// <summary>
/// Render context that translates relative coordinates to absolute screen positions.
/// All rendering is clipped to the region bounds.
/// </summary>
public sealed class RegionRenderContext : IRenderContext
{
    private readonly IAnsiTerminal _terminal;
    private readonly int _offsetX;
    private readonly int _offsetY;
    private readonly HitTestTree? _hitTest;

    /// <summary>
    /// Create a render context for a specific screen region.
    /// </summary>
    /// <param name="terminal">The terminal to render to.</param>
    /// <param name="offsetX">The X offset (screen column) of the region's top-left corner.</param>
    /// <param name="offsetY">The Y offset (screen row) of the region's top-left corner.</param>
    /// <param name="width">The width of the region.</param>
    /// <param name="height">The height of the region.</param>
    /// <param name="hitTest">Optional frame-scoped hit-test index for mouse routing.</param>
    public RegionRenderContext(IAnsiTerminal terminal, int offsetX, int offsetY, int width, int height, HitTestTree? hitTest = null)
    {
        _terminal = terminal;
        _offsetX = offsetX;
        _offsetY = offsetY;
        _hitTest = hitTest;
        Width = width;
        Height = height;
    }

    /// <inheritdoc />
    public int Width { get; }

    /// <inheritdoc />
    public int Height { get; }

    /// <inheritdoc />
    public void WriteAt(int x, int y, string text)
    {
        if (y < 0 || y >= Height || x >= Width)
            return;

        // Clip text to fit within region
        var startX = Math.Max(0, x);
        var skipWidth = startX - x;
        var availableWidth = Width - startX;

        if (TerminalText.GetDisplayWidth(text) <= skipWidth || availableWidth <= 0)
            return;

        var clippedText = TerminalText.SliceByWidth(text, skipWidth, availableWidth);
        if (clippedText.Length == 0)
            return;

        _terminal.MoveTo(_offsetX + startX, _offsetY + y);
        _terminal.Write(clippedText);
    }

    /// <inheritdoc />
    public void WriteAt(int x, int y, char c)
    {
        if (x < 0 || x >= Width || y < 0 || y >= Height)
            return;

        _terminal.MoveTo(_offsetX + x, _offsetY + y);
        _terminal.Write(c);
    }

    /// <inheritdoc />
    public void WriteControlAt(int x, int y, string sequence)
    {
        if (x < 0 || x >= Width || y < 0 || y >= Height || string.IsNullOrEmpty(sequence))
            return;

        _terminal.WriteControlAt(_offsetX + x, _offsetY + y, sequence);
    }

    /// <inheritdoc />
    public void SetForeground(Color color)
    {
        _terminal.SetForeground(color);
    }

    /// <inheritdoc />
    public void SetBackground(Color color)
    {
        _terminal.SetBackground(color);
    }

    /// <inheritdoc />
    public void ResetColors()
    {
        _terminal.ResetColors();
    }

    /// <inheritdoc />
    public void SetDecoration(TextDecoration decoration)
    {
        _terminal.SetDecoration(decoration);
    }

    /// <inheritdoc />
    public void ApplyStyle(TextStyle style)
    {
        if (style.HasForeground)
            SetForeground(style.Foreground);
        if (style.HasBackground)
            SetBackground(style.Background);
        if (style.HasDecoration)
            SetDecoration(style.Decoration);
    }

    /// <inheritdoc />
    public void Fill(int x, int y, int width, int height, char c = ' ')
    {
        // Clip to region bounds
        var startX = Math.Max(0, x);
        var startY = Math.Max(0, y);
        var endX = Math.Min(Width, x + width);
        var endY = Math.Min(Height, y + height);

        if (startX >= endX || startY >= endY)
            return;

        var fillWidth = endX - startX;
        var fillLine = new string(c, fillWidth);

        for (var row = startY; row < endY; row++)
        {
            _terminal.MoveTo(_offsetX + startX, _offsetY + row);
            _terminal.Write(fillLine);
        }
    }

    /// <inheritdoc />
    public void Clear()
    {
        Fill(0, 0, Width, Height);
    }

    /// <inheritdoc />
    public IRenderContext CreateSubContext(Layout.Rect bounds)
    {
        // Clip bounds to our region
        var clippedX = Math.Max(0, bounds.X);
        var clippedY = Math.Max(0, bounds.Y);
        var clippedRight = Math.Min(Width, bounds.Right);
        var clippedBottom = Math.Min(Height, bounds.Bottom);

        var clippedWidth = Math.Max(0, clippedRight - clippedX);
        var clippedHeight = Math.Max(0, clippedBottom - clippedY);

        return new RegionRenderContext(
            _terminal,
            _offsetX + clippedX,
            _offsetY + clippedY,
            clippedWidth,
            clippedHeight,
            _hitTest);
    }

    /// <inheritdoc />
    public void RegisterHit(object node, Layout.Rect bounds, HitTestKind kind)
    {
        if (_hitTest is null)
            return;

        var absolute = new Layout.Rect(_offsetX + bounds.X, _offsetY + bounds.Y, bounds.Width, bounds.Height);
        _hitTest.Register(node, absolute, kind);
    }

    /// <inheritdoc />
    public void SetLink(string? uri) => _terminal.SetLink(uri);
}
