using R3;
using Termina.Input;
using Termina.Reactive;

namespace Termina.Demo.Mouse.Pages;

/// <summary>
/// Tracks the latest mouse activity, link activation, and terminal focus state so the page can
/// show what the new SGR mouse pipeline is delivering.
/// </summary>
public class MouseViewModel : ReactiveViewModel
{
    public ReactiveProperty<string> LastMouse { get; } = new("(move or click in empty space)");
    public ReactiveProperty<string> LastLink { get; } = new("(none)");
    public ReactiveProperty<bool> TerminalFocused { get; } = new(true);

    public override void OnActivated()
    {
        Input.OfType<IInputEvent, MouseEvent>()
            .Subscribe(m => LastMouse.Value =
                $"{m.Kind} {m.Button} @ ({m.Column},{m.Row}) chain={m.ClickChain} mods={m.Modifiers}")
            .DisposeWith(Subscriptions);

        Input.OfType<IInputEvent, LinkActivatedEvent>()
            .Subscribe(l => LastLink.Value = l.Url)
            .DisposeWith(Subscriptions);

        Input.OfType<IInputEvent, TerminalFocusEvent>()
            .Subscribe(f => TerminalFocused.Value = f.HasFocus)
            .DisposeWith(Subscriptions);
    }

    public override void Dispose()
    {
        LastMouse.Dispose();
        LastLink.Dispose();
        TerminalFocused.Dispose();
        base.Dispose();
    }
}
