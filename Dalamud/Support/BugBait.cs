using System;
using System.Threading.Tasks;

using Dalamud.Plugin.Internal.Types.Manifest;

namespace Dalamud.Support;

/// <summary>
/// Class responsible for sending feedback.
/// </summary>
internal static class BugBait
{
    /// <summary>
    /// Send feedback to Discord.
    /// </summary>
    /// <param name="plugin">The plugin to send feedback about.</param>
    /// <param name="isTesting">Whether the plugin is a testing plugin.</param>
    /// <param name="content">The content of the feedback.</param>
    /// <param name="reporter">The reporter name.</param>
    /// <param name="includeException">Whether the most recent exception to occur should be included in the report.</param>
    /// <returns>A <see cref="Task"/> representing the asynchronous operation.</returns>
    public static Task SendFeedback(RemotePluginManifest plugin, bool isTesting, string content, string reporter, bool includeException) =>
        Task.FromException(new NotSupportedException("Standalone builds do not provide an official feedback service."));
}
