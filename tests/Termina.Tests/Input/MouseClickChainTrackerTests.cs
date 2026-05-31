// Copyright (c) Petabridge, LLC. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using Microsoft.Extensions.Time.Testing;
using Termina.Input;

namespace Termina.Tests.Input;

public class MouseClickChainTrackerTests
{
    private static MouseClickChainTracker NewTracker(out FakeTimeProvider time, int thresholdMs = 500)
    {
        time = new FakeTimeProvider();
        return new MouseClickChainTracker(TimeSpan.FromMilliseconds(thresholdMs), time);
    }

    [Fact]
    public void FirstPress_IsSingleClick()
    {
        var tracker = NewTracker(out _);
        Assert.Equal(1, tracker.Register(MouseButton.Left, 3, 4));
    }

    [Fact]
    public void SecondPress_SameCell_WithinWindow_IsDouble()
    {
        var tracker = NewTracker(out var time);
        tracker.Register(MouseButton.Left, 3, 4);
        time.Advance(TimeSpan.FromMilliseconds(200));
        Assert.Equal(2, tracker.Register(MouseButton.Left, 3, 4));
    }

    [Fact]
    public void ThirdPress_SameCell_WithinWindow_IsTriple_AndCaps()
    {
        var tracker = NewTracker(out var time);
        tracker.Register(MouseButton.Left, 3, 4);
        time.Advance(TimeSpan.FromMilliseconds(100));
        tracker.Register(MouseButton.Left, 3, 4);
        time.Advance(TimeSpan.FromMilliseconds(100));
        Assert.Equal(3, tracker.Register(MouseButton.Left, 3, 4));
        time.Advance(TimeSpan.FromMilliseconds(100));
        Assert.Equal(3, tracker.Register(MouseButton.Left, 3, 4)); // capped at triple
    }

    [Fact]
    public void SecondPress_AfterWindow_ResetsToSingle()
    {
        var tracker = NewTracker(out var time);
        tracker.Register(MouseButton.Left, 3, 4);
        time.Advance(TimeSpan.FromMilliseconds(600)); // past 500ms
        Assert.Equal(1, tracker.Register(MouseButton.Left, 3, 4));
    }

    [Fact]
    public void SecondPress_DifferentCell_ResetsToSingle()
    {
        var tracker = NewTracker(out var time);
        tracker.Register(MouseButton.Left, 3, 4);
        time.Advance(TimeSpan.FromMilliseconds(100));
        Assert.Equal(1, tracker.Register(MouseButton.Left, 9, 4));
    }

    [Fact]
    public void DifferentButton_ResetsToSingle()
    {
        var tracker = NewTracker(out var time);
        tracker.Register(MouseButton.Left, 3, 4);
        time.Advance(TimeSpan.FromMilliseconds(100));
        Assert.Equal(1, tracker.Register(MouseButton.Right, 3, 4));
    }
}
