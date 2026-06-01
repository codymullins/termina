// Copyright (c) Petabridge, LLC. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

namespace Termina.Terminal;

/// <summary>
/// Mouse tracking modes that can be combined to control what mouse activity the terminal
/// reports. Maps onto the DEC private modes the terminal understands; <see cref="IAnsiTerminal.SetMouseMode"/>
/// translates the flag set into the minimal set of enable/disable escape sequences.
/// </summary>
[Flags]
public enum MouseMode
{
    /// <summary>
    /// No mouse tracking. The host terminal owns click-drag selection and the wheel.
    /// </summary>
    None = 0,

    /// <summary>
    /// Report button press and release events (CSI ?1000h).
    /// </summary>
    Buttons = 1 << 0,

    /// <summary>
    /// Report motion while a button is held (drag) in addition to press/release (CSI ?1002h).
    /// Implies <see cref="Buttons"/>.
    /// </summary>
    Drag = 1 << 1,

    /// <summary>
    /// Report every cell the cursor crosses, even with no button held (CSI ?1003h). Required for
    /// hover. Floods stdin during normal motion — enable only when hover is actually needed.
    /// Implies <see cref="Drag"/> and <see cref="Buttons"/>.
    /// </summary>
    Hover = 1 << 2,

    /// <summary>
    /// Report terminal focus in/out events (CSI ?1004h).
    /// </summary>
    Focus = 1 << 3,

    /// <summary>
    /// Report coordinates in pixels rather than cells (CSI ?1016h). Should only be set after a
    /// DECRQM probe confirms support.
    /// </summary>
    Pixel = 1 << 4,
}
