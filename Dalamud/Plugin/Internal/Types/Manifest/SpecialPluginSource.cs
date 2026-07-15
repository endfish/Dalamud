namespace Dalamud.Plugin.Internal.Types.Manifest;

/// <summary>
/// A fake enum representing "special" sources for plugins.
/// </summary>
public static class SpecialPluginSource
{
    /// <summary>
    /// Legacy marker retained for plugins imported from an official Dalamud installation.
    /// Standalone does not create new installations with this source.
    /// </summary>
    public const string MainRepo = "OFFICIAL";

    /// <summary>
    /// Indication that this plugin is loaded as a dev plugin. See also <see cref="DalamudPluginInterface.IsDev"/>.
    /// </summary>
    public const string DevPlugin = "DEVPLUGIN";
}
