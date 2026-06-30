using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.DependencyInjection;
using SmartParking.Core.Hubs;
using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace SmartParking.Core.Services
{
    /// <summary>
    /// Background service for camera-based recognition.
    ///
    /// MODE: MANUAL CAPTURE (automatic detection intentionally disabled).
    ///
    /// Why manual: continuous polling of every camera produced duplicate detections
    /// (the same plate recognised across consecutive frames) and false check-ins/outs,
    /// and put constant load on the Python OCR service. The operator-driven
    /// "Capture Snapshot" flow (see CameraController.CaptureSnapshot) gives a single,
    /// deliberate recognition per vehicle and is the supported path.
    ///
    /// To re-enable automatic detection: restore a polling loop in <see cref="ExecuteAsync"/>
    /// that pulls frames from the streaming API, and reuse the PlateDetection
    /// debounce/similarity tracking below to suppress duplicates before emitting events.
    /// </summary>
    public class CameraMonitoringService : BackgroundService
    {
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly IConfiguration _configuration;
        private readonly IServiceScopeFactory _serviceScopeFactory;
        private readonly IHubContext<ParkingHub> _hubContext;
        private readonly ILogger<CameraMonitoringService> _logger;
        private readonly VehicleClassificationService _vehicleClassificationService;

        // Enhanced plate tracking with timestamps and similarity tracking
        private class PlateDetection
        {
            public string LicensePlate { get; set; }
            public DateTime DetectionTime { get; set; }
            public string VehicleType { get; set; }
            public float Confidence { get; set; }
        }

        private readonly Dictionary<string, List<PlateDetection>> _processedPlates = new();
        private readonly TimeSpan _detectionInterval = TimeSpan.FromSeconds(2);
        private readonly TimeSpan _plateExpirationTime = TimeSpan.FromSeconds(30); // Increased from 10 to 30 seconds
        private readonly TimeSpan _duplicateDetectionWindow = TimeSpan.FromSeconds(10); // Window to consider similar plates as duplicates

        public CameraMonitoringService(
            IHttpClientFactory httpClientFactory,
            IConfiguration configuration,
            IServiceScopeFactory serviceScopeFactory,
            IHubContext<ParkingHub> hubContext,
            VehicleClassificationService vehicleClassificationService,
            ILogger<CameraMonitoringService> logger)
        {
            _httpClientFactory = httpClientFactory;
            _configuration = configuration;
            _serviceScopeFactory = serviceScopeFactory;
            _hubContext = hubContext;
            _vehicleClassificationService = vehicleClassificationService;
            _logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("Camera Monitoring Service is starting.");
            _logger.LogInformation("Running in MANUAL CAPTURE MODE - automatic detection disabled");

            // In manual mode, we don't actively poll for detections
            // Instead, we just keep the service running to handle manual capture requests
            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    // Just clean up expired plates periodically
                    CleanupExpiredPlates();

                    // Wait longer between cleanup cycles since we're not actively monitoring
                    await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken);
                }
                catch (OperationCanceledException)
                {
                    // Normal cancellation, don't log as error
                    break;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error in Camera Monitoring Service");
                    await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
                }
            }

            _logger.LogInformation("Camera Monitoring Service is stopping.");
        }

        private async Task MonitorCameras(CancellationToken stoppingToken)
        {
            var client = _httpClientFactory.CreateClient();
            var streamApiUrl = _configuration.GetSection("StreamingAPI")["BaseUrl"];
            _logger.LogInformation($"Using Streaming API URL: {streamApiUrl}");

            try
            {
                // Get list of active cameras
                var camerasResponse = await client.GetAsync($"{streamApiUrl}/cameras", stoppingToken);
                if (!camerasResponse.IsSuccessStatusCode)
                {
                    _logger.LogWarning("Failed to get cameras from streaming API: {StatusCode}", camerasResponse.StatusCode);
                    return;
                }

                var camerasContent = await camerasResponse.Content.ReadAsStringAsync(stoppingToken);
                var camerasJson = JsonDocument.Parse(camerasContent).RootElement;

                if (!camerasJson.TryGetProperty("cameras", out var camerasArray))
                {
                    _logger.LogWarning("Invalid cameras response format");
                    return;
                }

                // Process each active camera
                foreach (var camera in camerasArray.EnumerateArray())
                {
                    if (!camera.TryGetProperty("id", out var cameraIdElement) ||
                        !camera.TryGetProperty("status", out var statusElement))
                    {
                        continue;
                    }

                    string cameraId = cameraIdElement.GetString();
                    string status = statusElement.GetString();

                    if (status != "RUNNING")
                    {
                        continue;
                    }

                    await ProcessCameraDetections(client, streamApiUrl, cameraId, stoppingToken);
                }

                // Clean up expired plates
                CleanupExpiredPlates();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error monitoring cameras");
            }
        }

        private async Task ProcessCameraDetections(HttpClient client, string streamApiUrl, string cameraId, CancellationToken stoppingToken)
        {
            try
            {
                var detectionsResponse = await client.GetAsync($"{streamApiUrl}/cameras/{cameraId}/detections", stoppingToken);
                if (!detectionsResponse.IsSuccessStatusCode)
                {
                    return;
                }

                var detectionsContent = await detectionsResponse.Content.ReadAsStringAsync(stoppingToken);
                var detections = JsonDocument.Parse(detectionsContent).RootElement;

                if (!detections.TryGetProperty("plates", out var platesElement) || platesElement.GetArrayLength() == 0)
                {
                    return;
                }

                // Initialize the list for this camera if it doesn't exist
                if (!_processedPlates.ContainsKey(cameraId))
                {
                    _processedPlates[cameraId] = new List<PlateDetection>();
                }

                // Process each detected license plate
                foreach (var plate in platesElement.EnumerateArray())
                {
                    if (plate.TryGetProperty("license_plate", out var licensePlateElement))
                    {
                        string licensePlate = licensePlateElement.GetString();
                        float confidence = 0.0f;

                        // Try to get confidence if available
                        if (plate.TryGetProperty("confidence", out var confidenceElement))
                        {
                            confidence = confidenceElement.GetSingle();
                        }

                        // Skip low confidence detections (below 0.4)
                        if (confidence < 0.4f && confidence > 0)
                        {
                            _logger.LogDebug($"Skipping low confidence detection: {licensePlate} ({confidence:F2})");
                            continue;
                        }

                        // Clean up the license plate - remove spaces, normalize dashes
                        licensePlate = NormalizeLicensePlate(licensePlate);

                        // Check for similar plates detected recently
                        var now = DateTime.UtcNow;
                        var similarPlate = FindSimilarPlate(cameraId, licensePlate, now);

                        if (similarPlate != null)
                        {
                            _logger.LogInformation($"Similar plate already processed: {licensePlate} is similar to {similarPlate.LicensePlate}");
                            continue;
                        }

                        // Determine if this is an entry or exit camera
                        bool isEntryCamera = cameraId.StartsWith("IN-");

                        // Process the vehicle
                        if (isEntryCamera)
                        {
                            // For entry cameras, check in the vehicle
                            await ProcessVehicleEntry(licensePlate, cameraId);
                        }
                        else
                        {
                            // For exit cameras, check out the vehicle
                            await ProcessVehicleExit(licensePlate, cameraId);
                        }

                        // Add to processed plates after processing
                        _processedPlates[cameraId].Add(new PlateDetection
                        {
                            LicensePlate = licensePlate,
                            DetectionTime = now,
                            Confidence = confidence
                        });
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error processing detections for camera {cameraId}");
            }
        }

        private void CleanupExpiredPlates()
        {
            var now = DateTime.UtcNow;
            foreach (var cameraId in _processedPlates.Keys.ToList())
            {
                // Remove only expired plate detections
                _processedPlates[cameraId] = _processedPlates[cameraId]
                    .Where(detection => (now - detection.DetectionTime) < _plateExpirationTime)
                    .ToList();

                // Log how many plates are still being tracked
                if (_processedPlates[cameraId].Count > 0)
                {
                    _logger.LogDebug($"Camera {cameraId} is tracking {_processedPlates[cameraId].Count} plates");
                }
            }
        }

        private async Task ProcessVehicleEntry(string licensePlate, string cameraId)
        {
            try
            {
                using (var scope = _serviceScopeFactory.CreateScope())
                {
                    var parkingService = scope.ServiceProvider.GetRequiredService<ParkingService>();

                    // Check if the vehicle is already parked
                    var parkedVehicles = await parkingService.GetParkedVehicles();
                    var existingVehicle = parkedVehicles.FirstOrDefault(v => v.LicensePlate == licensePlate);

                    if (existingVehicle != null)
                    {
                        _logger.LogInformation($"Vehicle with license plate {licensePlate} is already parked");
                        return;
                    }

                    // Lấy frame từ camera để phân loại phương tiện
                    string vehicleType = "UNKNOWN";
                    float classificationConfidence = 0.0f;
                    string debugFilePath = null;
                    try
                    {
                        // Lấy frame từ Python API (cùng frame đã dùng để nhận diện biển số)
                        var streamingApiUrl = _configuration.GetSection("StreamingAPI")["BaseUrl"] ?? "http://localhost:4051";
                        // Sử dụng raw-frame để lấy frame gốc không có chữ và đường viền
                        var frameUrl = $"{streamingApiUrl}/cameras/{cameraId}/raw-frame";

                        var httpClient = _httpClientFactory.CreateClient();
                        var response = await httpClient.GetAsync(frameUrl);

                        if (response.IsSuccessStatusCode)
                        {
                            var frameBytes = await response.Content.ReadAsByteArrayAsync();
                            if (frameBytes != null && frameBytes.Length > 0)
                            {
                                // Lưu frame vào file tạm thời để debug
                                string debugDir = Path.Combine(Directory.GetCurrentDirectory(), "DebugFrames");
                                if (!Directory.Exists(debugDir))
                                {
                                    Directory.CreateDirectory(debugDir);
                                }
                                debugFilePath = Path.Combine(debugDir, $"{cameraId}_{DateTime.Now:yyyyMMdd_HHmmss}.jpg");
                                await File.WriteAllBytesAsync(debugFilePath, frameBytes);
                                _logger.LogInformation($"Saved debug frame to {debugFilePath}");

                                // Phân loại phương tiện từ frame - đảm bảo xử lý đặc biệt cho webcam
                                var classificationResult = await _vehicleClassificationService.ClassifyVehicleFromFrame(frameBytes, cameraId, true);
                                if (classificationResult != null)
                                {
                                    // Lưu kết quả phân loại và độ tin cậy
                                    classificationConfidence = classificationResult.Confidence;

                                    // Chỉ sử dụng kết quả nếu độ tin cậy đủ cao
                                    if (classificationConfidence > 0.65f) // Tăng ngưỡng tin cậy để giảm false positives
                                    {
                                        vehicleType = classificationResult.VehicleType.ToUpper();
                                        _logger.LogInformation($"Classified vehicle as {vehicleType} with confidence {classificationConfidence}");
                                    }
                                    else
                                    {
                                        _logger.LogInformation($"Classification confidence too low: {classificationConfidence} for predicted type {classificationResult.VehicleType}");
                                    }
                                }
                            }
                        }
                        else
                        {
                            _logger.LogWarning($"Failed to get frame from camera {cameraId}: {response.StatusCode}");
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Error classifying vehicle type from camera frame");
                    }

                    // Improved vehicle classification logic
                    if (vehicleType == "UNKNOWN" || classificationConfidence < 0.6f) // Increased threshold
                    {
                        string formatBasedType = "UNKNOWN";
                        bool isMotorcyclePlate = false;

                        // Vietnamese motorcycle plates are typically shorter and don't have dashes
                        if (licensePlate.Length <= 8 && !licensePlate.Contains("-"))
                        {
                            formatBasedType = "MOTORBIKE";
                            isMotorcyclePlate = true;
                            _logger.LogInformation($"License plate format strongly suggests MOTORBIKE: {licensePlate}");
                        }
                        // Vietnamese car plates typically have a dash and are longer
                        else if (licensePlate.Length >= 9 && licensePlate.Contains("-"))
                        {
                            formatBasedType = "CAR";
                            _logger.LogInformation($"License plate format suggests CAR: {licensePlate}");
                        }
                        // For plates that don't match either pattern clearly
                        else
                        {
                            // Default to MOTORBIKE for plates with 8 or fewer characters
                            if (licensePlate.Length <= 8)
                            {
                                formatBasedType = "MOTORBIKE";
                                _logger.LogInformation($"License plate length suggests MOTORBIKE: {licensePlate}");
                            }
                            else
                            {
                                formatBasedType = "CAR";
                                _logger.LogInformation($"License plate length suggests CAR: {licensePlate}");
                            }
                        }

                        // If ML classification has some confidence but below threshold
                        if (vehicleType != "UNKNOWN" && classificationConfidence >= 0.4f)
                        {
                            // If ML and format-based classifications agree, use that type
                            if (vehicleType == formatBasedType)
                            {
                                _logger.LogInformation($"ML classification ({vehicleType}, {classificationConfidence:F2}) agrees with format-based classification. Using this type.");
                            }
                            else
                            {
                                // For motorcycle plates with strong format indicators, prefer the format-based classification
                                if (isMotorcyclePlate)
                                {
                                    _logger.LogInformation($"ML classification ({vehicleType}, {classificationConfidence:F2}) disagrees with strong motorcycle plate format. Using MOTORBIKE.");
                                    vehicleType = "MOTORBIKE";
                                }
                                // Otherwise, use ML if it's at least somewhat confident
                                else if (classificationConfidence >= 0.5f)
                                {
                                    _logger.LogInformation($"Using ML classification ({vehicleType}, {classificationConfidence:F2}) despite format suggestion of {formatBasedType}.");
                                }
                                else
                                {
                                    // If ML confidence is low, use format-based
                                    _logger.LogInformation($"ML classification ({vehicleType}, {classificationConfidence:F2}) has low confidence. Using format-based: {formatBasedType}.");
                                    vehicleType = formatBasedType;
                                }
                            }
                        }
                        else
                        {
                            // No ML classification or very low confidence, use format-based
                            vehicleType = formatBasedType;
                            _logger.LogInformation($"Using license plate format to classify as {vehicleType}: {licensePlate}");
                        }
                    }

                    // Park the vehicle
                    var vehicle = await parkingService.ParkVehicle(licensePlate, vehicleType);

                    _logger.LogInformation($"Vehicle with license plate {licensePlate} checked in with ID {vehicle.VehicleId}");

                    // Notify clients via SignalR with enhanced information
                    await _hubContext.Clients.All.SendAsync("ReceiveVehicleEntry", new
                    {
                        licensePlate = licensePlate,
                        vehicleId = vehicle.VehicleId,
                        vehicleType = vehicleType,
                        entryTime = vehicle.EntryTime,
                        slotId = vehicle.SlotId,
                        cameraId = cameraId,
                        classificationConfidence = classificationConfidence,
                        classificationMethod = vehicleType == "UNKNOWN" ? "fallback" : (classificationConfidence > 0.65f ? "ml" : "format"),
                        debugImage = debugFilePath != null ? Path.GetFileName(debugFilePath) : null
                    });
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error processing vehicle entry for license plate {licensePlate}");
            }
        }

        /// <summary>
        /// Normalizes a license plate by removing spaces and standardizing format
        /// </summary>
        private string NormalizeLicensePlate(string licensePlate)
        {
            if (string.IsNullOrEmpty(licensePlate))
                return licensePlate;

            // Remove spaces
            licensePlate = licensePlate.Replace(" ", "");

            // Ensure consistent dash format
            if (licensePlate.Contains("-"))
            {
                // Split by dash and rejoin to ensure only one dash
                var parts = licensePlate.Split('-', StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length >= 2)
                {
                    // For Vietnamese plates: first part is province code, second part is the number
                    return $"{parts[0]}-{string.Join("", parts.Skip(1))}";
                }
            }

            return licensePlate;
        }

        /// <summary>
        /// Finds a similar license plate that was recently detected
        /// </summary>
        private PlateDetection FindSimilarPlate(string cameraId, string licensePlate, DateTime now)
        {
            if (!_processedPlates.ContainsKey(cameraId) || _processedPlates[cameraId].Count == 0)
                return null;

            // Look for exact matches first
            var exactMatch = _processedPlates[cameraId]
                .Where(p => p.LicensePlate == licensePlate && (now - p.DetectionTime) < _duplicateDetectionWindow)
                .OrderByDescending(p => p.DetectionTime)
                .FirstOrDefault();

            if (exactMatch != null)
                return exactMatch;

            // Then look for similar plates (e.g., one character difference)
            return _processedPlates[cameraId]
                .Where(p => (now - p.DetectionTime) < _duplicateDetectionWindow &&
                       (IsOneCharacterDifferent(p.LicensePlate, licensePlate) ||
                        IsOneCharacterMissing(p.LicensePlate, licensePlate)))
                .OrderByDescending(p => p.DetectionTime)
                .FirstOrDefault();
        }

        /// <summary>
        /// Checks if two license plates differ by exactly one character
        /// </summary>
        private bool IsOneCharacterDifferent(string plate1, string plate2)
        {
            // If length difference is more than 1, they're not similar enough
            if (Math.Abs(plate1.Length - plate2.Length) > 1)
                return false;

            // If lengths are the same, check for one character difference
            if (plate1.Length == plate2.Length)
            {
                int differences = 0;
                for (int i = 0; i < plate1.Length; i++)
                {
                    if (plate1[i] != plate2[i])
                        differences++;

                    if (differences > 1)
                        return false;
                }

                return differences == 1;
            }

            return false;
        }

        /// <summary>
        /// Checks if one plate is the other with one character missing or added
        /// </summary>
        private bool IsOneCharacterMissing(string plate1, string plate2)
        {
            // Ensure plate1 is the shorter one
            if (plate1.Length > plate2.Length)
            {
                var temp = plate1;
                plate1 = plate2;
                plate2 = temp;
            }

            // If length difference is not exactly 1, they're not similar in this way
            if (plate2.Length - plate1.Length != 1)
                return false;

            // Check if plate1 is plate2 with one character removed
            for (int i = 0; i < plate2.Length; i++)
            {
                string shortened = plate2.Remove(i, 1);
                if (shortened == plate1)
                    return true;
            }

            return false;
        }

        private async Task ProcessVehicleExit(string licensePlate, string cameraId)
        {
            try
            {
                using (var scope = _serviceScopeFactory.CreateScope())
                {
                    var parkingService = scope.ServiceProvider.GetRequiredService<ParkingService>();

                    // Find the vehicle by license plate
                    var parkedVehicles = await parkingService.GetParkedVehicles();
                    var vehicle = parkedVehicles.FirstOrDefault(v => v.LicensePlate == licensePlate);

                    if (vehicle == null)
                    {
                        _logger.LogInformation($"No parked vehicle found with license plate {licensePlate}");
                        return;
                    }

                    // Capture exit frame for record keeping
                    string debugFilePath = null;
                    try
                    {
                        var streamingApiUrl = _configuration.GetSection("StreamingAPI")["BaseUrl"] ?? "http://localhost:4051";
                        var frameUrl = $"{streamingApiUrl}/cameras/{cameraId}/raw-frame";

                        var httpClient = _httpClientFactory.CreateClient();
                        var response = await httpClient.GetAsync(frameUrl);

                        if (response.IsSuccessStatusCode)
                        {
                            var frameBytes = await response.Content.ReadAsByteArrayAsync();
                            if (frameBytes != null && frameBytes.Length > 0)
                            {
                                // Save exit frame for record keeping
                                string debugDir = Path.Combine(Directory.GetCurrentDirectory(), "DebugFrames");
                                if (!Directory.Exists(debugDir))
                                {
                                    Directory.CreateDirectory(debugDir);
                                }
                                debugFilePath = Path.Combine(debugDir, $"{cameraId}_{DateTime.Now:yyyyMMdd_HHmmss}_exit_{licensePlate}.jpg");
                                await File.WriteAllBytesAsync(debugFilePath, frameBytes);
                                _logger.LogInformation($"Saved exit frame to {debugFilePath}");
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, $"Error capturing exit frame for license plate {licensePlate}");
                    }

                    // Check out the vehicle
                    var exitedVehicle = await parkingService.ExitVehicle(vehicle.VehicleId);

                    _logger.LogInformation($"Vehicle with license plate {licensePlate} checked out");

                    // Calculate parking duration
                    TimeSpan parkingDuration = exitedVehicle.ExitTime.Value - exitedVehicle.EntryTime;

                    // Notify clients via SignalR with enhanced information
                    await _hubContext.Clients.All.SendAsync("ReceiveVehicleExit", new
                    {
                        licensePlate = licensePlate,
                        vehicleId = exitedVehicle.VehicleId,
                        vehicleType = exitedVehicle.VehicleType,
                        entryTime = exitedVehicle.EntryTime,
                        exitTime = exitedVehicle.ExitTime,
                        slotId = exitedVehicle.SlotId,
                        cameraId = cameraId,
                        parkingDuration = $"{parkingDuration.Hours}h {parkingDuration.Minutes}m {parkingDuration.Seconds}s",
                        parkingDurationMinutes = parkingDuration.TotalMinutes,
                        debugImage = debugFilePath != null ? Path.GetFileName(debugFilePath) : null
                    });
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error processing vehicle exit for license plate {licensePlate}");
            }
        }
    }
}
