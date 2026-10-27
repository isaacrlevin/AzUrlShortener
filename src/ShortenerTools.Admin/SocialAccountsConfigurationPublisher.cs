#nullable enable

using Azure.Data.AppConfiguration;
using SocialPoster.Core.Services;
using SocialPoster.Posting.Services;

namespace ShortenerTools.Admin;

/// <summary>
/// Mirrors provider credentials managed in SocialPoster to Azure App Configuration,
/// the shared source read by the Functions apps.
/// </summary>
public sealed class SocialAccountsConfigurationPublisher : BackgroundService
{
    private const string SentinelKey = "SocialPoster:Accounts:Sentinel";

    // Store keys must be SocialPoster's persisted names (e.g. "bluesky.handle"), not the constant names.
    private static readonly (string StoreKey, string ConfigKey)[] AccountSettings =
    [
        (SettingKeys.BlueskyHandle, "Shortener:BlueskyUserName"),
        (SettingKeys.BlueskyAppPassword, "Shortener:BlueskyPassword"),
        (SettingKeys.XConsumerKey, "Shortener:TwitterConsumerKey"),
        (SettingKeys.XConsumerSecret, "Shortener:TwitterConsumerSecret"),
        (SettingKeys.XAccessToken, "Shortener:TwitterAccessToken"),
        (SettingKeys.XAccessTokenSecret, "Shortener:TwitterAccessSecret"),
        (SettingKeys.LinkedInAccessToken, "Shortener:LinkedInAccessToken"),
        (SettingKeys.MastodonInstance, "Shortener:MastodonInstance"),
        (SettingKeys.MastodonAccessToken, "Shortener:MastodonAccessToken")
    ];

    private readonly SecretStore _secretStore;
    private readonly SocialPosterStorage _storage;
    private readonly IConfiguration _configuration;
    private readonly ILogger<SocialAccountsConfigurationPublisher> _logger;
    private readonly Dictionary<string, string> _lastPublished = new(StringComparer.Ordinal);

    public SocialAccountsConfigurationPublisher(
        SecretStore secretStore,
        SocialPosterStorage storage,
        IConfiguration configuration,
        ILogger<SocialAccountsConfigurationPublisher> logger)
    {
        _secretStore = secretStore;
        _storage = storage;
        _configuration = configuration;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var connectionString = _configuration.GetConnectionString("social-accounts");
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            _logger.LogInformation(
                "Azure App Configuration sync is disabled; no social-accounts connection string is configured.");
            return;
        }

        // Never read the store before it opens; an unavailable store must not publish cleared accounts.
        await _storage.WaitAsync(stoppingToken);
        var client = new ConfigurationClient(connectionString);
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await SynchronizeAsync(client, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                _logger.LogError(exception, "Could not sync SocialPoster account settings to Azure App Configuration.");
            }

            await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
        }
    }

    private async Task SynchronizeAsync(ConfigurationClient client, CancellationToken cancellationToken)
    {
        var snapshot = await _secretStore.GetSnapshotAsync(
            AccountSettings.Select(setting => setting.StoreKey).ToArray(), cancellationToken);
        var changed = false;

        foreach (var (storeKey, configKey) in AccountSettings)
        {
            snapshot.TryGetValue(storeKey, out var configuredValue);
            // Publish empty values for cleared accounts so they override any stale
            // legacy environment setting still present on a Functions container.
            var value = configuredValue ?? "";
            if (_lastPublished.TryGetValue(configKey, out var previous) && previous == value)
            {
                continue;
            }

            await client.SetConfigurationSettingAsync(
                new ConfigurationSetting(configKey, value), cancellationToken: cancellationToken);

            _lastPublished[configKey] = value;
            changed = true;
        }

        if (changed)
        {
            await client.SetConfigurationSettingAsync(
                new ConfigurationSetting(SentinelKey, DateTimeOffset.UtcNow.ToString("O")),
                cancellationToken: cancellationToken);
            _logger.LogInformation("SocialPoster account settings were synchronized to Azure App Configuration.");
        }
    }
}