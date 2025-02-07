using Cloud5mins.ShortenerTools.Core.Domain.Socials.Bluesky;
using System.Net.Http.Json;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;

namespace Cloud5mins.ShortenerTools.Core.Domain.Socials.Threads
{
    public interface IThreadsManager
    {
        Task PostContentAsync(string message, string url, string token);
    }
    public class ThreadsManager : IThreadsManager
    {
        private readonly HttpClient _httpClient;

        public ThreadsManager()
        {
            _httpClient = new HttpClient();
        }

        public async Task PostContentAsync(string message, string url, string token)
        {
            var authUrl = $"https://graph.threads.net/refresh_access_token?grant_type=th_refresh_token&access_token={token}";

            var authResponse = await _httpClient.GetAsync(authUrl);
            authResponse.EnsureSuccessStatusCode();

            var authResponseString = await authResponse.Content.ReadAsStringAsync();
            var authJsonResponse = JsonDocument.Parse(authResponseString);
            var _accessToken = authJsonResponse.RootElement.GetProperty("access_token").GetString();

            if (string.IsNullOrEmpty(_accessToken))
            {
                throw new InvalidOperationException("You must authenticate first.");
            }

            //If image provided, get card for image
            string encodedUrl = UrlEncoder.Default.Encode(url);

            HttpClient client = new HttpClient();
            var card = await client.GetFromJsonAsync<BlueSkyCard>($"https://cardyb.bsky.app/v1/extract?url={encodedUrl}");

            string mediaContainerUrl = string.Empty;
            if (card != null && !string.IsNullOrEmpty(card.image))
            {
                mediaContainerUrl = $"https://graph.threads.net/v1.0/me/threads?media_type=IMAGE&image_url={card.image}&text={UrlEncoder.Default.Encode(message)}&access_token={_accessToken}";
            }
            else
            {
                mediaContainerUrl = $"https://graph.threads.net/v1.0/me/threads?media_type=TEXT&text={UrlEncoder.Default.Encode(message)}&access_token={_accessToken}";
            }


            var content = new StringContent(JsonSerializer.Serialize(new { }), Encoding.UTF8, "application/json");

            var mediaContainerResponse = await _httpClient.PostAsync(mediaContainerUrl, content);
            var mediaResponseString = await mediaContainerResponse.Content.ReadAsStringAsync();
            var mediaJsonResponse = JsonDocument.Parse(mediaResponseString);
            string mediaId = mediaJsonResponse.RootElement.GetProperty("id").GetString();

            Thread.Sleep(30000);
        }
    }
}
