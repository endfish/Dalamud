using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading;
using System.Threading.Tasks;

using Dalamud.Logging.Internal;
using Dalamud.Networking.Http;
using Dalamud.Plugin.Internal.Types.Manifest;
using Dalamud.Utility;

using Microsoft.Extensions.Caching.Abstractions;
using Microsoft.Extensions.Caching.InMemory;

using Newtonsoft.Json;

namespace Dalamud.Plugin.Internal.Types;

/// <summary>
/// This class represents a single plugin repository.
/// </summary>
internal class PluginRepository
{
    private const int HttpRequestTimeoutSeconds = 20;

    private static readonly ModuleLog Log = ModuleLog.Create<PluginRepository>();
    private static readonly InMemoryCacheHandler CacheHandler = new(
        new SocketsHttpHandler
        {
            AutomaticDecompression = DecompressionMethods.All,
            ConnectCallback = Service<HappyHttpClient>.Get().SharedHappyEyeballsCallback.ConnectCallback,
        },
        CacheExpirationProvider.CreateSimple(TimeSpan.FromHours(3), TimeSpan.FromSeconds(0), TimeSpan.FromSeconds(0)));

    private readonly HttpClient httpClient;

    /// <summary>
    /// Initializes a new instance of the <see cref="PluginRepository"/> class.
    /// </summary>
    /// <param name="happyHttpClient">An instance of <see cref="HappyHttpClient"/>.</param>
    /// <param name="pluginMasterUrl">The plugin master URL.</param>
    /// <param name="isEnabled">Whether the plugin repo is enabled.</param>
    public PluginRepository(HappyHttpClient happyHttpClient, string pluginMasterUrl, bool isEnabled)
    {
        this.httpClient = new(CacheHandler)
        {
            Timeout = TimeSpan.FromSeconds(5),
            DefaultRequestHeaders =
            {
                Accept =
                {
                    new MediaTypeWithQualityHeaderValue("application/json"),
                },
                CacheControl = new CacheControlHeaderValue
                {
                    NoCache = true,
                },
                UserAgent =
                {
                    new ProductInfoHeaderValue("Dalamud", Versioning.GetAssemblyVersion()),
                },
            },
        };
        this.PluginMasterUrl = pluginMasterUrl;
        this.IsThirdParty = true;
        this.IsEnabled = isEnabled;
    }

    /// <summary>
    /// Gets the pluginmaster.json URL.
    /// </summary>
    public string PluginMasterUrl { get; }

    /// <summary>
    /// Gets a value indicating whether this plugin repository is from a third party.
    /// </summary>
    public bool IsThirdParty { get; }

    /// <summary>
    /// Gets a value indicating whether this repo is enabled.
    /// </summary>
    public bool IsEnabled { get; }

    /// <summary>
    /// Gets the plugin master list of available plugins.
    /// </summary>
    public ReadOnlyCollection<RemotePluginManifest>? PluginMaster { get; private set; }

    /// <summary>
    /// Gets the initialization state of the plugin repository.
    /// </summary>
    public PluginRepositoryState State { get; private set; }

    /// <summary>
    /// Reload the plugin master asynchronously in a task.
    /// </summary>
    /// <param name="skipCache">Skip MemoryCache.</param>
    /// <returns>The new state.</returns>
    public async Task ReloadAsync(bool skipCache)
    {
        this.State = PluginRepositoryState.InProgress;
        this.PluginMaster = new List<RemotePluginManifest>().AsReadOnly();

        try
        {
            Log.Information($"Fetching repo: {this.PluginMasterUrl}");

            if (skipCache)
            {
                CacheHandler.InvalidateCache(new Uri(this.PluginMasterUrl));
                Log.Information($"Cache Clear: {this.PluginMasterUrl}");
            }

            // using var response = await HttpClient.GetAsync(this.PluginMasterUrl);
            using var response = await this.GetPluginMaster(this.PluginMasterUrl);

            response.EnsureSuccessStatusCode();

            var data = await response.Content.ReadAsStringAsync();
            var pluginMaster = JsonConvert.DeserializeObject<List<RemotePluginManifest>>(data) ?? throw new Exception("Deserialized PluginMaster was null.");
            pluginMaster.Sort((pm1, pm2) => string.Compare(pm1.Name, pm2.Name, StringComparison.Ordinal));

            // Set the source for each remote manifest. Allows for checking if is 3rd party.
            foreach (var manifest in pluginMaster)
            {
                manifest.SourceRepo = this;
            }

            this.PluginMaster = pluginMaster.Where(this.IsValidManifest).ToList().AsReadOnly();

            Log.Information($"Successfully fetched repo: {this.PluginMasterUrl}");
            this.State = PluginRepositoryState.Success;

            var stats = CacheHandler.StatsProvider.GetStatistics().Total;
            Log.Information($"Cache: TotalRequests:{stats.TotalRequests}/CacheHit:{stats.CacheHit}/CacheMiss:{stats.CacheMiss}");
        }
        catch (Exception ex)
        {
            Log.Error(ex, $"PluginMaster failed: {this.PluginMasterUrl}");
            this.State = PluginRepositoryState.Fail;
        }
    }

    private bool IsValidManifest(RemotePluginManifest manifest)
    {
        if (manifest.InternalName.IsNullOrWhitespace())
        {
            Log.Error("Repository at {RepoLink} has a plugin with an invalid InternalName.", this.PluginMasterUrl);
            return false;
        }

        if (manifest.Name.IsNullOrWhitespace())
        {
            Log.Error("Plugin {PluginName} in {RepoLink} has an invalid Name.", manifest.InternalName, this.PluginMasterUrl);
            return false;
        }

        // ReSharper disable once ConditionIsAlwaysTrueOrFalse
        if (manifest.AssemblyVersion == null)
        {
            Log.Error("Plugin {PluginName} in {RepoLink} has an invalid AssemblyVersion.", manifest.InternalName, this.PluginMasterUrl);
            return false;
        }

        if (manifest.TestingAssemblyVersion != null &&
            manifest.TestingAssemblyVersion > manifest.AssemblyVersion &&
            manifest.TestingDalamudApiLevel == null)
        {
            Log.Warning("The plugin {PluginName} in {RepoLink} has a testing version available, but it lacks an associated testing API. The 'TestingDalamudApiLevel' property is required.", manifest.InternalName, this.PluginMasterUrl);
        }

        return true;
    }

    private async Task<HttpResponseMessage> GetPluginMaster(string url, int timeout = HttpRequestTimeoutSeconds)
    {
        //var httpClient = Service<HappyHttpClient>.Get().SharedHttpClient;

        var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        request.Headers.CacheControl = new CacheControlHeaderValue { NoCache = true };

        using var requestCts = new CancellationTokenSource(TimeSpan.FromSeconds(timeout));

        return await this.httpClient.SendAsync(request, requestCts.Token);
    }
}
