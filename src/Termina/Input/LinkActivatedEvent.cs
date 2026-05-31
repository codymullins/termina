// Copyright (c) Petabridge, LLC. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

namespace Termina.Input;

/// <summary>
/// Fired when the user activates an in-app hyperlink — a modifier+click (Cmd on macOS, Ctrl
/// elsewhere) on a node that exposes a URL. Apps subscribe via the input observable to open the
/// URL with the OS handler, navigate, or whatever is appropriate.
/// </summary>
/// <remarks>
/// This is for <em>app-managed</em> links. Links the renderer emits as OSC 8 are activated by the
/// host terminal itself (it intercepts the modifier+click before synthesizing a mouse sequence),
/// so those never reach the application.
/// </remarks>
/// <param name="Url">The activated URL.</param>
/// <param name="Modifiers">Modifiers held during activation.</param>
public sealed record LinkActivatedEvent(string Url, ConsoleModifiers Modifiers) : IInputEvent;
