using System;
using System.Diagnostics.CodeAnalysis;
using System.Threading.Tasks;

using Dalamud.Utility;

using Newtonsoft.Json;

namespace Dalamud.Support;

/// <summary>
/// Service for fetching Dalamud release information.
/// </summary>
[ServiceManager.EarlyLoadedService]
internal class DalamudReleases : IServiceType
{
    /// <summary>
    /// Initializes a new instance of the <see cref="DalamudReleases"/> class.
    /// </summary>
    [ServiceManager.ServiceConstructor]
    public DalamudReleases()
    {
    }

    /// <summary>
    /// Get the latest version info for the current track.
    /// </summary>
    /// <returns>The version info for the current track.</returns>
    public Task<DalamudVersionInfo?> GetVersionForCurrentTrack() =>
        Task.FromResult<DalamudVersionInfo?>(new DalamudVersionInfo
        {
            Track = Versioning.GetActiveTrack() ?? "standalone-cn",
            AssemblyVersion = Versioning.GetScmVersion(),
            RuntimeVersion = Environment.Version.ToString(),
            RuntimeRequired = true,
            SupportedGameVer = string.Empty,
            IsApplicableForCurrentGameVer = true,
        });

    [SuppressMessage("StyleCop.CSharp.DocumentationRules", "SA1600:Elements should be documented", Justification = "laziness")]
    public class DalamudVersionInfo
    {
        [JsonProperty("key")]
        public string? Key { get; set; }

        [JsonProperty("track")]
        public string Track { get; set; } = null!;

        [JsonProperty("assemblyVersion")]
        public string AssemblyVersion { get; set; } = null!;

        [JsonProperty("runtimeVersion")]
        public string RuntimeVersion { get; set; } = null!;

        [JsonProperty("runtimeRequired")]
        public bool RuntimeRequired { get; set; }

        [JsonProperty("supportedGameVer")]
        public string SupportedGameVer { get; set; } = null!;

        [JsonProperty("isApplicableForCurrentGameVer")]
        public bool IsApplicableForCurrentGameVer { get; set; }
    }
}
