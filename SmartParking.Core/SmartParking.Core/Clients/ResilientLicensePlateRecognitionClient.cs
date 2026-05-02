using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using SmartParking.Core.Abstractions;
using System;
using System.IO;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace SmartParking.Core.Clients
{
    public class ResilientLicensePlateRecognitionClient : ILicensePlateRecognitionClient
    {
        private readonly HttpClient _httpClient;
        private readonly ILogger<ResilientLicensePlateRecognitionClient> _logger;
        private readonly int _maxRetries;
        private readonly int _circuitFailureThreshold;
        private readonly TimeSpan _circuitBreakDuration;
        private readonly object _circuitLock = new();
        private int _consecutiveFailures;
        private DateTimeOffset? _circuitOpenedUntil;

        public ResilientLicensePlateRecognitionClient(
            HttpClient httpClient,
            IConfiguration configuration,
            ILogger<ResilientLicensePlateRecognitionClient> logger)
        {
            _httpClient = httpClient;
            _logger = logger;
            _maxRetries = Math.Max(1, configuration.GetValue<int?>("AIService:Retry:MaxAttempts") ?? 3);
            _circuitFailureThreshold = Math.Max(1, configuration.GetValue<int?>("AIService:CircuitBreaker:FailureThreshold") ?? 5);
            _circuitBreakDuration = TimeSpan.FromSeconds(Math.Max(5, configuration.GetValue<int?>("AIService:CircuitBreaker:BreakDurationSeconds") ?? 30));
        }

        public async Task<string> RecognizeAsync(string imagePath, CancellationToken cancellationToken = default)
        {
            EnsureCircuitAllowsRequest();

            for (var attempt = 1; attempt <= _maxRetries; attempt++)
            {
                try
                {
                    using var content = new MultipartFormDataContent();
                    var bytes = await File.ReadAllBytesAsync(imagePath, cancellationToken);
                    content.Add(new ByteArrayContent(bytes), "image", Path.GetFileName(imagePath));

                    using var response = await _httpClient.PostAsync("recognize", content, cancellationToken);
                    response.EnsureSuccessStatusCode();

                    var responseContent = await response.Content.ReadAsStringAsync(cancellationToken);
                    var result = JsonSerializer.Deserialize<LicensePlateRecognitionResponse>(responseContent, new JsonSerializerOptions
                    {
                        PropertyNameCaseInsensitive = true
                    });

                    RecordSuccess();
                    return result?.LicensePlate ?? "Unknown";
                }
                catch (Exception ex) when (attempt < _maxRetries && ex is not OperationCanceledException)
                {
                    RecordFailure(ex);
                    var delay = TimeSpan.FromMilliseconds(200 * Math.Pow(2, attempt - 1));
                    _logger.LogWarning(ex, "License plate recognition attempt {Attempt}/{MaxRetries} failed. Retrying after {DelayMs} ms", attempt, _maxRetries, delay.TotalMilliseconds);
                    await Task.Delay(delay, cancellationToken);
                }
                catch (Exception ex)
                {
                    RecordFailure(ex);
                    throw;
                }
            }

            return "Unknown";
        }

        public async Task<bool> IsHealthyAsync(CancellationToken cancellationToken = default)
        {
            try
            {
                EnsureCircuitAllowsRequest();
                using var response = await _httpClient.GetAsync("health", cancellationToken);
                var healthy = response.IsSuccessStatusCode;

                if (healthy)
                {
                    RecordSuccess();
                }

                return healthy;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                RecordFailure(ex);
                _logger.LogWarning(ex, "License plate recognition API health check failed");
                return false;
            }
        }

        private void EnsureCircuitAllowsRequest()
        {
            lock (_circuitLock)
            {
                if (_circuitOpenedUntil.HasValue && _circuitOpenedUntil.Value > DateTimeOffset.UtcNow)
                {
                    throw new InvalidOperationException($"License plate recognition circuit is open until {_circuitOpenedUntil.Value:O}");
                }

                if (_circuitOpenedUntil.HasValue && _circuitOpenedUntil.Value <= DateTimeOffset.UtcNow)
                {
                    _circuitOpenedUntil = null;
                    _consecutiveFailures = 0;
                }
            }
        }

        private void RecordSuccess()
        {
            lock (_circuitLock)
            {
                _consecutiveFailures = 0;
                _circuitOpenedUntil = null;
            }
        }

        private void RecordFailure(Exception exception)
        {
            lock (_circuitLock)
            {
                _consecutiveFailures++;
                if (_consecutiveFailures >= _circuitFailureThreshold)
                {
                    _circuitOpenedUntil = DateTimeOffset.UtcNow.Add(_circuitBreakDuration);
                    _logger.LogError(exception, "License plate recognition circuit opened for {BreakDurationSeconds} seconds after {FailureCount} consecutive failures", _circuitBreakDuration.TotalSeconds, _consecutiveFailures);
                }
            }
        }

        private sealed class LicensePlateRecognitionResponse
        {
            public bool Success { get; set; }

            public string? LicensePlate { get; set; }

            public string? Error { get; set; }
        }
    }
}