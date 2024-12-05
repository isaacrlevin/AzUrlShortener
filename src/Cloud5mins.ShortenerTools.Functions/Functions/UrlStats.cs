using Cloud5mins.ShortenerTools.Core.Domain;
using Cloud5mins.ShortenerTools.Core.Messages;
using Microsoft.AspNetCore.Http;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;
using System.Net;
using System.Text.Json;

namespace Cloud5mins.ShortenerTools.Functions.Functions
{
    public class UrlStats
    {
        private readonly ILogger _logger;
        private readonly ShortenerSettings _settings;

        public UrlStats(ILoggerFactory loggerFactory, ShortenerSettings settings)
        {
            _logger = loggerFactory.CreateLogger<UrlStats>();
            _settings = settings;
        }

        [Function("UrlStats")]
        public async Task<HttpResponseData> Run(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", "post", Route = "api/UrlStats")] HttpRequestData req,
        ExecutionContext context)
        {
            _logger.LogInformation($"HTTP trigger: Stats");

            string userId = string.Empty;
            UrlClickStatsRequest input;
            var result = new ClickStatsEntityList();

            try
            {
                StorageTableHelper stgHelper = new StorageTableHelper(_settings.DataStorage);
                if (req != null)
                {
                    using (var reader = new StreamReader(req.Body))
                    {
                        var strBody = await reader.ReadToEndAsync();
                        input = JsonSerializer.Deserialize<UrlClickStatsRequest>(strBody, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                        if (input == null || string.IsNullOrEmpty(input.Vanity))
                        {
                            result.ClickStatsList = await stgHelper.GetAllStats();
                        }
                        else
                        {
                            result.ClickStatsList = await stgHelper.GetAllStatsByVanity(input.Vanity);
                        }
                    }
                }
                else
                {
                    return req.CreateResponse(HttpStatusCode.NotFound);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "An unexpected error was encountered.");
                var badRequest = req.CreateResponse(HttpStatusCode.BadRequest);
                await badRequest.WriteAsJsonAsync(new { Message = $"{ex.Message}" });
                return badRequest;
            }

            var response = req.CreateResponse(HttpStatusCode.OK);
            await response.WriteAsJsonAsync(result);
            return response;
        }
    }
}
