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

namespace Cloud5mins.ShortenerTools.Functions.Functions
{
    public class SchedulePost
    {
        private readonly ILogger _logger;
        private readonly ShortenerSettings _settings;
        private readonly ILinkedInManager _linkedInManager;
        public readonly string ShortenerBase = "https://isaacl.dev/";

        public readonly MastodonClient mastodonClient;
        public readonly ATProtocol atProtocol;
        public readonly TweetsV2Poster poster;

        public SchedulePost(ILoggerFactory loggerFactory, ShortenerSettings settings, ILinkedInManager linkedInManager)
        {
            _logger = loggerFactory.CreateLogger<UrlRedirect>();
            _settings = settings;
            _linkedInManager = linkedInManager;


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


            ITwitterResult tweetResult = await poster.PostTweet(
                new TweetV2PostRequest
                {
                    Text = $"{linkInfo.Title} \n {linkInfo.Message} \n\n {ShortenerBase}{linkInfo.RowKey}"
                }
            );

            if (tweetResult.Response.IsSuccessStatusCode == false)
            {
                throw new Exception(
                    "Error when posting tweet: " + Environment.NewLine + tweetResult.Content
                );
            }
        }

        private async Task PublishToMastodon(ShortUrlEntity linkInfo)
        {
            var status = await mastodonClient.PublishStatus($"{linkInfo.Title} \n {linkInfo.Message} \n\n {ShortenerBase}{linkInfo.RowKey}", new Mastonet.Visibility?((Mastonet.Visibility)0), (string)null, (IEnumerable<string>)null, false, (string)null, new DateTime?(), (string)null, (PollParameters)null);
        }

        private async Task PostToBlueSky(ShortUrlEntity linkInfo)
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

            try
            {
                var image = await BlueskyUtilities.UploadImage(shortUrl, atProtocol, facets, postTemplate);

                if (facets != null && image != null)
                {
                    var postResult = await atProtocol.Feed.CreatePostAsync(postTemplate, facets: facets, embed: new EmbedImages(images: new() { image }));

                    postResult.Switch(
                        success =>
                        {
                            Console.WriteLine($"Post: {success.Uri} {success.Cid}");
                        },
                        error =>
                        {
                            Console.WriteLine($"Error: {error.StatusCode} {error.Detail}");
                        });
                }
                else
                {
                    var postResult = await atProtocol.Feed.CreatePostAsync(postTemplate, facets: facets);
                    postResult.Switch(
                        success =>
                        {
                            Console.WriteLine($"Post: {success.Uri} {success.Cid}");
                        },
                        error =>
                        {
                            Console.WriteLine($"Error: {error.StatusCode} {error.Detail}");
                        }
                        );
                }
            }
            catch (Exception ex)
            {
                _logger.LogError($"Error: {ex.Message}");
            }
        }

        private async Task PostToLinkedIn(ShortUrlEntity linkInfo)
        {
            var user = await _linkedInManager.GetMyLinkedInUserProfile(_settings.LinkedInAccessToken);

            var text = $"{linkInfo.Title} \n {linkInfo.Message} \n\n {ShortenerBase}{linkInfo.RowKey}";
            var id = await _linkedInManager.PostShareTextAndLink(_settings.LinkedInAccessToken, user.Sub, text, $"{ShortenerBase}{linkInfo.RowKey}");
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
