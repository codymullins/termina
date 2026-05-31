// Copyright (c) Petabridge, LLC. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using Termina.Components.Streaming;
using Termina.Terminal;

namespace Termina.Tests.Components;

public class StyledSegmentLinkTests
{
    [Fact]
    public void Link_SurvivesSubstring()
    {
        var seg = new StyledSegment("hello world", TextStyle.Default) { Link = "https://x" };
        Assert.Equal("https://x", seg.Substring(6).Link);
        Assert.Equal("https://x", seg.Substring(0, 5).Link);
    }

    [Fact]
    public void Link_PreservedByWithText()
    {
        var seg = new StyledSegment("a", TextStyle.Default) { Link = "u" };
        Assert.Equal("u", seg.WithText("bc").Link);
    }

    [Fact]
    public void Equality_DistinguishesLink()
    {
        var a = new StyledSegment("x", TextStyle.Default) { Link = "u1" };
        var b = new StyledSegment("x", TextStyle.Default) { Link = "u2" };
        var c = new StyledSegment("x", TextStyle.Default) { Link = "u1" };
        Assert.NotEqual(a, b);
        Assert.Equal(a, c);
    }

    [Fact]
    public void StyledLine_DoesNotCoalesceDifferentLinks()
    {
        var line = new StyledLine();
        line.Append(new StyledSegment("foo", TextStyle.Default) { Link = "u1" });
        line.Append(new StyledSegment("bar", TextStyle.Default) { Link = "u2" });
        Assert.Equal(2, line.Segments.Count);
    }

    [Fact]
    public void StyledLine_CoalescesSameLink_PreservingIt()
    {
        var line = new StyledLine();
        line.Append(new StyledSegment("foo", TextStyle.Default) { Link = "u" });
        line.Append(new StyledSegment("bar", TextStyle.Default) { Link = "u" });
        var seg = Assert.Single(line.Segments);
        Assert.Equal("foobar", seg.Text);
        Assert.Equal("u", seg.Link);
    }
}
