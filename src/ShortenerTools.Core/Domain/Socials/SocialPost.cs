namespace ShortenerTools.Core.Domain.Socials;

public sealed record SocialPost(string Key, string Title, string Text, string Url)
{
    public string? TwitterText { get; init; }
    public string? MastodonText { get; init; }
    public string? BlueskyText { get; init; }
    public string? LinkedInText { get; init; }
    public string? ThreadsText { get; init; }
    public string? ThreadsUrl { get; init; }
}

public interface ISocialMediaPublisher
{
    Task PublishAsync(SocialPost post, bool includeThreads = true);
}
