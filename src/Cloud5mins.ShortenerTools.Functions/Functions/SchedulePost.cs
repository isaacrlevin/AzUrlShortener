using Cloud5mins.ShortenerTools.Core.Domain;
using Cloud5mins.ShortenerTools.Core.Domain.Socials;
using Cloud5mins.ShortenerTools.Core.Domain.Socials.Bluesky;
using Cloud5mins.ShortenerTools.Core.Domain.Socials.LinkedIn.Models;
using Cloud5mins.ShortenerTools.Core.Domain.Socials.Threads;
using Cloud5mins.ShortenerTools.Core.Domain.Socials.Twitter;
using FishyFlip;
using FishyFlip.Lexicon;
using FishyFlip.Lexicon.App.Bsky.Embed;
using FishyFlip.Lexicon.App.Bsky.Richtext;
using Mastonet;
using Mastonet.Entities;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Debug;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Processing;
using System.Diagnostics;

namespace Cloud5mins.ShortenerTools.Functions.Functions
{
    public class SchedulePost
    {
        private readonly ILogger _logger;
        private readonly ShortenerSettings _settings;
        private readonly ILinkedInManager _linkedInManager;
        private readonly EmailService _emailService;
        public readonly string ShortenerBase = "https://isaacl.dev/";
        public readonly IThreadsManager _threadsManager;
        public readonly MastodonClient mastodonClient;
        public readonly ATProtocol atProtocol;

        public SchedulePost(ILoggerFactory loggerFactory, ShortenerSettings settings, ILinkedInManager linkedInManager, EmailService emailService, IThreadsManager threadsManager)
        {
            _logger = loggerFactory.CreateLogger<SchedulePost>();
            _settings = settings;
            _linkedInManager = linkedInManager;
            _emailService = emailService;
            _threadsManager = threadsManager;

            mastodonClient = new MastodonClient("fosstodon.org", _settings.MastodonAccessToken);
            atProtocol = new ATProtocolBuilder()
                 .WithLogger(new DebugLoggerProvider().CreateLogger("FishyFlip")).Build();

            _threadsManager = threadsManager;

        }

        [Function("TestShortUrl")]
        public async Task Run([HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "test/{shortUrl}")] HttpRequestData req, string shortUrl, ExecutionContext context)
        {
            if (Debugger.IsAttached)
            {
                if (!string.IsNullOrWhiteSpace(shortUrl))
                {
                    StorageTableHelper stgHelper = new StorageTableHelper(_settings.DataStorage);

                    var tempUrl = new ShortUrlEntity(string.Empty, shortUrl);
                    var item = await stgHelper.GetShortUrlEntity(tempUrl);

                    if (item != null)
                    {
                        await Tweet(item);
                        await PostToBlueSky(item);
                        await PostToLinkedIn(item);
                        await PublishToMastodon(item);
                    }
                }
            }
        }

        [Function("SchedulePostTimer")]
        public async Task SchedulePostTimer([TimerTrigger("0 0 13,16,19,23 * * 1-5")] TimerInfo myTimer)
        {
            if (!Debugger.IsAttached)
            {
                await PublishToSocial();
            }
        }

        [Function("SchedulePostHttp")]
        public async Task SchedulePostHttp([HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "api/SchedulePost")] HttpRequestData req, ExecutionContext context)
        {
            if (Debugger.IsAttached)
            {
                await PublishToSocial();
            }
        }


        private async Task PublishToSocial()
        {
            StorageTableHelper stgHelper = new StorageTableHelper(_settings.DataStorage);

            var list = await stgHelper.GetAllShortUrlEntities();
            var item = list.Where(p => p.Posted == false).OrderBy(a => a.Timestamp).FirstOrDefault();


            if (item != null)
            {
                await Tweet(item);
                await PostToBlueSky(item);
                await PostToLinkedIn(item);
                await PublishToMastodon(item);
                await PostToThreads(item);
                item.Posted = true;
                var result = await stgHelper.UpdateShortUrlEntity(item);
            }
        }

        private async Task Tweet(ShortUrlEntity linkInfo)
        {
            try
            {
                // Build the tweet text (same logic as before)
                var text = $"{linkInfo.Message}\n\n{ShortenerBase}{linkInfo.RowKey}";

                if (!string.IsNullOrEmpty((linkInfo.Title)))
                {
                    text = $"{linkInfo.Title}\n{text}";
                }


                if (text.Length > 280)
                {
                    text = $"{linkInfo.Title}\n\n{ShortenerBase}{linkInfo.RowKey}";

                    if (!string.IsNullOrEmpty((linkInfo.Title)))
                    {
                        text = $"{linkInfo.Title}\n{text}";
                    }
                }

                // Build the X intent URL so the tweet can be posted with one click.
                // Hashtags already embedded in the text are preserved automatically.
                var intentUrl = TwitterIntentHelper.BuildIntentUrl(text, _settings.TwitterViaHandle);

                var hashtags = TwitterIntentHelper.ExtractHashtags(text);
                string hashtagInfo = hashtags.Any() ? $" (hashtags: {string.Join(", ", hashtags)})" : string.Empty;

                _logger.LogInformation($"Twitter intent URL generated for {linkInfo.RowKey}{hashtagInfo}");

                // Send an email with the intent URL so it can be clicked to post immediately.
                await _emailService.SendTwitterIntentEmail(
                    $"Ready to post on X: {linkInfo.Title}",
                    intentUrl,
                    text);
            }
            catch (Exception ex)
            {
                await _emailService.SendExceptionEmail($"Error when building Twitter intent for {linkInfo.RowKey}", ex);
            }
        }

        private async Task PublishToMastodon(ShortUrlEntity linkInfo)
        {
            try
            {
                var text = $"{linkInfo.Message}\n\n{ShortenerBase}{linkInfo.RowKey}";

                if (!string.IsNullOrEmpty((linkInfo.Title)))
                {
                    text = $"{linkInfo.Title}\n{text}";
                }
                
                var status = await mastodonClient.PublishStatus(text, new Mastonet.Visibility?((Mastonet.Visibility)0), (string)null, (IEnumerable<string>)null, false, (string)null, new DateTime?(), (string)null, (PollParameters)null);
            }
            catch (Exception ex)
            {
                await _emailService.SendExceptionEmail($"Error when posting {linkInfo.RowKey} to Mastodon", ex);
            }
        }

        private async Task PostToBlueSky(ShortUrlEntity linkInfo)
        {
            try
            {
                var session = await atProtocol.AuthenticateWithPasswordResultAsync(_settings.BlueskyUserName, _settings.BlueskyPassword);

                if (session is null)
                {
                    _logger.LogError("Failed to authenticate."); ;
                    return;
                }

                string shortUrl = $"{ShortenerBase}{linkInfo.RowKey}";

                string postTemplate = $"{linkInfo.Message}\n\n{shortUrl}";

                if (!string.IsNullOrEmpty((linkInfo.Title)))
                {
                    postTemplate = $"{linkInfo.Title}\n{postTemplate}";

                }

                HttpClient client = new HttpClient();

                List<Facet> facets = new List<Facet>();

              
                var tags = BlueskyUtilities.ExtractTags(postTemplate);
                foreach (var tag in tags)
                {
                    facets.Add(Facet.CreateFacetHashtag(tag.start, tag.end + 1, tag.tag.Replace("#","")));
                }

                var facetUrl = (await BlueskyUtilities.ExtractUrls(postTemplate)).First();

                facets.Add(Facet.CreateFacetLink(facetUrl.start, facetUrl.end, facetUrl.url));

                var image = await BlueskyUtilities.UploadImage(shortUrl, atProtocol, facets, postTemplate);

                if (facets != null && image != null)
                {
                    var postResult = await atProtocol.Feed.CreatePostAsync(postTemplate, facets: facets, embed: new EmbedImages(images: new() { image }));

                    postResult.Switch(
                        success =>
                        {
                            _logger.LogInformation($"Post: {success.Uri} {success.Cid}");
                        },
                        error =>
                        {
                            _logger.LogInformation($"Error: {error.StatusCode} {error.Detail}");
                        });
                }
                else
                {
                    var postResult = await atProtocol.Feed.CreatePostAsync(postTemplate, facets: facets);
                    postResult.Switch(
                        success =>
                        {
                            _logger.LogInformation($"Post: {success.Uri} {success.Cid}");
                        },
                        error =>
                        {
                            _logger.LogInformation($"Error: {error.StatusCode} {error.Detail}");
                        }
                        );
                }

            }
            catch (Exception ex)
            {
                await _emailService.SendExceptionEmail($"Error when posting {linkInfo.RowKey} to Bluesky", ex);
            }
        }

        private async Task PostToLinkedIn(ShortUrlEntity linkInfo)
        {
            try
            {
                var user = await _linkedInManager.GetMyLinkedInUserProfile(_settings.LinkedInAccessToken);

                var text = $"{linkInfo.Message}\n\n{ShortenerBase}{linkInfo.RowKey}";
                if (!string.IsNullOrEmpty((linkInfo.Title)))
                {
                    text = $"{linkInfo.Title}\n{text}";
                }
                var id = await _linkedInManager.PostShareTextAndLink(_settings.LinkedInAccessToken, user.Sub, text, $"{ShortenerBase}{linkInfo.RowKey}");
            }

            catch (Exception ex)
            {
                await _emailService.SendExceptionEmail($"Error when posting {linkInfo.RowKey} to LinkedIn", ex);
            }
        }

        private async Task PostToThreads(ShortUrlEntity linkInfo)
        {
            try
            {
                var postTemplate = $"{linkInfo.Message}\n\n{ShortenerBase}{linkInfo.RowKey}";
                if (!string.IsNullOrEmpty((linkInfo.Title)))
                {
                    postTemplate = $"{linkInfo.Title}\n{postTemplate}";
                }

                postTemplate = postTemplate.Replace("\r\n", " ");


                await _threadsManager.PostContentAsync(postTemplate, linkInfo.Url, _settings.ThreadsToken);
            }
            catch (Exception ex)
            {
                await _emailService.SendExceptionEmail($"Error when posting {linkInfo.RowKey} to Threads", ex);
            }
        }

        public async Task<byte[]> ScaleImage(byte[] imageBytes, int maxSizeInBytes = 999999)
        {
            using var image = SixLabors.ImageSharp.Image.Load(imageBytes);
            var ratio = Math.Sqrt((double)maxSizeInBytes / imageBytes.Length);
            var newWidth = (int)(image.Width * ratio);
            var newHeight = (int)(image.Height * ratio);

            image.Mutate(x => x.Resize(newWidth, newHeight));

            using var ms = new MemoryStream();
            await image.SaveAsJpegAsync(ms);
            return ms.ToArray();
        }
    }
}
