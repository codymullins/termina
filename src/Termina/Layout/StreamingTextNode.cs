// Copyright (c) Petabridge, LLC. All rights reserved.
// Licensed under the Apache 2.0 license. See LICENSE file in the project root for full license information.

using System.Text;
using R3;
using Termina.Components.Streaming;
using Termina.Diagnostics;
using Termina.Input;
using Termina.Rendering;
using Termina.Terminal;

namespace Termina.Layout;

/// <summary>
/// A layout node that displays streaming text content with support for scrolling and word wrapping.
/// </summary>
/// <remarks>
/// StreamingTextNode wraps an IStreamingTextBuffer (either PersistedStreamBuffer or WindowedStreamBuffer)
/// and renders the content with automatic word wrapping and optional scrolling. It also supports
/// mouse text selection (drag/word/line), clickable link spans (segments carrying a
/// <see cref="StyledSegment.Link"/>), and link hover.
/// </remarks>
public sealed class StreamingTextNode : LayoutNode, IInvalidatingNode, IScrollable, IMouseAware, IHoverAware
{
    private readonly IStreamingTextBuffer _buffer;
    private readonly Subject<Unit> _invalidated = new();
    private readonly Subject<string> _linkActivated = new();
    private readonly Subject<string?> _hoveredLink = new();
    private readonly Subject<string> _selectionCompleted = new();

    // Selection state, expressed in absolute wrapped-line coordinates at the current content width.
    // A position is (wrapped line index, char index into that wrapped line's plain text).
    private (int Line, int Col)? _selAnchor;
    private (int Line, int Col)? _selCursor;
    private (int Line, int Col)? _pressPos;
    private bool _pressWasSingleClick;
    private bool _selectionEnabled = true;
    private string? _hoveredUrl;

    // Render geometry captured each frame for reverse-mapping screen cells to positions.
    private int _lastTopIndex;
    private int _lastPrefixLen;

    // Cached full wrapped-line list with logical provenance (which logical line each wrapped line
    // came from), so selected soft-wrapped fragments rejoin without spurious newlines on copy.
    private List<(StyledLine Line, int LogicalIndex)>? _wrappedCache;
    private int _wrappedCacheWidth = -1;
    private int _wrappedCacheLineCount = -1;
    private int _wrappedCacheCharCount = -1;

    // Scrollbar
    private ScrollbarOptions? _scrollbarOptions;

    // Cached viewport dimensions, updated during Render() and used by IScrollable
    private int _lastViewportWidth = 80;
    private int _lastViewportHeight = 24;

    // Tracked segment infrastructure
    private readonly List<ContentElement> _content = new();  // Ordered list of all content
    private readonly Dictionary<SegmentId, int> _segmentIndices = new();  // ID -> index in _content
    private readonly Dictionary<SegmentId, IDisposable> _subscriptions = new();  // Animation subscriptions
    private readonly object _contentLock = new();  // Thread safety for content mutations
    private readonly HashSet<string> _activeBeforeRenderControlSequences = new();

    // Content element types for tracking
    private abstract record ContentElement;
    private record StaticElement(StyledSegment Segment) : ContentElement;  // Untracked text
    private record TrackedElement(SegmentId Id, ITextSegment Segment) : ContentElement;  // Tracked segment

    /// <inheritdoc />
    public Observable<Unit> Invalidated => _invalidated;

    /// <summary>
    /// Observable that emits when content changes. Alias for Invalidated.
    /// </summary>
    public Observable<Unit> ContentChanged => _invalidated;

    /// <summary>
    /// Gets or sets the foreground color.
    /// </summary>
    public Color? Foreground { get; private set; }

    /// <summary>
    /// Gets or sets the background color.
    /// </summary>
    public Color? Background { get; private set; }

    /// <summary>
    /// Gets or sets a prefix to add before each line.
    /// </summary>
    public string? Prefix { get; set; }

    /// <summary>
    /// Gets or sets the color for the prefix.
    /// </summary>
    public Color? PrefixColor { get; set; }

    /// <summary>
    /// Gets the underlying buffer.
    /// </summary>
    public IStreamingTextBuffer Buffer => _buffer;

    /// <summary>Foreground color applied to selected text.</summary>
    public Color SelectionForeground { get; set; } = Color.Black;

    /// <summary>Background color applied to selected text.</summary>
    public Color SelectionBackground { get; set; } = Color.BrightYellow;

    /// <summary>Whether mouse text selection is enabled. Default true.</summary>
    public bool SelectionEnabled
    {
        get => _selectionEnabled;
        set => _selectionEnabled = value;
    }

    /// <summary>Emits the URL when a link span is activated (single click without drag).</summary>
    public Observable<string> LinkActivated => _linkActivated;

    /// <summary>Emits the hovered link URL, or null when the cursor leaves all links.</summary>
    public Observable<string?> HoveredLink => _hoveredLink;

    /// <summary>Emits the selected text when a drag-selection completes (mouse up).</summary>
    public Observable<string> SelectionCompleted => _selectionCompleted;

    /// <summary>Whether there is a non-empty selection.</summary>
    public bool HasSelection =>
        _selAnchor is { } a && _selCursor is { } c && (a.Line != c.Line || a.Col != c.Col);

    /// <summary>The currently selected text, with soft-wrapped lines rejoined.</summary>
    public string SelectedText => BuildSelectedText();

    /// <summary>Clears any active selection.</summary>
    public void ClearSelection()
    {
        if (_selAnchor is null && _selCursor is null)
            return;
        _selAnchor = null;
        _selCursor = null;
        _invalidated.OnNext(Unit.Default);
    }

    /// <summary>
    /// Creates a new StreamingTextNode with a persisted buffer (retains all content).
    /// </summary>
    public static StreamingTextNode Create()
    {
        return new StreamingTextNode(new PersistedStreamBuffer());
    }

    /// <summary>
    /// Creates a new StreamingTextNode with a windowed buffer (rolling window).
    /// </summary>
    /// <param name="windowSize">Maximum number of lines to retain.</param>
    public static StreamingTextNode CreateWindowed(int windowSize = 100)
    {
        return new StreamingTextNode(new WindowedStreamBuffer(windowSize));
    }

    /// <summary>
    /// Creates a new StreamingTextNode with a custom buffer.
    /// </summary>
    /// <param name="buffer">The buffer to use.</param>
    public StreamingTextNode(IStreamingTextBuffer buffer)
    {
        _buffer = buffer;
        WidthConstraint = new SizeConstraint.Fill();
        HeightConstraint = new SizeConstraint.Fill();
    }

    /// <summary>
    /// Appends text to the buffer and triggers a redraw.
    /// Untracked - cannot be removed or replaced later.
    /// </summary>
    public void Append(string text)
    {
        lock (_contentLock)
        {
            var segment = new StyledSegment(text, TextStyle.Default);
            _content.Add(new StaticElement(segment));
            _buffer.Append(text);
        }
        NotifyChanged();
    }

    /// <summary>
    /// Appends styled text to the buffer and triggers a redraw.
    /// Untracked - cannot be removed or replaced later.
    /// </summary>
    /// <param name="text">The text to append.</param>
    /// <param name="foreground">The foreground color (optional).</param>
    /// <param name="background">The background color (optional).</param>
    /// <param name="decoration">Text decorations like bold, italic, etc. (optional).</param>
    public void Append(string text, Color? foreground = null, Color? background = null,
        TextDecoration decoration = TextDecoration.None)
    {
        var style = new TextStyle(
            foreground ?? Color.Default,
            background ?? Color.Default,
            decoration);
        var segment = new StyledSegment(text, style);

        lock (_contentLock)
        {
            _content.Add(new StaticElement(segment));
            _buffer.Append(segment);
        }
        NotifyChanged();
    }

    /// <summary>
    /// Appends a styled segment to the buffer and triggers a redraw.
    /// Untracked - cannot be removed or replaced later.
    /// </summary>
    /// <param name="segment">The styled segment to append.</param>
    public void Append(StyledSegment segment)
    {
        lock (_contentLock)
        {
            _content.Add(new StaticElement(segment));
            _buffer.Append(segment);
        }
        NotifyChanged();
    }

    /// <summary>
    /// Appends a line to the buffer and triggers a redraw.
    /// Untracked - cannot be removed or replaced later.
    /// </summary>
    public void AppendLine(string line)
    {
        lock (_contentLock)
        {
            var segment = new StyledSegment(line + "\n", TextStyle.Default);
            _content.Add(new StaticElement(segment));
            _buffer.AppendLine(line);
        }
        NotifyChanged();
    }

    /// <summary>
    /// Appends a styled line to the buffer and triggers a redraw.
    /// Untracked - cannot be removed or replaced later.
    /// </summary>
    /// <param name="line">The line to append.</param>
    /// <param name="foreground">The foreground color (optional).</param>
    /// <param name="background">The background color (optional).</param>
    /// <param name="decoration">Text decorations like bold, italic, etc. (optional).</param>
    public void AppendLine(string line, Color? foreground = null, Color? background = null,
        TextDecoration decoration = TextDecoration.None)
    {
        var style = new TextStyle(
            foreground ?? Color.Default,
            background ?? Color.Default,
            decoration);
        var segment = new StyledSegment(line + "\n", style);

        lock (_contentLock)
        {
            _content.Add(new StaticElement(segment));
            _buffer.AppendLine(line, style);
        }
        NotifyChanged();
    }

    /// <summary>
    /// Appends a tracked text segment that can be removed or replaced later.
    /// The caller provides the ID to reference this segment.
    /// If the segment is a <see cref="BlockSegment"/>, it will start on a new line.
    /// </summary>
    /// <param name="id">The unique identifier for this segment (provided by caller).</param>
    /// <param name="segment">The text segment to append.</param>
    /// <exception cref="ArgumentException">Thrown if the ID is already in use.</exception>
    public void AppendTracked(SegmentId id, ITextSegment segment)
    {
        lock (_contentLock)
        {
            if (id == SegmentId.None)
                throw new ArgumentException("SegmentId.None cannot be used for tracked segments", nameof(id));

            if (_segmentIndices.ContainsKey(id))
                throw new ArgumentException($"SegmentId {id.Value} is already in use", nameof(id));

            // If this is a block segment, ensure it starts on a new line
            EnsureBlockNewLine(segment);

            // Unwrap BlockSegment to get the inner segment
            var innerSegment = UnwrapBlock(segment);

            var element = new TrackedElement(id, segment);
            var index = _content.Count;

            _content.Add(element);
            _segmentIndices[id] = index;
            _buffer.Append(innerSegment.GetCurrentSegment());

            // Subscribe to animation invalidation if this is an animated segment
            if (innerSegment is IAnimatedTextSegment animated)
            {
                var subscription = animated.Invalidated.Subscribe(_ =>
                {
                    // On animation frame change, rebuild buffer to show new frame
                    RebuildBuffer();
                    NotifyChanged();
                });
                _subscriptions[id] = subscription;
            }

            NotifyChanged();
        }
    }

    /// <summary>
    /// Removes a tracked segment by ID and rebuilds the buffer.
    /// </summary>
    /// <param name="id">The segment ID to remove.</param>
    /// <returns>True if the segment was found and removed, false otherwise.</returns>
    public bool Remove(SegmentId id)
    {
        lock (_contentLock)
        {
            if (!_segmentIndices.TryGetValue(id, out var index))
                return false;

            // Dispose animation subscription if present
            if (_subscriptions.TryGetValue(id, out var subscription))
            {
                subscription.Dispose();
                _subscriptions.Remove(id);
            }

            // Dispose the segment itself
            if (_content[index] is TrackedElement { Segment: var segment })
            {
                segment.Dispose();
            }

            // Remove from content list
            _content.RemoveAt(index);
            _segmentIndices.Remove(id);

            // Update indices for all segments after this one
            for (var i = index; i < _content.Count; i++)
            {
                if (_content[i] is TrackedElement { Id: var otherId })
                {
                    _segmentIndices[otherId] = i;
                }
            }

            // Rebuild buffer from remaining content
            RebuildBuffer();
            NotifyChanged();
            return true;
        }
    }

    /// <summary>
    /// Replaces a tracked segment with a new segment.
    /// </summary>
    /// <param name="id">The segment ID to replace.</param>
    /// <param name="newSegment">The new segment to replace it with.</param>
    /// <param name="keepTracked">If true, keeps the segment tracked with the same ID. If false, converts to untracked static content.</param>
    /// <returns>True if the segment was found and replaced, false otherwise.</returns>
    public bool Replace(SegmentId id, ITextSegment newSegment, bool keepTracked = true)
    {
        lock (_contentLock)
        {
            if (!_segmentIndices.TryGetValue(id, out var index))
                return false;

            // Dispose old animation subscription if present
            if (_subscriptions.TryGetValue(id, out var oldSubscription))
            {
                oldSubscription.Dispose();
                _subscriptions.Remove(id);
            }

            // Dispose old segment
            if (_content[index] is TrackedElement { Segment: var oldSegment })
            {
                oldSegment.Dispose();
            }

            if (keepTracked)
            {
                // Replace with new tracked segment
                _content[index] = new TrackedElement(id, newSegment);

                // Subscribe to new animation if applicable
                if (newSegment is IAnimatedTextSegment animated)
                {
                    var subscription = animated.Invalidated.Subscribe(_ =>
                    {
                        RebuildBuffer();
                        NotifyChanged();
                    });
                    _subscriptions[id] = subscription;
                }
            }
            else
            {
                // Replace with static element (no longer tracked)
                var staticSegment = newSegment.GetCurrentSegment();
                _content[index] = new StaticElement(staticSegment);
                _segmentIndices.Remove(id);

                // Dispose the new segment since we only needed its current content
                newSegment.Dispose();

                // Update indices for all segments after this one
                for (var i = index + 1; i < _content.Count; i++)
                {
                    if (_content[i] is TrackedElement { Id: var otherId })
                    {
                        _segmentIndices[otherId] = i;
                    }
                }
            }

            // Rebuild buffer with new content
            RebuildBuffer();
            NotifyChanged();
            return true;
        }
    }

    /// <summary>
    /// Rebuilds the buffer from the content list.
    /// Called when tracked segments are added, removed, or replaced.
    /// </summary>
    private void RebuildBuffer()
    {
        _buffer.Clear();

        foreach (var element in _content)
        {
            switch (element)
            {
                case StaticElement s:
                    _buffer.Append(s.Segment);
                    break;
                case TrackedElement t:
                    // Handle block segments in rebuild
                    EnsureBlockNewLine(t.Segment);
                    var inner = UnwrapBlock(t.Segment);
                    _buffer.Append(inner.GetCurrentSegment());
                    break;
                default:
                    throw new InvalidOperationException($"Unknown content element type: {element.GetType()}");
            }
        }
    }

    /// <summary>
    /// If the segment is a BlockSegment and the current line has content,
    /// ensures we start on a new line.
    /// </summary>
    private void EnsureBlockNewLine(ITextSegment segment)
    {
        if (segment is BlockSegment && _buffer.HasContentOnCurrentLine)
        {
            _buffer.AppendLine(string.Empty);
        }
    }

    /// <summary>
    /// Unwraps a BlockSegment to get the inner segment, or returns the segment as-is.
    /// </summary>
    private static ITextSegment UnwrapBlock(ITextSegment segment)
    {
        return segment is BlockSegment block ? block.Inner : segment;
    }

    /// <summary>
    /// Clears all content from the buffer, including tracked segments.
    /// </summary>
    public void Clear()
    {
        lock (_contentLock)
        {
            // Dispose all subscriptions
            foreach (var subscription in _subscriptions.Values)
            {
                subscription.Dispose();
            }
            _subscriptions.Clear();

            // Dispose all tracked segments
            foreach (var element in _content)
            {
                if (element is TrackedElement { Segment: var segment })
                {
                    segment.Dispose();
                }
            }

            _content.Clear();
            _segmentIndices.Clear();
            _buffer.Clear();
        }
        NotifyChanged();
    }

    /// <summary>
    /// Scrolls up by the specified number of lines (only applies to PersistedStreamBuffer).
    /// </summary>
    public void ScrollUp(int lines = 1, int viewportWidth = 80)
    {
        if (_buffer is PersistedStreamBuffer persisted)
        {
            persisted.ScrollUp(lines, viewportWidth);
            NotifyChanged();
        }
    }

    /// <summary>
    /// Scrolls down by the specified number of lines (only applies to PersistedStreamBuffer).
    /// </summary>
    public void ScrollDown(int lines = 1)
    {
        if (_buffer is PersistedStreamBuffer persisted)
        {
            persisted.ScrollDown(lines);
            NotifyChanged();
        }
    }

    /// <summary>
    /// Scrolls to the bottom (only applies to PersistedStreamBuffer).
    /// </summary>
    public void ScrollToBottom()
    {
        if (_buffer is PersistedStreamBuffer persisted)
        {
            persisted.ScrollToBottom();
            NotifyChanged();
        }
    }

    /// <summary>
    /// Handle keyboard input for scrolling. Returns true if the input was handled.
    /// </summary>
    /// <param name="key">The key info to handle.</param>
    /// <param name="viewportHeight">The height of the visible area (for page scrolling).</param>
    /// <param name="viewportWidth">The width of the visible area (for word wrap calculations).</param>
    public bool HandleInput(ConsoleKeyInfo key, int viewportHeight, int viewportWidth)
    {
        // Only PersistedStreamBuffer supports scrolling
        if (_buffer is not PersistedStreamBuffer)
            return false;

        switch (key.Key)
        {
            case ConsoleKey.PageUp:
                ScrollUp(Math.Max(1, viewportHeight - 1), viewportWidth);
                return true;

            case ConsoleKey.PageDown:
                ScrollDown(Math.Max(1, viewportHeight - 1));
                return true;

            case ConsoleKey.Home when key.Modifiers.HasFlag(ConsoleModifiers.Control):
                // Ctrl+Home scrolls to top
                if (_buffer is PersistedStreamBuffer persisted)
                {
                    persisted.ScrollToTop(viewportWidth);
                    NotifyChanged();
                }
                return true;

            case ConsoleKey.End when key.Modifiers.HasFlag(ConsoleModifiers.Control):
                // Ctrl+End scrolls to bottom
                ScrollToBottom();
                return true;

            default:
                return false;
        }
    }

    private void NotifyChanged()
    {
        _invalidated.OnNext(Unit.Default);
    }

    /// <summary>
    /// Set foreground color.
    /// </summary>
    public StreamingTextNode WithForeground(Color color)
    {
        Foreground = color;
        return this;
    }

    /// <summary>
    /// Set background color.
    /// </summary>
    public StreamingTextNode WithBackground(Color color)
    {
        Background = color;
        return this;
    }

    /// <summary>
    /// Set prefix string.
    /// </summary>
    public StreamingTextNode WithPrefix(string prefix, Color? color = null)
    {
        Prefix = prefix;
        PrefixColor = color;
        return this;
    }

    /// <summary>
    /// Enable the visual scrollbar with default options.
    /// The scrollbar occupies the rightmost column of the node's bounds.
    /// It is hidden automatically when all content fits within the viewport.
    /// </summary>
    public StreamingTextNode WithScrollbar() => WithScrollbar(new ScrollbarOptions());

    /// <summary>
    /// Enable the visual scrollbar with the specified options.
    /// The scrollbar occupies the rightmost column of the node's bounds.
    /// </summary>
    /// <param name="options">Scrollbar appearance and behavior options.</param>
    public StreamingTextNode WithScrollbar(ScrollbarOptions options)
    {
        _scrollbarOptions = options;
        return this;
    }

    /// <inheritdoc cref="IScrollable.CanScrollUp"/>
    /// <remarks>
    /// Depends on cached viewport dimensions updated during <see cref="Render"/>.
    /// Returns <see langword="false"/> before the first render.
    /// </remarks>
    public bool CanScrollUp => _buffer is PersistedStreamBuffer p &&
        p.ScrollOffset < p.GetMaxScrollOffset(_lastViewportWidth);

    /// <inheritdoc cref="IScrollable.CanScrollDown"/>
    /// <remarks>
    /// Depends on cached viewport dimensions updated during <see cref="Render"/>.
    /// Returns <see langword="false"/> before the first render.
    /// </remarks>
    public bool CanScrollDown => _buffer is PersistedStreamBuffer p && p.ScrollOffset > 0;

    void IScrollable.ScrollUp(int lines) => ScrollUp(lines, _lastViewportWidth);

    void IScrollable.ScrollDown(int lines) => ScrollDown(lines);

    /// <inheritdoc />
    public override Size Measure(Size available)
    {
        var width = WidthConstraint.Compute(available.Width, available.Width, available.Width);
        var height = HeightConstraint.Compute(available.Height, _buffer.LineCount, available.Height);
        return new Size(width, height);
    }

    /// <inheritdoc />
    public override void Render(IRenderContext context, Rect bounds)
    {
        if (!bounds.HasArea)
            return;

        // Create a sub-context so coordinates are relative to this node's bounds
        var streamContext = context.CreateSubContext(bounds);

        var prefixLen = TerminalText.GetDisplayWidth(Prefix);
        var scrollbarWidth = ShouldDrawScrollbar(bounds) ? 1 : 0;
        var contentWidth = bounds.Width - prefixLen - scrollbarWidth;
        if (contentWidth <= 0)
            return;

        // Update cached viewport dimensions for IScrollable + mouse reverse-mapping
        _lastViewportWidth = contentWidth;
        _lastViewportHeight = bounds.Height;
        _lastPrefixLen = prefixLen;
        _lastTopIndex = _buffer.GetFirstVisibleWrappedIndex(bounds.Height, contentWidth);

        // Register the whole node as a selectable/link-bearing text region.
        context.RegisterHit(this, bounds, HitTestKind.Text);

        // Get styled lines from buffer
        var styledLines = _buffer.GetVisibleStyledLines(bounds.Height, contentWidth);
        EmitBeforeRenderControlSequences(streamContext, styledLines);

        var (selMin, selMax) = NormalizedSelection();

        for (var i = 0; i < bounds.Height && i < styledLines.Count; i++)
        {
            // Draw prefix if any
            if (!string.IsNullOrEmpty(Prefix))
            {
                streamContext.ResetColors();
                if (PrefixColor.HasValue)
                    streamContext.SetForeground(PrefixColor.Value);
                streamContext.WriteAt(0, i, Prefix);
                streamContext.ResetColors();
            }

            DrawLine(streamContext, i, styledLines[i], _lastTopIndex + i, prefixLen, contentWidth, selMin, selMax);
        }

        streamContext.ResetColors();

        if (scrollbarWidth > 0)
            DrawScrollbar(streamContext, bounds, contentWidth);
    }

    /// <summary>
    /// Draws one visible wrapped line, applying selection highlight, link hover underline, and OSC 8
    /// hyperlink sequences. Uses a fast whole-segment path when the line is not part of the
    /// selection, and a per-character path when it is.
    /// </summary>
    private void DrawLine(
        IRenderContext context, int row, StyledLine styledLine, int absLine,
        int prefixLen, int contentWidth,
        (int Line, int Col)? selMin, (int Line, int Col)? selMax)
    {
        var lineSelected = selMin is { } mn && selMax is { } mx && absLine >= mn.Line && absLine <= mx.Line;
        var x = prefixLen;
        var charOffset = 0;

        foreach (var segment in styledLine.Segments)
        {
            var text = segment.Text;
            var availableWidth = contentWidth - (x - prefixLen);
            if (availableWidth <= 0)
                break;
            if (TerminalText.GetDisplayWidth(text) > availableWidth)
                text = TerminalText.TruncateToWidth(text, availableWidth);
            if (text.Length == 0)
                continue;

            var isLink = !string.IsNullOrEmpty(segment.Link);
            var isHoveredLink = isLink && segment.Link == _hoveredUrl;
            var baseStyle = GetEffectiveStyle(segment.Style);
            if (isHoveredLink)
                baseStyle = new TextStyle(baseStyle.Foreground, baseStyle.Background, baseStyle.Decoration | TextDecoration.Underline);

            // Tag subsequent writes with the hyperlink. The diffing terminal records this per cell
            // and emits the OSC 8 wrapper in-band at flush, so it survives double-buffering and lets
            // terminals with native modifier-click follow the link.
            context.SetLink(segment.Link);

            if (!isLink)
            {
                var controlSequence = segment.GetControlSequence();
                if (controlSequence is not null)
                    context.WriteControlAt(x, row, controlSequence);
            }

            if (lineSelected)
            {
                // Per-character so selected cells get the selection colors.
                var col = 0;
                foreach (var ch in text)
                {
                    var globalCol = charOffset + col;
                    var selected = IsColumnSelected(absLine, globalCol, selMin!.Value, selMax!.Value);
                    var style = selected
                        ? new TextStyle(SelectionForeground, SelectionBackground, baseStyle.Decoration)
                        : baseStyle;
                    context.ResetColors();
                    context.ApplyStyle(style);
                    context.WriteAt(x, row, ch);
                    x += TerminalText.GetDisplayWidth(ch.ToString());
                    col++;
                }
            }
            else
            {
                context.ResetColors();
                context.ApplyStyle(baseStyle);
                context.WriteAt(x, row, text);
                x += TerminalText.GetDisplayWidth(text);
            }

            context.SetLink(null);

            charOffset += segment.Text.Length;
        }
    }

    private static bool IsColumnSelected(int line, int col, (int Line, int Col) min, (int Line, int Col) max)
    {
        var afterMin = line > min.Line || (line == min.Line && col >= min.Col);
        var beforeMax = line < max.Line || (line == max.Line && col < max.Col);
        return afterMin && beforeMax;
    }

    private (( int Line, int Col)? Min, (int Line, int Col)? Max) NormalizedSelection()
    {
        if (_selAnchor is not { } a || _selCursor is not { } c)
            return (null, null);
        var before = a.Line < c.Line || (a.Line == c.Line && a.Col <= c.Col);
        return before ? (a, c) : (c, a);
    }

    private void EmitBeforeRenderControlSequences(IRenderContext context, IReadOnlyList<StyledLine> styledLines)
    {
        var current = new HashSet<string>();
        foreach (var line in styledLines)
        {
            foreach (var segment in line.Segments)
            {
                if (segment.BeforeRenderControlSequence is not null)
                    current.Add(segment.BeforeRenderControlSequence);
            }
        }

        if (_activeBeforeRenderControlSequences.Count > 0 || current.Count > 0)
        {
            foreach (var sequence in _activeBeforeRenderControlSequences.Union(current))
                context.WriteControlAt(0, 0, sequence);
        }

        _activeBeforeRenderControlSequences.Clear();
        foreach (var sequence in current)
            _activeBeforeRenderControlSequences.Add(sequence);
    }

    private bool ShouldDrawScrollbar(Rect bounds)
    {
        if (_scrollbarOptions == null || _buffer is not PersistedStreamBuffer persisted)
            return false;

        var prefixLen = TerminalText.GetDisplayWidth(Prefix);
        var contentWidthWithScrollbar = bounds.Width - prefixLen - 1;
        if (contentWidthWithScrollbar <= 0)
            return false;

        if (!_scrollbarOptions.AutoHide)
            return true;

        // Show scrollbar only when wrapped line count exceeds the visible viewport height
        return persisted.GetWrappedLineCount(contentWidthWithScrollbar) > bounds.Height;
    }

    private void DrawScrollbar(IRenderContext context, Rect bounds, int contentWidth)
    {
        if (_buffer is not PersistedStreamBuffer persisted) return;

        var scrollOffset = persisted.ScrollOffset;
        var maxScroll = persisted.GetMaxScrollOffset(contentWidth);
        if (maxScroll <= 0) return;

        var x = bounds.Width - 1;
        var trackHeight = bounds.Height;
        var totalLines = trackHeight + maxScroll;
        var thumbHeight = Math.Max(1, (int)((float)trackHeight / totalLines * trackHeight));
        var maxThumbTop = trackHeight - thumbHeight;

        // PersistedStreamBuffer uses 0 = bottom convention, so invert the thumb position:
        // scrollOffset=0       → bottom → thumbTop = maxThumbTop
        // scrollOffset=maxScroll → top  → thumbTop = 0
        var thumbTop = maxScroll > 0
            ? (int)((1f - (float)scrollOffset / maxScroll) * maxThumbTop)
            : maxThumbTop;

        var opts = _scrollbarOptions!;
        var trackColor = opts.TrackColor ?? Color.BrightBlack;
        var thumbColor = opts.ThumbColor ?? Color.White;

        for (var y = 0; y < trackHeight; y++)
        {
            var isThumb = y >= thumbTop && y < thumbTop + thumbHeight;
            context.SetForeground(isThumb ? thumbColor : trackColor);
            context.WriteAt(x, y, isThumb ? opts.ThumbChar : opts.TrackChar);
        }

        context.ResetColors();
    }

    /// <summary>
    /// Gets the effective style by combining segment style with node-level defaults.
    /// </summary>
    private TextStyle GetEffectiveStyle(TextStyle segmentStyle)
    {
        // Use segment colors if set, otherwise fall back to node-level colors
        var fg = segmentStyle.HasForeground ? segmentStyle.Foreground
            : (Foreground ?? Color.Default);
        var bg = segmentStyle.HasBackground ? segmentStyle.Background
            : (Background ?? Color.Default);

        return new TextStyle(fg, bg, segmentStyle.Decoration);
    }

    // ── Mouse selection + links ──────────────────────────────────────────────

    /// <summary>
    /// Builds (or returns cached) the full wrapped-line list with logical provenance at the given
    /// width. Keyed on width + buffer line/char counts so append and clear invalidate it.
    /// </summary>
    private List<(StyledLine Line, int LogicalIndex)> EnsureWrapped(int width)
    {
        var lineCount = _buffer.LineCount;
        var charCount = _buffer.CharacterCount;
        if (_wrappedCache is not null && _wrappedCacheWidth == width
            && _wrappedCacheLineCount == lineCount && _wrappedCacheCharCount == charCount)
            return _wrappedCache;

        var result = new List<(StyledLine, int)>();
        var logical = _buffer.GetAllStyledLines();
        for (var li = 0; li < logical.Count; li++)
        {
            foreach (var wrapped in StyledWordWrapper.WrapLine(logical[li], width))
                result.Add((wrapped, li));
        }

        _wrappedCache = result;
        _wrappedCacheWidth = width;
        _wrappedCacheLineCount = lineCount;
        _wrappedCacheCharCount = charCount;
        return result;
    }

    /// <summary>Maps a click relative to this node's bounds to an absolute wrapped position.</summary>
    private (int Line, int Col) ScreenToPosition(int localColumn, int localRow, List<(StyledLine Line, int LogicalIndex)> wrapped)
    {
        if (wrapped.Count == 0)
            return (0, 0);

        var absLine = Math.Clamp(_lastTopIndex + localRow, 0, wrapped.Count - 1);
        var plain = wrapped[absLine].Line.ToPlainText();
        var displayCol = Math.Max(0, localColumn - _lastPrefixLen);
        var charIndex = DisplayColumnToCharIndex(plain, displayCol);
        return (absLine, charIndex);
    }

    private static int DisplayColumnToCharIndex(string text, int displayColumn)
    {
        var width = 0;
        for (var i = 0; i < text.Length; i++)
        {
            var cw = TerminalText.GetDisplayWidth(text[i].ToString());
            if (width + cw > displayColumn)
                return i;
            width += cw;
        }
        return text.Length;
    }

    private static (int Start, int End) WordBoundsAt(string text, int pos)
    {
        pos = Math.Clamp(pos, 0, text.Length);
        var start = pos;
        while (start > 0 && !char.IsWhiteSpace(text[start - 1]))
            start--;
        var end = pos;
        while (end < text.Length && !char.IsWhiteSpace(text[end]))
            end++;
        return (start, end);
    }

    /// <summary>Returns the link URL for the segment covering a wrapped position, or null.</summary>
    private static string? LinkAt((int Line, int Col) pos, List<(StyledLine Line, int LogicalIndex)> wrapped)
    {
        if (pos.Line < 0 || pos.Line >= wrapped.Count)
            return null;
        var acc = 0;
        foreach (var segment in wrapped[pos.Line].Line.Segments)
        {
            var len = segment.Text.Length;
            if (pos.Col >= acc && pos.Col < acc + len)
                return segment.Link;
            acc += len;
        }
        return null;
    }

    /// <inheritdoc />
    public bool HandleMouse(MouseEvent e, Rect bounds)
    {
        if (!_selectionEnabled)
            return false;

        var width = _lastViewportWidth;
        if (width <= 0)
            return false;

        var wrapped = EnsureWrapped(width);
        var localColumn = e.Column - bounds.X;
        var localRow = e.Row - bounds.Y;

        if (TerminaTrace.IsEnabled)
        {
            var dbgPos = ScreenToPosition(localColumn, localRow, wrapped);
            var msg = $"STN.HandleMouse kind={e.Kind} col={e.Column} row={e.Row} " +
                $"bounds=({bounds.X},{bounds.Y},{bounds.Width},{bounds.Height}) local=({localColumn},{localRow}) " +
                $"top={_lastTopIndex} prefix={_lastPrefixLen} width={width} wrappedCount={wrapped.Count} " +
                $"absLine={dbgPos.Line} colIdx={dbgPos.Col} link={LinkAt(dbgPos, wrapped) ?? "<null>"}";
            TerminaTrace.Input.Debug(this, msg);
        }

        switch (e.Kind)
        {
            case MouseEventKind.Down:
            {
                if (e.Button != Termina.Input.MouseButton.Left)
                    return false;

                var pos = ScreenToPosition(localColumn, localRow, wrapped);
                _pressPos = pos;
                _pressWasSingleClick = e.ClickChain == 1;

                if (e.ClickChain == 2)
                {
                    var plain = wrapped[pos.Line].Line.ToPlainText();
                    var (ws, we) = WordBoundsAt(plain, pos.Col);
                    _selAnchor = (pos.Line, ws);
                    _selCursor = (pos.Line, we);
                }
                else if (e.ClickChain >= 3)
                {
                    var plain = wrapped[pos.Line].Line.ToPlainText();
                    _selAnchor = (pos.Line, 0);
                    _selCursor = (pos.Line, plain.Length);
                }
                else if ((e.Modifiers & ConsoleModifiers.Shift) != 0 && _selAnchor is not null)
                {
                    _selCursor = pos;
                }
                else
                {
                    _selAnchor = pos;
                    _selCursor = pos;
                }

                _invalidated.OnNext(Unit.Default);
                return true;
            }

            case MouseEventKind.Drag:
            {
                _selAnchor ??= _pressPos ?? ScreenToPosition(localColumn, localRow, wrapped);
                _selCursor = ScreenToPosition(localColumn, localRow, wrapped);

                // Auto-scroll when the drag leaves the viewport vertically.
                if (localRow < 0)
                    ScrollUp(1, width);
                else if (localRow >= _lastViewportHeight)
                    ScrollDown(1);

                _invalidated.OnNext(Unit.Default);
                return true;
            }

            case MouseEventKind.Up:
            {
                // A click on a link activates it. We key off "no resulting selection" rather than
                // "no drag occurred" because a real click frequently includes sub-cell jitter that
                // the terminal reports as a same-cell drag (especially with ?1003h hover on). A
                // genuine drag-select produces a non-empty selection, which suppresses activation.
                if (_pressWasSingleClick && !HasSelection && _pressPos is { } pp)
                {
                    var url = LinkAt(pp, wrapped);
                    if (!string.IsNullOrEmpty(url))
                    {
                        _selAnchor = null;
                        _selCursor = null;
                        _linkActivated.OnNext(url);
                        _invalidated.OnNext(Unit.Default);
                        return true;
                    }
                }

                if (HasSelection)
                    _selectionCompleted.OnNext(SelectedText);
                return true;
            }

            case MouseEventKind.Move:
            {
                UpdateHoveredLink(localColumn, localRow, wrapped);
                return false;
            }

            default:
                return false;
        }
    }

    private void UpdateHoveredLink(int localColumn, int localRow, List<(StyledLine Line, int LogicalIndex)> wrapped)
    {
        string? url = null;
        if (localRow >= 0 && localRow < _lastViewportHeight)
        {
            var pos = ScreenToPosition(localColumn, localRow, wrapped);
            url = LinkAt(pos, wrapped);
        }

        if (url == _hoveredUrl)
            return;

        _hoveredUrl = url;
        _hoveredLink.OnNext(url);
        _invalidated.OnNext(Unit.Default);
    }

    /// <inheritdoc />
    public void OnMouseEnter() { }

    /// <inheritdoc />
    public void OnMouseLeave()
    {
        if (_hoveredUrl is null)
            return;
        _hoveredUrl = null;
        _hoveredLink.OnNext(null);
        _invalidated.OnNext(Unit.Default);
    }

    private string BuildSelectedText()
    {
        var (min, max) = NormalizedSelection();
        if (min is not { } mn || max is not { } mx || (mn.Line == mx.Line && mn.Col == mx.Col))
            return string.Empty;

        var wrapped = EnsureWrapped(_lastViewportWidth);
        if (wrapped.Count == 0)
            return string.Empty;

        var sb = new StringBuilder();
        for (var line = mn.Line; line <= mx.Line && line < wrapped.Count; line++)
        {
            var plain = wrapped[line].Line.ToPlainText();
            var start = line == mn.Line ? mn.Col : 0;
            var end = line == mx.Line ? mx.Col : plain.Length;
            start = Math.Clamp(start, 0, plain.Length);
            end = Math.Clamp(end, 0, plain.Length);
            if (end < start)
                end = start;

            if (line > mn.Line)
            {
                // Newline only when crossing a logical-line boundary, so soft-wrapped fragments rejoin.
                if (wrapped[line].LogicalIndex != wrapped[line - 1].LogicalIndex)
                    sb.Append('\n');
            }

            sb.Append(plain, start, end - start);
        }

        return sb.ToString();
    }

    /// <inheritdoc />
    public override void Dispose()
    {
        lock (_contentLock)
        {
            // Dispose all subscriptions
            foreach (var subscription in _subscriptions.Values)
            {
                subscription.Dispose();
            }
            _subscriptions.Clear();

            // Dispose all tracked segments
            foreach (var element in _content)
            {
                if (element is TrackedElement { Segment: var segment })
                {
                    segment.Dispose();
                }
            }

            _content.Clear();
            _segmentIndices.Clear();
        }

        _invalidated.OnCompleted();
        _invalidated.Dispose();
        _linkActivated.OnCompleted();
        _linkActivated.Dispose();
        _hoveredLink.OnCompleted();
        _hoveredLink.Dispose();
        _selectionCompleted.OnCompleted();
        _selectionCompleted.Dispose();
        base.Dispose();
    }
}
