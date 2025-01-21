using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Tweetinvi;
using Tweetinvi.Core.Web;
using Tweetinvi.Models;

namespace Cloud5mins.ShortenerTools.Core.Domain.Socials
{
    public class TweetsV2Poster
    {
        private readonly ITwitterClient client;

        public TweetsV2Poster(ITwitterClient client) => this.client = client;

        public Task<ITwitterResult> PostTweet(TweetV2PostRequest tweetParams) => client.Execute.AdvanceRequestAsync(request =>
        {
            StringContent stringContent = new StringContent(client.Json.Serialize(tweetParams), Encoding.UTF8, "application/json");
            request.Query.Url = "https://api.twitter.com/2/tweets";
            request.Query.HttpMethod = Tweetinvi.Models.HttpMethod.POST;
            request.Query.HttpContent = stringContent;
        });
    }

    public class TweetV2PostRequest
    {
        [JsonProperty("text")]
        public string Text { get; set; } = string.Empty;
    }
}
