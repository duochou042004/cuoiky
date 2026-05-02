using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using SmartParking.Core.Models;
using SmartParking.Core.Services;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace SmartParking.Core.Controllers
{
    [Route("api/parking")]
    [ApiController]
    [Authorize]
    public class ParkingController : ControllerBase
    {
        private readonly ParkingService _parkingService;
        private readonly SettingsService _settingsService;
        private readonly ILogger<ParkingController> _logger;

        public ParkingController(
            ParkingService parkingService,
            SettingsService settingsService,
            ILogger<ParkingController> logger)
        {
            _parkingService = parkingService;
            _settingsService = settingsService;
            _logger = logger;
        }

        [HttpGet("initialize")]
        public async Task<IActionResult> InitializeParkingSlots()
        {
            try
            {
                await _parkingService.InitializeParkingSlots();
                return Ok(new { message = "Parking slots initialized successfully" });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { error = ex.Message });
            }
        }

        [HttpGet("slots")]
        public async Task<ActionResult<List<ParkingSlot>>> GetAllSlots()
        {
            try
            {
                var slots = await _parkingService.GetAllParkingSlots();
                return Ok(slots);
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { error = ex.Message });
            }
        }

        [HttpGet("vehicles/parked")]
        public async Task<ActionResult<List<Vehicle>>> GetParkedVehicles()
        {
            try
            {
                var vehicles = await _parkingService.GetParkedVehicles();
                return Ok(vehicles);
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { error = ex.Message });
            }
        }

        [HttpGet("configuration")]
        public async Task<IActionResult> GetParkingConfiguration()
        {
            try
            {
                var parkingSettings = await _settingsService.GetParkingSpaceSettingsAsync();
                var slots = await _parkingService.GetAllParkingSlots();

                var motorcycleSlots = slots.Count(s => s.Type == "MOTORBIKE");
                var carSlots = slots.Count(s => s.Type == "CAR");

                var occupiedMotorcycleSlots = slots.Count(s => s.Type == "MOTORBIKE" && s.Status == "OCCUPIED");
                var occupiedCarSlots = slots.Count(s => s.Type == "CAR" && s.Status == "OCCUPIED");

                return Ok(new
                {
                    ConfiguredMotorcycleSlots = parkingSettings.MotorcycleSlots,
                    ConfiguredCarSlots = parkingSettings.CarSlots,
                    ActualMotorcycleSlots = motorcycleSlots,
                    ActualCarSlots = carSlots,
                    OccupiedMotorcycleSlots = occupiedMotorcycleSlots,
                    OccupiedCarSlots = occupiedCarSlots,
                    Zones = parkingSettings.Zones
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting parking configuration");
                return StatusCode(500, new { error = ex.Message });
            }
        }

        [HttpPut("spaces")]
        public async Task<IActionResult> AdjustParkingSpaces([FromBody] AdjustParkingSpacesRequest request)
        {
            try
            {
                var result = await _parkingService.AdjustParkingSpacesAsync(
                    request.MotorcycleSlots,
                    request.CarSlots);

                return Ok(new
                {
                    message = "Parking spaces adjusted successfully",
                    newMotorcycleSlots = request.MotorcycleSlots,
                    newCarSlots = request.CarSlots,
                    addedMotorcycleSlots = result.AddedMotorcycleSlots,
                    addedCarSlots = result.AddedCarSlots,
                    removedMotorcycleSlots = result.RemovedMotorcycleSlots,
                    removedCarSlots = result.RemovedCarSlots
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error adjusting parking spaces");
                return StatusCode(500, new { error = ex.Message });
            }
        }

        [HttpPost("reset")]
        public async Task<IActionResult> ResetParkingLot()
        {
            try
            {
                var result = await _parkingService.ResetParkingLotAsync();

                return Ok(new
                {
                    message = "Parking lot reset successfully",
                    motorcycleSlots = result.MotorcycleSlots,
                    carSlots = result.CarSlots
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error resetting parking lot");
                return StatusCode(500, new { error = ex.Message });
            }
        }
    }

    public class AdjustParkingSpacesRequest
    {
        public int MotorcycleSlots { get; set; }
        public int CarSlots { get; set; }
    }
}
