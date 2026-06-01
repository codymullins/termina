// Copyright (c) Petabridge, LLC. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

namespace Termina.Input;

/// <summary>
/// Synthesizes the click-chain count (single / double / triple) for mouse press events. Terminals
/// never report multi-clicks — they send N separate <see cref="MouseEventKind.Down"/> events — so
/// the chain is reconstructed here from press timing and position.
/// </summary>
/// <remarks>
/// Uses an injectable <see cref="TimeProvider"/> so tests can advance time deterministically.
/// </remarks>
public sealed class MouseClickChainTracker
{
    private readonly TimeProvider _timeProvider;
    private TimeSpan _threshold;

    private MouseButton _lastButton = MouseButton.None;
    private int _lastColumn = -1;
    private int _lastRow = -1;
    private long _lastDownTimestamp;
    private int _chain;

    /// <summary>
    /// Creates a tracker.
    /// </summary>
    /// <param name="threshold">Maximum gap between presses in the same cell to extend the chain.</param>
    /// <param name="timeProvider">Clock source; defaults to <see cref="TimeProvider.System"/>.</param>
    public MouseClickChainTracker(TimeSpan threshold, TimeProvider? timeProvider = null)
    {
        _threshold = threshold;
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    /// <summary>
    /// Updates the double-click threshold at runtime.
    /// </summary>
    public void SetThreshold(TimeSpan threshold) => _threshold = threshold;

    /// <summary>
    /// Returns the click-chain count (1, 2, or 3) for a press at the given cell with the given
    /// button, advancing internal state. Non-press events should not be passed here.
    /// </summary>
    public int Register(MouseButton button, int column, int row)
    {
        var now = _timeProvider.GetTimestamp();
        var elapsed = _lastChainExists()
            ? _timeProvider.GetElapsedTime(_lastDownTimestamp, now)
            : TimeSpan.MaxValue;

        if (button == _lastButton && column == _lastColumn && row == _lastRow && elapsed <= _threshold)
        {
            _chain = Math.Min(_chain + 1, 3);
        }
        else
        {
            _chain = 1;
        }

        _lastButton = button;
        _lastColumn = column;
        _lastRow = row;
        _lastDownTimestamp = now;
        return _chain;

        bool _lastChainExists() => _lastButton != MouseButton.None || _lastColumn >= 0;
    }
}
