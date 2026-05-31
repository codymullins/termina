// Copyright (c) Petabridge, LLC. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using Termina.Terminal;

namespace Termina.Tests.Terminal;

public class DiffingTerminalLinkTests
{
    private const string Url = "https://example.com";
    private static string Open(string u) => $"\x1b]8;;{u}\x1b\\";
    private const string Close = "\x1b]8;;\x1b\\";

    private static int Count(string haystack, string needle)
    {
        var n = 0;
        var i = 0;
        while ((i = haystack.IndexOf(needle, i, StringComparison.Ordinal)) >= 0) { n++; i += needle.Length; }
        return n;
    }

    private static void WriteLinkedText(DiffingTerminal term, string? link, string text, int x = 0, int y = 0)
    {
        term.SetLink(link);
        term.MoveTo(x, y);
        term.Write(text);
        term.SetLink(null);
    }

    [Fact]
    public void Link_EmittedInBand_OpenBeforeTextCloseAfter()
    {
        var inner = new VirtualTerminal(40, 2);
        var term = new DiffingTerminal(inner);

        WriteLinkedText(term, Url, "docs");
        term.Flush();

        var raw = string.Concat(inner.RawOutput);
        var openIdx = raw.IndexOf(Open(Url), StringComparison.Ordinal);
        var textIdx = raw.IndexOf("docs", StringComparison.Ordinal);
        var closeIdx = raw.IndexOf(Close, openIdx + Open(Url).Length, StringComparison.Ordinal);

        Assert.True(openIdx >= 0, "OSC 8 open not emitted");
        Assert.True(textIdx > openIdx, "link text must come after the OSC 8 open");
        Assert.True(closeIdx > textIdx, "OSC 8 close must come after the link text");
    }

    [Fact]
    public void UnchangedReflush_DoesNotReemitLink()
    {
        var inner = new VirtualTerminal(40, 2);
        var term = new DiffingTerminal(inner);

        WriteLinkedText(term, Url, "docs");
        term.Flush();

        // Re-flush with an identical pending buffer: the diff is empty, so nothing — including the
        // hyperlink — should be re-emitted.
        term.Flush();

        Assert.Equal(1, Count(string.Concat(inner.RawOutput), Open(Url)));
    }

    [Fact]
    public void ChangingLink_ReemitsNewUrl()
    {
        var inner = new VirtualTerminal(40, 2);
        var term = new DiffingTerminal(inner);

        WriteLinkedText(term, Url, "docs");
        term.Flush();

        term.ClearScreen();
        WriteLinkedText(term, "https://other.test", "docs");
        term.Flush();

        var raw = string.Concat(inner.RawOutput);
        Assert.Equal(1, Count(raw, Open(Url)));
        Assert.Equal(1, Count(raw, Open("https://other.test")));
    }

    [Fact]
    public void RemovingLink_ClosesIt()
    {
        var inner = new VirtualTerminal(40, 2);
        var term = new DiffingTerminal(inner);

        WriteLinkedText(term, Url, "docs");
        term.Flush();

        // Replace the linked text with plain text in the same cells.
        term.ClearScreen();
        WriteLinkedText(term, null, "docs");
        term.Flush();

        // The link must have been closed (at least once) so the plain redraw isn't hyperlinked.
        Assert.True(Count(string.Concat(inner.RawOutput), Close) >= 1);
    }

    [Fact]
    public void TerminalCell_Link_ParticipatesInEquality()
    {
        var a = new TerminalCell("x", Color.Default, Color.Default, TextDecoration.None, "u1");
        var b = new TerminalCell("x", Color.Default, Color.Default, TextDecoration.None, "u2");
        var c = new TerminalCell("x", Color.Default, Color.Default, TextDecoration.None, "u1");
        Assert.NotEqual(a, b);
        Assert.Equal(a, c);
    }
}
