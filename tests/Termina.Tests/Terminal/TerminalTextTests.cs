// Copyright (c) Petabridge, LLC. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using Termina.Terminal;

namespace Termina.Tests.Terminal;

public class TerminalTextTests
{
    [Theory]
    [InlineData("abc", 3)]
    [InlineData("🖼", 2)]
    [InlineData("e\u0301", 1)]
    [InlineData("Ｘ", 2)]
    public void GetDisplayWidth_UsesTerminalCellWidth(string text, int expectedWidth)
    {
        Assert.Equal(expectedWidth, TerminalText.GetDisplayWidth(text));
    }

    [Fact]
    public void TruncateToWidth_DoesNotSplitGraphemeClusters()
    {
        Assert.Equal("🖼 ", TerminalText.TruncateToWidth("🖼 HTML", 3));
        Assert.Equal("🖼", TerminalText.TruncateToWidth("🖼 HTML", 1));
        Assert.Equal("e\u0301", TerminalText.TruncateToWidth("e\u0301x", 1));
    }

    [Fact]
    public void SliceByWidth_UsesDisplayColumns()
    {
        Assert.Equal("HTML", TerminalText.SliceByWidth("🖼 HTML", 3, 4));
    }
}
