using Cloud5mins.ShortenerTools.Core.Domain;
using Cloud5mins.ShortenerTools.Core.Domain.Socials;
using Cloud5mins.ShortenerTools.Core.Domain.Socials.LinkedIn.Models;
using FishyFlip;
using FishyFlip.Models;
using Mastonet;
using Mastonet.Entities;
using Microsoft.ApplicationInsights;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Debug;
using System.Diagnostics;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using Tweetinvi;
using Tweetinvi.Core.Web;

namespace Cloud5mins.ShortenerTools.Functions.Functions
{
    public class SchedulePost
    {
        private readonly ILogger _logger;
        private readonly ShortenerSettings _settings;
        private TelemetryClient _telemetryClient;
        private readonly ILinkedInManager _linkedInManager;


        public readonly string TwitterConsumerKey = Environment.GetEnvironmentVariable(nameof(TwitterConsumerKey));
        public readonly string TwitterConsumerSecret = Environment.GetEnvironmentVariable(nameof(TwitterConsumerSecret)) ?? "";
        public readonly string TwitterAccessToken = Environment.GetEnvironmentVariable(nameof(TwitterAccessToken)) ?? "";
        public readonly string TwitterAccessSecret = Environment.GetEnvironmentVariable(nameof(TwitterAccessSecret)) ?? "";
        public readonly string MastodonAccessToken = Environment.GetEnvironmentVariable(nameof(MastodonAccessToken)) ?? "";
        public readonly string LinkedInAccessToken = Environment.GetEnvironmentVariable(nameof(LinkedInAccessToken)) ?? "";
        public readonly bool PostSocials = Convert.ToBoolean(Environment.GetEnvironmentVariable(nameof(PostSocials)));

        public readonly string ShortenerBase = "http://isaacl.dev/";

        public SchedulePost(ILoggerFactory loggerFactory, ShortenerSettings settings, TelemetryClient telemetry, ILinkedInManager linkedInManager)
        {
            _logger = loggerFactory.CreateLogger<UrlRedirect>();
            _settings = settings;
            _telemetryClient = telemetry;
            _linkedInManager = linkedInManager;
        }

        [Function("SchedulePostTimer")]
        public async Task SchedulePostTimer([TimerTrigger("0 */5 * * * *")] TimerInfo myTimer)
        {
            if (!Debugger.IsAttached)
            {
                await PublishToSocial();
            }
        }

        [Function("SchedulePostHttp")]
        public async Task SchedulePostHttp([HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "api/schedulepost")] HttpRequestData req, ExecutionContext context)
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
                //await Tweet(item);
                //await PostToBlueSky(item);
                //await PostToLinkedIn(item);
                //await PublishToMastodon(item);
                item.Posted = true;
                var result = await stgHelper.UpdateShortUrlEntity(item);
            }
        }

        private async Task Tweet(ShortUrlEntity linkInfo)
        {
            var client = new TwitterClient(
                TwitterConsumerKey,
                TwitterConsumerSecret,
                TwitterAccessToken,
                TwitterAccessSecret
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
            var client = new MastodonClient("fosstodon.org", this.MastodonAccessToken);
            var status = await client.PublishStatus($"{linkInfo.Title} \n {linkInfo.Message} \n\n {ShortenerBase}{linkInfo.RowKey}", new Mastonet.Visibility?((Mastonet.Visibility)0), (string)null, (IEnumerable<string>)null, false, (string)null, new DateTime?(), (string)null, (PollParameters)null);
        }

        private async Task PostToBlueSky(ShortUrlEntity linkInfo)
        {
            var atProtocol = new ATProtocolBuilder()
                        .WithLogger(new DebugLoggerProvider().CreateLogger("FishyFlip"))
            .Build();

            var session = await atProtocol.AuthenticateWithPasswordAsync("isaaclevin.com", "is04aac!");

            if (session is null)
            {
                Console.WriteLine("Failed to authenticate.");
                return;
            }

            string postTemplate = $"{linkInfo.Title} \n {linkInfo.Message} \n\n {ShortenerBase}{linkInfo.RowKey}";


            HttpClient client = new HttpClient();

            Facet? facet = null;
            FishyFlip.Models.Image? image = null;

            //check to see if linkInfo.Url is a valid link


            try
            {
                var card = await client.GetFromJsonAsync<BlueSkyCard>(linkInfo.Url);

                if (card != null)
                {
                    var uri = new Uri(card.image);

                    var uriWithoutQuery = uri.GetLeftPart(UriPartial.Path);
                    var fileExtension = Path.GetExtension(uriWithoutQuery);

                    var fileName = card.title;

                    var imageBytes = await client.GetByteArrayAsync(uri);
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

                            int promptStart = postTemplate.IndexOf(linkInfo.Url, StringComparison.InvariantCulture);
                            int promptEnd = promptStart + Encoding.Default.GetBytes(linkInfo.Url).Length;
                            var index = new FacetIndex(promptStart, promptEnd);
                            var link = FacetFeature.CreateLink(card.url);
                            facet = new Facet(index, link);
                        },
                        async error =>
                        {
                            Console.WriteLine($"Error: {error.StatusCode} {error.Detail}");
                        }
                        );
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error: {ex.Message}");
            }
            if (facet != null && image != null)
            {
                var postResult = await atProtocol.Repo.CreatePostAsync(postTemplate, new[] { facet }, new ImagesEmbed(image, $"Link to {linkInfo.Url}"));

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
                var postResult = await atProtocol.Repo.CreatePostAsync(postTemplate);
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

        private async Task PostToLinkedIn(ShortUrlEntity linkInfo)
        {
            var user = await _linkedInManager.GetMyLinkedInUserProfile(LinkedInAccessToken);

            var text = $"{linkInfo.Title} \n {linkInfo.Message} \n\n {ShortenerBase}{linkInfo.RowKey}";
            var id = await _linkedInManager.PostShareTextAndLink(LinkedInAccessToken, user.Sub, text, $"{ShortenerBase}{linkInfo.RowKey}");
        }
    }
}
