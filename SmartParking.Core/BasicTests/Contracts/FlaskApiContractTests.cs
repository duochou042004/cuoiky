using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace BasicTests.Contracts;

public sealed class FlaskApiContractTests
{
    [Fact]
    public void License_plate_recognition_success_response_matches_dotnet_contract()
    {
        const string payload = """
        {
          "success": true,
          "licensePlate": "59A-12345"
        }
        """;

        var response = JsonSerializer.Deserialize<LicensePlateRecognitionContract>(payload, JsonOptions);

        Assert.NotNull(response);
        Assert.True(response.Success);
        Assert.Equal("59A-12345", response.LicensePlate);
    }

    [Fact]
    public void Streaming_detection_response_matches_dotnet_contract()
    {
        const string payload = """
        {
          "plates": [
            {
              "license_plate": "59A-12345",
              "confidence": 0.91,
              "bbox": [10, 20, 120, 40]
            }
          ],
          "timestamp": 1714567890.123,
          "detection_time": 0.05
        }
        """;

        var response = JsonSerializer.Deserialize<StreamingDetectionContract>(payload, JsonOptions);

        Assert.NotNull(response);
        Assert.Single(response.Plates);
        Assert.Equal("59A-12345", response.Plates[0].LicensePlate);
        Assert.Equal(4, response.Plates[0].Bbox.Count);
        Assert.InRange(response.Plates[0].Confidence, 0, 1);
    }

    [Fact]
    public async Task Live_flask_health_contract_is_valid_when_enabled()
    {
        if (!string.Equals(Environment.GetEnvironmentVariable("SMARTPARKING_RUN_FLASK_CONTRACT_TESTS"), "true", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        using var httpClient = new HttpClient
        {
            BaseAddress = new Uri(Environment.GetEnvironmentVariable("SMARTPARKING_LICENSE_PLATE_API") ?? "http://localhost:4050")
        };

        var response = await httpClient.GetFromJsonAsync<HealthContract>("/health", JsonOptions);

        Assert.NotNull(response);
        Assert.Equal("ok", response.Status);
    }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private sealed class LicensePlateRecognitionContract
    {
        public bool Success { get; set; }

        public string? LicensePlate { get; set; }

        public string? Error { get; set; }
    }

    private sealed class StreamingDetectionContract
    {
        public List<StreamingPlateContract> Plates { get; set; } = [];

        public double? Timestamp { get; set; }

        [JsonPropertyName("detection_time")]
        public double? DetectionTime { get; set; }
    }

    private sealed class StreamingPlateContract
    {
        [JsonPropertyName("license_plate")]
        public string? LicensePlate { get; set; }

        public double Confidence { get; set; }

        public List<int> Bbox { get; set; } = [];
    }

    private sealed class HealthContract
    {
        public string? Status { get; set; }

        public string? Message { get; set; }
    }
}