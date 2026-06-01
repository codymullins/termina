// Copyright (c) Petabridge, LLC. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

namespace Termina.Input;

/// <summary>
/// Type of mouse button.
/// </summary>
public enum MouseButton
{
    /// <summary>
    /// No button (e.g. a hover/move event with no button held).
    /// </summary>
    None,

    /// <summary>
    /// Left mouse button.
    /// </summary>
    Left,

    /// <summary>
    /// Right mouse button.
    /// </summary>
    Right,

    /// <summary>
    /// Middle mouse button (scroll wheel click).
    /// </summary>
    Middle,

    /// <summary>
    /// Back / button 4 (extended button, SGR code 8).
    /// </summary>
    Back,

    /// <summary>
    /// Forward / button 5 (extended button, SGR code 9).
    /// </summary>
    Forward,
}

/// <summary>
/// Kind of mouse event. Press and release are reported separately; the application synthesizes
/// clicks from a press/release pair. Scroll directions are distinct kinds because the wheel never
/// produces a release event.
/// </summary>
public enum MouseEventKind
{
    /// <summary>
    /// A mouse button was pressed.
    /// </summary>
    Down,

    /// <summary>
    /// A mouse button was released.
    /// </summary>
    Up,

    /// <summary>
    /// The mouse moved while a button was held.
    /// </summary>
    Drag,

    /// <summary>
    /// The mouse moved with no button held (only reported under <see cref="Terminal.MouseMode.Hover"/>).
    /// </summary>
    Move,

    /// <summary>
    /// The wheel scrolled up (toward older content).
    /// </summary>
    ScrollUp,

    /// <summary>
    /// The wheel scrolled down (toward newer content).
    /// </summary>
    ScrollDown,

    /// <summary>
    /// The wheel scrolled left.
    /// </summary>
    ScrollLeft,

    /// <summary>
    /// The wheel scrolled right.
    /// </summary>
    ScrollRight,
}

/// <summary>
/// A decoded mouse input event.
/// </summary>
/// <param name="Column">0-based column (cell) of the mouse cursor.</param>
/// <param name="Row">0-based row (cell) of the mouse cursor.</param>
/// <param name="Button">The mouse button involved (<see cref="MouseButton.None"/> for move/scroll).</param>
/// <param name="Kind">The kind of mouse event.</param>
/// <param name="Modifiers">Any keyboard modifiers held during the event.</param>
/// <param name="ClickChain">
/// 1 for a single click, 2 for a double click, 3 for a triple click. Synthesized by the input
/// source from consecutive <see cref="MouseEventKind.Down"/> events in the same cell within the
/// double-click window. Always 1 for non-Down events.
/// </param>
/// <param name="PixelX">Sub-cell X pixel offset when pixel reporting is active, else -1.</param>
/// <param name="PixelY">Sub-cell Y pixel offset when pixel reporting is active, else -1.</param>
public sealed record MouseEvent(
    int Column,
    int Row,
    MouseButton Button,
    MouseEventKind Kind,
    ConsoleModifiers Modifiers = 0,
    int ClickChain = 1,
    int PixelX = -1,
    int PixelY = -1) : IInputEvent
{
    /// <summary>
    /// Set by a handler that has consumed this event, stopping it from bubbling to ancestors.
    /// </summary>
    public bool Handled { get; set; }

    /// <summary>
    /// Whether this is a scroll-wheel event (one of the Scroll* kinds).
    /// </summary>
    public bool IsScroll => Kind is MouseEventKind.ScrollUp or MouseEventKind.ScrollDown
        or MouseEventKind.ScrollLeft or MouseEventKind.ScrollRight;
}
