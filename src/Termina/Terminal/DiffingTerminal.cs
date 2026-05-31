// Copyright (c) Petabridge, LLC. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

namespace Termina.Terminal;

/// <summary>
/// Terminal wrapper that provides diff-based rendering to eliminate flickering.
/// Uses double-buffering: renders to a pending buffer, then only outputs changed cells.
/// </summary>
/// <remarks>
/// <para>
/// Instead of clearing the screen and redrawing everything on each frame,
/// DiffingTerminal maintains two frame buffers:
/// </para>
/// <list type="bullet">
/// <item><description><c>_currentFrame</c>: What's currently displayed on the terminal</description></item>
/// <item><description><c>_pendingFrame</c>: What we want to display (rendering target)</description></item>
/// </list>
/// <para>
/// On <see cref="Flush"/>, only cells that differ between the two buffers are output,
/// minimizing ANSI traffic and eliminating visual flicker.
/// </para>
/// </remarks>
public sealed class DiffingTerminal : IAnsiTerminal, IDisposable
{
    private readonly IAnsiTerminal _inner;
    private FrameBuffer _currentFrame;
    private FrameBuffer _pendingFrame;

    // Cached dimensions - refreshed at start of each frame to avoid repeated Console calls
    private int _cachedWidth;
    private int _cachedHeight;

    // Current rendering state (for pending buffer writes)
    private int _cursorX;
    private int _cursorY;
    private Color _currentForeground = Color.Default;
    private Color _currentBackground = Color.Default;
    private TextDecoration _currentDecoration = TextDecoration.None;
    private string? _currentLink;

    // Saved cursor position
    private int _savedCursorX;
    private int _savedCursorY;

    // Force full refresh on next Flush (e.g., after resize)
    private bool _forceFullRefresh = true;

    // Last output state tracking for efficient ANSI emission
    private Color _lastOutputForeground = Color.Default;
    private Color _lastOutputBackground = Color.Default;
    private TextDecoration _lastOutputDecoration = TextDecoration.None;
    private string? _lastOutputLink;

    /// <summary>
    /// Creates a new DiffingTerminal wrapping the specified terminal.
    /// </summary>
    /// <param name="inner">The underlying terminal to write to.</param>
    public DiffingTerminal(IAnsiTerminal inner)
    {
        _inner = inner ?? throw new ArgumentNullException(nameof(inner));

        _cachedWidth = Math.Max(1, inner.Width);
        _cachedHeight = Math.Max(1, inner.Height);

        _currentFrame = new FrameBuffer(_cachedWidth, _cachedHeight);
        _pendingFrame = new FrameBuffer(_cachedWidth, _cachedHeight);
    }

    /// <inheritdoc />
    public int Width => _cachedWidth;

    /// <inheritdoc />
    public int Height => _cachedHeight;

    /// <summary>
    /// Forces a full screen redraw on the next <see cref="Flush"/> call.
    /// Call this after terminal resize or when the terminal state may be corrupted.
    /// </summary>
    public void ForceFullRefresh()
    {
        _forceFullRefresh = true;
    }

    /// <inheritdoc />
    public void MoveTo(int x, int y)
    {
        _cursorX = Math.Clamp(x, 0, Width - 1);
        _cursorY = Math.Clamp(y, 0, Height - 1);
    }

    /// <inheritdoc />
    public void Write(string text)
    {
        foreach (var grapheme in TerminalText.EnumerateGraphemes(text))
        {
            WriteGraphemeToBuffer(grapheme.Text, grapheme.Width);
        }
    }

    /// <inheritdoc />
    public void Write(char c)
    {
        Write(c.ToString());
    }

    /// <inheritdoc />
    public void WriteControlAt(int x, int y, string sequence)
    {
        if (string.IsNullOrEmpty(sequence))
            return;

        _inner.WriteControlAt(x, y, sequence);
    }

    private void WriteGraphemeToBuffer(string text, int width)
    {
        if (_cursorX < 0 || _cursorX >= Width || _cursorY < 0 || _cursorY >= Height)
            return;

        // Handle special characters
        switch (text)
        {
            case "\n":
                _cursorY++;
                _cursorX = 0;
                return;
            case "\r":
                _cursorX = 0;
                return;
            case "\t":
                // Tab to next 8-column boundary
                var nextTab = ((_cursorX / 8) + 1) * 8;
                while (_cursorX < nextTab && _cursorX < Width)
                {
                    WriteGraphemeToBuffer(" ", 1);
                }
                return;
        }

        if (width <= 0)
            return;
        if (width > Width)
            return;
        if (_cursorX + width > Width)
        {
            _cursorX = 0;
            _cursorY++;
            if (_cursorY >= Height)
                return;
        }

        // Write the cell to pending buffer
        var cell = new TerminalCell(text, _currentForeground, _currentBackground, _currentDecoration, _currentLink);
        _pendingFrame.TrySet(_cursorX, _cursorY, cell);
        for (var i = 1; i < width; i++)
            _pendingFrame.TrySet(_cursorX + i, _cursorY,
                TerminalCell.Continuation(_currentForeground, _currentBackground, _currentDecoration, _currentLink));

        // Advance cursor
        _cursorX += width;
        if (_cursorX >= Width)
        {
            _cursorX = 0;
            _cursorY++;
        }
    }

    /// <inheritdoc />
    public void SetForeground(Color color)
    {
        _currentForeground = color;
    }

    /// <inheritdoc />
    public void SetBackground(Color color)
    {
        _currentBackground = color;
    }

    /// <summary>
    /// Sets the text decoration for subsequent writes.
    /// </summary>
    public void SetDecoration(TextDecoration decoration)
    {
        _currentDecoration = decoration;
    }

    /// <inheritdoc />
    public void SetLink(string? uri)
    {
        // Ambient state, recorded onto cells in WriteGraphemeToBuffer and emitted at flush time.
        // Deliberately independent of ResetColors so a link survives the per-segment style resets
        // a node performs while drawing the linked text.
        _currentLink = uri;
    }

    /// <inheritdoc />
    public void ResetColors()
    {
        _currentForeground = Color.Default;
        _currentBackground = Color.Default;
        _currentDecoration = TextDecoration.None;
    }

    /// <inheritdoc />
    public void SaveCursor()
    {
        _savedCursorX = _cursorX;
        _savedCursorY = _cursorY;
    }

    /// <inheritdoc />
    public void RestoreCursor()
    {
        _cursorX = _savedCursorX;
        _cursorY = _savedCursorY;
    }

    /// <inheritdoc />
    public void SetCursorVisible(bool visible)
    {
        // Pass through to inner terminal immediately
        _inner.SetCursorVisible(visible);
    }

    /// <inheritdoc />
    public void ClearRegion(int x, int y, int width, int height)
    {
        // Clear region in pending buffer (not actual terminal)
        _pendingFrame.Fill(x, y, width, height, TerminalCell.Empty);
    }

    /// <inheritdoc />
    public void ClearScreen()
    {
        // Clear pending buffer only - DO NOT emit ANSI clear
        // This is the key to eliminating flickering!
        _pendingFrame.Clear();
        _cursorX = 0;
        _cursorY = 0;
    }

    /// <inheritdoc />
    public void Flush()
    {
        // Handle resize if dimensions changed
        HandleResize();

        if (_forceFullRefresh)
        {
            FlushFull();
            _forceFullRefresh = false;
        }
        else
        {
            FlushDiff();
        }

        // Copy pending to current (pending is now what's on screen)
        _currentFrame.CopyFrom(_pendingFrame);

        // Flush the inner terminal
        _inner.Flush();
    }

    private void HandleResize()
    {
        // Refresh cached dimensions from actual console (only place we call inner.Width/Height)
        var newWidth = _inner.Width;
        var newHeight = _inner.Height;

        // Skip resize if dimensions are invalid (e.g., headless CI environment)
        if (newWidth <= 0 || newHeight <= 0)
            return;

        // Update cache
        _cachedWidth = newWidth;
        _cachedHeight = newHeight;

        if (newWidth != _pendingFrame.Width || newHeight != _pendingFrame.Height)
        {
            _pendingFrame.Resize(newWidth, newHeight);
            _currentFrame.Resize(newWidth, newHeight);
            _forceFullRefresh = true;
        }
    }

    /// <summary>
    /// Output the entire pending buffer (used for first frame or after resize).
    /// </summary>
    private void FlushFull()
    {
        // Clear the actual terminal first for a clean slate
        _inner.ClearScreen();

        // Reset output state
        _lastOutputForeground = Color.Default;
        _lastOutputBackground = Color.Default;
        _lastOutputDecoration = TextDecoration.None;
        _lastOutputLink = null;

        for (var y = 0; y < _pendingFrame.Height; y++)
        {
            _inner.MoveTo(0, y);

            for (var x = 0; x < _pendingFrame.Width; x++)
            {
                var cell = _pendingFrame[x, y];
                if (cell.IsContinuation)
                    continue;
                EmitStyleChanges(cell);
                _inner.Write(cell.Text);
            }
        }

        // Close any dangling hyperlink so the terminal isn't left in an open-link state.
        if (_lastOutputLink is not null)
        {
            _inner.SetLink(null);
            _lastOutputLink = null;
        }

        _inner.ResetColors();
    }

    /// <summary>
    /// Output only changed cells by diffing pending vs current buffer.
    /// </summary>
    private void FlushDiff()
    {
        // Reset output state tracking
        _lastOutputForeground = Color.Default;
        _lastOutputBackground = Color.Default;
        _lastOutputDecoration = TextDecoration.None;
        _lastOutputLink = null;

        // Use GetChangedRuns for efficient output (groups consecutive changes)
        foreach (var (y, startX, cells) in _pendingFrame.GetChangedRuns(_currentFrame))
        {
            _inner.MoveTo(startX, y);
            var outputX = startX;

            for (var i = 0; i < cells.Length; i++)
            {
                var cell = cells[i];
                var cellX = startX + i;
                if (cell.IsContinuation)
                    continue;
                if (outputX != cellX)
                {
                    _inner.MoveTo(cellX, y);
                    outputX = cellX;
                }
                EmitStyleChanges(cell);
                _inner.Write(cell.Text);
                outputX += TerminalText.GetDisplayWidth(cell.Text);
            }

            // A changed run may end inside a link span; close it so the link never bleeds past the
            // emitted run into cells the diff didn't touch.
            if (_lastOutputLink is not null)
            {
                _inner.SetLink(null);
                _lastOutputLink = null;
            }
        }

        _inner.ResetColors();
        _lastOutputForeground = Color.Default;
        _lastOutputBackground = Color.Default;
        _lastOutputDecoration = TextDecoration.None;
    }

    /// <summary>
    /// Emit ANSI sequences for style changes if needed.
    /// </summary>
    private void EmitStyleChanges(TerminalCell cell)
    {
        // Open/close the OSC 8 hyperlink in-band, before the cell's text, so the wrapper travels
        // with the deferred cell writes and survives diffing.
        if (cell.Link != _lastOutputLink)
        {
            _inner.SetLink(cell.Link);
            _lastOutputLink = cell.Link;
        }

        // Check if decoration changed
        if (cell.Decoration != _lastOutputDecoration)
        {
            // Reset all decorations first, then apply new ones
            // This is simpler than tracking individual decoration bits
            _inner.ResetColors();
            _lastOutputForeground = Color.Default;
            _lastOutputBackground = Color.Default;

            _inner.SetDecoration(cell.Decoration);
            _lastOutputDecoration = cell.Decoration;
        }

        // Check foreground
        if (cell.Foreground != _lastOutputForeground)
        {
            _inner.SetForeground(cell.Foreground);
            _lastOutputForeground = cell.Foreground;
        }

        // Check background
        if (cell.Background != _lastOutputBackground)
        {
            _inner.SetBackground(cell.Background);
            _lastOutputBackground = cell.Background;
        }
    }

    // Pass-through methods that don't affect buffering

    /// <inheritdoc />
    public void EnterAlternateScreen()
    {
        _inner.EnterAlternateScreen();
        _forceFullRefresh = true;
    }

    /// <inheritdoc />
    public void ExitAlternateScreen()
    {
        _inner.ExitAlternateScreen();
    }

    /// <inheritdoc />
    public void EnableMouse()
    {
        _inner.EnableMouse();
    }

    /// <inheritdoc />
    public void DisableMouse()
    {
        _inner.DisableMouse();
    }

    /// <inheritdoc />
    public void SetMouseMode(MouseMode mode)
    {
        _inner.SetMouseMode(mode);
    }

    /// <inheritdoc />
    public void DisableAllMouseTracking()
    {
        _inner.DisableAllMouseTracking();
    }

    /// <inheritdoc />
    public void EnableWheelScroll()
    {
        _inner.EnableWheelScroll();
    }

    /// <inheritdoc />
    public void DisableWheelScroll()
    {
        _inner.DisableWheelScroll();
    }

    /// <inheritdoc />
    public void CopyToClipboard(string text)
    {
        _inner.CopyToClipboard(text);
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_inner is IDisposable disposable)
        {
            disposable.Dispose();
        }
    }
}
