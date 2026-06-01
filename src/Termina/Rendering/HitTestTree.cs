// Copyright (c) Petabridge, LLC. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using Termina.Layout;

namespace Termina.Rendering;

/// <summary>
/// Classifies a hit-test entry so dispatch can filter the walk (e.g. find the topmost text region
/// for selection, or treat a modal backdrop as an event sink).
/// </summary>
public enum HitTestKind
{
    /// <summary>A generic interactive component.</summary>
    Component,

    /// <summary>A region that holds selectable / editable text.</summary>
    Text,

    /// <summary>A clickable link span.</summary>
    Link,

    /// <summary>A scrollable region.</summary>
    Scrollable,

    /// <summary>
    /// A full-area sink (e.g. a modal's backdrop). Dispatch stops bubbling when it reaches a
    /// backdrop so clicks never fall through to nodes drawn underneath it.
    /// </summary>
    Backdrop,
}

/// <summary>
/// A single registered hit-test region in absolute screen coordinates.
/// </summary>
/// <param name="Bounds">Absolute screen bounds of the node.</param>
/// <param name="Node">The node that registered this region.</param>
/// <param name="Kind">What kind of region this is.</param>
/// <param name="Sequence">Draw order — higher means drawn later (on top).</param>
public readonly record struct HitTestEntry(Rect Bounds, object Node, HitTestKind Kind, int Sequence);

/// <summary>
/// A flat, frame-scoped spatial index of rendered nodes. Nodes register their absolute bounds in
/// draw order during a render pass; lookups walk in reverse draw order so the top-most node wins.
/// </summary>
/// <remarks>
/// Lookup is O(n) in the number of registered (interactive) nodes — typically a few hundred at
/// most for a TUI — which is well within budget. If it ever becomes hot, an R-tree can replace the
/// backing list without changing this API.
/// </remarks>
public sealed class HitTestTree
{
    private readonly List<HitTestEntry> _entries = new();

    /// <summary>
    /// Whether any registered node implements <see cref="Termina.Input.IHoverAware"/>. The
    /// application uses this to decide whether to enable any-event (hover) mouse tracking.
    /// </summary>
    public bool HasHoverTarget { get; private set; }

    /// <summary>
    /// Clears all entries. Called at the start of each render pass.
    /// </summary>
    public void Clear()
    {
        _entries.Clear();
        HasHoverTarget = false;
    }

    /// <summary>
    /// The number of registered entries (for diagnostics / tests).
    /// </summary>
    public int Count => _entries.Count;

    /// <summary>
    /// Registers a node's absolute bounds in draw order.
    /// </summary>
    public void Register(object node, Rect absoluteBounds, HitTestKind kind)
    {
        if (!absoluteBounds.HasArea)
            return;
        _entries.Add(new HitTestEntry(absoluteBounds, node, kind, _entries.Count));
        if (node is Termina.Input.IHoverAware)
            HasHoverTarget = true;
    }

    /// <summary>
    /// Returns the top-most entry whose bounds contain the point, or <c>null</c> if none do.
    /// </summary>
    public HitTestEntry? HitTest(int column, int row)
    {
        for (var i = _entries.Count - 1; i >= 0; i--)
        {
            if (_entries[i].Bounds.Contains(column, row))
                return _entries[i];
        }
        return null;
    }

    /// <summary>
    /// Returns the top-most entry of the given kind whose bounds contain the point.
    /// </summary>
    public HitTestEntry? HitTest(int column, int row, HitTestKind kind)
    {
        for (var i = _entries.Count - 1; i >= 0; i--)
        {
            if (_entries[i].Kind == kind && _entries[i].Bounds.Contains(column, row))
                return _entries[i];
        }
        return null;
    }

    /// <summary>
    /// Returns every entry containing the point, top-most first. In a clean nested layout this is
    /// the deepest-to-shallowest chain at the point, which dispatch walks for event bubbling.
    /// Walking stops at (and includes) the first <see cref="HitTestKind.Backdrop"/> so events do
    /// not fall through a modal backdrop to nodes drawn beneath it.
    /// </summary>
    public IReadOnlyList<HitTestEntry> HitTestPath(int column, int row)
    {
        var path = new List<HitTestEntry>();
        for (var i = _entries.Count - 1; i >= 0; i--)
        {
            var entry = _entries[i];
            if (!entry.Bounds.Contains(column, row))
                continue;
            path.Add(entry);
            if (entry.Kind == HitTestKind.Backdrop)
                break;
        }
        return path;
    }
}
