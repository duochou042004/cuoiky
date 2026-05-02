using MongoDB.Driver;
using SmartParking.Core.Data;
using SmartParking.Core.Models;
using SmartParking.Core.Hubs;
using Microsoft.Extensions.Configuration;
using Microsoft.AspNetCore.SignalR;
using System;
using System.Threading.Tasks;
using System.Collections.Generic;
using Microsoft.Extensions.Logging;

namespace SmartParking.Core.Services
{
    public class ParkingService
    {
        private readonly MongoDBContext _context;
        private readonly IDGeneratorService _idGenerator;
        private readonly IConfiguration _configuration;
        private readonly IHubContext<ParkingHub> _hubContext;
        private readonly ILogger<ParkingService> _logger;
        private readonly MonthlyVehicleService _monthlyVehicleService;
        private readonly SettingsService _settingsService;

        public ParkingService(
            MongoDBContext context,
            IDGeneratorService idGenerator,
            IConfiguration configuration,
            IHubContext<ParkingHub> hubContext,
            ILogger<ParkingService> logger,
            SettingsService settingsService,
            MonthlyVehicleService? monthlyVehicleService = null) // Optional to avoid circular dependency
        {
            _context = context;
            _idGenerator = idGenerator;
            _configuration = configuration;
            _hubContext = hubContext;
            _logger = logger;
            _settingsService = settingsService;
            _monthlyVehicleService = monthlyVehicleService;
        }

        public async Task InitializeParkingSlots()
        {
            // Check if slots are already initialized
            var count = await _context.ParkingSlots.CountDocumentsAsync(FilterDefinition<ParkingSlot>.Empty);
            if (count > 0)
                return;

            // Get slot counts from settings
            var parkingSettings = await _settingsService.GetParkingSpaceSettingsAsync();
            int motorcycleSlots = parkingSettings.MotorcycleSlots;
            int carSlots = parkingSettings.CarSlots;

            _logger.LogInformation($"Initializing parking slots: {motorcycleSlots} motorcycle slots, {carSlots} car slots");

            var slots = new List<ParkingSlot>();

            // Create motorcycle slots
            for (int i = 1; i <= motorcycleSlots; i++)
            {
                slots.Add(new ParkingSlot
                {
                    SlotId = $"M{i:D3}",
                    Type = "MOTORBIKE",
                    Status = "AVAILABLE",
                    CurrentVehicleId = null
                });
            }

            // Create car slots
            for (int i = 1; i <= carSlots; i++)
            {
                slots.Add(new ParkingSlot
                {
                    SlotId = $"C{i:D3}",
                    Type = "CAR",
                    Status = "AVAILABLE",
                    CurrentVehicleId = null
                });
            }

            // Insert all slots
            await _context.ParkingSlots.InsertManyAsync(slots);
            _logger.LogInformation($"Created {slots.Count} parking slots");
        }

        public async Task<ParkingSlot> AssignParkingSlot(string vehicleType)
        {
            string slotType = vehicleType.ToUpper() == "CAR" ? "CAR" : "MOTORBIKE";

            // Find the first available slot of the correct type
            // Important: Only consider AVAILABLE slots, not RESERVED ones (which are for monthly vehicles)
            var filter = Builders<ParkingSlot>.Filter.And(
                Builders<ParkingSlot>.Filter.Eq(s => s.Type, slotType),
                Builders<ParkingSlot>.Filter.Eq(s => s.Status, "AVAILABLE")
            );

            var slot = await _context.ParkingSlots.Find(filter).FirstOrDefaultAsync();

            if (slot == null)
                throw new Exception($"No available parking slots for {slotType}");

            return slot;
        }

        public async Task<bool> IsMonthlyRegisteredVehicle(string licensePlate)
        {
            if (_monthlyVehicleService == null)
            {
                _logger.LogWarning("MonthlyVehicleService is not available. Assuming vehicle is not monthly registered.");
                return false;
            }

            try
            {
                return await _monthlyVehicleService.IsVehicleRegisteredMonthlyAsync(licensePlate);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error checking if vehicle with license plate {licensePlate} is monthly registered");
                return false;
            }
        }

        public async Task<Vehicle> ParkVehicle(string licensePlate, string vehicleType)
        {
            // Check if the vehicle is registered monthly
            bool isMonthlyRegistered = await IsMonthlyRegisteredVehicle(licensePlate);

            // Generate a unique ID for the vehicle
            string vehicleId;
            string slotId;

            if (isMonthlyRegistered)
            {
                // Get the monthly vehicle details
                var monthlyVehicle = await _monthlyVehicleService.GetMonthlyVehicleByLicensePlateAsync(licensePlate);
                vehicleId = monthlyVehicle.VehicleId; // Use the monthly vehicle ID
                _logger.LogInformation($"Monthly registered vehicle detected: {licensePlate} with ID {vehicleId}");

                // Use the fixed slot assigned to this monthly vehicle
                if (!string.IsNullOrEmpty(monthlyVehicle.FixedSlotId))
                {
                    slotId = monthlyVehicle.FixedSlotId;
                    _logger.LogInformation($"Using fixed parking slot {slotId} for monthly vehicle {licensePlate}");

                    // Get the slot
                    var fixedSlot = await _context.ParkingSlots.Find(s => s.SlotId == slotId).FirstOrDefaultAsync();

                    // Check if the slot is available or reserved
                    if (fixedSlot.Status != "AVAILABLE" && fixedSlot.Status != "RESERVED")
                    {
                        _logger.LogWarning($"Fixed slot {slotId} for monthly vehicle {licensePlate} is already occupied. Finding another slot.");
                        var slot = await AssignParkingSlot(vehicleType);
                        slotId = slot.SlotId;
                    }
                    else if (fixedSlot.Status == "RESERVED")
                    {
                        _logger.LogInformation($"Fixed slot {slotId} for monthly vehicle {licensePlate} is reserved and will be used.");
                    }
                }
                else
                {
                    // If no fixed slot is assigned (shouldn't happen with the new implementation), find an available slot
                    _logger.LogWarning($"No fixed slot found for monthly vehicle {licensePlate}. Finding an available slot.");
                    var slot = await AssignParkingSlot(vehicleType);
                    slotId = slot.SlotId;
                }
            }
            else
            {
                // Generate a regular vehicle ID
                vehicleId = await _idGenerator.GenerateVehicleId(vehicleType);

                // Find an available parking slot for casual vehicles
                var slot = await AssignParkingSlot(vehicleType);
                slotId = slot.SlotId;
            }

            // Create a new vehicle record
            var vehicle = new Vehicle
            {
                VehicleId = vehicleId,
                LicensePlate = licensePlate,
                VehicleType = vehicleType.ToUpper(),
                Status = "PARKED",
                EntryTime = DateTime.Now,
                SlotId = slotId,
                IsMonthlyRegistered = isMonthlyRegistered
            };

            // Save the vehicle to the database
            await _context.Vehicles.InsertOneAsync(vehicle);

            // Update the parking slot status
            var filter = Builders<ParkingSlot>.Filter.Eq(s => s.SlotId, slotId);
            var update = Builders<ParkingSlot>.Update
                .Set(s => s.Status, "OCCUPIED")
                .Set(s => s.CurrentVehicleId, vehicleId);

            await _context.ParkingSlots.UpdateOneAsync(filter, update);

            // Get the updated slot
            var updatedSlot = await _context.ParkingSlots.Find(filter).FirstOrDefaultAsync();

            // Send real-time updates via SignalR
            await _hubContext.Clients.All.SendAsync("ReceiveParkingUpdate", updatedSlot);
            await _hubContext.Clients.All.SendAsync("ReceiveVehicleUpdate", vehicle);

            return vehicle;
        }

        public async Task<Vehicle> ExitVehicle(string vehicleId)
        {
            // Find the vehicle
            var filter = Builders<Vehicle>.Filter.Eq(v => v.VehicleId, vehicleId);
            var vehicle = await _context.Vehicles.Find(filter).FirstOrDefaultAsync();

            if (vehicle == null)
                throw new Exception($"Vehicle with ID {vehicleId} not found");

            if (vehicle.Status != "PARKED")
                throw new Exception($"Vehicle with ID {vehicleId} is not currently parked");

            // Update the vehicle status
            var update = Builders<Vehicle>.Update
                .Set(v => v.Status, "LEFT")
                .Set(v => v.ExitTime, DateTime.Now);

            await _context.Vehicles.UpdateOneAsync(filter, update);

            // Update the parking slot status
            var slotFilter = Builders<ParkingSlot>.Filter.Eq(s => s.SlotId, vehicle.SlotId);

            // Check if this is a monthly registered vehicle with a fixed slot
            bool isFixedSlotForMonthlyVehicle = false;
            if (vehicle.IsMonthlyRegistered && _monthlyVehicleService != null)
            {
                try
                {
                    var monthlyVehicle = await _monthlyVehicleService.GetMonthlyVehicleByVehicleIdAsync(vehicleId);
                    if (monthlyVehicle != null &&
                        monthlyVehicle.Status == "VALID" &&
                        monthlyVehicle.FixedSlotId == vehicle.SlotId)
                    {
                        isFixedSlotForMonthlyVehicle = true;
                        _logger.LogInformation($"Vehicle {vehicleId} is monthly registered. Setting slot {vehicle.SlotId} status to RESERVED.");
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, $"Error checking if slot {vehicle.SlotId} is fixed for monthly vehicle {vehicleId}");
                }
            }

            // Set the appropriate status based on whether this is a fixed slot for a monthly vehicle
            var newStatus = isFixedSlotForMonthlyVehicle ? "RESERVED" : "AVAILABLE";
            var slotUpdate = Builders<ParkingSlot>.Update
                .Set(s => s.Status, newStatus)
                .Set(s => s.CurrentVehicleId, null);

            await _context.ParkingSlots.UpdateOneAsync(slotFilter, slotUpdate);

            // Get the updated vehicle and slot
            vehicle = await _context.Vehicles.Find(filter).FirstOrDefaultAsync();
            var updatedSlot = await _context.ParkingSlots.Find(slotFilter).FirstOrDefaultAsync();

            // Send real-time updates via SignalR
            await _hubContext.Clients.All.SendAsync("ReceiveParkingUpdate", updatedSlot);
            await _hubContext.Clients.All.SendAsync("ReceiveVehicleUpdate", vehicle);

            return vehicle;
        }

        public async Task<List<ParkingSlot>> GetAllParkingSlots()
        {
            return await _context.ParkingSlots.Find(FilterDefinition<ParkingSlot>.Empty).ToListAsync();
        }

        public async Task<List<Vehicle>> GetParkedVehicles()
        {
            var filter = Builders<Vehicle>.Filter.Eq(v => v.Status, "PARKED");
            return await _context.Vehicles.Find(filter).ToListAsync();
        }

        public async Task<Vehicle?> GetParkedVehicleByLicensePlate(string licensePlate)
        {
            var filter = Builders<Vehicle>.Filter.And(
                Builders<Vehicle>.Filter.Eq(v => v.Status, "PARKED"),
                Builders<Vehicle>.Filter.Eq(v => v.LicensePlate, licensePlate)
            );

            return await _context.Vehicles.Find(filter).FirstOrDefaultAsync();
        }

        public async Task<Vehicle> GetVehicleById(string vehicleId)
        {
            var filter = Builders<Vehicle>.Filter.Eq(v => v.VehicleId, vehicleId);
            return await _context.Vehicles.Find(filter).FirstOrDefaultAsync();
        }

        /// <summary>
        /// Adjust the number of parking spaces without resetting the database
        /// </summary>
        public async Task<(int AddedMotorcycleSlots, int AddedCarSlots, int RemovedMotorcycleSlots, int RemovedCarSlots)> AdjustParkingSpacesAsync(int newMotorcycleSlots, int newCarSlots)
        {
            // Validate input
            if (newMotorcycleSlots < 0)
                throw new ArgumentException("Number of motorcycle slots cannot be negative", nameof(newMotorcycleSlots));

            if (newCarSlots < 0)
                throw new ArgumentException("Number of car slots cannot be negative", nameof(newCarSlots));

            // Get current slots
            var motorcycleSlots = await _context.ParkingSlots.Find(s => s.Type == "MOTORBIKE").ToListAsync();
            var carSlots = await _context.ParkingSlots.Find(s => s.Type == "CAR").ToListAsync();

            int currentMotorcycleSlots = motorcycleSlots.Count;
            int currentCarSlots = carSlots.Count;

            _logger.LogInformation($"Current parking spaces: {currentMotorcycleSlots} motorcycle slots, {currentCarSlots} car slots");
            _logger.LogInformation($"Adjusting to: {newMotorcycleSlots} motorcycle slots, {newCarSlots} car slots");

            int addedMotorcycleSlots = 0;
            int addedCarSlots = 0;
            int removedMotorcycleSlots = 0;
            int removedCarSlots = 0;

            // Add motorcycle slots if needed
            if (newMotorcycleSlots > currentMotorcycleSlots)
            {
                var newSlots = new List<ParkingSlot>();
                int startIndex = currentMotorcycleSlots + 1;

                for (int i = startIndex; i <= newMotorcycleSlots; i++)
                {
                    newSlots.Add(new ParkingSlot
                    {
                        SlotId = $"M{i:D3}",
                        Type = "MOTORBIKE",
                        Status = "AVAILABLE",
                        CurrentVehicleId = null,
                        CreatedAt = DateTime.UtcNow,
                        UpdatedAt = DateTime.UtcNow
                    });
                }

                if (newSlots.Count > 0)
                {
                    await _context.ParkingSlots.InsertManyAsync(newSlots);
                    addedMotorcycleSlots = newSlots.Count;
                    _logger.LogInformation($"Added {addedMotorcycleSlots} new motorcycle slots");
                }
            }

            // Add car slots if needed
            if (newCarSlots > currentCarSlots)
            {
                var newSlots = new List<ParkingSlot>();
                int startIndex = currentCarSlots + 1;

                for (int i = startIndex; i <= newCarSlots; i++)
                {
                    newSlots.Add(new ParkingSlot
                    {
                        SlotId = $"C{i:D3}",
                        Type = "CAR",
                        Status = "AVAILABLE",
                        CurrentVehicleId = null,
                        CreatedAt = DateTime.UtcNow,
                        UpdatedAt = DateTime.UtcNow
                    });
                }

                if (newSlots.Count > 0)
                {
                    await _context.ParkingSlots.InsertManyAsync(newSlots);
                    addedCarSlots = newSlots.Count;
                    _logger.LogInformation($"Added {addedCarSlots} new car slots");
                }
            }

            // Remove motorcycle slots if needed
            if (newMotorcycleSlots < currentMotorcycleSlots)
            {
                // Sort slots by ID in descending order to remove highest numbered slots first
                var slotsToRemove = motorcycleSlots
                    .OrderByDescending(s => s.SlotId)
                    .Take(currentMotorcycleSlots - newMotorcycleSlots)
                    .ToList();

                // Check if any slots are occupied
                var occupiedSlots = slotsToRemove.Where(s => s.Status == "OCCUPIED").ToList();
                if (occupiedSlots.Any())
                {
                    throw new InvalidOperationException($"Cannot remove {occupiedSlots.Count} motorcycle slots because they are currently occupied");
                }

                // Remove slots
                foreach (var slot in slotsToRemove)
                {
                    await _context.ParkingSlots.DeleteOneAsync(s => s.Id == slot.Id);
                }

                removedMotorcycleSlots = slotsToRemove.Count;
                _logger.LogInformation($"Removed {removedMotorcycleSlots} motorcycle slots");
            }

            // Remove car slots if needed
            if (newCarSlots < currentCarSlots)
            {
                // Sort slots by ID in descending order to remove highest numbered slots first
                var slotsToRemove = carSlots
                    .OrderByDescending(s => s.SlotId)
                    .Take(currentCarSlots - newCarSlots)
                    .ToList();

                // Check if any slots are occupied
                var occupiedSlots = slotsToRemove.Where(s => s.Status == "OCCUPIED").ToList();
                if (occupiedSlots.Any())
                {
                    throw new InvalidOperationException($"Cannot remove {occupiedSlots.Count} car slots because they are currently occupied");
                }

                // Remove slots
                foreach (var slot in slotsToRemove)
                {
                    await _context.ParkingSlots.DeleteOneAsync(s => s.Id == slot.Id);
                }

                removedCarSlots = slotsToRemove.Count;
                _logger.LogInformation($"Removed {removedCarSlots} car slots");
            }

            // Update settings
            var parkingSettings = await _settingsService.GetParkingSpaceSettingsAsync();
            parkingSettings.MotorcycleSlots = newMotorcycleSlots;
            parkingSettings.CarSlots = newCarSlots;
            await _settingsService.UpdateParkingSpaceSettingsAsync(parkingSettings);

            // Notify clients about the changes
            await _hubContext.Clients.All.SendAsync("ReceiveParkingConfigurationUpdate", new
            {
                MotorcycleSlots = newMotorcycleSlots,
                CarSlots = newCarSlots,
                AddedMotorcycleSlots = addedMotorcycleSlots,
                AddedCarSlots = addedCarSlots,
                RemovedMotorcycleSlots = removedMotorcycleSlots,
                RemovedCarSlots = removedCarSlots
            });

            return (addedMotorcycleSlots, addedCarSlots, removedMotorcycleSlots, removedCarSlots);
        }

        /// <summary>
        /// Reset the parking lot by removing all slots and creating new ones
        /// </summary>
        public async Task<(int MotorcycleSlots, int CarSlots)> ResetParkingLotAsync()
        {
            // Check if there are any parked vehicles
            var parkedVehicles = await GetParkedVehicles();
            if (parkedVehicles.Any())
            {
                throw new InvalidOperationException($"Cannot reset parking lot because there are {parkedVehicles.Count} vehicles currently parked");
            }

            // Delete all parking slots
            await _context.ParkingSlots.DeleteManyAsync(FilterDefinition<ParkingSlot>.Empty);
            _logger.LogInformation("Deleted all parking slots");

            // Initialize new slots
            await InitializeParkingSlots();

            // Get the new slot counts
            var parkingSettings = await _settingsService.GetParkingSpaceSettingsAsync();

            // Notify clients about the reset
            await _hubContext.Clients.All.SendAsync("ReceiveParkingConfigurationUpdate", new
            {
                MotorcycleSlots = parkingSettings.MotorcycleSlots,
                CarSlots = parkingSettings.CarSlots,
                Reset = true
            });

            return (parkingSettings.MotorcycleSlots, parkingSettings.CarSlots);
        }
    }
}
