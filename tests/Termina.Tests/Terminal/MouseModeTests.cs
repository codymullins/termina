// Copyright (c) Petabridge, LLC. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Text;
using Termina.Terminal;

namespace Termina.Tests.Terminal;

public class MouseModeTests
{
    private static (AnsiTerminal Terminal, StringBuilder Output) NewTerminal()
    {
        var sb = new StringBuilder();
        var terminal = new AnsiTerminal(new StringWriter(sb), useAlternateScreen: false);
        return (terminal, sb);
    }

    [Fact]
    public void SetMouseMode_ButtonsDragFocus_EmitsExpectedEnables_SgrLast()
    {
        var (terminal, sb) = NewTerminal();
        terminal.SetMouseMode(MouseMode.Buttons | MouseMode.Drag | MouseMode.Focus);
        terminal.Flush();
        var output = sb.ToString();

        Assert.Contains("\x1b[?1004h", output); // focus
        Assert.Contains("\x1b[?1000h", output); // base buttons
        Assert.Contains("\x1b[?1002h", output); // drag
        Assert.Contains("\x1b[?1006h", output); // SGR transport

        // SGR (1006) must come after the base-tracking enable (1000) so it wins.
        Assert.True(output.IndexOf("\x1b[?1006h", StringComparison.Ordinal)
            > output.IndexOf("\x1b[?1000h", StringComparison.Ordinal));
    }

    [Fact]
    public void SetMouseMode_Hover_EmitsAnyEvent_NotButtonEvent()
    {
        var (terminal, sb) = NewTerminal();
        terminal.SetMouseMode(MouseMode.Buttons | MouseMode.Hover);
        terminal.Flush();
        var output = sb.ToString();

        Assert.Contains("\x1b[?1003h", output);       // any-event
        Assert.DoesNotContain("\x1b[?1002h", output); // not button-event when hover is on
    }

    [Fact]
    public void SetMouseMode_None_AfterEnabled_EmitsDisables()
    {
        var (terminal, sb) = NewTerminal();
        terminal.SetMouseMode(MouseMode.Buttons | MouseMode.Drag);
        terminal.Flush();
        sb.Clear();

        terminal.SetMouseMode(MouseMode.None);
        terminal.Flush();
        var output = sb.ToString();

        Assert.Contains("\x1b[?1006l", output);
        Assert.Contains("\x1b[?1002l", output);
        Assert.Contains("\x1b[?1000l", output);
    }

    [Fact]
    public void SetMouseMode_Idempotent_NoOutputWhenUnchanged()
    {
        var (terminal, sb) = NewTerminal();
        terminal.SetMouseMode(MouseMode.Buttons | MouseMode.Drag);
        terminal.Flush();
        sb.Clear();

        terminal.SetMouseMode(MouseMode.Buttons | MouseMode.Drag);
        terminal.Flush();

        Assert.Equal(string.Empty, sb.ToString());
    }

    [Fact]
    public void DisableAllMouseTracking_EmitsAllDisablesDefensively()
    {
        var (terminal, sb) = NewTerminal();
        terminal.DisableAllMouseTracking();
        terminal.Flush();
        var output = sb.ToString();

        Assert.Contains("\x1b[?1016l", output);
        Assert.Contains("\x1b[?1006l", output);
        Assert.Contains("\x1b[?1003l", output);
        Assert.Contains("\x1b[?1002l", output);
        Assert.Contains("\x1b[?1000l", output);
        Assert.Contains("\x1b[?1004l", output);
    }
}
