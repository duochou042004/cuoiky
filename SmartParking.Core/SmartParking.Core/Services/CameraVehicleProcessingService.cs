using Microsoft.AspNetCore.SignalR;
using SmartParking.Core.Hubs;
using System;
using System.IO;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace SmartParking.Core.Services
{
    public class CameraVehicleProcessingService
    {
        private readonly ParkingService _parkingService;
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly IConfiguration _configuration;
        private readonly IHubContext<ParkingHub> _hubContext;
        private readonly VehicleClassificationService _vehicleClassificationService;
        private readonly ILogger<CameraVehicleProcessingService> _logger;

        public CameraVehicleProcessingService(
            ParkingService parkingService,
            IHttpClientFactory httpClientFactory,
            IConfiguration configuration,
            IHubContext<ParkingHub> hubContext,
            VehicleClassificationService vehicleClassificationService,
            ILogger<CameraVehicleProcessingService> logger)
        {
            _parkingService = parkingService;
            _httpClientFactory = httpClientFactory;
            _configuration = configuration;
            _hubContext = hubContext;
            _vehicleClassificationService = vehicleClassificationService;
            _logger = logger;
        }

        public Task ProcessDetectionAsync(CameraDetectionWorkItem workItem, CancellationToken cancellationToken = default)
        {
            return workItem.IsEntryCamera
                ? ProcessVehicleEntry(workItem, cancellationToken)
                : ProcessVehicleExit(workItem, cancellationToken);
        }

        private async Task ProcessVehicleEntry(CameraDetectionWorkItem workItem, CancellationToken cancellationToken)
        {
            var existingVehicle = await _parkingService.GetParkedVehicleByLicensePlate(workItem.LicensePlate);
            if (existingVehicle != null)
            {
                _logger.LogInformation("Vehicle with license plate {LicensePlate} is already parked", workItem.LicensePlate);
                return;
            }

            var classification = await TryClassifyVehicle(workItem.CameraId, workItem.LicensePlate, cancellationToken);
            var vehicle = await _parkingService.ParkVehicle(workItem.LicensePlate, classification.VehicleType);

            _logger.LogInformation("Vehicle with license plate {LicensePlate} checked in with ID {VehicleId}", workItem.LicensePlate, vehicle.VehicleId);

            await _hubContext.Clients.All.SendAsync("ReceiveVehicleEntry", new
            {
                licensePlate = workItem.LicensePlate,
                vehicleId = vehicle.VehicleId,
                vehicleType = classification.VehicleType,
                entryTime = vehicle.EntryTime,
                slotId = vehicle.SlotId,
                cameraId = workItem.CameraId,
                classificationConfidence = classification.Confidence,
                classificationMethod = classification.Method,
                debugImage = classification.DebugFilePath != null ? Path.GetFileName(classification.DebugFilePath) : null
            }, cancellationToken);
        }

        private async Task ProcessVehicleExit(CameraDetectionWorkItem workItem, CancellationToken cancellationToken)
        {
            var vehicle = await _parkingService.GetParkedVehicleByLicensePlate(workItem.LicensePlate);
            if (vehicle == null)
            {
                _logger.LogInformation("No parked vehicle found with license plate {LicensePlate}", workItem.LicensePlate);
                return;
            }

            var debugFilePath = await CaptureFrameForAudit(workItem.CameraId, $"exit_{workItem.LicensePlate}", cancellationToken);
            var exitedVehicle = await _parkingService.ExitVehicle(vehicle.VehicleId);

            var parkingDuration = exitedVehicle.ExitTime.HasValue
                ? exitedVehicle.ExitTime.Value - exitedVehicle.EntryTime
                : TimeSpan.Zero;

            _logger.LogInformation("Vehicle with license plate {LicensePlate} checked out", workItem.LicensePlate);

            await _hubContext.Clients.All.SendAsync("ReceiveVehicleExit", new
            {
                licensePlate = workItem.LicensePlate,
                vehicleId = exitedVehicle.VehicleId,
                vehicleType = exitedVehicle.VehicleType,
                entryTime = exitedVehicle.EntryTime,
                exitTime = exitedVehicle.ExitTime,
                slotId = exitedVehicle.SlotId,
                cameraId = workItem.CameraId,
                parkingDuration = $"{parkingDuration.Hours}h {parkingDuration.Minutes}m {parkingDuration.Seconds}s",
                parkingDurationMinutes = parkingDuration.TotalMinutes,
                debugImage = debugFilePath != null ? Path.GetFileName(debugFilePath) : null
            }, cancellationToken);
        }

        private async Task<(string VehicleType, float Confidence, string Method, string? DebugFilePath)> TryClassifyVehicle(
            string cameraId,
            string licensePlate,
            CancellationToken cancellationToken)
        {
            var vehicleType = "UNKNOWN";
            var confidence = 0.0f;
            string? debugFilePath = null;

            try
            {
                var frameBytes = await GetRawFrame(cameraId, cancellationToken);
                if (frameBytes is { Length: > 0 })
                {
                    debugFilePath = await SaveDebugFrame(cameraId, null, frameBytes, cancellationToken);
                    var classificationResult = await _vehicleClassificationService.ClassifyVehicleFromFrame(frameBytes, cameraId, true);
                    if (classificationResult != null)
                    {
                        confidence = classificationResult.Confidence;
                        if (confidence > 0.65f)
                        {
                            vehicleType = classificationResult.VehicleType.ToUpperInvariant();
                        }
                    }
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogWarning(ex, "Could not classify vehicle type from camera frame for {CameraId}", cameraId);
            }

            var formatBasedType = InferVehicleTypeFromLicensePlate(licensePlate);
            if (vehicleType == "UNKNOWN" || confidence < 0.6f)
            {
                vehicleType = formatBasedType;
                return (vehicleType, confidence, "format", debugFilePath);
            }

            return (vehicleType, confidence, "ml", debugFilePath);
        }

        private async Task<string?> CaptureFrameForAudit(string cameraId, string suffix, CancellationToken cancellationToken)
        {
            try
            {
                var frameBytes = await GetRawFrame(cameraId, cancellationToken);
                return frameBytes is { Length: > 0 }
                    ? await SaveDebugFrame(cameraId, suffix, frameBytes, cancellationToken)
                    : null;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogWarning(ex, "Could not capture audit frame for {CameraId}", cameraId);
                return null;
            }
        }

        private async Task<byte[]?> GetRawFrame(string cameraId, CancellationToken cancellationToken)
        {
            var streamingApiUrl = _configuration.GetSection("StreamingAPI")["BaseUrl"] ?? "http://localhost:4051";
            var httpClient = _httpClientFactory.CreateClient("streaming-api");
            using var response = await httpClient.GetAsync($"cameras/{cameraId}/raw-frame", cancellationToken);
            return response.IsSuccessStatusCode
                ? await response.Content.ReadAsByteArrayAsync(cancellationToken)
                : null;
        }

        private static async Task<string> SaveDebugFrame(string cameraId, string? suffix, byte[] frameBytes, CancellationToken cancellationToken)
        {
            var debugDir = Path.Combine(Directory.GetCurrentDirectory(), "DebugFrames");
            Directory.CreateDirectory(debugDir);

            var safeSuffix = string.IsNullOrWhiteSpace(suffix) ? string.Empty : $"_{suffix.Replace("/", "_").Replace("\\", "_")}";
            var debugFilePath = Path.Combine(debugDir, $"{cameraId}_{DateTime.Now:yyyyMMdd_HHmmss}{safeSuffix}.jpg");
            await File.WriteAllBytesAsync(debugFilePath, frameBytes, cancellationToken);
            return debugFilePath;
        }

        private static string InferVehicleTypeFromLicensePlate(string licensePlate)
        {
            if (string.IsNullOrWhiteSpace(licensePlate))
            {
                return "MOTORBIKE";
            }

            if (licensePlate.Length >= 9 && licensePlate.Contains('-'))
            {
                return "CAR";
            }

            return licensePlate.Length <= 8 ? "MOTORBIKE" : "CAR";
        }
    }
}