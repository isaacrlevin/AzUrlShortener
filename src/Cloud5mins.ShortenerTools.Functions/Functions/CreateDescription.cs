using Cloud5mins.ShortenerTools.Core.Messages;
using Microsoft.AspNetCore.Http;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using System.Net;
using System.Text.Json;

namespace Cloud5mins.ShortenerTools.Functions.Functions
{
    public class CreateDescription
    {
        private readonly ILogger<CreateDescription> _logger;
        private readonly IChatClient _client;

        public CreateDescription(ILogger<CreateDescription> logger, IChatClient client)
        {
            _logger = logger;
            _client = client;
        }

        [Function("CreateDescription")]
        public async Task<HttpResponseData> Run(
            [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "api/CreateDescription")] HttpRequestData req)
        {
            try
            {
                // Validation of the inputs
                if (req == null)
                {
                    return req.CreateResponse(HttpStatusCode.NotFound);
                }

                ShortUrlRequest shortUrlRequest = new ShortUrlRequest();

                using (var reader = new StreamReader(req.Body))
                {
                    var strBody = await reader.ReadToEndAsync();
                    shortUrlRequest = JsonSerializer.Deserialize<ShortUrlRequest>(strBody, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                    if (string.IsNullOrEmpty(shortUrlRequest.Url))
                    {
                        return req.CreateResponse(HttpStatusCode.NotFound);
                    }
                }

                // If the Url parameter only contains whitespaces or is empty return with BadRequest.
                if (string.IsNullOrWhiteSpace(shortUrlRequest.Url))
                {
                    var badResponse = req.CreateResponse(HttpStatusCode.BadRequest);
                    await badResponse.WriteAsJsonAsync(new { Message = "The url parameter can not be empty." });
                    return badResponse;
                }

                // Validates if input.url is a valid aboslute url, aka is a complete refrence to the resource, ex: http(s)://google.com
                if (!Uri.IsWellFormedUriString(shortUrlRequest.Url, UriKind.Absolute))
                {
                    var badResponse = req.CreateResponse(HttpStatusCode.BadRequest);
                    await badResponse.WriteAsJsonAsync(new { Message = $"{shortUrlRequest.Url} is not a valid absolute Url. The Url parameter must start with 'http://' or 'http://'." });
                    return badResponse;
                }

                int contentLength = 0;

                if (!string.IsNullOrEmpty(shortUrlRequest.Title))
                {                    
                    contentLength += shortUrlRequest.Title.Length;
                }

                string shortUrlTemplate = "https://isaacl.dev/aaaa";
                contentLength += shortUrlTemplate.Length;


                var systemPrompt = "You are a world class social media expert that creates engaging posts for various social media platforms. " +
                    "You have a deep understanding of social media trends, audience engagement strategies, and content creation best practices. " +
                    "Your goal is to craft compelling and concise social media posts that resonate with the target audience and drive engagement, " + 
                    "that audience being developers.";

                var message = @$"Create a professional social media post for this link with proper hastags. 
                                 Avoid unnecessary filler words such as 'unleash' or 'harness'
                                 Do not include the link in the response or the title of the page. Only return meaningful content regarding page referenced, 
                                 nothing else in the response. The entire response should not exceed {280 - contentLength - 5} characters. {shortUrlRequest.Url}";

                var chatResponse = await _client.GetResponseAsync(
                    [
                    new(ChatRole.System, systemPrompt),
                    new(ChatRole.User, message),
                    ]);

                var response = req.CreateResponse(HttpStatusCode.OK);
                await response.WriteAsJsonAsync(chatResponse.Text);

                return response;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "An unexpected error was encountered.");

                var badResponse = req.CreateResponse(HttpStatusCode.BadRequest);
                await badResponse.WriteAsJsonAsync(new { ex.Message });
                return badResponse;
            }
        }
    }
}
