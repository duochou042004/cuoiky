using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using SmartParking.Core.Models;
using SmartParking.Core.Services;
using System;
using System.Threading.Tasks;
using System.IO;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.DependencyInjection;

namespace SmartParking.Core.Controllers
{
    [Route("api/vehicle")]
    [ApiController]
    [Authorize]
    public class CheckInOutController : ControllerBase
    {
        private readonly ParkingService _parkingService;
        private readonly LicensePlateService _licensePlateService;
        private readonly ILogger<CheckInOutController> _logger;

        public CheckInOutController(ParkingService parkingService, LicensePlateService licensePlateService, ILogger<CheckInOutController> logger)
        {
            _parkingService = parkingService;
            _licensePlateService = licensePlateService;
            _logger = logger;
        }

        [HttpPost("checkin")]
        [Consumes("multipart/form-data")]
        public async Task<IActionResult> CheckIn([FromForm] VehicleUploadDto uploadDto)
        {
            try
            {
                if (uploadDto?.Image == null || uploadDto.Image.Length == 0)
                {
                    return BadRequest("No image uploaded.");
                }

                // Process the image to get license plate and vehicle type
                var (licensePlate, vehicleType) = await _licensePlateService.ProcessVehicleImage(uploadDto.Image);

                if (licensePlate == "Error" || licensePlate == "Unknown")
                {
                    return BadRequest("Could not recognize license plate.");
                }

                // Check if a vehicle with this license plate is already parked
                var existingVehicle = await _parkingService.GetParkedVehicleByLicensePlate(licensePlate);

                if (existingVehicle != null)
                {
                    return BadRequest(new {
                        error = $"Vehicle with license plate {licensePlate} is already parked in slot {existingVehicle.SlotId}",
                        existingVehicle = existingVehicle
                    });
                }

                // Park the vehicle
                var vehicle = await _parkingService.ParkVehicle(licensePlate, vehicleType);

                return Ok(new
                {
                    message = "Vehicle checked in successfully",
                    vehicle = vehicle
                });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { error = ex.Message });
            }
        }

        [HttpPost("verify-checkout")]
        [Consumes("multipart/form-data")]
        public async Task<IActionResult> VerifyCheckout([FromForm] VehicleUploadDto uploadDto)
        {
            try
            {
                if (uploadDto?.Image == null || uploadDto.Image.Length == 0)
                {
                    return BadRequest("No image uploaded.");
                }

                // Save the image for debugging purposes
                string debugDir = Path.Combine(Directory.GetCurrentDirectory(), "DebugFrames");
                if (!Directory.Exists(debugDir))
                {
                    Directory.CreateDirectory(debugDir);
                }

                string timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
                string debugFilePath = Path.Combine(debugDir, $"checkout_verify_{timestamp}.jpg");

                using (var stream = new FileStream(debugFilePath, FileMode.Create))
                {
                    await uploadDto.Image.CopyToAsync(stream);
                }

                // Process the image to get license plate and vehicle type
                var (licensePlate, vehicleType) = await _licensePlateService.ProcessVehicleImage(uploadDto.Image);

                if (licensePlate == "Error" || licensePlate == "Unknown")
                {
                    return BadRequest("Could not recognize license plate.");
                }

                // Find the vehicle by license plate
                var vehicle = await _parkingService.GetParkedVehicleByLicensePlate(licensePlate);

                if (vehicle == null)
                {
                    return NotFound(new { error = $"No parked vehicle found with license plate {licensePlate}" });
                }

                // Check if the vehicle type matches
                bool vehicleTypeMatches = string.Equals(vehicle.VehicleType, vehicleType, StringComparison.OrdinalIgnoreCase);

                return Ok(new
                {
                    success = true,
                    message = "Vehicle verified successfully",
                    vehicle = vehicle,
                    recognizedLicensePlate = licensePlate,
                    recognizedVehicleType = vehicleType,
                    vehicleTypeMatches = vehicleTypeMatches,
                    debugImage = Path.GetFileName(debugFilePath)
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error verifying vehicle for checkout");
                return StatusCode(500, new { error = ex.Message });
            }
        }

        [HttpPost("checkout/{vehicleId}")]
        public async Task<IActionResult> CheckOut(string vehicleId, [FromBody] CheckoutRequest? request = null)
        {
            try
            {
                // Get vehicle details before checkout
                var vehicle = await _parkingService.GetVehicleById(vehicleId);
                if (vehicle == null)
                {
                    return NotFound(new { error = $"Vehicle with ID {vehicleId} not found" });
                }

                // For monthly registered vehicles, skip payment and process checkout directly
                if (vehicle.IsMonthlyRegistered)
                {
                    _logger.LogInformation($"Monthly registered vehicle {vehicleId} detected. Skipping payment and processing checkout directly.");

                    // Process checkout
                    var exitedVehicle = await _parkingService.ExitVehicle(vehicleId);

                    return Ok(new
                    {
                        message = "Monthly registered vehicle checked out successfully",
                        vehicle = exitedVehicle,
                        paymentConfirmed = true,
                        isMonthlyRegistered = true
                    });
                }

                // For casual vehicles, handle payment
                if (request == null || !request.PaymentConfirmed)
                {
                    // Calculate parking fee
                    var parkingFeeService = HttpContext.RequestServices.GetRequiredService<ParkingFeeService>();
                    var exitTime = DateTime.Now;
                    var fee = parkingFeeService.CalculateParkingFee(
                        vehicle.VehicleType,
                        vehicle.EntryTime,
                        exitTime,
                        vehicle.IsMonthlyRegistered
                    );

                    // Calculate parking duration
                    var duration = parkingFeeService.FormatParkingDuration(
                        vehicle.EntryTime,
                        exitTime
                    );

                    return Ok(new
                    {
                        message = "Payment required before checkout",
                        vehicle = vehicle,
                        parkingFee = fee,
                        parkingDuration = duration,
                        paymentRequired = true,
                        isMonthlyRegistered = false
                    });
                }

                // Process checkout for casual vehicles with confirmed payment
                var exitedCasualVehicle = await _parkingService.ExitVehicle(vehicleId);

                return Ok(new
                {
                    message = "Vehicle checked out successfully",
                    vehicle = exitedCasualVehicle,
                    paymentConfirmed = true,
                    isMonthlyRegistered = false
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error checking out vehicle {vehicleId}");
                return StatusCode(500, new { error = ex.Message });
            }
        }
    }

    public class CheckoutRequest
    {
        public bool PaymentConfirmed { get; set; }
        public string TransactionId { get; set; }
    }
}
