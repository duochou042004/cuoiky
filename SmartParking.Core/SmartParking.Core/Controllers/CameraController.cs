using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.DependencyInjection;
using SmartParking.Core.Hubs;
using SmartParking.Core.Models;
using SmartParking.Core.Services;
using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;

namespace SmartParking.Core.Controllers
{
    [Route("api/cameras")]
    [ApiController]
    [Authorize]
    public class CameraController : ControllerBase
    {
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly IConfiguration _configuration;
        private readonly IServiceScopeFactory _serviceScopeFactory;
        private readonly IHubContext<ParkingHub> _hubContext;
        private readonly ILogger<CameraController> _logger;

        public CameraController(
            IHttpClientFactory httpClientFactory,
            IConfiguration configuration,
            IServiceScopeFactory serviceScopeFactory,
            IHubContext<ParkingHub> hubContext,
            ILogger<CameraController> logger)
        {
            _httpClientFactory = httpClientFactory;
            _configuration = configuration;
            _serviceScopeFactory = serviceScopeFactory;
            _hubContext = hubContext;
            _logger = logger;
        }

        [HttpGet]
        public async Task<IActionResult> GetCameras()
        {
            try
            {
                var client = _httpClientFactory.CreateClient();
                var streamApiUrl = _configuration.GetSection("StreamingAPI")["BaseUrl"];
                var response = await client.GetAsync($"{streamApiUrl}/cameras");

                if (response.IsSuccessStatusCode)
                {
                    var content = await response.Content.ReadAsStringAsync();
                    return Ok(JsonDocument.Parse(content).RootElement);
                }
                else
                {
                    return StatusCode((int)response.StatusCode, new { error = "Failed to get cameras from streaming API" });
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting cameras");
                return StatusCode(500, new { error = ex.Message });
            }
        }

        [HttpPost("{cameraId}/start")]
        public async Task<IActionResult> StartCamera(string cameraId, [FromBody] CameraStartRequest request)
        {
            try
            {
                var client = _httpClientFactory.CreateClient();
                var streamApiUrl = _configuration.GetSection("StreamingAPI")["BaseUrl"];
                var response = await client.PostAsJsonAsync($"{streamApiUrl}/cameras/{cameraId}/start", request);

                if (response.IsSuccessStatusCode)
                {
                    var content = await response.Content.ReadAsStringAsync();
                    await _hubContext.Clients.All.SendAsync("ReceiveCameraUpdate", new
                    {
                        cameraId = cameraId,
                        status = "RUNNING"
                    });
                    return Ok(JsonDocument.Parse(content).RootElement);
                }
                else
                {
                    return StatusCode((int)response.StatusCode, new { error = "Failed to start camera" });
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error starting camera {cameraId}");
                return StatusCode(500, new { error = ex.Message });
            }
        }

        [HttpPost("{cameraId}/stop")]
        public async Task<IActionResult> StopCamera(string cameraId)
        {
            try
            {
                var client = _httpClientFactory.CreateClient();
                var streamApiUrl = _configuration.GetSection("StreamingAPI")["BaseUrl"];
                var response = await client.PostAsync($"{streamApiUrl}/cameras/{cameraId}/stop", null);

                if (response.IsSuccessStatusCode)
                {
                    var content = await response.Content.ReadAsStringAsync();
                    await _hubContext.Clients.All.SendAsync("ReceiveCameraUpdate", new
                    {
                        cameraId = cameraId,
                        status = "STOPPED"
                    });
                    return Ok(JsonDocument.Parse(content).RootElement);
                }
                else
                {
                    return StatusCode((int)response.StatusCode, new { error = "Failed to stop camera" });
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error stopping camera {cameraId}");
                return StatusCode(500, new { error = ex.Message });
            }
        }

        [HttpGet("{cameraId}/detections")]
        public async Task<IActionResult> GetDetections(string cameraId)
        {
            try
            {
                var client = _httpClientFactory.CreateClient();
                var streamApiUrl = _configuration.GetSection("StreamingAPI")["BaseUrl"];
                var response = await client.GetAsync($"{streamApiUrl}/cameras/{cameraId}/detections");

                if (response.IsSuccessStatusCode)
                {
                    var content = await response.Content.ReadAsStringAsync();
                    var detections = JsonDocument.Parse(content).RootElement;

                    // Process detections and check for vehicle entry/exit
                    await ProcessDetections(cameraId, detections);

                    return Ok(detections);
                }
                else
                {
                    return StatusCode((int)response.StatusCode, new { error = "Failed to get detections" });
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error getting detections for camera {cameraId}");
                return StatusCode(500, new { error = ex.Message });
            }
        }

        [HttpGet("{cameraId}/metrics")]
        public async Task<IActionResult> GetMetrics(string cameraId)
        {
            try
            {
                var client = _httpClientFactory.CreateClient();
                var streamApiUrl = _configuration.GetSection("StreamingAPI")["BaseUrl"];
                var response = await client.GetAsync($"{streamApiUrl}/cameras/{cameraId}/metrics");

                if (response.IsSuccessStatusCode)
                {
                    var content = await response.Content.ReadAsStringAsync();
                    return Ok(JsonDocument.Parse(content).RootElement);
                }
                else
                {
                    return StatusCode((int)response.StatusCode, new { error = "Failed to get metrics" });
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error getting metrics for camera {cameraId}");
                return StatusCode(500, new { error = ex.Message });
            }
        }

        [HttpGet("metrics")]
        public async Task<IActionResult> GetAllMetrics()
        {
            try
            {
                var client = _httpClientFactory.CreateClient();
                var streamApiUrl = _configuration.GetSection("StreamingAPI")["BaseUrl"];
                var response = await client.GetAsync($"{streamApiUrl}/metrics");

                if (response.IsSuccessStatusCode)
                {
                    var content = await response.Content.ReadAsStringAsync();
                    return Ok(JsonDocument.Parse(content).RootElement);
                }
                else
                {
                    return StatusCode((int)response.StatusCode, new { error = "Failed to get metrics" });
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting all metrics");
                return StatusCode(500, new { error = ex.Message });
            }
        }

        [HttpPost("{cameraId}/capture-snapshot")]
        public async Task<IActionResult> CaptureSnapshot(string cameraId)
        {
            try
            {
                _logger.LogInformation($"Manual snapshot requested for camera {cameraId}");

                // Get the raw frame from the camera
                var client = _httpClientFactory.CreateClient();
                var streamApiUrl = _configuration.GetSection("StreamingAPI")["BaseUrl"];
                var frameResponse = await client.GetAsync($"{streamApiUrl}/cameras/{cameraId}/raw-frame");

                if (!frameResponse.IsSuccessStatusCode)
                {
                    return StatusCode((int)frameResponse.StatusCode, new { error = "Failed to get frame from camera" });
                }

                var frameBytes = await frameResponse.Content.ReadAsByteArrayAsync();
                if (frameBytes == null || frameBytes.Length == 0)
                {
                    return BadRequest(new { error = "Received empty frame from camera" });
                }

                // Save the frame for debugging
                string debugDir = Path.Combine(Directory.GetCurrentDirectory(), "DebugFrames");
                if (!Directory.Exists(debugDir))
                {
                    Directory.CreateDirectory(debugDir);
                }

                string timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
                string debugFilePath = Path.Combine(debugDir, $"{cameraId}_{timestamp}_manual.jpg");
                await System.IO.File.WriteAllBytesAsync(debugFilePath, frameBytes);

                // Get license plate from the frame
                var licensePlateApiUrl = _configuration.GetSection("LicensePlateAPI")["BaseUrl"] ?? "http://localhost:4050";
                using var content = new MultipartFormDataContent();
                var fileContent = new ByteArrayContent(frameBytes);
                content.Add(fileContent, "image", $"{cameraId}_{timestamp}.jpg");

                var lpResponse = await client.PostAsync($"{licensePlateApiUrl}/recognize", content);
                if (!lpResponse.IsSuccessStatusCode)
                {
                    return StatusCode((int)lpResponse.StatusCode, new { error = "Failed to recognize license plate", debugImage = Path.GetFileName(debugFilePath) });
                }

                var lpContent = await lpResponse.Content.ReadAsStringAsync();
                var lpResult = JsonSerializer.Deserialize<LicensePlateResponse>(lpContent, new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                });

                string licensePlate = lpResult?.LicensePlate ?? "Unknown";

                // Classify the vehicle using ML.NET
                using (var scope = _serviceScopeFactory.CreateScope())
                {
                    var vehicleClassificationService = scope.ServiceProvider.GetRequiredService<VehicleClassificationService>();
                    var classificationResult = await vehicleClassificationService.ClassifyVehicleFromFrame(frameBytes, cameraId, true);

                    string vehicleType = "UNKNOWN";
                    float confidence = 0.0f;
                    string classificationMethod = "unknown";

                    if (classificationResult != null)
                    {
                        vehicleType = classificationResult.VehicleType;
                        confidence = classificationResult.Confidence;
                        classificationMethod = confidence > 0.65f ? "ml" : "low-confidence-ml";
                    }

                    // If ML classification failed or has low confidence, use license plate format
                    if (vehicleType == "UNKNOWN" || confidence < 0.5f)
                    {
                        bool isMotorcyclePlate = false;
                        string formatBasedType = "UNKNOWN";

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
                        if (vehicleType != "UNKNOWN" && confidence >= 0.4f)
                        {
                            // If ML and format-based classifications agree, use that type
                            if (vehicleType == formatBasedType)
                            {
                                _logger.LogInformation($"ML classification ({vehicleType}, {confidence:F2}) agrees with format-based classification. Using this type.");
                                classificationMethod = "ml-format-agreement";
                            }
                            else
                            {
                                // For motorcycle plates with strong format indicators, prefer the format-based classification
                                if (isMotorcyclePlate)
                                {
                                    _logger.LogInformation($"ML classification ({vehicleType}, {confidence:F2}) disagrees with strong motorcycle plate format. Using MOTORBIKE.");
                                    vehicleType = "MOTORBIKE";
                                    classificationMethod = "format-override";
                                }
                                // Otherwise, use ML if it's at least somewhat confident
                                else if (confidence >= 0.5f)
                                {
                                    _logger.LogInformation($"Using ML classification ({vehicleType}, {confidence:F2}) despite format suggestion of {formatBasedType}.");
                                    classificationMethod = "ml-preferred";
                                }
                                else
                                {
                                    // If ML confidence is low, use format-based
                                    _logger.LogInformation($"ML classification ({vehicleType}, {confidence:F2}) has low confidence. Using format-based: {formatBasedType}.");
                                    vehicleType = formatBasedType;
                                    classificationMethod = "format-fallback";
                                }
                            }
                        }
                        else
                        {
                            // No ML classification or very low confidence, use format-based
                            vehicleType = formatBasedType;
                            classificationMethod = "format-only";
                            _logger.LogInformation($"Using license plate format to classify as {vehicleType}: {licensePlate}");
                        }
                    }

                    // Check if the vehicle is already parked (for potential exit)
                    var parkingService = scope.ServiceProvider.GetRequiredService<ParkingService>();
                    var existingVehicle = await parkingService.GetParkedVehicleByLicensePlate(licensePlate);

                    bool isEntryCamera = cameraId.StartsWith("IN-");
                    bool isExitCamera = cameraId.StartsWith("OUT-");
                    bool canCheckIn = isEntryCamera && existingVehicle == null;
                    bool canCheckOut = existingVehicle != null;

                    // Send the result to all clients via SignalR
                    await _hubContext.Clients.All.SendAsync("ReceiveManualSnapshot", new
                    {
                        cameraId = cameraId,
                        timestamp = DateTime.Now,
                        licensePlate = licensePlate,
                        vehicleType = vehicleType,
                        confidence = confidence,
                        classificationMethod = classificationMethod,
                        debugImage = Path.GetFileName(debugFilePath),
                        isParked = existingVehicle != null,
                        vehicleId = existingVehicle?.VehicleId,
                        slotId = existingVehicle?.SlotId,
                        entryTime = existingVehicle?.EntryTime,
                        canCheckIn = canCheckIn,
                        canCheckOut = canCheckOut,
                        suggestedAction = existingVehicle != null ? "checkout" : "checkin"
                    });

                    return Ok(new
                    {
                        cameraId = cameraId,
                        timestamp = DateTime.Now,
                        licensePlate = licensePlate,
                        vehicleType = vehicleType,
                        confidence = confidence,
                        classificationMethod = classificationMethod,
                        debugImage = Path.GetFileName(debugFilePath),
                        isParked = existingVehicle != null,
                        vehicleId = existingVehicle?.VehicleId,
                        slotId = existingVehicle?.SlotId,
                        entryTime = existingVehicle?.EntryTime,
                        canCheckIn = canCheckIn,
                        canCheckOut = canCheckOut,
                        suggestedAction = existingVehicle != null ? "checkout" : "checkin"
                    });
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error capturing snapshot from camera {cameraId}");
                return StatusCode(500, new { error = ex.Message });
            }
        }

        [HttpPost("{cameraId}/process-vehicle")]
        public async Task<IActionResult> ProcessVehicle(string cameraId, [FromBody] ProcessVehicleRequest request)
        {
            try
            {
                _logger.LogInformation($"Manual vehicle processing requested for camera {cameraId}: {request.Action} for {request.LicensePlate}");

                using (var scope = _serviceScopeFactory.CreateScope())
                {
                    var parkingService = scope.ServiceProvider.GetRequiredService<ParkingService>();

                    // Process based on the requested action
                    if (request.Action.ToLower() == "checkin")
                    {
                        // Check if the vehicle is already parked
                        var existingVehicle = await parkingService.GetParkedVehicleByLicensePlate(request.LicensePlate);

                        if (existingVehicle != null)
                        {
                            return BadRequest(new {
                                error = $"Vehicle with license plate {request.LicensePlate} is already parked in slot {existingVehicle.SlotId}",
                                existingVehicle = existingVehicle
                            });
                        }

                        // Park the vehicle
                        var vehicle = await parkingService.ParkVehicle(request.LicensePlate, request.VehicleType);

                        _logger.LogInformation($"Vehicle with license plate {request.LicensePlate} manually checked in with ID {vehicle.VehicleId}");

                        // Notify clients via SignalR
                        await _hubContext.Clients.All.SendAsync("ReceiveVehicleEntry", new
                        {
                            licensePlate = request.LicensePlate,
                            vehicleId = vehicle.VehicleId,
                            vehicleType = request.VehicleType,
                            entryTime = vehicle.EntryTime,
                            slotId = vehicle.SlotId,
                            cameraId = cameraId,
                            classificationConfidence = request.Confidence,
                            classificationMethod = request.ClassificationMethod,
                            debugImage = request.DebugImage,
                            manualEntry = true
                        });

                        return Ok(new
                        {
                            success = true,
                            message = $"Vehicle with license plate {request.LicensePlate} checked in successfully",
                            vehicleId = vehicle.VehicleId,
                            slotId = vehicle.SlotId
                        });
                    }
                    else if (request.Action.ToLower() == "checkout")
                    {
                        // Find the vehicle by license plate
                        var vehicle = await parkingService.GetParkedVehicleByLicensePlate(request.LicensePlate);

                        if (vehicle == null)
                        {
                            return BadRequest(new { error = $"No parked vehicle found with license plate {request.LicensePlate}" });
                        }

                        // For monthly registered vehicles, automatically check out
                        if (vehicle.IsMonthlyRegistered)
                        {
                            // Check out the vehicle
                            var exitedVehicle = await parkingService.ExitVehicle(vehicle.VehicleId);

                            _logger.LogInformation($"Monthly registered vehicle with license plate {request.LicensePlate} manually checked out");

                            // Calculate parking duration
                            TimeSpan parkingDuration = exitedVehicle.ExitTime.Value - exitedVehicle.EntryTime;

                            // Notify clients via SignalR
                            await _hubContext.Clients.All.SendAsync("ReceiveVehicleExit", new
                            {
                                licensePlate = request.LicensePlate,
                                vehicleId = exitedVehicle.VehicleId,
                                vehicleType = exitedVehicle.VehicleType,
                                entryTime = exitedVehicle.EntryTime,
                                exitTime = exitedVehicle.ExitTime,
                                slotId = exitedVehicle.SlotId,
                                cameraId = cameraId,
                                parkingDuration = $"{parkingDuration.Hours}h {parkingDuration.Minutes}m {parkingDuration.Seconds}s",
                                parkingDurationMinutes = parkingDuration.TotalMinutes,
                                debugImage = request.DebugImage,
                                manualExit = true,
                                isMonthlyRegistered = true,
                                automaticCheckout = true
                            });

                            return Ok(new
                            {
                                success = true,
                                message = $"Monthly registered vehicle with license plate {request.LicensePlate} checked out successfully",
                                parkingDuration = $"{parkingDuration.Hours}h {parkingDuration.Minutes}m {parkingDuration.Seconds}s",
                                parkingDurationMinutes = parkingDuration.TotalMinutes,
                                isMonthlyRegistered = true
                            });
                        }
                        else
                        {
                            // For casual vehicles, redirect to payment process
                            // Calculate parking fee
                            var parkingFeeService = scope.ServiceProvider.GetRequiredService<ParkingFeeService>();
                            var exitTime = DateTime.Now;
                            var fee = parkingFeeService.CalculateParkingFee(
                                vehicle.VehicleType,
                                vehicle.EntryTime,
                                exitTime,
                                false // Not monthly registered
                            );

                            // Calculate parking duration
                            TimeSpan parkingDuration = exitTime - vehicle.EntryTime;

                            // Notify clients via SignalR that a vehicle is at the exit and needs payment
                            await _hubContext.Clients.All.SendAsync("ReceiveVehicleAtExit", new
                            {
                                licensePlate = request.LicensePlate,
                                vehicleId = vehicle.VehicleId,
                                vehicleType = vehicle.VehicleType,
                                entryTime = vehicle.EntryTime,
                                slotId = vehicle.SlotId,
                                cameraId = cameraId,
                                parkingDuration = $"{parkingDuration.Hours}h {parkingDuration.Minutes}m {parkingDuration.Seconds}s",
                                parkingDurationMinutes = parkingDuration.TotalMinutes,
                                parkingFee = fee,
                                debugImage = request.DebugImage,
                                isMonthlyRegistered = false,
                                requiresPayment = true
                            });

                            return Ok(new
                            {
                                success = true,
                                message = $"Vehicle with license plate {request.LicensePlate} requires payment before checkout",
                                parkingDuration = $"{parkingDuration.Hours}h {parkingDuration.Minutes}m {parkingDuration.Seconds}s",
                                parkingDurationMinutes = parkingDuration.TotalMinutes,
                                parkingFee = fee,
                                requiresPayment = true,
                                isMonthlyRegistered = false,
                                vehicleId = vehicle.VehicleId
                            });
                        }
                    }
                    else
                    {
                        return BadRequest(new { error = $"Invalid action: {request.Action}. Must be 'checkin' or 'checkout'." });
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error processing vehicle for camera {cameraId}");
                return StatusCode(500, new { error = ex.Message });
            }
        }

        private async Task ProcessDetections(string cameraId, JsonElement detections)
        {
            try
            {
                // Check if there are any plates detected
                if (!detections.TryGetProperty("plates", out var platesElement) || platesElement.GetArrayLength() == 0)
                {
                    return;
                }

                // Process each detected license plate
                foreach (var plate in platesElement.EnumerateArray())
                {
                    if (plate.TryGetProperty("license_plate", out var licensePlateElement))
                    {
                        string licensePlate = licensePlateElement.GetString();

                        // Determine if this is an entry or exit camera
                        bool isEntryCamera = cameraId.StartsWith("IN-");

                        if (isEntryCamera)
                        {
                            // For entry cameras, check in the vehicle
                            await ProcessVehicleEntry(licensePlate);
                        }
                        else
                        {
                            // For exit cameras, check out the vehicle
                            await ProcessVehicleExit(licensePlate);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error processing detections for camera {cameraId}");
            }
        }

        private async Task ProcessVehicleEntry(string licensePlate)
        {
            try
            {
                using (var scope = _serviceScopeFactory.CreateScope())
                {
                    var parkingService = scope.ServiceProvider.GetRequiredService<ParkingService>();

                    // Check if the vehicle is already parked
                    var existingVehicle = await parkingService.GetParkedVehicleByLicensePlate(licensePlate);

                    if (existingVehicle != null)
                    {
                        _logger.LogInformation($"Vehicle with license plate {licensePlate} is already parked");
                        return;
                    }

                    // Determine vehicle type based on license plate format or other logic
                    // This is a simplified example - in a real system, you would use the ML model
                    string vehicleType = licensePlate.Length <= 8 ? "MOTORBIKE" : "CAR";

                    // Park the vehicle
                    var vehicle = await parkingService.ParkVehicle(licensePlate, vehicleType);

                    _logger.LogInformation($"Vehicle with license plate {licensePlate} checked in with ID {vehicle.VehicleId}");

                    // Notify clients via SignalR
                    await _hubContext.Clients.All.SendAsync("ReceiveVehicleEntry", new
                    {
                        licensePlate = licensePlate,
                        vehicleId = vehicle.VehicleId,
                        vehicleType = vehicleType,
                        entryTime = vehicle.EntryTime,
                        slotId = vehicle.SlotId
                    });
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error processing vehicle entry for license plate {licensePlate}");
            }
        }

        private async Task ProcessVehicleExit(string licensePlate)
        {
            try
            {
                using (var scope = _serviceScopeFactory.CreateScope())
                {
                    var parkingService = scope.ServiceProvider.GetRequiredService<ParkingService>();

                    // Find the vehicle by license plate
                    var vehicle = await parkingService.GetParkedVehicleByLicensePlate(licensePlate);

                    if (vehicle == null)
                    {
                        _logger.LogInformation($"No parked vehicle found with license plate {licensePlate}");
                        return;
                    }

                    // For monthly registered vehicles, automatically check out
                    if (vehicle.IsMonthlyRegistered)
                    {
                        _logger.LogInformation($"Monthly registered vehicle with license plate {licensePlate} detected. Processing automatic checkout.");

                        // Check out the vehicle
                        var exitedVehicle = await parkingService.ExitVehicle(vehicle.VehicleId);

                        _logger.LogInformation($"Monthly registered vehicle with license plate {licensePlate} automatically checked out");

                        // Notify clients via SignalR
                        await _hubContext.Clients.All.SendAsync("ReceiveVehicleExit", new
                        {
                            licensePlate = licensePlate,
                            vehicleId = exitedVehicle.VehicleId,
                            vehicleType = exitedVehicle.VehicleType,
                            entryTime = exitedVehicle.EntryTime,
                            exitTime = exitedVehicle.ExitTime,
                            slotId = exitedVehicle.SlotId,
                            isMonthlyRegistered = true,
                            automaticCheckout = true
                        });
                    }
                    else
                    {
                        // For casual vehicles, just notify that the vehicle is ready for checkout
                        // The actual checkout will happen through the payment process
                        _logger.LogInformation($"Casual vehicle with license plate {licensePlate} detected at exit. Notifying for payment and checkout.");

                        // Notify clients via SignalR that a vehicle is at the exit and needs payment
                        await _hubContext.Clients.All.SendAsync("ReceiveVehicleAtExit", new
                        {
                            licensePlate = licensePlate,
                            vehicleId = vehicle.VehicleId,
                            vehicleType = vehicle.VehicleType,
                            entryTime = vehicle.EntryTime,
                            slotId = vehicle.SlotId,
                            isMonthlyRegistered = false,
                            requiresPayment = true
                        });
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error processing vehicle exit for license plate {licensePlate}");
            }
        }
    }

    public class CameraStartRequest
    {
        public int CameraIndex { get; set; } = 0;
    }

    public class LicensePlateResponse
    {
        public string LicensePlate { get; set; }
        public bool Success { get; set; }
    }

    public class ProcessVehicleRequest
    {
        public string LicensePlate { get; set; }
        public string VehicleType { get; set; }
        public string Action { get; set; } // "checkin" or "checkout"
        public float Confidence { get; set; }
        public string ClassificationMethod { get; set; }
        public string DebugImage { get; set; }
    }
}
