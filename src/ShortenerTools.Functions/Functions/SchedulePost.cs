using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using ShortenerTools.Core.Domain;
using ShortenerTools.Core.Domain.Socials;
using SkiaSharp;
using System.Diagnostics;

namespace ShortenerTools.Functions.Functions;

public class SchedulePost(ShortenerSettings settings, ISocialMediaPublisher publisher)
{
    public string ShortenerBase => settings.CustomDomain.TrimEnd('/') + "/";

    [Function("TestShortUrl")]
    public async Task Run(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "test/{shortUrl}")]
        HttpRequestData req, string shortUrl, ExecutionContext context)
    {
        if (settings.ExternalPostingAllowed && Debugger.IsAttached && !string.IsNullOrWhiteSpace(shortUrl))
        {
            var storage = new StorageTableHelper(settings.DataStorage);
            var item = await storage.GetShortUrlEntity(new ShortUrlEntity(string.Empty, shortUrl));
            if (item is not null)
            {
                await publisher.PublishAsync(CreatePost(item), includeThreads: false);
            }
        }
    }

    [Function("ShortPostTimer")]
    public async Task ShortPostTimer([TimerTrigger("0 0 13,16,19,23 * * 1-5")] TimerInfo myTimer)
    {
        if (settings.ExternalPostingAllowed && !Debugger.IsAttached)
        {
            await PublishToSocial();
        }
    }

    [Function("ShortPostHttp")]
    public async Task ShortPostHttp(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "api/SchedulePost")]
        HttpRequestData req, ExecutionContext context)
    {
        if (settings.ExternalPostingAllowed && Debugger.IsAttached)
        {
            await PublishToSocial();
        }
    }

    private async Task PublishToSocial()
    {
        if (!settings.ExternalPostingAllowed)
        {
            return;
        }
        var storage = new StorageTableHelper(settings.DataStorage);
        var items = await storage.GetAllShortUrlEntities();
        var item = items.Where(p => !p.Posted).OrderBy(p => p.Timestamp).FirstOrDefault();
        if (item is not null)
        {
            await publisher.PublishAsync(CreatePost(item));
            item.Posted = true;
            await storage.UpdateShortUrlEntity(item);
        }
    }

    internal SocialPost CreatePost(ShortUrlEntity item)
    {
        var url = $"{ShortenerBase}{item.RowKey}";
        var text = $"{item.Message}\n\n{url}";
        if (!string.IsNullOrEmpty(item.Title))
        {
            text = $"{item.Title}\n{text}";
        }
        // Preserve the existing shortener's X fallback for long posts.
        var twitterText = text;
        if (twitterText.Length > 280)
        {
            twitterText = $"{item.Title}\n\n{url}";
            if (!string.IsNullOrEmpty(item.Title))
            {
                twitterText = $"{item.Title}\n{twitterText}";
            }
        }
        return new SocialPost(item.RowKey, item.Title, text, url)
        {
            TwitterText = twitterText,
            ThreadsUrl = item.Url
        };
    }

    public Task<byte[]> ScaleImage(byte[] imageBytes, int maxSizeInBytes = 999999)
    {
        ArgumentNullException.ThrowIfNull(imageBytes);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxSizeInBytes);
        using var data = SKData.CreateCopy(imageBytes);
        using var codec = SKCodec.Create(data)
            ?? throw new InvalidDataException("The image could not be decoded.");
        using var image = SKBitmap.Decode(codec)
            ?? throw new InvalidDataException("The image could not be decoded.");
        var ratio = Math.Sqrt((double)maxSizeInBytes / imageBytes.Length);
        var width = Math.Max(1, (int)(image.Width * ratio));
        var height = Math.Max(1, (int)(image.Height * ratio));
        using var resized = image.Resize(new SKImageInfo(width, height), new SKSamplingOptions(SKCubicResampler.Mitchell))
            ?? throw new InvalidOperationException("The image could not be resized.");
        using var encoded = resized.Encode(SKEncodedImageFormat.Jpeg, 75)
            ?? throw new InvalidOperationException("The image could not be encoded as JPEG.");
        return Task.FromResult(encoded.ToArray());
    }
}
