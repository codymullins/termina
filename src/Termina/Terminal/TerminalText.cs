// Copyright (c) Petabridge, LLC. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Globalization;
using System.Text;

namespace Termina.Terminal;

/// <summary>
/// Utilities for terminal cell-width-aware text handling.
/// </summary>
public static class TerminalText
{
    public readonly record struct Grapheme(string Text, int Width);

    public static IEnumerable<Grapheme> EnumerateGraphemes(string? text)
    {
        if (string.IsNullOrEmpty(text))
            yield break;

        var enumerator = StringInfo.GetTextElementEnumerator(text);
        while (enumerator.MoveNext())
        {
            var element = enumerator.GetTextElement();
            yield return new Grapheme(element, GetGraphemeWidth(element));
        }
    }

    public static int GetDisplayWidth(string? text)
    {
        if (string.IsNullOrEmpty(text))
            return 0;

        var width = 0;
        foreach (var grapheme in EnumerateGraphemes(text))
            width += grapheme.Width;
        return width;
    }

    public static string TruncateToWidth(string? text, int maxWidth)
    {
        if (string.IsNullOrEmpty(text) || maxWidth <= 0)
            return string.Empty;

        var sb = new StringBuilder(text.Length);
        var width = 0;
        foreach (var grapheme in EnumerateGraphemes(text))
        {
            if (grapheme.Width > 0 && width + grapheme.Width > maxWidth)
            {
                if (width == 0)
                    sb.Append(grapheme.Text);
                break;
            }
            sb.Append(grapheme.Text);
            width += grapheme.Width;
        }
        return sb.ToString();
    }

    public static string SliceByWidth(string? text, int startWidth, int width)
    {
        if (string.IsNullOrEmpty(text) || width <= 0)
            return string.Empty;
        if (startWidth < 0)
            throw new ArgumentOutOfRangeException(nameof(startWidth), "Start width cannot be negative.");

        var endWidth = startWidth + width;
        var sb = new StringBuilder(text.Length);
        var currentWidth = 0;

        foreach (var grapheme in EnumerateGraphemes(text))
        {
            var nextWidth = currentWidth + grapheme.Width;
            if (grapheme.Width == 0)
            {
                if (currentWidth >= startWidth && currentWidth <= endWidth)
                    sb.Append(grapheme.Text);
            }
            else if (currentWidth >= startWidth && nextWidth <= endWidth)
            {
                sb.Append(grapheme.Text);
            }

            currentWidth = nextWidth;
            if (currentWidth >= endWidth)
                break;
        }

        return sb.ToString();
    }

    public static string PadRight(string text, int totalWidth)
    {
        var padding = totalWidth - GetDisplayWidth(text);
        return padding <= 0 ? text : text + new string(' ', padding);
    }

    private static int GetGraphemeWidth(string text)
    {
        var hasVisibleRune = false;
        var hasWideRune = false;

        foreach (var rune in text.EnumerateRunes())
        {
            if (IsZeroWidth(rune))
                continue;

            hasVisibleRune = true;
            if (IsWide(rune))
                hasWideRune = true;
        }

        if (!hasVisibleRune)
            return 0;
        return hasWideRune ? 2 : 1;
    }

    private static bool IsZeroWidth(Rune rune)
    {
        var category = Rune.GetUnicodeCategory(rune);
        return category is UnicodeCategory.Control
            or UnicodeCategory.Format
            or UnicodeCategory.NonSpacingMark
            or UnicodeCategory.EnclosingMark
            or UnicodeCategory.Surrogate
            || IsVariationSelector(rune.Value);
    }

    private static bool IsVariationSelector(int value) =>
        value is >= 0xFE00 and <= 0xFE0F
            or >= 0xE0100 and <= 0xE01EF;

    private static bool IsWide(Rune rune)
    {
        var value = rune.Value;
        return value is >= 0x1100 and <= 0x115F
            or 0x2329 or 0x232A
            or >= 0x2E80 and <= 0xA4CF
            or >= 0xAC00 and <= 0xD7A3
            or >= 0xF900 and <= 0xFAFF
            or >= 0xFE10 and <= 0xFE19
            or >= 0xFE30 and <= 0xFE6F
            or >= 0xFF00 and <= 0xFF60
            or >= 0xFFE0 and <= 0xFFE6
            or >= 0x1F000 and <= 0x1FAFF
            or >= 0x20000 and <= 0x3FFFD;
    }
}
