using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using SmartParking.Core.Abstractions;
using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace SmartParking.Core.HealthChecks
{
    public class ExternalApiHealthCheck : IHealthCheck
    {
        private readonly ILicensePlateRecognitionClient _licensePlateClient;
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly IConfiguration _configuration;

        public ExternalApiHealthCheck(
            ILicensePlateRecognitionClient licensePlateClient,
            IHttpClientFactory httpClientFactory,
            IConfiguration configuration)
        {
            _licensePlateClient = licensePlateClient;
            _httpClientFactory = httpClientFactory;
            _configuration = configuration;
        }

        public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
        {
            var details = new Dictionary<string, object>();
            var failures = new List<string>();

            if (await _licensePlateClient.IsHealthyAsync(cancellationToken))
            {
                details["licensePlateApi"] = "healthy";
            }
            else
            {
                details["licensePlateApi"] = "unhealthy";
                failures.Add("License plate API is unavailable");
            }

            var streamingBaseUrl = _configuration.GetSection("StreamingAPI")["BaseUrl"];
            if (!string.IsNullOrWhiteSpace(streamingBaseUrl))
            {
                try
                {
                    var client = _httpClientFactory.CreateClient("streaming-api");
                    using var response = await client.GetAsync("health", cancellationToken);
                    if (response.IsSuccessStatusCode)
                    {
                        details["streamingApi"] = "healthy";
                    }
                    else
                    {
                        details["streamingApi"] = $"unhealthy:{(int)response.StatusCode}";
                        failures.Add("Streaming API returned an unhealthy status");
                    }
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    details["streamingApi"] = "unhealthy";
                    failures.Add($"Streaming API is unavailable: {ex.Message}");
                }
            }

            return failures.Count == 0
                ? HealthCheckResult.Healthy("External AI APIs are healthy", details)
                : HealthCheckResult.Unhealthy(string.Join("; ", failures), data: details);
        }
    }
}