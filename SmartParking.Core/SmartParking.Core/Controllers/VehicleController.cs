using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using SmartParking.Core.Models;
using SmartParking.Core.Services;
using SmartParking.Core.Data;
using System.IO;
using MongoDB.Driver;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace SmartParking.Core.Controllers
{
    [Route("api/vehicle")]
    [ApiController]
    [Authorize]
    public class VehicleController : ControllerBase
    {
        private readonly MLModelPrediction _mlModelPrediction;
        private readonly MongoDBContext _context;
        private readonly ILogger<VehicleController> _logger;
        private readonly ParkingService _parkingService;

        public VehicleController(
            MongoDBContext context,
            ILogger<VehicleController> logger,
            ParkingService parkingService,
            MLModelPrediction mlModelPrediction)
        {
            _context = context;
            _logger = logger;
            _parkingService = parkingService;
            _mlModelPrediction = mlModelPrediction;
        }

        [HttpPost("analyze")]
        [Consumes("multipart/form-data")]
        public IActionResult AnalyzeVehicle([FromForm] VehicleUploadDto uploadDto)
        {
            if (uploadDto?.Image == null || uploadDto.Image.Length == 0)
            {
                return BadRequest("No file uploaded.");
            }

            // Tạo thư mục Uploads nếu chưa tồn tại
            string uploadsFolder = Path.Combine(Directory.GetCurrentDirectory(), "Uploads");
            if (!Directory.Exists(uploadsFolder))
            {
                Directory.CreateDirectory(uploadsFolder);
            }

            string filePath = Path.Combine(uploadsFolder, uploadDto.Image.FileName);
            using (var stream = new FileStream(filePath, FileMode.Create))
            {
                uploadDto.Image.CopyTo(stream);
            }

            // Gọi ML.NET để phân loại xe (giả sử model đã được huấn luyện và lưu tại MLModels/VehicleClassification.zip)
            var prediction = _mlModelPrediction.PredictVehicleType(filePath);

            return Ok(new { vehicleType = prediction.PredictedLabel, confidence = prediction.GetHighestScore() });
        }

        [HttpGet("exited-count")]
        public async Task<IActionResult> GetExitedVehiclesCount([FromQuery] DateTime? startDate)
        {
            try
            {
                _logger.LogInformation("Getting count of exited vehicles");

                // Set default date to today if not provided
                var start = startDate ?? DateTime.Today;

                // Build filter for exited vehicles
                var builder = Builders<Vehicle>.Filter;
                var filter = builder.And(
                    builder.Eq(v => v.Status, "EXITED"),
                    builder.Gte(v => v.ExitTime, start)
                );

                // Count exited vehicles
                var count = await _context.Vehicles.CountDocumentsAsync(filter);

                _logger.LogInformation($"Found {count} exited vehicles since {start}");

                return Ok(count);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting exited vehicles count");
                return StatusCode(500, new { error = ex.Message });
            }
        }

        [HttpGet("history")]
        public async Task<IActionResult> GetVehicleHistory(
            [FromQuery] DateTime? startDate,
            [FromQuery] DateTime? endDate,
            [FromQuery] string? licensePlate,
            [FromQuery] string? vehicleType,
            [FromQuery] string? status,
            [FromQuery] string? slotId)
        {
            try
            {
                _logger.LogInformation("Getting vehicle history with filters");

                // Set default date range if not provided
                var start = startDate ?? DateTime.Now.AddDays(-7);
                var end = endDate ?? DateTime.Now;

                // Build filter
                var builder = Builders<Vehicle>.Filter;
                var filters = new List<FilterDefinition<Vehicle>>();

                // Date range filter
                filters.Add(builder.Gte(v => v.EntryTime, start));
                filters.Add(builder.Lte(v => v.EntryTime, end));

                // Optional filters
                if (!string.IsNullOrEmpty(licensePlate))
                {
                    filters.Add(builder.Regex(v => v.LicensePlate, new MongoDB.Bson.BsonRegularExpression(licensePlate, "i")));
                }

                if (!string.IsNullOrEmpty(vehicleType) && vehicleType != "ALL")
                {
                    filters.Add(builder.Eq(v => v.VehicleType, vehicleType));
                }

                if (!string.IsNullOrEmpty(status) && status != "ALL")
                {
                    filters.Add(builder.Eq(v => v.Status, status));
                }

                if (!string.IsNullOrEmpty(slotId))
                {
                    filters.Add(builder.Eq(v => v.SlotId, slotId));
                }

                // Combine all filters
                var filter = builder.And(filters);

                // Get vehicles
                var vehicles = await _context.Vehicles
                    .Find(filter)
                    .SortByDescending(v => v.EntryTime)
                    .ToListAsync();

                _logger.LogInformation($"Found {vehicles.Count} vehicles matching the criteria");

                return Ok(vehicles);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting vehicle history");
                return StatusCode(500, new { error = ex.Message });
            }
        }

        [HttpGet("{vehicleId}")]
        public async Task<IActionResult> GetVehicleById(string vehicleId)
        {
            try
            {
                var vehicle = await _parkingService.GetVehicleById(vehicleId);
                if (vehicle == null)
                {
                    return NotFound(new { error = $"Vehicle with ID {vehicleId} not found" });
                }
                return Ok(vehicle);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error getting vehicle with ID {vehicleId}");
                return StatusCode(500, new { error = ex.Message });
            }
        }
    }

    public class VehicleUploadDto
    {
        public IFormFile? Image { get; set; }
    }
}
