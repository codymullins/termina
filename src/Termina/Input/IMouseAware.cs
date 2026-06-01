// Copyright (c) Petabridge, LLC. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using Termina.Layout;

namespace Termina.Input;

/// <summary>
/// Implemented by layout nodes that want to receive mouse events. A node registers its bounds with
/// the hit-test index during render (via <c>IRenderContext.RegisterHit</c>); dispatch then routes
/// matching mouse events to <see cref="HandleMouse"/>.
/// </summary>
public interface IMouseAware : ILayoutNode
{
    /// <summary>
    /// Handle a mouse event that landed within this node.
    /// </summary>
    /// <param name="e">The mouse event, with coordinates in absolute screen cells.</param>
    /// <param name="bounds">This node's absolute screen bounds (use to compute local coordinates).</param>
    /// <returns><c>true</c> if the event was consumed and should stop bubbling to ancestors.</returns>
    bool HandleMouse(MouseEvent e, Rect bounds);
}

/// <summary>
/// Implemented by <see cref="IMouseAware"/> nodes that want hover notifications. Presence of any
/// hover-aware node in the active page causes the application to enable any-event mouse tracking
/// (CSI ?1003h) so move events are reported.
/// </summary>
public interface IHoverAware : IMouseAware
{
    /// <summary>
    /// Called when the mouse cursor enters this node's bounds.
    /// </summary>
    void OnMouseEnter();

    /// <summary>
    /// Called when the mouse cursor leaves this node's bounds.
    /// </summary>
    void OnMouseLeave();
}
