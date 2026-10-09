using AppBsky.Richtext;
using CarpaNet;
using ComAtproto.Repo;
using Mastonet;
using Microsoft.Extensions.Logging;
using ShortenerTools.Core.Domain;
using ShortenerTools.Core.Domain.Socials;
using ShortenerTools.Core.Domain.Socials.Bluesky;
using ShortenerTools.Core.Domain.Socials.LinkedIn.Models;
using ShortenerTools.Core.Domain.Socials.Threads;
using ShortenerTools.Core.Domain.Socials.Twitter;

namespace ShortenerTools.Functions.Socials;

public sealed class SocialMediaPublisher(
    ILogger<SocialMediaPublisher> logger,
    ShortenerSettings settings,
    ILinkedInManager linkedInManager,
    EmailService emailService,
    IThreadsManager threadsManager) : ISocialMediaPublisher
{
    public async Task PublishAsync(SocialPost post, bool includeThreads = true)
    {
        if (!settings.ExternalPostingAllowed)
        {
            return;
        }

        await SendAsync("X", post, async () =>
        {
            var text = post.TwitterText ?? post.Text;
            var intentUrl = TwitterIntentHelper.BuildIntentUrl(text, settings.TwitterViaHandle);
            await emailService.SendTwitterIntentEmail($"Ready to post on X: {post.Title}", intentUrl, text);
        });
        await SendAsync("Bluesky", post, () => PostToBlueskyAsync(post));
        await SendAsync("LinkedIn", post, async () =>
        {
            var user = await linkedInManager.GetMyLinkedInUserProfile(settings.LinkedInAccessToken);
            await linkedInManager.PostShareTextAndLink(
                settings.LinkedInAccessToken, user.Sub, post.LinkedInText ?? post.Text, post.Url);
        });
        await SendAsync("Mastodon", post, async () =>
        {
            var client = new MastodonClient("fosstodon.org", settings.MastodonAccessToken);
            await client.PublishStatus(post.MastodonText ?? post.Text, Mastonet.Visibility.Public);
        });
        if (includeThreads)
        {
            await SendAsync("Threads", post, () => threadsManager.PostContentAsync(
                (post.ThreadsText ?? post.Text).Replace("\r\n", " "),
                post.ThreadsUrl ?? post.Url, settings.ThreadsToken));
        }
    }

    private async Task SendAsync(string platform, SocialPost post, Func<Task> send)
    {
        try
        {
            await send();
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Error when posting {PostKey} to {Platform}", post.Key, platform);
            await emailService.SendExceptionEmail(
                $"Error when posting {post.Key} to {platform}", exception, post.Text);
        }
    }

    private async Task PostToBlueskyAsync(SocialPost post)
    {
        var client = await ATProtoClientFactory.CreateWithSessionAsync(
            settings.BlueskyUserName, settings.BlueskyPassword);
        if (client is null || !client.IsAuthenticated)
        {
            throw new InvalidOperationException("Failed to authenticate to Bluesky.");
        }

        var text = post.BlueskyText ?? post.Text;
        var facets = new List<Facet>();
        foreach (var mention in BlueskyUtilities.ExtractMentions(text))
        {
            var did = await BlueskyUtilities.GetDid(mention.mention, client);
            if (did is not null)
            {
                facets.Add(new Facet
                {
                    Index = new FacetByteSlice { ByteStart = mention.start, ByteEnd = mention.end },
                    Features = [new FacetMention { Did = did.Value }]
                });
            }
        }
        foreach (var url in await BlueskyUtilities.ExtractUrls(text))
        {
            facets.Add(new Facet
            {
                Index = new FacetByteSlice { ByteStart = url.start, ByteEnd = url.end },
                Features = [new FacetLink { Uri = url.url }]
            });
        }
        foreach (var tag in BlueskyUtilities.ExtractTags(text))
        {
            facets.Add(new Facet
            {
                Index = new FacetByteSlice { ByteStart = tag.start, ByteEnd = tag.end },
                Features = [new FacetTag { Tag = tag.tag.TrimStart('#') }]
            });
        }

        var image = await BlueskyUtilities.UploadImage(post.Url, client, facets, text);
        var result = await client.ComAtprotoRepoCreateRecordAsync(new CreateRecordInput
        {
            Repo = new ATIdentifier(client.AuthenticatedDid!),
            Collection = "app.bsky.feed.post",
            Record = BlueskyUtilities.CreatePostRecord(text, facets, image)
        });
        logger.LogInformation("Bluesky post published: {Uri} {Cid}", result.Uri, result.Cid);
    }
}
