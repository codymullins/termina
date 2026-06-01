using Termina.Terminal;

namespace Termina.Components.Streaming;

/// <summary>
/// Utility class for word-wrapping text to fit within a specified width.
/// </summary>
public static class WordWrapper
{
    /// <summary>
    /// Wraps a single line of text to fit within the specified width.
    /// </summary>
    /// <param name="text">Text to wrap.</param>
    /// <param name="width">Maximum width per line.</param>
    /// <returns>List of wrapped lines.</returns>
    public static List<string> WrapLine(string text, int width)
    {
        if (width <= 0)
            throw new ArgumentOutOfRangeException(nameof(width), "Width must be positive.");

        if (string.IsNullOrEmpty(text))
            return [""];

        var result = new List<string>();

        // Handle text that's shorter than width
        if (TerminalText.GetDisplayWidth(text) <= width)
        {
            result.Add(text);
            return result;
        }

        var currentLine = new System.Text.StringBuilder();
        var currentWidth = 0;
        var words = SplitIntoWords(text);

        foreach (var word in words)
        {
            var wordWidth = TerminalText.GetDisplayWidth(word);

            // If word itself is longer than width, break it
            if (wordWidth > width)
            {
                // Flush current line if it has content
                if (currentWidth > 0)
                {
                    result.Add(currentLine.ToString());
                    currentLine.Clear();
                    currentWidth = 0;
                }

                // Break long word into chunks
                var remaining = word;
                while (TerminalText.GetDisplayWidth(remaining) > width)
                {
                    var chunk = TerminalText.TruncateToWidth(remaining, width);
                    result.Add(chunk);
                    remaining = TerminalText.SliceByWidth(
                        remaining,
                        TerminalText.GetDisplayWidth(chunk),
                        TerminalText.GetDisplayWidth(remaining) - TerminalText.GetDisplayWidth(chunk));
                }

                if (remaining.Length > 0)
                {
                    currentLine.Append(remaining);
                    currentWidth = TerminalText.GetDisplayWidth(remaining);
                }
            }
            else if (currentWidth == 0)
            {
                // Start of line
                currentLine.Append(word);
                currentWidth = wordWidth;
            }
            else if (currentWidth + 1 + wordWidth <= width)
            {
                // Word fits with space
                currentLine.Append(' ');
                currentLine.Append(word);
                currentWidth += 1 + wordWidth;
            }
            else
            {
                // Word doesn't fit, start new line
                result.Add(currentLine.ToString());
                currentLine.Clear();
                currentLine.Append(word);
                currentWidth = wordWidth;
            }
        }

        // Flush remaining content
        if (currentLine.Length > 0)
        {
            result.Add(currentLine.ToString());
        }

        return result;
    }

    /// <summary>
    /// Wraps multiple lines of text.
    /// </summary>
    /// <param name="lines">Lines to wrap.</param>
    /// <param name="width">Maximum width per line.</param>
    /// <returns>All wrapped lines.</returns>
    public static List<string> WrapLines(IEnumerable<string> lines, int width)
    {
        var result = new List<string>();
        foreach (var line in lines)
        {
            result.AddRange(WrapLine(line, width));
        }
        return result;
    }

    /// <summary>
    /// Calculates how many wrapped lines a piece of text will produce.
    /// </summary>
    /// <param name="text">Text to measure.</param>
    /// <param name="width">Width for wrapping.</param>
    /// <returns>Number of wrapped lines.</returns>
    public static int CalculateWrappedLineCount(string text, int width)
    {
        return WrapLine(text, width).Count;
    }

    /// <summary>
    /// Calculates total wrapped line count for multiple lines.
    /// </summary>
    public static int CalculateTotalWrappedLineCount(IEnumerable<string> lines, int width)
    {
        return lines.Sum(line => CalculateWrappedLineCount(line, width));
    }

    private static List<string> SplitIntoWords(string text)
    {
        var words = new List<string>();
        var currentWord = new System.Text.StringBuilder();

        foreach (var grapheme in TerminalText.EnumerateGraphemes(text))
        {
            if (grapheme.Text.Length > 0 && char.IsWhiteSpace(grapheme.Text, 0))
            {
                if (currentWord.Length > 0)
                {
                    words.Add(currentWord.ToString());
                    currentWord.Clear();
                }
                // Preserve multiple spaces as empty words? Or collapse?
                // For now, collapse whitespace
            }
            else
            {
                currentWord.Append(grapheme.Text);
            }
        }

        if (currentWord.Length > 0)
        {
            words.Add(currentWord.ToString());
        }

        return words;
    }
}
