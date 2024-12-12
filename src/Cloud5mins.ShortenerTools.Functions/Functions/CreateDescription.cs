using Cloud5mins.ShortenerTools.Core.Messages;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using OllamaSharp;
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
            [HttpTrigger(AuthorizationLevel.Function, "post", Route = "api/CreateDescription")] HttpRequestData req)
        {
            var result = string.Empty;
            var url = string.Empty;
            try
            {
                // Validation of the inputs
                if (req == null)
                {
                    return req.CreateResponse(HttpStatusCode.NotFound);
                }

                using (var reader = new StreamReader(req.Body))
                {
                    var strBody = await reader.ReadToEndAsync();
                    url = JsonSerializer.Deserialize<string>(strBody, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                    if (string.IsNullOrEmpty(url))
                    {
                        return req.CreateResponse(HttpStatusCode.NotFound);
                    }
                }

                // If the Url parameter only contains whitespaces or is empty return with BadRequest.
                if (string.IsNullOrWhiteSpace(url))
                {
                    var badResponse = req.CreateResponse(HttpStatusCode.BadRequest);
                    await badResponse.WriteAsJsonAsync(new { Message = "The url parameter can not be empty." });
                    return badResponse;
                }

                // Validates if input.url is a valid aboslute url, aka is a complete refrence to the resource, ex: http(s)://google.com
                if (!Uri.IsWellFormedUriString(url, UriKind.Absolute))
                {
                    var badResponse = req.CreateResponse(HttpStatusCode.BadRequest);
                    await badResponse.WriteAsJsonAsync(new { Message = $"{url} is not a valid absolute Url. The Url parameter must start with 'http://' or 'http://'." });
                    return badResponse;
                }


            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "An unexpected error was encountered.");

                var badResponse = req.CreateResponse(HttpStatusCode.BadRequest);
                await badResponse.WriteAsJsonAsync(new { ex.Message });
                return badResponse;
            }
            var message = $"Create a professional social media post for this link with proper hastags. Do not include the link in the post or the title of the page. Only return the post, nothing else in the response. The entire post, including the title should not exceed 120 characters. {url}";
            var chatResponse = await _client.CompleteAsync(message);

            var response = req.CreateResponse(HttpStatusCode.OK);
            await response.WriteAsJsonAsync(chatResponse.Message.Text);

            return response;
        }
    }
}
