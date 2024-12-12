using Cloud5mins.ShortenerTools.Core.Domain;
using Cloud5mins.ShortenerTools.Core.Domain.Socials;
using Cloud5mins.ShortenerTools.Core.Domain.Socials.LinkedIn.Models;
using FishyFlip;
using FishyFlip.Models;
using Mastonet;
using Mastonet.Entities;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Debug;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Processing;
using System.Diagnostics;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Encodings.Web;
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

        public SchedulePost(ILoggerFactory loggerFactory, ShortenerSettings settings, ILinkedInManager linkedInManager)
        {
            _logger = loggerFactory.CreateLogger<UrlRedirect>();
            _settings = settings;
            _linkedInManager = linkedInManager;
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
            var client = new TwitterClient(
               _settings.TwitterConsumerKey,
                _settings.TwitterConsumerSecret,
                _settings.TwitterAccessToken,
                _settings.TwitterAccessSecret
                );

            var poster = new TweetsV2Poster(client);

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
            var client = new MastodonClient("fosstodon.org", _settings.MastodonAccessToken);
            var status = await client.PublishStatus($"{linkInfo.Title} \n {linkInfo.Message} \n\n {ShortenerBase}{linkInfo.RowKey}", new Mastonet.Visibility?((Mastonet.Visibility)0), (string)null, (IEnumerable<string>)null, false, (string)null, new DateTime?(), (string)null, (PollParameters)null);
        }

        private async Task PostToBlueSky(ShortUrlEntity linkInfo)
        {
            var atProtocol = new ATProtocolBuilder()
                        .WithLogger(new DebugLoggerProvider().CreateLogger("FishyFlip"))
            .Build();

            var session = await atProtocol.AuthenticateWithPasswordAsync(_settings.BlueskyUserName, _settings.BlueskyPassword);

            if (session is null)
            {
                _logger.LogError("Failed to authenticate."); ;
                return;
            }

            string shortUrl = $"{ShortenerBase}{linkInfo.RowKey}";

            string postTemplate = $"{linkInfo.Title} \n {linkInfo.Message} \n\n {shortUrl}";


            HttpClient client = new HttpClient();

            List<Facet> facets = new List<Facet>();
            FishyFlip.Models.Image? image = null;

            try
            {
                string encodedUrl = HtmlEncoder.Default.Encode(shortUrl);
                var card = await client.GetFromJsonAsync<BlueSkyCard>($"https://cardyb.bsky.app/v1/extract?url={encodedUrl}");

                if (card != null)
                {
                    var uri = new Uri(card.image);

                    var uriWithoutQuery = uri.GetLeftPart(UriPartial.Path);
                    var fileExtension = Path.GetExtension(uriWithoutQuery);

                    var fileName = card.title;

                    var imageBytes = await client.GetByteArrayAsync(uri);

                    if (imageBytes.Length != 0)
                    {
                        if (imageBytes.Length > 999999)
                        {
                            imageBytes = await ScaleImage(imageBytes);
                        }

                        await File.WriteAllBytesAsync(Path.Combine(Path.GetTempPath(), $"{fileName}.jpg"), imageBytes);

                        var stream = File.OpenRead(Path.Combine(Path.GetTempPath(), $"{fileName}.jpg"));
                        var content = new StreamContent(stream);
                        content.Headers.ContentLength = stream.Length;

                        content.Headers.ContentType = new MediaTypeHeaderValue("image/jpg");
                        var blobResult = await atProtocol.Repo.UploadBlobAsync(content);

                        await blobResult.SwitchAsync(
                            async success =>
                            {
                                image = success.Blob.ToImage();
                            },
                            async error =>
                            {
                                _logger.LogError($"Error: {error.StatusCode} {error.Detail}");
                            }
                            );
                    }

                    int promptStart = postTemplate.IndexOf($"{shortUrl}", StringComparison.InvariantCulture);
                    int promptEnd = promptStart + Encoding.Default.GetBytes($"{shortUrl}").Length;
                    var index = new FacetIndex(promptStart, promptEnd);
                    var link = FacetFeature.CreateLink(card.url);
                    facets.Add(new Facet(index, link));

                }
            }
            catch (Exception ex)
            {
                _logger.LogError($"Error: {ex.Message}");
            }


            Result<CreatePostResponse> postResult = null;

            if (image != null)
            {
                postResult = await atProtocol.Repo.CreatePostAsync(postTemplate, facets.ToArray(), new ImagesEmbed(image, $"{shortUrl}"));
            }
            else
            {
                postResult = await atProtocol.Repo.CreatePostAsync(postTemplate, facets.ToArray());
            }

            if (postResult != null)
            {
                postResult.Switch(
                    success =>
                    {
                        _logger.LogInformation($"Post: {success.Uri} {success.Cid}");
                    },
                    error =>
                    {
                        _logger.LogError($"Error: {error.StatusCode} {error.Detail}");
                    }
                    );
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
