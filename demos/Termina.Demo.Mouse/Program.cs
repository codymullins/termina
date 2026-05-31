using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Termina.Demo.Mouse.Pages;
using Termina.Hosting;

// Termina.Demo.Mouse
// -----------------------------------------------------------------------------
// Exercises full SGR mouse support: click-to-focus, click-to-position-caret,
// drag-to-select, double/triple-click word/line selection, hover, clickable
// links, and focus in/out events.
//
// By default TerminaApplication enables the mouse stack
// (?1000h ?1002h ?1006h ?1004h). Note: while mouse tracking is on the host
// terminal's NATIVE text selection is suppressed — hold Shift and drag to use
// the terminal's own selection instead (WezTerm / Kitty / Ghostty / Alacritty /
// iTerm2 / Windows Terminal all use Shift as the bypass modifier).
//
// What to try once it's running:
//   • Click either input — it focuses AND the caret lands where you clicked.
//   • Double-click a word to select it; triple-click to select the whole line.
//   • Click-drag across the read-only block to select; Ctrl+C copies it.
//   • Click the link line — the status bar shows the activated URL.
//   • Move the mouse over the link — it underlines (hover).
//   • Click away/onto the terminal — the focus in/out indicator updates.
//   • Ctrl+C twice to quit.
//
// Run:
//   dotnet run --project demos/Termina.Demo.Mouse
// -----------------------------------------------------------------------------

var builder = Host.CreateApplicationBuilder(args);
builder.Logging.SetMinimumLevel(LogLevel.Warning);

builder.Services.AddTermina("/mouse", termina =>
{
    termina.RegisterRoute<MousePage, MouseViewModel>("/mouse");
});

await builder.Build().RunAsync();
