// Copyright (c) Petabridge, LLC. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

namespace Termina.Input;

/// <summary>
/// Tunable behavior for mouse handling.
/// </summary>
public sealed class MouseOptions
{
    /// <summary>
    /// Maximum time between two presses in the same cell for them to count as a double (or triple)
    /// click. Defaults to 500 ms, matching GTK / Qt / Cocoa.
    /// </summary>
    public TimeSpan DoubleClickThreshold { get; set; } = TimeSpan.FromMilliseconds(500);

    /// <summary>
    /// When <c>true</c>, finishing a drag-selection copies the selected text to the clipboard
    /// automatically (primary-selection style). Off by default to avoid surprising Windows/macOS
    /// users who expect an explicit copy.
    /// </summary>
    public bool CopyOnSelect { get; set; }

    /// <summary>
    /// The modifier that must be held while clicking to activate a hyperlink. Defaults to
    /// <see cref="ConsoleModifiers.Control"/>, except on macOS where it defaults to the platform
    /// command behavior reported as <see cref="ConsoleModifiers.Alt"/> by most terminals.
    /// </summary>
    public ConsoleModifiers LinkActivationModifier { get; set; } =
        OperatingSystem.IsMacOS() ? ConsoleModifiers.Alt : ConsoleModifiers.Control;
}
