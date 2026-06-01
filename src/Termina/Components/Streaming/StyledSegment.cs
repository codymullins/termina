// Copyright (c) Petabridge, LLC. All rights reserved.
// Licensed under the Apache 2.0 license. See LICENSE file in the project root for full license information.

using Termina.Terminal;

namespace Termina.Components.Streaming;

/// <summary>
/// A segment of text with associated style. Used for inline styled text rendering.
/// </summary>
/// <remarks>
/// <para>
/// StyledSegment is the fundamental unit for styled text. A line of styled text
/// is composed of multiple segments, each with potentially different styling.
/// </para>
/// <para>
/// Example usage:
/// <code>
/// var segment = new StyledSegment("Hello", new TextStyle(Color.Green));
/// var boldSegment = new StyledSegment("World", new TextStyle(Color.Red, Color.Default, TextDecoration.Bold));
/// </code>
/// </para>
/// </remarks>
public readonly record struct StyledSegment : IEquatable<StyledSegment>
{
    /// <summary>
    /// The text content of this segment.
    /// </summary>
    public string Text { get; init; }

    /// <summary>
    /// The style applied to this segment's text.
    /// </summary>
    public TextStyle Style { get; init; }

    /// <summary>
    /// Optional terminal control sequence emitted at the segment's render position before text.
    /// The sequence does not contribute to display width and is not part of plain text output.
    /// </summary>
    public string? ControlSequence { get; init; }

    /// <summary>
    /// Optional factory for terminal control sequences that need render-time state.
    /// </summary>
    public Func<string>? ControlSequenceFactory { get; init; }

    /// <summary>
    /// Optional terminal control sequence emitted once before a render pass when this segment is visible.
    /// </summary>
    public string? BeforeRenderControlSequence { get; init; }

    /// <summary>
    /// Optional hyperlink URL associated with this segment. When set, the text is part of a
    /// clickable link. Unlike control sequences, the link travels with every <see cref="Substring(int)"/>
    /// so a link span stays clickable after word-wrapping splits it across lines.
    /// </summary>
    public string? Link { get; init; }

    /// <summary>
    /// Creates a new StyledSegment with the specified text and default style.
    /// </summary>
    /// <param name="text">The text content.</param>
    public StyledSegment(string text)
    {
        Text = text ?? string.Empty;
        Style = TextStyle.Default;
        ControlSequence = null;
        ControlSequenceFactory = null;
        BeforeRenderControlSequence = null;
        Link = null;
    }

    /// <summary>
    /// Creates a new StyledSegment with the specified text and style.
    /// </summary>
    /// <param name="text">The text content.</param>
    /// <param name="style">The style to apply.</param>
    public StyledSegment(string text, TextStyle style)
    {
        Text = text ?? string.Empty;
        Style = style;
        ControlSequence = null;
        ControlSequenceFactory = null;
        BeforeRenderControlSequence = null;
        Link = null;
    }

    /// <summary>
    /// Creates a new styled segment with an associated terminal control sequence.
    /// </summary>
    public StyledSegment(
        string text,
        TextStyle style,
        string? controlSequence,
        string? beforeRenderControlSequence = null,
        Func<string>? controlSequenceFactory = null,
        string? link = null)
    {
        Text = text ?? string.Empty;
        Style = style;
        ControlSequence = controlSequence;
        ControlSequenceFactory = controlSequenceFactory;
        BeforeRenderControlSequence = beforeRenderControlSequence;
        Link = link;
    }

    /// <summary>
    /// Creates a new StyledSegment with the specified text and foreground color.
    /// </summary>
    /// <param name="text">The text content.</param>
    /// <param name="foreground">The foreground color.</param>
    public StyledSegment(string text, Color foreground)
    {
        Text = text ?? string.Empty;
        Style = new TextStyle(foreground);
        ControlSequence = null;
        ControlSequenceFactory = null;
        BeforeRenderControlSequence = null;
        Link = null;
    }

    /// <summary>
    /// Creates a new StyledSegment with the specified text, colors, and decoration.
    /// </summary>
    /// <param name="text">The text content.</param>
    /// <param name="foreground">The foreground color.</param>
    /// <param name="background">The background color.</param>
    /// <param name="decoration">The text decoration.</param>
    public StyledSegment(string text, Color foreground, Color background, TextDecoration decoration = TextDecoration.None)
    {
        Text = text ?? string.Empty;
        Style = new TextStyle(foreground, background, decoration);
        ControlSequence = null;
        ControlSequenceFactory = null;
        BeforeRenderControlSequence = null;
        Link = null;
    }

    /// <summary>
    /// The display width of this segment in terminal cells.
    /// </summary>
    public int Length => TerminalText.GetDisplayWidth(Text);

    /// <summary>
    /// Returns true if this segment has no text content.
    /// </summary>
    public bool IsEmpty => Text.Length == 0;

    /// <summary>
    /// Returns true when this segment emits terminal control sequences while rendering.
    /// </summary>
    public bool HasControlSequence => ControlSequence is not null || ControlSequenceFactory is not null;

    /// <summary>
    /// Gets the render-time control sequence for this segment, if any.
    /// </summary>
    public string? GetControlSequence() => ControlSequenceFactory?.Invoke() ?? ControlSequence;

    /// <summary>
    /// Creates a new segment with the same style but different text.
    /// </summary>
    /// <param name="newText">The new text content.</param>
    /// <returns>A new StyledSegment with the updated text.</returns>
    public StyledSegment WithText(string newText) =>
        new(newText, Style, ControlSequence, BeforeRenderControlSequence, ControlSequenceFactory, Link);

    /// <summary>
    /// Creates a new segment with the same text but different style.
    /// </summary>
    /// <param name="newStyle">The new style.</param>
    /// <returns>A new StyledSegment with the updated style.</returns>
    public StyledSegment WithStyle(TextStyle newStyle) =>
        new(Text, newStyle, ControlSequence, BeforeRenderControlSequence, ControlSequenceFactory, Link);

    /// <summary>
    /// Creates a copy of this segment associated with the given hyperlink URL.
    /// </summary>
    public StyledSegment WithLink(string? link) =>
        new(Text, Style, ControlSequence, BeforeRenderControlSequence, ControlSequenceFactory, link);

    /// <summary>
    /// Creates a substring of this segment, preserving the style.
    /// </summary>
    /// <param name="startIndex">The starting character index.</param>
    /// <returns>A new StyledSegment containing the substring.</returns>
    public StyledSegment Substring(int startIndex) =>
        new(
            TerminalText.SliceByWidth(Text, startIndex, Math.Max(0, Length - startIndex)),
            Style,
            startIndex == 0 ? ControlSequence : null,
            startIndex == 0 ? BeforeRenderControlSequence : null,
            startIndex == 0 ? ControlSequenceFactory : null,
            Link);

    /// <summary>
    /// Creates a substring of this segment, preserving the style.
    /// </summary>
    /// <param name="startIndex">The starting character index.</param>
    /// <param name="length">The number of characters to include.</param>
    /// <returns>A new StyledSegment containing the substring.</returns>
    public StyledSegment Substring(int startIndex, int length)
    {
        var slice = TerminalText.SliceByWidth(Text, startIndex, length);
        if (slice.Length == 0 && startIndex == 0 && length > 0)
            slice = TerminalText.TruncateToWidth(Text, length);
        return new StyledSegment(
            slice,
            Style,
            startIndex == 0 ? ControlSequence : null,
            startIndex == 0 ? BeforeRenderControlSequence : null,
            startIndex == 0 ? ControlSequenceFactory : null,
            Link);
    }

    /// <summary>
    /// Creates an empty segment with no style.
    /// </summary>
    public static StyledSegment Empty => new(string.Empty);

    /// <inheritdoc />
    public bool Equals(StyledSegment other) =>
        Text == other.Text &&
        Style.Equals(other.Style) &&
        ControlSequence == other.ControlSequence &&
        BeforeRenderControlSequence == other.BeforeRenderControlSequence &&
        Equals(ControlSequenceFactory, other.ControlSequenceFactory) &&
        Link == other.Link;

    /// <inheritdoc />
    public override int GetHashCode() =>
        HashCode.Combine(Text, Style, ControlSequence, BeforeRenderControlSequence, ControlSequenceFactory, Link);

    /// <inheritdoc />
    public override string ToString() => Text;
}
