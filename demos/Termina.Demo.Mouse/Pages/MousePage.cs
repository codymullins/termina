using R3;
using Termina.Clipboard;
using Termina.Extensions;
using Termina.Input;
using Termina.Layout;
using Termina.Reactive;
using Termina.Rendering;
using Termina.Terminal;

namespace Termina.Demo.Mouse.Pages;

/// <summary>
/// Demonstrates SGR mouse support: click-to-focus + caret placement on inputs, drag/word/line
/// selection on a read-only block, a clickable link with hover underline, and a live status bar.
/// </summary>
public class MousePage : ReactivePage<MouseViewModel>
{
    private TextInputNode _name = null!;
    private TextInputNode _email = null!;
    private CopyableTextNode _copyable = null!;
    private HyperlinkNode _link = null!;

    public MousePage()
    {
        FocusPolicy = FocusPolicy.FirstFocusable;
    }

    protected override void OnBound()
    {
        base.OnBound();

        _name = new TextInputNode()
            .WithPlaceholder("Click here, then click between characters to place the caret…")
            .WithForeground(Color.Cyan);

        _email = new TextInputNode()
            .WithPlaceholder("Another input — click to focus it directly…")
            .WithForeground(Color.Green);

        _copyable = new CopyableTextNode(new NullClipboard(),
            "Drag across this text to select it. Double-click selects a word, " +
            "triple-click selects the line. Ctrl+C copies the selection.")
            .WithHint("Drag to select · double/triple-click · Ctrl+C to copy");

        _link = new HyperlinkNode("https://github.com/petabridge/termina", "https://github.com/petabridge/termina")
            .WithForeground(Color.BrightBlue);
    }

    public override void OnNavigatedTo()
    {
        base.OnNavigatedTo();
        _link.Activated
            .Subscribe(_ => { /* surfaced via ViewModel.LastLink too */ })
            .DisposeWith(Subscriptions);
    }

    public override ILayoutNode BuildLayout()
    {
        return Layouts.Vertical().WithSpacing(1)
            .WithChild(new TextNode("SGR Mouse Demo — click, drag, double/triple-click, hover, links")
                .WithForeground(Color.Yellow).Bold().Height(1))
            .WithChild(new TextNode("Shift+drag uses the terminal's native selection instead. Ctrl+C twice to quit.")
                .WithForeground(Color.BrightBlack).Height(1))
            .WithChild(new PanelNode()
                .WithTitle("Name (click to focus + place caret)")
                .WithBorder(BorderStyle.Rounded).WithBorderColor(Color.Cyan)
                .WithContent(_name).Height(3))
            .WithChild(new PanelNode()
                .WithTitle("Email (click to focus)")
                .WithBorder(BorderStyle.Rounded).WithBorderColor(Color.Green)
                .WithContent(_email).Height(3))
            .WithChild(new PanelNode()
                .WithTitle("Selectable text")
                .WithBorder(BorderStyle.Rounded).WithBorderColor(Color.Magenta)
                .WithContent(_copyable).Fill())
            .WithChild(new PanelNode()
                .WithTitle("Link (click or focus+Enter to activate)")
                .WithBorder(BorderStyle.Rounded).WithBorderColor(Color.Blue)
                .WithContent(_link).Height(3))
            .WithChild(Observable.CombineLatest(
                    ViewModel.LastMouse, ViewModel.LastLink, ViewModel.TerminalFocused,
                    (mouse, link, focused) =>
                        $"  Mouse: {mouse}\n  Link: {link}   Terminal: {(focused ? "focused" : "unfocused")}")
                .Select<string, ILayoutNode>(s => new TextNode(s).WithForeground(Color.White).NoWrap())
                .AsLayout()
                .Height(2));
    }

    private sealed class NullClipboard : IClipboardService
    {
        public bool Copy(string text) => false;
    }
}
