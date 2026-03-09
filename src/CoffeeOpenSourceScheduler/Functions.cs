using Cloud5mins.ShortenerTools.Core.Domain;
using Cloud5mins.ShortenerTools.Core.Domain.Socials;
using Cloud5mins.ShortenerTools.Core.Domain.Socials.Bluesky;
using Cloud5mins.ShortenerTools.Core.Domain.Socials.LinkedIn.Models;
using Cloud5mins.ShortenerTools.Core.Domain.Socials.Threads;
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
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System.Diagnostics;
using System.Net;
using System.Text.Json;
using Tweetinvi;
using Tweetinvi.Core.Web;
using Tweetinvi.Exceptions;
using TwitterScheduler.Models;

namespace TwitterScheduler
{
    public class Functions
    {
        private readonly ILogger _logger;
        private readonly ShortenerSettings _settings;
        private readonly ILinkedInManager _linkedInManager;
        private readonly EmailService _emailService;
        public readonly string ShortenerBase = "https://isaacl.dev/";

        public readonly MastodonClient mastodonClient;
        public readonly ATProtocol atProtocol;
        public readonly TweetsV2Poster poster;
        public readonly IThreadsManager _threadsManager;

        public Functions(ILoggerFactory loggerFactory, ShortenerSettings settings, ILinkedInManager linkedInManager, EmailService emailService, IThreadsManager threadsManager)
        {
            _logger = loggerFactory.CreateLogger<Functions>();
            _settings = settings;
            _linkedInManager = linkedInManager;
            _emailService = emailService;
            _threadsManager = threadsManager;

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

        #region Timers

        [Function("PostTeaserTimer")]
        public async Task PostTeaserTimer([TimerTrigger("0 0 17 * * MON")] TimerInfo myTimer)
        {
            if (!Debugger.IsAttached)
            {
                await this.PostTeaser();
            }
        }

        [Function("PostAnnouncementTimer")]
        public async Task PostAnnouncementTimer([TimerTrigger("0 0 17 * * *")] TimerInfo myTimer)
        {
            if (!Debugger.IsAttached)
            {
                await this.PostAnnouncement();
            }
        }

        [Function("PostArchiveTimer")]
        public async Task PostArchiveTimer([TimerTrigger("0 0 16 * * MON")] TimerInfo myTimer)
        {
            if (!Debugger.IsAttached)
            {
                await this.PostArchive();
            }
        }

        #endregion

        #region Http

        [Function("PostPublishHttp")]
        public async Task<HttpResponseData> PostPublishHttp([HttpTrigger(AuthorizationLevel.Anonymous, "post")] HttpRequestData req)
        {
            // Validation of the inputs
            if (req == null)
            {
                return req.CreateResponse(HttpStatusCode.NotFound);
            }
            string guestKey = "";
            using (var reader = new StreamReader(req.Body))
            {
                var strBody = await reader.ReadToEndAsync();
                guestKey = System.Text.Json.JsonSerializer.Deserialize<string>(strBody, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

                if (string.IsNullOrEmpty(guestKey))
                {
                    return req.CreateResponse(HttpStatusCode.NotFound);
                }
            }

            await this.PostPublish(guestKey);

            var response = req.CreateResponse(HttpStatusCode.OK);
            await response.WriteAsJsonAsync("Complete");

            return response;
        }


        [Function("PostTeaserHttp")]
        public async Task PostTeaserHttp([HttpTrigger(AuthorizationLevel.Function, "get")] HttpRequestData req)
        {
            await this.PostTeaser();
        }

        [Function("PostAnnouncementHttp")]
        public async Task PostAnnouncementHttp([HttpTrigger(AuthorizationLevel.Function, "get")] HttpRequestData req)
        {
            await this.PostAnnouncement();
        }

        [Function("PostArchiveHttp")]
        public async Task PostArchiveHttp([HttpTrigger(AuthorizationLevel.Function, "get")] HttpRequestData req)
        {
            await this.PostArchive();
        }

        #endregion

        private async Task PostTeaser()
        {
            if (_settings.PostSocials)
            {
                List<Guest> source = await GetGuestList();

                List<Guest> list = source.Where<Guest>((Func<Guest, bool>)(a => a.DateTimeUTC > DateTime.UtcNow && a.DateTimeUTC < DateTime.UtcNow.AddDays(5.0))).ToList<Guest>();
                if (list.Count <= 0)
                    return;
                Guest guest = list.FirstOrDefault<Guest>();
                _logger.LogInformation("Upcoming Guest: " + guest.PartitionKey);


                DateTimeOffset thisTime = DateTimeOffset.Now;
                TimeZoneInfo tzi = TimeZoneInfo.FindSystemTimeZoneById("Pacific Standard Time");
                bool isDaylight = tzi.IsDaylightSavingTime(thisTime);

                // convert the value of guest.DateTimeAsString to UTC based on the current time zone
                DateTime dt = DateTime.Parse(guest.DateTimeAsString);


                string postTemplate = "Coming up this week on Coffee & OSS I will be chatting with [HANDLE] about all sorts of #tech and #opensource topics. Streaming live on #Twitch this " +
                    (dt.ToString("dddd MMMM d") + Functions.GetDayNumberSuffix(dt.Day.ToString()) + " at " + dt.ToString("h:mm tt")) + (isDaylight ? " PDT" : " PST") +
                    ". Come say hello and join the conversation. \r\nhttps://www.coffeeandopensource.com/schedule.html";


                await Tweet(postTemplate, guest);
                await PostToLinkedIn(postTemplate, guest);
                await PublishToMastodon(postTemplate, guest);
                await PostToBlueSky(postTemplate, "https://www.coffeeandopensource.com/schedule.html", guest);
                await PostToThreads(postTemplate, guest);
            }
        }

        private async Task PostAnnouncement()
        {
            if (_settings.PostSocials)
            {
                List<Guest> source = await GetGuestList();

                List<Guest> list = source.Where<Guest>((Func<Guest, bool>)(a => a.DateTimeUTC > DateTime.UtcNow && a.DateTimeUTC < DateTime.UtcNow.AddHours(3))).ToList<Guest>();
                if (list.Count <= 0)
                    return;
                Guest guest = list.FirstOrDefault<Guest>();
                _logger.LogInformation("Upcoming Guest: " + guest.PartitionKey);

                DateTimeOffset thisTime = DateTimeOffset.Now;
                TimeZoneInfo tzi = TimeZoneInfo.FindSystemTimeZoneById("Pacific Standard Time");
                bool isDaylight = tzi.IsDaylightSavingTime(thisTime);

                // convert the value of guest.DateTimeAsString to UTC based on the current time zone
                DateTime dt = DateTime.Parse(guest.DateTimeAsString);

                string postTemplate = "Coming up on Coffee & OSS I will be chatting with [HANDLE] about all sorts of #tech and #opensource topics. Streaming live today on #Twitch at " +
                    dt.ToString("h:mm tt") + (isDaylight ? " PDT" : " PST") + ". Come say hello and join the conversation. \r\nhttps://www.twitch.tv/isaacrlevin";


                await Tweet(postTemplate, guest);
                await PostToLinkedIn(postTemplate, guest);
                await PublishToMastodon(postTemplate, guest);
                await PostToBlueSky(postTemplate, "https://www.twitch.tv/isaacrlevin", guest);
                await PostToThreads(postTemplate, guest);
            }
        }

        private async Task PostPublish(string guestKey)
        {
            if (_settings.PostSocials)
            {
                List<Guest> source = await GetGuestList();

                Guest guest = source.Where<Guest>((Func<Guest, bool>)(a => a.IsPublished && a.PartitionKey == guestKey)).FirstOrDefault<Guest>();

                if (guest != null)
                {
                    _logger.LogInformation("Publishing Episode: " + guest.PartitionKey);   



                    string postTemplate = $"Had a great time chatting with [HANDLE] on Coffee & OSS today about all kinds of #tech topics. " +
                                          $"Video is live on YouTube and podcast is available wherever you find them. Take a look/listen and thanks! \r\nhttps://www.coffeeandopensource.com/guest/{guest.PartitionKey}.html";


                    await Tweet(postTemplate, guest);
                    await PostToLinkedIn(postTemplate, guest);
                    await PublishToMastodon(postTemplate, guest);
                    await PostToBlueSky(postTemplate, $"https://www.coffeeandopensource.com/guest/{guest.PartitionKey}.html", guest);
                    await PostToThreads(postTemplate, guest);
                }
            }
        }

        private async Task PostArchive()
        {
            if (_settings.PostSocials)
            {
                List<Guest> source = await GetGuestList();

                List<Guest> list = source.Where<Guest>((Func<Guest, bool>)(a => a.IsPublished)).ToList<Guest>();
                int index = new Random().Next(list.Count - 1);
                Guest pickedGuest = list[index];
                _logger.LogInformation("Picked Guest: " + pickedGuest.PartitionKey);
                string postTemplate = $"From the Coffee & OSS Archives, I chatted with [HANDLE] about all sorts of great #tech and #oss topics. " +
                    $"Access the stream or listen to the podcast below. Be sure to like/subscribe. Thanks for tuning in! \r\nhttps://www.coffeeandopensource.com/guest/{pickedGuest.PartitionKey}.html";

                await Tweet(postTemplate, pickedGuest);
                await PostToLinkedIn(postTemplate, pickedGuest);
                await PublishToMastodon(postTemplate, pickedGuest);
                await PostToBlueSky(postTemplate, $"https://www.coffeeandopensource.com/guest/{pickedGuest.PartitionKey}.html", pickedGuest);
                await PostToThreads(postTemplate, pickedGuest);
            }
        }

        private async Task Tweet(string postTemplate, Guest pickedGuest)
        {
            if (pickedGuest.Socials.ContainsKey("X"))
            {
                postTemplate = postTemplate.Replace("[HANDLE]", "@" + ((IEnumerable<string>)pickedGuest.Socials["X"].Split("/")).LastOrDefault<string>());
            }
            else
            {
                postTemplate = postTemplate.Replace("[HANDLE]", FormatPartitionKey(pickedGuest.PartitionKey));
            }

            try
            {
                ITwitterResult itwitterResult = await new TweetsV2Poster((ITwitterClient)new TwitterClient(_settings.TwitterConsumerKey, _settings.TwitterConsumerSecret, _settings.TwitterAccessToken, _settings.TwitterAccessSecret)).PostTweet(new TweetV2PostRequest()
                {
                    Text = postTemplate
                });

                if (!itwitterResult.Response.IsSuccessStatusCode)
                    throw new Exception("Error when posting tweet: " + Environment.NewLine + itwitterResult.Content);
                _logger.LogInformation("Tweet Published");

            }
            catch (TwitterException ex)
            {
                await _emailService.SendExceptionEmail($"Error when posting {FormatPartitionKey(pickedGuest.PartitionKey)} to Twitter", ex, postTemplate);
            }
        }

        private async Task PublishToMastodon(string postTemplate, Guest pickedGuest)
        {
            postTemplate = postTemplate.Replace("@CoffeeAndOSS", "@CoffeeAndOSS@mastodon.social");

            if (pickedGuest.Socials.ContainsKey("Mastodon"))
            {
                string social = pickedGuest.Socials["Mastodon"];
                postTemplate = postTemplate.Replace("[HANDLE]", social);
            }
            else
            {
                postTemplate = postTemplate.Replace("[HANDLE]", FormatPartitionKey(pickedGuest.PartitionKey));
            }
            try
            {

                var client = new MastodonClient("fosstodon.org", _settings.MastodonAccessToken);
                var status = await client.PublishStatus(postTemplate, new Mastonet.Visibility?((Mastonet.Visibility)0), (string)null, (IEnumerable<string>)null, false, (string)null, new DateTime?(), (string)null, (PollParameters)null);
            }

            catch (Exception ex)
            {
                await _emailService.SendExceptionEmail($"Error when posting {FormatPartitionKey(pickedGuest.PartitionKey)} to Mastodon", ex, postTemplate);
            }
        }

        private async Task PostToLinkedIn(string postTemplate, Guest pickedGuest)
        {

            postTemplate = postTemplate.Replace("@CoffeeAndOSS", "Coffee and Open Source");
            postTemplate = postTemplate.Replace("[HANDLE]", FormatPartitionKey(pickedGuest.PartitionKey));

            try
            {
                var user = await _linkedInManager.GetMyLinkedInUserProfile(_settings.LinkedInAccessToken);

                var id = await _linkedInManager.PostShareTextAndLink(_settings.LinkedInAccessToken, user.Sub, postTemplate, $"https://www.coffeeandopensource.com/guest/{pickedGuest.PartitionKey}.html");
            }

            catch (Exception ex)
            {
                await _emailService.SendExceptionEmail($"Error when posting {FormatPartitionKey(pickedGuest.PartitionKey)} to LinkedIn", ex, postTemplate);
            }
        }

        private async Task PostToBlueSky(string postTemplate, string url, Guest pickedGuest)
        {
            postTemplate = postTemplate.Replace("@CoffeeAndOSS", "@coffeeandopensource.com").Replace("Coffee & OSS", "@coffeeandopensource.com");

            var atProtocol = new ATProtocolBuilder()
                        .WithLogger(new DebugLoggerProvider().CreateLogger("FishyFlip")).Build();

            var session = await atProtocol.AuthenticateWithPasswordResultAsync("isaacrlevin.com", "is04aac!");

            if (session is null)
            {
                _logger.LogError("Failed to authenticate.");
                return;
            }
            List<Facet> facets = new List<Facet>();


            if (pickedGuest.Socials.ContainsKey("Bluesky"))
            {
                var guestHandle = ((IEnumerable<string>)pickedGuest.Socials["Bluesky"].Split("/")).LastOrDefault<string>();
                postTemplate = postTemplate.Replace("[HANDLE]", "@" + guestHandle);

                var mentions = BlueskyUtilities.ExtractMentions(postTemplate);

                foreach (var mention in mentions)
                {
                    var did = await BlueskyUtilities.GetDid(mention.mention, atProtocol);
                    facets.Add(Facet.CreateFacetMention(mention.start, mention.end, did));
                }
            }
            else
            {
                postTemplate = postTemplate.Replace("[HANDLE]", FormatPartitionKey(pickedGuest.PartitionKey));
            }



            var facetUrls = await BlueskyUtilities.ExtractUrls(postTemplate);

            foreach (var facetUrl in facetUrls)
            {
                facets.Add(Facet.CreateFacetLink(facetUrl.start, facetUrl.end, facetUrl.url));
            }


            var tags = BlueskyUtilities.ExtractTags(postTemplate);
            foreach (var tag in tags)
            {
                facets.Add(Facet.CreateFacetHashtag(tag.start, tag.end + 1, tag.tag.Replace("#", "")));
            }

            var image = await BlueskyUtilities.UploadImage(url, atProtocol, facets, postTemplate);

            try
            {
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
                await _emailService.SendExceptionEmail($"Error when posting {FormatPartitionKey(pickedGuest.PartitionKey)} to Bluesky", ex, postTemplate);
            }
        }

        private async Task PostToThreads(string postTemplate, Guest pickedGuest)
        {
            postTemplate = postTemplate.Replace("\r\n", " ");
            if (pickedGuest.Socials.ContainsKey("Threads"))
            {
                var token = pickedGuest.Socials["Threads"];
                postTemplate = postTemplate.Replace("[HANDLE]", "@" + ((IEnumerable<string>)pickedGuest.Socials["Threads"].Split("/")).LastOrDefault<string>());
                await new ThreadsManager().PostContentAsync(postTemplate, $"https://www.coffeeandopensource.com/guest/{pickedGuest.PartitionKey}.html", token);
            }
            else
            {
                postTemplate = postTemplate.Replace("[HANDLE]", FormatPartitionKey(pickedGuest.PartitionKey));
            }
            try
            {

                await _threadsManager.PostContentAsync(postTemplate, $"https://www.coffeeandopensource.com/guest/{pickedGuest.PartitionKey}.html", _settings.ThreadsToken);
            }
            catch (Exception ex)
            {
                await _emailService.SendExceptionEmail($"Error when posting {FormatPartitionKey(pickedGuest.PartitionKey)} to Threads", ex, postTemplate);
            }
        }
        private string FormatPartitionKey(string partitionKey)
        {
            if (string.IsNullOrEmpty(partitionKey)) return string.Empty;

            return string.Join("-",
                partitionKey.Split('-')
                .Select(word => char.ToUpperInvariant(word[0]) + word.Substring(1).ToLowerInvariant())).Replace("-"," ");
        }
        private async Task<List<Guest>> GetGuestList()
        {
            JToken jtoken1 = JToken.Parse(await new HttpClient().GetStringAsync("https://raw.githubusercontent.com/isaacrlevin/CoffeeAndOpenSource.com/main/data/guests.json"));
            List<Guest> source = new List<Guest>();
            foreach (JToken jtoken2 in ((IEnumerable<JToken>)jtoken1.Children()).ToList<JToken>())
            {
                Guest guest1 = JsonConvert.DeserializeObject<Guest>(JsonConvert.SerializeObject((object)((IEnumerable<JToken>)jtoken2.Children()).FirstOrDefault<JToken>()));
                source.Add(guest1);
            }
            return source;
        }
        private static string GetDayNumberSuffix(string day)
        {
            string dayNumberSuffix = "th";
            if (int.Parse(day) < 11 || int.Parse(day) > 20)
            {
                char[] charArray = day.ToCharArray();
                day = charArray[charArray.Length - 1].ToString();
                switch (day)
                {
                    case "1":
                        dayNumberSuffix = "st";
                        break;
                    case "2":
                        dayNumberSuffix = "nd";
                        break;
                    case "3":
                        dayNumberSuffix = "rd";
                        break;
                }
            }
            return dayNumberSuffix;
        }
    }
}
