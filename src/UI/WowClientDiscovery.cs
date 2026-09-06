using System.Collections.Generic;
using WoWBuddy.Core.Client;
using WoWBuddy.Presentation;

namespace WoWBuddy.UI;

/// <summary>
/// Finds running clients, for the window to list.
/// </summary>
/// <remarks>
/// The whole of this class is a translation between what the core layer knows and what the
/// window shows. Keeping it here rather than in the view model is what lets the view model's
/// logic be tested without a game running.
/// </remarks>
public sealed class WowClientDiscovery : IClientDiscovery
{
    /// <summary>The processes found, and the one the window should show for each.</summary>
    public IReadOnlyList<ClientOption> Find()
    {
        List<ClientOption> options = [];

        foreach (WowClientCandidate candidate in WowClientLocator.FindAll())
        {
            options.Add(new ClientOption(
                candidate.Process.Id,
                candidate.Describe(),
                candidate.Build.IsSupported));
        }

        return options;
    }
}
