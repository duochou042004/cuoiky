using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Logging;
using SmartParking.Core.Models;
using SmartParking.Core.Services;
using SmartParking.Core.Data;
using MongoDB.Driver;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace SmartParking.Core.Controllers
{
    [ApiController]
    [Route("api/monthlyvehicle")]
    [Authorize]
    public class MonthlyVehicleController : ControllerBase
    {
        private readonly ILogger<MonthlyVehicleController> _logger;
        private readonly MonthlyVehicleService _monthlyVehicleService;
        private readonly TransactionService _transactionService;
        private readonly MomoPaymentService _momoPaymentService;
        private readonly StripePaymentService _stripePaymentService;
        private readonly MongoDBContext _context;
        private readonly LicensePlateService _licensePlateService;

        public MonthlyVehicleController(
            ILogger<MonthlyVehicleController> logger,
            MonthlyVehicleService monthlyVehicleService,
            TransactionService transactionService,
            MomoPaymentService momoPaymentService,
            StripePaymentService stripePaymentService,
            MongoDBContext context,
            LicensePlateService licensePlateService)
        {
            _logger = logger;
            _monthlyVehicleService = monthlyVehicleService;
            _transactionService = transactionService;
            _momoPaymentService = momoPaymentService;
            _stripePaymentService = stripePaymentService;
            _context = context;
            _licensePlateService = licensePlateService;
        }

        [HttpGet]
        public async Task<IActionResult> GetAllMonthlyVehicles()
        {
            try
            {
                var vehicles = await _monthlyVehicleService.GetAllMonthlyVehiclesAsync();
                _logger.LogInformation($"Retrieved {vehicles.Count} monthly vehicles");

                // Check if there are any vehicles in the database
                if (vehicles.Count == 0)
                {
                    _logger.LogWarning("No monthly vehicles found in the database");
                }

                return Ok(vehicles);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting all monthly vehicles");
                return StatusCode(500, new { error = ex.Message });
            }
        }

        [HttpGet("{id}")]
        public async Task<IActionResult> GetMonthlyVehicleById(string id)
        {
            try
            {
                var vehicle = await _monthlyVehicleService.GetMonthlyVehicleByIdAsync(id);
                if (vehicle == null)
                {
                    return NotFound(new { error = $"Monthly vehicle with ID {id} not found" });
                }
                return Ok(vehicle);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error getting monthly vehicle with ID {id}");
                return StatusCode(500, new { error = ex.Message });
            }
        }

        [HttpGet("by-status/{status}")]
        public async Task<IActionResult> GetMonthlyVehiclesByStatus(string status)
        {
            try
            {
                var vehicles = await _monthlyVehicleService.GetMonthlyVehiclesByStatusAsync(status);
                return Ok(vehicles);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error getting monthly vehicles with status {status}");
                return StatusCode(500, new { error = ex.Message });
            }
        }

        [HttpGet("count")]
        public async Task<IActionResult> GetMonthlyVehiclesCount()
        {
            try
            {
                _logger.LogInformation("Getting count of monthly vehicles with VALID status");
                var count = await _monthlyVehicleService.GetMonthlyVehiclesCountAsync();
                _logger.LogInformation($"Found {count} valid monthly vehicles");
                return Ok(count);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting monthly vehicles count");
                return StatusCode(500, new { error = ex.Message });
            }
        }

        [HttpGet("check-expiring")]
        public async Task<IActionResult> GetExpiringVehicles([FromQuery] int days = 7)
        {
            try
            {
                _logger.LogInformation($"Getting monthly vehicles expiring in the next {days} days");
                var vehicles = await _monthlyVehicleService.GetExpiringVehiclesAsync(days);
                _logger.LogInformation($"Found {vehicles.Count} vehicles expiring in the next {days} days");
                return Ok(vehicles);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error getting vehicles expiring in the next {days} days");
                return StatusCode(500, new { error = ex.Message });
            }
        }

        [HttpGet("check-license-plate/{licensePlate}")]
        public async Task<IActionResult> CheckLicensePlate(string licensePlate)
        {
            try
            {
                var isRegistered = await _monthlyVehicleService.IsVehicleRegisteredMonthlyAsync(licensePlate);
                return Ok(new { isRegistered });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error checking license plate {licensePlate}");
                return StatusCode(500, new { error = ex.Message });
            }
        }

        [HttpGet("calculate-price")]
        public async Task<IActionResult> CalculatePackagePrice([FromQuery] string vehicleType, [FromQuery] int durationMonths)
        {
            try
            {
                if (string.IsNullOrEmpty(vehicleType) || durationMonths <= 0)
                {
                    return BadRequest(new { error = "Vehicle type and duration months are required" });
                }

                // Get fee settings from database
                var feeSettings = await _monthlyVehicleService.GetParkingFeeSettingsAsync();

                // Set base price based on vehicle type from settings
                decimal basePrice = vehicleType.ToUpper() == "CAR"
                    ? feeSettings.MonthlyCarFee
                    : feeSettings.MonthlyMotorbikeFee;

                // Calculate total price without discount
                decimal totalBeforeDiscount = basePrice * durationMonths;

                // Get the final price with discount applied
                var price = await _monthlyVehicleService.CalculatePackagePrice(vehicleType, durationMonths);
                var discountPercentage = _monthlyVehicleService.GetDiscountPercentage(durationMonths);

                return Ok(new
                {
                    vehicleType,
                    durationMonths,
                    price,
                    discountPercentage,
                    basePrice,
                    totalBeforeDiscount
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error calculating package price for {vehicleType} for {durationMonths} months");
                return StatusCode(500, new { error = ex.Message });
            }
        }

        [HttpPost("register")]
        public async Task<IActionResult> RegisterMonthlyVehicle([FromBody] MonthlyVehicleRegistrationRequest request)
        {
            try
            {
                // Validate request
                if (string.IsNullOrEmpty(request.LicensePlate) ||
                    string.IsNullOrEmpty(request.VehicleType) ||
                    string.IsNullOrEmpty(request.CustomerName) ||
                    string.IsNullOrEmpty(request.CustomerPhone) ||
                    string.IsNullOrEmpty(request.CustomerEmail) ||
                    request.PackageDuration <= 0)
                {
                    return BadRequest(new { error = "All fields are required" });
                }

                // Check if license plate is already registered
                var isRegistered = await _monthlyVehicleService.IsVehicleRegisteredMonthlyAsync(request.LicensePlate);
                if (isRegistered)
                {
                    return BadRequest(new { error = $"License plate {request.LicensePlate} is already registered with a valid monthly package" });
                }

                // Calculate package price
                var packageAmount = await _monthlyVehicleService.CalculatePackagePrice(request.VehicleType, request.PackageDuration);
                var discountPercentage = _monthlyVehicleService.GetDiscountPercentage(request.PackageDuration);

                // Create a pending registration
                var pendingRegistration = await _monthlyVehicleService.CreatePendingRegistrationAsync(
                    request.LicensePlate,
                    request.VehicleType,
                    request.CustomerName,
                    request.CustomerPhone,
                    request.CustomerEmail,
                    request.PackageDuration,
                    packageAmount,
                    discountPercentage
                );

                // Return the pending registration with payment information
                return Ok(new
                {
                    success = true,
                    message = "Monthly vehicle registration pending payment",
                    registrationId = pendingRegistration.Id,
                    requiresPayment = true,
                    paymentAmount = pendingRegistration.PackageAmount,
                    paymentDetails = new
                    {
                        vehicleId = pendingRegistration.VehicleId,
                        licensePlate = pendingRegistration.LicensePlate,
                        vehicleType = pendingRegistration.VehicleType,
                        customerName = pendingRegistration.CustomerName,
                        packageDuration = pendingRegistration.PackageDuration,
                        packageAmount = pendingRegistration.PackageAmount,
                        description = $"Monthly parking registration for {pendingRegistration.LicensePlate} ({pendingRegistration.PackageDuration} months)"
                    }
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error registering monthly vehicle");
                return StatusCode(500, new { error = ex.Message });
            }
        }

        [HttpPost("payment/cash")]
        public async Task<IActionResult> CreateCashPayment([FromBody] MonthlyVehiclePaymentRequest request)
        {
            try
            {
                // Validate request
                if (string.IsNullOrEmpty(request.VehicleId))
                {
                    return BadRequest(new { error = "Registration ID is required" });
                }

                // For renewal
                if (request.IsRenewal)
                {
                    // Get monthly vehicle details
                    var monthlyVehicle = await _monthlyVehicleService.GetMonthlyVehicleByIdAsync(request.VehicleId);
                    if (monthlyVehicle == null)
                    {
                        return NotFound(new { error = $"Monthly vehicle with ID {request.VehicleId} not found" });
                    }

                    // Create transaction (completed status for cash)
                    var transaction = await _transactionService.CreateCashTransactionAsync(
                        monthlyVehicle.VehicleId,
                        monthlyVehicle.PackageAmount,
                        "MONTHLY_RENEWAL",
                        $"Renewal of monthly subscription for {monthlyVehicle.PackageDuration} months for {monthlyVehicle.VehicleType} with license plate {monthlyVehicle.LicensePlate}"
                    );

                    // Complete the renewal
                    var pendingRenewal = await _context.PendingRenewals
                        .Find(r => r.VehicleId == monthlyVehicle.Id && r.Status == "PENDING")
                        .FirstOrDefaultAsync();

                    if (pendingRenewal != null)
                    {
                        await _monthlyVehicleService.CompleteRenewalAsync(
                            pendingRenewal.Id,
                            transaction.TransactionId
                        );
                    }
                    else
                    {
                        // If no pending renewal exists, create and complete one
                        var newRenewal = await _monthlyVehicleService.RenewMonthlyVehicleAsync(
                            monthlyVehicle.Id,
                            monthlyVehicle.PackageDuration,
                            monthlyVehicle.PackageAmount,
                            monthlyVehicle.DiscountPercentage
                        );
                    }

                    return Ok(new
                    {
                        success = true,
                        message = "Cash payment for monthly vehicle renewal processed successfully",
                        transactionId = transaction.TransactionId,
                        amount = transaction.Amount,
                        vehicleId = monthlyVehicle.Id
                    });
                }
                else
                {
                    // For new registration
                    // Get pending registration details
                    var pendingRegistration = await _context.PendingRegistrations
                        .Find(r => r.Id == request.VehicleId && r.Status == "PENDING")
                        .FirstOrDefaultAsync();

                    if (pendingRegistration == null)
                    {
                        return NotFound(new { error = $"Pending registration with ID {request.VehicleId} not found or already processed" });
                    }

                    // Create transaction (completed status for cash)
                    var transaction = await _transactionService.CreateCashTransactionAsync(
                        pendingRegistration.VehicleId,
                        pendingRegistration.PackageAmount,
                        "MONTHLY_SUBSCRIPTION",
                        $"Registration of monthly subscription for {pendingRegistration.PackageDuration} months for {pendingRegistration.VehicleType} with license plate {pendingRegistration.LicensePlate}"
                    );

                    // Complete the registration
                    await _monthlyVehicleService.CompleteRegistrationAsync(
                        pendingRegistration.Id,
                        transaction.TransactionId
                    );

                    return Ok(new
                    {
                        success = true,
                        message = "Cash payment for monthly vehicle registration processed successfully",
                        transactionId = transaction.TransactionId,
                        amount = transaction.Amount,
                        registrationId = pendingRegistration.Id
                    });
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing cash payment for monthly vehicle");
                return StatusCode(500, new { error = ex.Message });
            }
        }

        [HttpPost("complete-registration")]
        public async Task<IActionResult> CompleteRegistration([FromBody] CompleteRegistrationRequest request)
        {
            try
            {
                // Validate request
                if (request == null || string.IsNullOrEmpty(request.RegistrationId) || string.IsNullOrEmpty(request.TransactionId))
                {
                    return BadRequest(new { error = "Invalid request data" });
                }

                // Complete the registration
                var monthlyVehicle = await _monthlyVehicleService.CompleteRegistrationAsync(
                    request.RegistrationId,
                    request.TransactionId
                );

                return Ok(new
                {
                    success = true,
                    message = "Monthly vehicle registration completed successfully",
                    vehicle = monthlyVehicle
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error completing monthly vehicle registration");
                return StatusCode(500, new { error = ex.Message });
            }
        }

        [HttpPost("renew/{id}")]
        public async Task<IActionResult> RenewMonthlyVehicle(string id, [FromBody] MonthlyVehicleRenewalRequest request)
        {
            try
            {
                // Validate request
                if (request.PackageDuration <= 0)
                {
                    return BadRequest(new { error = "Package duration must be greater than 0" });
                }

                // Get the monthly vehicle
                var monthlyVehicle = await _monthlyVehicleService.GetMonthlyVehicleByIdAsync(id);
                if (monthlyVehicle == null)
                {
                    return NotFound(new { error = $"Monthly vehicle with ID {id} not found" });
                }

                // Calculate package price
                var packageAmount = await _monthlyVehicleService.CalculatePackagePrice(monthlyVehicle.VehicleType, request.PackageDuration);
                var discountPercentage = _monthlyVehicleService.GetDiscountPercentage(request.PackageDuration);

                // Create a pending renewal
                var pendingRenewal = await _monthlyVehicleService.CreatePendingRenewalAsync(
                    id,
                    request.PackageDuration,
                    packageAmount,
                    discountPercentage
                );

                // Return the pending renewal with payment information
                return Ok(new
                {
                    success = true,
                    message = "Monthly vehicle renewal pending payment",
                    renewalId = pendingRenewal.Id,
                    requiresPayment = true,
                    paymentAmount = pendingRenewal.PackageAmount,
                    paymentDetails = new
                    {
                        vehicleId = pendingRenewal.VehicleId,
                        licensePlate = pendingRenewal.LicensePlate,
                        vehicleType = pendingRenewal.VehicleType,
                        customerName = pendingRenewal.CustomerName,
                        packageDuration = pendingRenewal.PackageDuration,
                        packageAmount = pendingRenewal.PackageAmount,
                        description = $"Monthly parking renewal for {pendingRenewal.LicensePlate} ({pendingRenewal.PackageDuration} months)"
                    }
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error renewing monthly vehicle with ID {id}");
                return StatusCode(500, new { error = ex.Message });
            }
        }

        [HttpPost("complete-renewal")]
        public async Task<IActionResult> CompleteRenewal([FromBody] CompleteRegistrationRequest request)
        {
            try
            {
                // Validate request
                if (request == null || string.IsNullOrEmpty(request.RegistrationId) || string.IsNullOrEmpty(request.TransactionId))
                {
                    return BadRequest(new { error = "Invalid request data" });
                }

                // Complete the renewal
                var monthlyVehicle = await _monthlyVehicleService.CompleteRenewalAsync(
                    request.RegistrationId,
                    request.TransactionId
                );

                return Ok(new
                {
                    success = true,
                    message = "Monthly vehicle renewal completed successfully",
                    vehicle = monthlyVehicle
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error completing monthly vehicle renewal");
                return StatusCode(500, new { error = ex.Message });
            }
        }

        [HttpPost("cancel/{id}")]
        public async Task<IActionResult> CancelMonthlyVehicle(string id)
        {
            try
            {
                // Get the monthly vehicle
                var monthlyVehicle = await _monthlyVehicleService.GetMonthlyVehicleByIdAsync(id);
                if (monthlyVehicle == null)
                {
                    return NotFound(new { error = $"Monthly vehicle with ID {id} not found" });
                }

                // Cancel the monthly vehicle
                var cancelledVehicle = await _monthlyVehicleService.CancelMonthlyVehicleAsync(id);

                return Ok(cancelledVehicle);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error cancelling monthly vehicle with ID {id}");
                return StatusCode(500, new { error = ex.Message });
            }
        }

        [HttpPost("payment/momo")]
        public async Task<IActionResult> CreateMomoPayment([FromBody] MonthlyVehiclePaymentRequest request)
        {
            try
            {
                // Validate request
                if (string.IsNullOrEmpty(request.VehicleId))
                {
                    return BadRequest(new { error = "Registration ID is required" });
                }

                // For renewal
                if (request.IsRenewal)
                {
                    // Get monthly vehicle details
                    var monthlyVehicle = await _monthlyVehicleService.GetMonthlyVehicleByIdAsync(request.VehicleId);
                    if (monthlyVehicle == null)
                    {
                        return NotFound(new { error = $"Monthly vehicle with ID {request.VehicleId} not found" });
                    }

                    // Create transaction (pending status)
                    var transaction = await _transactionService.CreateMomoTransactionAsync(
                        monthlyVehicle.VehicleId,
                        monthlyVehicle.PackageAmount,
                        "MONTHLY_RENEWAL",
                        $"Renewal of monthly subscription for {monthlyVehicle.PackageDuration} months for {monthlyVehicle.VehicleType} with license plate {monthlyVehicle.LicensePlate}"
                    );

                    // Create Momo payment request
                    var orderInfo = $"Monthly subscription renewal for {monthlyVehicle.LicensePlate}";
                    var momoResponse = await _momoPaymentService.CreatePaymentAsync(
                        transaction.TransactionId,
                        orderInfo,
                        monthlyVehicle.PackageAmount,
                        transaction.Id // Store transaction ID as extra data
                    );

                    if (momoResponse.ResultCode != 0)
                    {
                        return BadRequest(new { error = momoResponse.Message });
                    }

                    return Ok(new
                    {
                        paymentUrl = momoResponse.PayUrl,
                        transactionId = transaction.TransactionId,
                        amount = monthlyVehicle.PackageAmount,
                        orderInfo = orderInfo,
                        isRenewal = true,
                        vehicleId = monthlyVehicle.Id
                    });
                }
                else
                {
                    // For new registration
                    // Get pending registration details
                    var pendingRegistration = await _context.PendingRegistrations
                        .Find(r => r.Id == request.VehicleId && r.Status == "PENDING")
                        .FirstOrDefaultAsync();

                    if (pendingRegistration == null)
                    {
                        return NotFound(new { error = $"Pending registration with ID {request.VehicleId} not found or already processed" });
                    }

                    // Create transaction (pending status)
                    var transaction = await _transactionService.CreateMomoTransactionAsync(
                        pendingRegistration.VehicleId,
                        pendingRegistration.PackageAmount,
                        "MONTHLY_SUBSCRIPTION",
                        $"Registration of monthly subscription for {pendingRegistration.PackageDuration} months for {pendingRegistration.VehicleType} with license plate {pendingRegistration.LicensePlate}"
                    );

                    // Create Momo payment request
                    var orderInfo = $"Monthly subscription registration for {pendingRegistration.LicensePlate}";
                    var momoResponse = await _momoPaymentService.CreatePaymentAsync(
                        transaction.TransactionId,
                        orderInfo,
                        pendingRegistration.PackageAmount,
                        transaction.Id // Store transaction ID as extra data
                    );

                    if (momoResponse.ResultCode != 0)
                    {
                        return BadRequest(new { error = momoResponse.Message });
                    }

                    return Ok(new
                    {
                        paymentUrl = momoResponse.PayUrl,
                        transactionId = transaction.TransactionId,
                        amount = pendingRegistration.PackageAmount,
                        orderInfo = orderInfo,
                        isRenewal = false,
                        registrationId = pendingRegistration.Id
                    });
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error creating Momo payment for monthly vehicle");
                return StatusCode(500, new { error = ex.Message });
            }
        }

        [HttpPost("payment/stripe")]
        public async Task<IActionResult> CreateStripePayment([FromBody] MonthlyVehiclePaymentRequest request)
        {
            try
            {
                // Validate request
                if (string.IsNullOrEmpty(request.VehicleId))
                {
                    return BadRequest(new { error = "Registration ID is required" });
                }

                // For renewal
                if (request.IsRenewal)
                {
                    // Get monthly vehicle details
                    var monthlyVehicle = await _monthlyVehicleService.GetMonthlyVehicleByIdAsync(request.VehicleId);
                    if (monthlyVehicle == null)
                    {
                        return NotFound(new { error = $"Monthly vehicle with ID {request.VehicleId} not found" });
                    }

                    // Create transaction (pending status)
                    var transaction = await _transactionService.CreateStripeTransactionAsync(
                        monthlyVehicle.VehicleId,
                        monthlyVehicle.PackageAmount,
                        "MONTHLY_RENEWAL",
                        $"Renewal of monthly subscription for {monthlyVehicle.PackageDuration} months for {monthlyVehicle.VehicleType} with license plate {monthlyVehicle.LicensePlate}"
                    );

                    // Create Stripe payment intent
                    var description = $"Monthly subscription renewal for {monthlyVehicle.LicensePlate}";
                    var stripeResponse = await _stripePaymentService.CreatePaymentIntentAsync(
                        transaction.TransactionId,
                        description,
                        monthlyVehicle.PackageAmount,
                        transaction.Id
                    );

                    if (stripeResponse == null || string.IsNullOrEmpty(stripeResponse.ClientSecret))
                    {
                        return BadRequest(new { error = "Failed to create Stripe payment intent" });
                    }

                    return Ok(new
                    {
                        clientSecret = stripeResponse.ClientSecret,
                        transactionId = transaction.TransactionId,
                        amount = monthlyVehicle.PackageAmount,
                        description = description,
                        isRenewal = true,
                        vehicleId = monthlyVehicle.Id
                    });
                }
                else
                {
                    // For new registration
                    // Get pending registration details
                    var pendingRegistration = await _context.PendingRegistrations
                        .Find(r => r.Id == request.VehicleId && r.Status == "PENDING")
                        .FirstOrDefaultAsync();

                    if (pendingRegistration == null)
                    {
                        return NotFound(new { error = $"Pending registration with ID {request.VehicleId} not found or already processed" });
                    }

                    // Create transaction (pending status)
                    var transaction = await _transactionService.CreateStripeTransactionAsync(
                        pendingRegistration.VehicleId,
                        pendingRegistration.PackageAmount,
                        "MONTHLY_SUBSCRIPTION",
                        $"Registration of monthly subscription for {pendingRegistration.PackageDuration} months for {pendingRegistration.VehicleType} with license plate {pendingRegistration.LicensePlate}"
                    );

                    // Create Stripe payment intent
                    var description = $"Monthly subscription registration for {pendingRegistration.LicensePlate}";
                    var stripeResponse = await _stripePaymentService.CreatePaymentIntentAsync(
                        transaction.TransactionId,
                        description,
                        pendingRegistration.PackageAmount,
                        transaction.Id
                    );

                    if (stripeResponse == null || string.IsNullOrEmpty(stripeResponse.ClientSecret))
                    {
                        return BadRequest(new { error = "Failed to create Stripe payment intent" });
                    }

                    return Ok(new
                    {
                        clientSecret = stripeResponse.ClientSecret,
                        transactionId = transaction.TransactionId,
                        amount = pendingRegistration.PackageAmount,
                        description = description,
                        isRenewal = false,
                        registrationId = pendingRegistration.Id
                    });
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error creating Stripe payment for monthly vehicle");
                return StatusCode(500, new { error = ex.Message });
            }
        }

        [HttpPost("update-expired")]
        public async Task<IActionResult> UpdateExpiredVehicles()
        {
            try
            {
                await _monthlyVehicleService.UpdateExpiredVehiclesAsync();
                return Ok(new { message = "Expired vehicles updated successfully" });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error updating expired vehicles");
                return StatusCode(500, new { error = ex.Message });
            }
        }

        [HttpPost("create-test-data")]
        public async Task<IActionResult> CreateTestData()
        {
            try
            {
                bool created = await _monthlyVehicleService.CreateTestMonthlyVehicleAsync();
                if (created)
                {
                    return Ok(new { message = "Test monthly vehicle created successfully" });
                }
                else
                {
                    return Ok(new { message = "Test data already exists or could not be created" });
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error creating test monthly vehicle");
                return StatusCode(500, new { error = ex.Message });
            }
        }

        [HttpPost("generate-historical-transactions")]
        public async Task<IActionResult> GenerateHistoricalTransactions()
        {
            try
            {
                _logger.LogInformation("Starting generation of historical transactions for monthly vehicles");

                // Get all monthly vehicles
                var allMonthlyVehicles = await _monthlyVehicleService.GetAllMonthlyVehiclesAsync();
                _logger.LogInformation($"Found {allMonthlyVehicles.Count} monthly vehicles");

                // Get all existing transactions for monthly vehicles
                var existingTransactions = await _transactionService.GetAllMonthlySubscriptionTransactionsAsync();
                var existingTransactionVehicleIds = existingTransactions
                    .Select(t => t.VehicleId)
                    .Distinct()
                    .ToList();

                _logger.LogInformation($"Found {existingTransactions.Count} existing monthly subscription transactions");

                int newRegistrationTransactions = 0;
                int newRenewalTransactions = 0;
                List<Transaction> createdTransactions = new List<Transaction>();

                // Process each monthly vehicle
                foreach (var vehicle in allMonthlyVehicles)
                {
                    // Check if there's already a registration transaction for this vehicle
                    bool hasRegistrationTransaction = existingTransactions
                        .Any(t => t.VehicleId == vehicle.VehicleId && t.Type == "MONTHLY_SUBSCRIPTION");

                    // If no registration transaction exists, create one
                    if (!hasRegistrationTransaction)
                    {
                        var registrationTransaction = await _transactionService.CreateCashTransactionAsync(
                            vehicle.VehicleId,
                            vehicle.PackageAmount,
                            "MONTHLY_SUBSCRIPTION",
                            $"Historical record: Registration of monthly subscription for {vehicle.VehicleType} with license plate {vehicle.LicensePlate}",
                            $"historical_registration_{vehicle.Id}"
                        );

                        // Set the timestamp to the registration date
                        var filter = Builders<Transaction>.Filter.Eq(t => t.Id, registrationTransaction.Id);
                        var update = Builders<Transaction>.Update.Set(t => t.Timestamp, vehicle.RegistrationDate);
                        await _context.Transactions.UpdateOneAsync(filter, update);

                        // Get the updated transaction
                        registrationTransaction = await _context.Transactions
                            .Find(t => t.Id == registrationTransaction.Id)
                            .FirstOrDefaultAsync();

                        createdTransactions.Add(registrationTransaction);
                        newRegistrationTransactions++;
                    }

                    // Check if there are renewal records (based on LastRenewalDate)
                    if (vehicle.LastRenewalDate.HasValue)
                    {
                        // Check if there's already a renewal transaction for this vehicle
                        bool hasRenewalTransaction = existingTransactions
                            .Any(t => t.VehicleId == vehicle.VehicleId && t.Type == "MONTHLY_RENEWAL");

                        // If no renewal transaction exists, create one
                        if (!hasRenewalTransaction)
                        {
                            var renewalTransaction = await _transactionService.CreateCashTransactionAsync(
                                vehicle.VehicleId,
                                vehicle.PackageAmount,
                                "MONTHLY_RENEWAL",
                                $"Historical record: Renewal of monthly subscription for {vehicle.VehicleType} with license plate {vehicle.LicensePlate}",
                                $"historical_renewal_{vehicle.Id}"
                            );

                            // Set the timestamp to the last renewal date
                            var filter = Builders<Transaction>.Filter.Eq(t => t.Id, renewalTransaction.Id);
                            var update = Builders<Transaction>.Update.Set(t => t.Timestamp, vehicle.LastRenewalDate.Value);
                            await _context.Transactions.UpdateOneAsync(filter, update);

                            // Get the updated transaction
                            renewalTransaction = await _context.Transactions
                                .Find(t => t.Id == renewalTransaction.Id)
                                .FirstOrDefaultAsync();

                            createdTransactions.Add(renewalTransaction);
                            newRenewalTransactions++;
                        }
                    }
                }

                return Ok(new {
                    message = $"Successfully generated historical transactions for monthly vehicles",
                    totalMonthlyVehicles = allMonthlyVehicles.Count,
                    newRegistrationTransactions,
                    newRenewalTransactions,
                    totalCreatedTransactions = newRegistrationTransactions + newRenewalTransactions,
                    createdTransactions = createdTransactions.Select(t => new {
                        t.TransactionId,
                        t.VehicleId,
                        t.Type,
                        t.Amount,
                        Timestamp = t.Timestamp.ToString("yyyy-MM-dd HH:mm:ss"),
                        t.PaymentMethod,
                        t.Status
                    }).ToList()
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error generating historical transactions for monthly vehicles");
                return StatusCode(500, new { error = ex.Message });
            }
        }

        [HttpPost("recognize-vehicle")]
        [Consumes("multipart/form-data")]
        public async Task<IActionResult> RecognizeVehicle([FromForm] VehicleUploadDto uploadDto)
        {
            try
            {
                if (uploadDto?.Image == null || uploadDto.Image.Length == 0)
                {
                    return BadRequest(new { error = "No image uploaded" });
                }

                // Process the image to get license plate and vehicle type
                var (licensePlate, vehicleType) = await _licensePlateService.ProcessVehicleImage(uploadDto.Image);

                if (licensePlate == "Error" || licensePlate == "Unknown")
                {
                    return BadRequest(new { error = "Could not recognize license plate" });
                }

                // Check if license plate is already registered
                var isRegistered = await _monthlyVehicleService.IsVehicleRegisteredMonthlyAsync(licensePlate);
                if (isRegistered)
                {
                    return BadRequest(new {
                        error = $"License plate {licensePlate} is already registered with a valid monthly package",
                        licensePlate,
                        vehicleType
                    });
                }

                return Ok(new
                {
                    success = true,
                    licensePlate,
                    vehicleType
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error recognizing vehicle");
                return StatusCode(500, new { error = ex.Message });
            }
        }
    }

    public class MonthlyVehicleRegistrationRequest
    {
        public string LicensePlate { get; set; }
        public string VehicleType { get; set; }
        public string CustomerName { get; set; }
        public string CustomerPhone { get; set; }
        public string CustomerEmail { get; set; }
        public int PackageDuration { get; set; }
    }

    public class MonthlyVehicleRenewalRequest
    {
        public int PackageDuration { get; set; }
    }

    public class MonthlyVehiclePaymentRequest
    {
        public string VehicleId { get; set; }
        public bool IsRenewal { get; set; }
    }

    public class CompleteRegistrationRequest
    {
        public string RegistrationId { get; set; }
        public string TransactionId { get; set; }
    }
}
