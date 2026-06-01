// Copyright (c) Petabridge, LLC. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

namespace Termina.Input;

/// <summary>
/// Fired when the terminal window gains or loses focus, reported via focus tracking
/// (CSI ?1004h). The terminal sends <c>CSI I</c> on focus-in and <c>CSI O</c> on focus-out.
/// Must be consumed by the parser or it leaks into the application as spurious key input.
/// </summary>
/// <param name="HasFocus"><c>true</c> when the terminal gained focus, <c>false</c> when it lost focus.</param>
public sealed record TerminalFocusEvent(bool HasFocus) : IInputEvent;
