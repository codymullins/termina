// Copyright (c) Petabridge, LLC. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

namespace Termina.Terminal;

/// <summary>
/// Represents a single cell in the terminal buffer.
/// Each cell contains text and its associated styling (colors and decorations).
/// </summary>
/// <remarks>
/// Used for double-buffering in diff-based rendering to track what's on screen
/// and compare against pending changes to minimize ANSI output.
/// </remarks>
public readonly record struct TerminalCell : IEquatable<TerminalCell>
{
    /// <summary>
    /// The text displayed in this cell. For wide graphemes, this is set only on the lead cell.
    /// </summary>
    public string Text { get; init; }

    /// <summary>
    /// The first character of <see cref="Text"/>, used by legacy tests and callers that only
    /// inspect single-cell ASCII output.
    /// </summary>
    public char Character => string.IsNullOrEmpty(Text) ? ' ' : Text[0];

    /// <summary>
    /// Whether this cell is occupied by the trailing column of a wide grapheme.
    /// </summary>
    public bool IsContinuation { get; init; }

    /// <summary>
    /// The foreground (text) color.
    /// </summary>
    public Color Foreground { get; init; }

    /// <summary>
    /// The background color.
    /// </summary>
    public Color Background { get; init; }

    /// <summary>
    /// Text decorations (bold, italic, underline, etc.).
    /// </summary>
    public TextDecoration Decoration { get; init; }

    /// <summary>
    /// Optional hyperlink URL for this cell. When set, the cell is part of an OSC 8 hyperlink. It
    /// participates in equality so a cell gaining, losing, or changing its link is treated as dirty
    /// by the diff and re-emitted with the correct hyperlink wrapper.
    /// </summary>
    public string? Link { get; init; }

    /// <summary>
    /// Creates a new terminal cell with the specified character and styling.
    /// </summary>
    public TerminalCell(char character, Color foreground, Color background, TextDecoration decoration)
        : this(character.ToString(), foreground, background, decoration)
    {
    }

    /// <summary>
    /// Creates a new terminal cell with the specified grapheme and styling.
    /// </summary>
    public TerminalCell(string text, Color foreground, Color background, TextDecoration decoration, string? link = null)
    {
        Text = string.IsNullOrEmpty(text) ? " " : text;
        Foreground = foreground;
        Background = background;
        Decoration = decoration;
        IsContinuation = false;
        Link = link;
    }

    /// <summary>
    /// An empty cell - a space with default colors and no decoration.
    /// </summary>
    public static TerminalCell Empty => new()
    {
        Text = " ",
        Foreground = Color.Default,
        Background = Color.Default,
        Decoration = TextDecoration.None,
        IsContinuation = false
    };

    /// <summary>
    /// A trailing cell occupied by a wide grapheme's lead cell.
    /// </summary>
    public static TerminalCell Continuation(Color foreground, Color background, TextDecoration decoration, string? link = null) => new()
    {
        Text = string.Empty,
        Foreground = foreground,
        Background = background,
        Decoration = decoration,
        IsContinuation = true,
        Link = link
    };

    /// <summary>
    /// Creates a cell with the specified character and default styling.
    /// </summary>
    public static TerminalCell FromChar(char c) => new()
    {
        Text = c.ToString(),
        Foreground = Color.Default,
        Background = Color.Default,
        Decoration = TextDecoration.None,
        IsContinuation = false
    };

    /// <summary>
    /// Returns a new cell with the same styling but a different character.
    /// </summary>
    public TerminalCell WithCharacter(char c) => this with { Text = c.ToString(), IsContinuation = false };

    /// <summary>
    /// Returns a new cell with the same character but different foreground color.
    /// </summary>
    public TerminalCell WithForeground(Color color) => this with { Foreground = color };

    /// <summary>
    /// Returns a new cell with the same character but different background color.
    /// </summary>
    public TerminalCell WithBackground(Color color) => this with { Background = color };

    /// <summary>
    /// Returns a new cell with the same character but different decoration.
    /// </summary>
    public TerminalCell WithDecoration(TextDecoration decoration) => this with { Decoration = decoration };

    /// <summary>
    /// Returns a new cell with the same content but a different hyperlink.
    /// </summary>
    public TerminalCell WithLink(string? link) => this with { Link = link };

    /// <summary>
    /// Checks if this cell has the same styling (colors and decoration) as another cell.
    /// Useful for optimizing ANSI output - only emit style changes when needed.
    /// </summary>
    public bool HasSameStyle(TerminalCell other) =>
        Foreground == other.Foreground &&
        Background == other.Background &&
        Decoration == other.Decoration;

    public bool Equals(TerminalCell other) =>
        Text == other.Text &&
        IsContinuation == other.IsContinuation &&
        Foreground == other.Foreground &&
        Background == other.Background &&
        Decoration == other.Decoration &&
        Link == other.Link;

    public override int GetHashCode() => HashCode.Combine(Text, IsContinuation, Foreground, Background, Decoration, Link);

    public override string ToString() => IsContinuation
        ? $"<continuation> (FG:{Foreground}, BG:{Background}, Deco:{Decoration})"
        : $"'{Text}' (FG:{Foreground}, BG:{Background}, Deco:{Decoration})";
}
