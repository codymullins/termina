// Copyright (c) Petabridge, LLC. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Text;
using Termina.Diagnostics;

namespace Termina.Terminal;

/// <summary>
/// Real terminal implementation using ANSI escape sequences.
/// Writes to standard output.
/// </summary>
public sealed class AnsiTerminal : IAnsiTerminal, IDisposable
{
    /// <summary>
    /// Explicit output writer for tests/benchmarks, or <c>null</c> to write to
    /// <see cref="Console.Out"/>. A cached <see cref="Console.Out"/> is deliberately
    /// never stored here — see <see cref="Output"/>.
    /// </summary>
    private readonly TextWriter? _explicitOutput;
    private readonly StringBuilder _buffer = new();
    private readonly bool _useAlternateScreen;
    private bool _inAlternateScreen;
    private MouseMode _mouseMode;
    private bool _wheelScrollEnabled;
    private string? _currentLink;
    private long _totalBytesWritten;
    private int _flushCount;

    /// <summary>
    /// Create an AnsiTerminal writing to standard output.
    /// </summary>
    /// <param name="useAlternateScreen">Whether to use alternate screen buffer on startup.</param>
    public AnsiTerminal(bool useAlternateScreen = true)
        : this(null, useAlternateScreen)
    {
    }

    /// <summary>
    /// Create an AnsiTerminal. Pass an explicit <paramref name="output"/> writer for
    /// tests/benchmarks, or <c>null</c> to write to <see cref="Console.Out"/>.
    /// </summary>
    internal AnsiTerminal(TextWriter? output, bool useAlternateScreen = true)
    {
        _explicitOutput = output;
        _useAlternateScreen = useAlternateScreen;

        TerminaTrace.Platform.Debug(this, "AnsiTerminal created: output={0}, useAlternateScreen={1}",
            output?.GetType().Name ?? "Console.Out", useAlternateScreen);

        // NOTE: UTF-8 output encoding is configured once by the platform console
        // (ConsoleEnvironment.EnsureUtf8Output). This terminal deliberately does not
        // set Console.OutputEncoding and never caches Console.Out, because that setter
        // replaces Console.Out with a new TextWriter — see issue #204.

        if (_useAlternateScreen)
        {
            EnterAlternateScreen();
        }

        TerminaTrace.Platform.Debug(this, "AnsiTerminal initialization complete");
    }

    /// <summary>
    /// The writer used for output. Resolved on every access — never cached — because
    /// setting <see cref="Console.OutputEncoding"/> replaces <see cref="Console.Out"/>
    /// with a new <see cref="TextWriter"/>. Caching it risks writing through a writer
    /// bound to a stale encoding, garbling non-ASCII output. See issue #204.
    /// </summary>
    private TextWriter Output => _explicitOutput ?? Console.Out;

    /// <inheritdoc />
    public int Width => GetConsoleWidth();

    /// <inheritdoc />
    public int Height => GetConsoleHeight();

    /// <summary>
    /// Gets the console width, with fallback for non-TTY environments.
    /// </summary>
    private static int GetConsoleWidth()
    {
        try
        {
            return Console.WindowWidth;
        }
        catch (IOException)
        {
            // No TTY available (e.g., CI environment, redirected output)
            return 80;
        }
    }

    /// <summary>
    /// Gets the console height, with fallback for non-TTY environments.
    /// </summary>
    private static int GetConsoleHeight()
    {
        try
        {
            return Console.WindowHeight;
        }
        catch (IOException)
        {
            // No TTY available (e.g., CI environment, redirected output)
            return 24;
        }
    }

    /// <inheritdoc />
    public void MoveTo(int x, int y)
    {
        _buffer.Append(AnsiCodes.MoveTo(y, x));
    }

    /// <inheritdoc />
    public void Write(string text)
    {
        _buffer.Append(text);
    }

    /// <inheritdoc />
    public void Write(char c)
    {
        _buffer.Append(c);
    }

    /// <inheritdoc />
    public void WriteControlAt(int x, int y, string sequence)
    {
        if (string.IsNullOrEmpty(sequence))
            return;

        MoveTo(x, y);
        _buffer.Append(sequence);
    }

    /// <inheritdoc />
    public void SetForeground(Color color)
    {
        _buffer.Append(color.ToForegroundAnsi());
    }

    /// <inheritdoc />
    public void SetBackground(Color color)
    {
        _buffer.Append(color.ToBackgroundAnsi());
    }

    /// <inheritdoc />
    public void ResetColors()
    {
        _buffer.Append(AnsiCodes.Reset);
    }

    /// <inheritdoc />
    public void SetDecoration(TextDecoration decoration)
    {
        _buffer.Append(AnsiCodes.ResetBold);
        _buffer.Append(AnsiCodes.ResetItalic);
        _buffer.Append(AnsiCodes.ResetUnderline);
        _buffer.Append(AnsiCodes.ResetStrikethrough);

        if (decoration == TextDecoration.None)
            return;

        if (decoration.HasFlag(TextDecoration.Bold))
            _buffer.Append(AnsiCodes.Bold);
        if (decoration.HasFlag(TextDecoration.Dim))
            _buffer.Append(AnsiCodes.Dim);
        if (decoration.HasFlag(TextDecoration.Italic))
            _buffer.Append(AnsiCodes.Italic);
        if (decoration.HasFlag(TextDecoration.Underline))
            _buffer.Append(AnsiCodes.Underline);
        if (decoration.HasFlag(TextDecoration.Strikethrough))
            _buffer.Append(AnsiCodes.Strikethrough);
    }

    /// <inheritdoc />
    public void SetLink(string? uri)
    {
        if (uri == _currentLink)
            return;

        // Close the previous link before opening a new one (or just closing).
        if (_currentLink is not null)
            _buffer.Append(AnsiCodes.HyperlinkEnd);
        if (!string.IsNullOrEmpty(uri))
            _buffer.Append(AnsiCodes.Hyperlink(uri));

        _currentLink = uri;
    }

    /// <inheritdoc />
    public void SaveCursor()
    {
        _buffer.Append(AnsiCodes.SaveCursor);
    }

    /// <inheritdoc />
    public void RestoreCursor()
    {
        _buffer.Append(AnsiCodes.RestoreCursor);
    }

    /// <inheritdoc />
    public void SetCursorVisible(bool visible)
    {
        _buffer.Append(visible ? AnsiCodes.ShowCursor : AnsiCodes.HideCursor);
    }

    /// <inheritdoc />
    public void ClearRegion(int x, int y, int width, int height)
    {
        var spaces = new string(' ', width);
        for (var row = 0; row < height; row++)
        {
            MoveTo(x, y + row);
            Write(spaces);
        }
    }

    /// <inheritdoc />
    public void ClearScreen()
    {
        _buffer.Append(AnsiCodes.ClearScreen);
        MoveTo(0, 0);
    }

    /// <inheritdoc />
    public void Flush()
    {
        if (_buffer.Length > 0)
        {
            var content = _buffer.ToString();
            var byteCount = Encoding.UTF8.GetByteCount(content);

            _flushCount++;
            _totalBytesWritten += byteCount;

            // Log flush details - truncate content preview for readability
            var preview = content.Length > 100
                ? content.Substring(0, 100).Replace("\x1b", "\\e") + "..."
                : content.Replace("\x1b", "\\e");

            TerminaTrace.Render.Debug(this, "Flush #{0}: {1} chars, {2} bytes",
                _flushCount, content.Length, byteCount);
            TerminaTrace.Render.Debug(this, "Content preview: {0}", preview);

            // Resolve Console.Out fresh on every flush — never cache it (see Output).
            var output = Output;
            output.Write(content);
            output.Flush();
            _buffer.Clear();

            TerminaTrace.Render.Debug(this, "Flush #{0} complete", _flushCount);
        }
    }

    /// <inheritdoc />
    public void EnterAlternateScreen()
    {
        if (!_inAlternateScreen)
        {
            TerminaTrace.Platform.Debug(this, "Entering alternate screen buffer");
            _buffer.Append(AnsiCodes.EnterAlternateScreen);
            _inAlternateScreen = true;
        }
    }

    /// <inheritdoc />
    public void ExitAlternateScreen()
    {
        if (_inAlternateScreen)
        {
            TerminaTrace.Platform.Debug(this, "Exiting alternate screen buffer");
            _buffer.Append(AnsiCodes.ExitAlternateScreen);
            _inAlternateScreen = false;
        }
    }

    /// <inheritdoc />
    public void EnableMouse() => SetMouseMode(MouseMode.Buttons | MouseMode.Drag);

    /// <inheritdoc />
    public void DisableMouse() => SetMouseMode(MouseMode.None);

    /// <inheritdoc />
    public void SetMouseMode(MouseMode mode)
    {
        if (mode == _mouseMode)
            return;

        var old = _mouseMode;

        // Decompose each flag set into the concrete DEC private modes the terminal understands.
        // Hover implies Drag implies Buttons; pixel reporting only applies when tracking is on.
        static bool AnyTracking(MouseMode m) => (m & (MouseMode.Buttons | MouseMode.Drag | MouseMode.Hover)) != 0;
        static bool WantsButtonEvent(MouseMode m) => (m & MouseMode.Drag) != 0 && (m & MouseMode.Hover) == 0;
        static bool WantsAnyEvent(MouseMode m) => (m & MouseMode.Hover) != 0;
        static bool WantsPixels(MouseMode m) => (m & MouseMode.Pixel) != 0 && AnyTracking(m);

        var oldTrack = AnyTracking(old);
        var newTrack = AnyTracking(mode);

        // Disables first (pixel/SGR last-in, first-out), then enables (SGR last so it wins).
        if (WantsPixels(old) && !WantsPixels(mode))
            _buffer.Append(AnsiCodes.DisableMousePixels);
        if (oldTrack && !newTrack)
            _buffer.Append(AnsiCodes.DisableMouseSgr);
        if (WantsAnyEvent(old) && !WantsAnyEvent(mode))
            _buffer.Append(AnsiCodes.DisableMouseAnyEvent);
        if (WantsButtonEvent(old) && !WantsButtonEvent(mode))
            _buffer.Append(AnsiCodes.DisableMouseButtonEvent);
        if (oldTrack && !newTrack)
            _buffer.Append(AnsiCodes.DisableMouseNormal);
        if ((old & MouseMode.Focus) != 0 && (mode & MouseMode.Focus) == 0)
            _buffer.Append(AnsiCodes.DisableFocusTracking);

        if ((mode & MouseMode.Focus) != 0 && (old & MouseMode.Focus) == 0)
            _buffer.Append(AnsiCodes.EnableFocusTracking);
        if (newTrack && !oldTrack)
            _buffer.Append(AnsiCodes.EnableMouseNormal);
        if (WantsButtonEvent(mode) && !WantsButtonEvent(old))
            _buffer.Append(AnsiCodes.EnableMouseButtonEvent);
        if (WantsAnyEvent(mode) && !WantsAnyEvent(old))
            _buffer.Append(AnsiCodes.EnableMouseAnyEvent);
        if (newTrack && !oldTrack)
            _buffer.Append(AnsiCodes.EnableMouseSgr);
        if (WantsPixels(mode) && !WantsPixels(old))
            _buffer.Append(AnsiCodes.EnableMousePixels);

        _mouseMode = mode;
    }

    /// <inheritdoc />
    public void DisableAllMouseTracking()
    {
        // Write every disable unconditionally — defensive teardown must clear modes that a
        // capability probe or app may have enabled without updating our tracked state.
        _buffer.Append(AnsiCodes.DisableMousePixels);
        _buffer.Append(AnsiCodes.DisableMouseSgr);
        _buffer.Append(AnsiCodes.DisableMouseAnyEvent);
        _buffer.Append(AnsiCodes.DisableMouseButtonEvent);
        _buffer.Append(AnsiCodes.DisableMouseNormal);
        _buffer.Append(AnsiCodes.DisableFocusTracking);
        _mouseMode = MouseMode.None;
    }

    /// <inheritdoc />
    public void EnableWheelScroll()
    {
        if (!_wheelScrollEnabled)
        {
            _buffer.Append(AnsiCodes.EnableAlternateScroll);
            // DECCKM: keyboard arrows -> SS3 (ESC O A/B), wheel still sends CSI (ESC [ A/B).
            // The EscapeSequenceParser uses this distinction to emit MouseScrollEvent for wheel
            // ticks while leaving real keyboard arrows as plain KeyPressed(UpArrow/DownArrow).
            _buffer.Append(AnsiCodes.EnableCursorKeyApplicationMode);
            _wheelScrollEnabled = true;
        }
    }

    /// <inheritdoc />
    public void DisableWheelScroll()
    {
        if (_wheelScrollEnabled)
        {
            _buffer.Append(AnsiCodes.DisableCursorKeyApplicationMode);
            _buffer.Append(AnsiCodes.DisableAlternateScroll);
            _wheelScrollEnabled = false;
        }
    }

    /// <inheritdoc />
    public void CopyToClipboard(string text)
    {
        var belSequence = AnsiCodes.Osc52Clipboard(text);
        var stSequence = AnsiCodes.Osc52Clipboard(text, useStringTerminator: true);
        TerminaTrace.Platform.Info(this, "AnsiTerminal.CopyToClipboard: textLength={0}, tmux={1}", text.Length, Environment.GetEnvironmentVariable("TMUX") is not null);
        TerminaTrace.Platform.Debug(this, "OSC52 lengths: bel={0}, st={1}", belSequence.Length, stSequence.Length);
        if (Environment.GetEnvironmentVariable("TMUX") is not null)
        {
            // Emit both OSC terminators and both tmux delivery modes to maximize
            // compatibility across tmux versions and terminal emulators.
            _buffer.Append(belSequence);
            _buffer.Append(stSequence);
            _buffer.Append(AnsiCodes.TmuxPassthrough(belSequence));
            _buffer.Append(AnsiCodes.TmuxPassthrough(stSequence));
            TerminaTrace.Platform.Debug(this, "Queued OSC52 BEL/ST plain + tmux passthrough sequences");
            return;
        }

        _buffer.Append(belSequence);
        _buffer.Append(stSequence);
        TerminaTrace.Platform.Debug(this, "Queued OSC52 BEL/ST plain sequences");
    }

    /// <summary>
    /// Dispose the terminal, restoring original state.
    /// </summary>
    public void Dispose()
    {
        if (_mouseMode != MouseMode.None)
        {
            DisableAllMouseTracking();
        }

        if (_wheelScrollEnabled)
        {
            DisableWheelScroll();
        }

        if (_inAlternateScreen)
        {
            ExitAlternateScreen();
        }

        SetCursorVisible(true);
        ResetColors();
        Flush();
    }
}
