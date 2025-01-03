using Cloud5mins.ShortenerTools.Core.Domain;
using Cloud5mins.ShortenerTools.Core.Domain.Socials;
using Cloud5mins.ShortenerTools.Core.Domain.Socials.Bluesky;
using Cloud5mins.ShortenerTools.Core.Domain.Socials.LinkedIn.Models;
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
using Tweetinvi;
using Tweetinvi.Core.Web;
using Tweetinvi.Exceptions;

namespace Cloud5mins.ShortenerTools.Functions.Functions
{
    public class SchedulePost
    {
        private readonly ILogger _logger;
        private readonly ShortenerSettings _settings;
        private readonly ILinkedInManager _linkedInManager;
        private readonly EmailService _emailService;
        public readonly string ShortenerBase = "https://isaacl.dev/";

        public readonly MastodonClient mastodonClient;
        public readonly ATProtocol atProtocol;
        public readonly TweetsV2Poster poster;

        public SchedulePost(ILoggerFactory loggerFactory, ShortenerSettings settings, ILinkedInManager linkedInManager, EmailService emailService)
        {
            _logger = loggerFactory.CreateLogger<SchedulePost>();
            _settings = settings;
            _linkedInManager = linkedInManager;
            _emailService = emailService;

            mastodonClient = new MastodonClient("fosstodon.org", _settings.MastodonAccessToken);
            atProtocol = new ATProtocolBuilder()
                 .WithLogger(new DebugLoggerProvider().CreateLogger("FishyFlip")).Build();

            var client = new TwitterClient(
                _settings.TwitterConsumerKey,
                _settings.TwitterConsumerSecret,
                _settings.TwitterAccessToken,
                _settings.TwitterAccessSecret
                );

            poster = new TweetsV2Poster(client);
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
        public async Task SchedulePostTimer([TimerTrigger("0 0 16,19,23 * * 1-5")] TimerInfo myTimer)
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
                item.Posted = true;
                var result = await stgHelper.UpdateShortUrlEntity(item);
            }
        }

        private async Task Tweet(ShortUrlEntity linkInfo)
        {
            try
            {
                var text = $"{linkInfo.Title} \n {linkInfo.Message} \n\n {ShortenerBase}{linkInfo.RowKey}";

                if (text.Length > 280)
                {
                    text = $"{linkInfo.Title} \n\n {ShortenerBase}{linkInfo.RowKey}";
                }
                ITwitterResult tweetResult = await poster.PostTweet(
                    new TweetV2PostRequest
                    {
                        Text = text
                    }
                );

                if (tweetResult.Response.IsSuccessStatusCode == false)
                {
                    throw new Exception(
                        "Error when posting tweet: " + Environment.NewLine + tweetResult.Content
                    );
                }
            }
            catch (TwitterException ex)
            {
                await _emailService.SendExceptionEmail($"Error when posting {linkInfo.ShortUrl} to Twitter", ex);
            }
        }

        private async Task PublishToMastodon(ShortUrlEntity linkInfo)
        {
            try
            {
                var status = await mastodonClient.PublishStatus($"{linkInfo.Title} \n {linkInfo.Message} \n\n {ShortenerBase}{linkInfo.RowKey}", new Mastonet.Visibility?((Mastonet.Visibility)0), (string)null, (IEnumerable<string>)null, false, (string)null, new DateTime?(), (string)null, (PollParameters)null);
            }
            catch (Exception ex)
            {
                await _emailService.SendExceptionEmail($"Error when posting {linkInfo.ShortUrl} to Mastodon", ex);
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

                string postTemplate = $"{linkInfo.Title} \n {linkInfo.Message} \n\n {shortUrl}";


                HttpClient client = new HttpClient();

                List<Facet> facets = new List<Facet>();

                var hashtags = BlueskyUtilities.ExtractHashtags(postTemplate);

                foreach (var hashtag in hashtags)
                {
                    (int hashtagStart, int hashtagEnd) = BlueskyUtilities.ParsePrompt(postTemplate, hashtag);
                    facets.Add(Facet.CreateFacetHashtag(hashtagStart, hashtagEnd, hashtag.Replace("#", string.Empty)));
                }


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
                await _emailService.SendExceptionEmail($"Error when posting {linkInfo.ShortUrl} to Bluesky", ex);
            }
        }

        private async Task PostToLinkedIn(ShortUrlEntity linkInfo)
        {
            try
            {
                var user = await _linkedInManager.GetMyLinkedInUserProfile(_settings.LinkedInAccessToken);

                var text = $"{linkInfo.Title} \n {linkInfo.Message} \n\n {ShortenerBase}{linkInfo.RowKey}";
                var id = await _linkedInManager.PostShareTextAndLink(_settings.LinkedInAccessToken, user.Sub, text, $"{ShortenerBase}{linkInfo.RowKey}");
            }

            catch (Exception ex)
            {
                await _emailService.SendExceptionEmail($"Error when posting {linkInfo.ShortUrl} to LinkedIn", ex);
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
