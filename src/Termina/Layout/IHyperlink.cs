// Copyright (c) Petabridge, LLC. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

namespace Termina.Layout;

/// <summary>
/// A node that carries a URL and registers itself as a link region in the hit-test index. The
/// application activates it on a modifier+click and raises a <see cref="Input.LinkActivatedEvent"/>.
/// </summary>
public interface IHyperlink : ILayoutNode
{
    /// <summary>
    /// The URL this link points to.
    /// </summary>
    string Url { get; }
}
