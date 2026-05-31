using System.Threading.Channels;

namespace Termina.Input;

/// <summary>
/// Virtual input source for testing and programmatic input.
/// External code pushes events via Enqueue methods, which get forwarded to the event loop.
/// </summary>
public sealed class VirtualInputSource : IInputSource
{
    private readonly Channel<IInputEvent> _inputChannel = Channel.CreateUnbounded<IInputEvent>();

    /// <summary>
    /// Enqueue a key event to be processed.
    /// </summary>
    public void EnqueueKey(ConsoleKeyInfo key)
    {
        _inputChannel.Writer.TryWrite(new KeyPressed(key));
    }

    /// <summary>
    /// Enqueue a key by ConsoleKey (no character, no modifiers).
    /// </summary>
    public void EnqueueKey(ConsoleKey key)
    {
        EnqueueKey(new ConsoleKeyInfo('\0', key, shift: false, alt: false, control: false));
    }

    /// <summary>
    /// Enqueue a key by ConsoleKey with modifiers.
    /// </summary>
    public void EnqueueKey(ConsoleKey key, bool shift = false, bool alt = false, bool control = false)
    {
        char keyChar = key >= ConsoleKey.A && key <= ConsoleKey.Z
            ? (char)('a' + (key - ConsoleKey.A))
            : '\0';
        EnqueueKey(new ConsoleKeyInfo(keyChar, key, shift, alt, control));
    }

    /// <summary>
    /// Enqueue a character key.
    /// </summary>
    public void EnqueueChar(char c)
    {
        var consoleKey = char.ToUpper(c) switch
        {
            >= 'A' and <= 'Z' => (ConsoleKey)(char.ToUpper(c) - 'A' + (int)ConsoleKey.A),
            >= '0' and <= '9' => (ConsoleKey)(c - '0' + (int)ConsoleKey.D0),
            ' ' => ConsoleKey.Spacebar,
            _ => ConsoleKey.NoName
        };
        EnqueueKey(new ConsoleKeyInfo(c, consoleKey, shift: false, alt: false, control: false));
    }

    /// <summary>
    /// Enqueue a string as a series of character keys.
    /// </summary>
    public void EnqueueString(string text)
    {
        foreach (var c in text)
            EnqueueChar(c);
    }

    /// <summary>
    /// Enqueue a mouse event.
    /// </summary>
    /// <param name="x">X position (column).</param>
    /// <param name="y">Y position (row).</param>
    /// <param name="button">The mouse button.</param>
    /// <param name="kind">The kind of mouse event.</param>
    /// <param name="modifiers">Optional keyboard modifiers.</param>
    /// <param name="clickChain">Click chain count (1 single, 2 double, 3 triple).</param>
    public void EnqueueMouse(int x, int y, MouseButton button, MouseEventKind kind, ConsoleModifiers modifiers = 0, int clickChain = 1)
    {
        _inputChannel.Writer.TryWrite(new MouseEvent(x, y, button, kind, modifiers, clickChain));
    }

    /// <summary>
    /// Enqueue a mouse button press at the given cell.
    /// </summary>
    public void EnqueueClick(int x, int y, MouseButton button = MouseButton.Left, ConsoleModifiers modifiers = 0, int clickChain = 1)
    {
        EnqueueMouse(x, y, button, MouseEventKind.Down, modifiers, clickChain);
    }

    /// <summary>
    /// Enqueue a press followed by a release at the same cell — a complete click.
    /// </summary>
    public void EnqueuePressRelease(int x, int y, MouseButton button = MouseButton.Left, ConsoleModifiers modifiers = 0)
    {
        EnqueueMouse(x, y, button, MouseEventKind.Down, modifiers);
        EnqueueMouse(x, y, button, MouseEventKind.Up, modifiers);
    }

    /// <summary>
    /// Enqueue a mouse scroll event, emitted both as a rich <see cref="MouseEvent"/> and the
    /// legacy <see cref="MouseScrollEvent"/> that focused scrollables consume.
    /// </summary>
    public void EnqueueScroll(int x, int y, bool up)
    {
        EnqueueMouse(x, y, MouseButton.None, up ? MouseEventKind.ScrollUp : MouseEventKind.ScrollDown);
        _inputChannel.Writer.TryWrite(new MouseScrollEvent(up ? +1 : -1));
    }

    /// <summary>
    /// Enqueue a terminal resize event.
    /// </summary>
    /// <param name="width">New terminal width.</param>
    /// <param name="height">New terminal height.</param>
    public void EnqueueResize(int width, int height)
    {
        _inputChannel.Writer.TryWrite(new ResizeEvent(width, height));
    }

    /// <summary>
    /// Signal that no more input will be provided.
    /// </summary>
    public void Complete()
    {
        _inputChannel.Writer.TryComplete();
    }

    /// <inheritdoc />
    public async Task RunAsync(ChannelWriter<object> writer, CancellationToken cancellationToken)
    {
        // Forward all enqueued input to the shared event channel
        await foreach (var evt in _inputChannel.Reader.ReadAllAsync(cancellationToken))
        {
            await writer.WriteAsync(evt, cancellationToken);
        }
    }
}
