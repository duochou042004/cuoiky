using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using SmartParking.Core.Models;
using SmartParking.Core.Services;
using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Text;
using Microsoft.AspNetCore.StaticFiles;

namespace SmartParking.Core.Controllers
{
    [Route("api/payment")]
    [ApiController]
    public class PaymentController : ControllerBase
    {
        private readonly TransactionService _transactionService;
        private readonly ParkingService _parkingService;
        private readonly ParkingFeeService _parkingFeeService;
        private readonly MomoPaymentService _momoPaymentService;
        private readonly StripePaymentService _stripePaymentService;
        private readonly InvoiceService _invoiceService;
        private readonly IConfiguration _configuration;
        private readonly ILogger<PaymentController> _logger;

        public PaymentController(
            TransactionService transactionService,
            ParkingService parkingService,
            ParkingFeeService parkingFeeService,
            MomoPaymentService momoPaymentService,
            StripePaymentService stripePaymentService,
            InvoiceService invoiceService,
            IConfiguration configuration,
            ILogger<PaymentController> logger)
        {
            _transactionService = transactionService;
            _parkingService = parkingService;
            _parkingFeeService = parkingFeeService;
            _momoPaymentService = momoPaymentService;
            _stripePaymentService = stripePaymentService;
            _invoiceService = invoiceService;
            _configuration = configuration;
            _logger = logger;
        }

        [HttpPost("cash")]
        public async Task<IActionResult> ProcessCashPayment([FromBody] PaymentRequest request)
        {
            try
            {
                // Validate request
                if (string.IsNullOrEmpty(request.VehicleId))
                {
                    return BadRequest(new { error = "Vehicle ID is required" });
                }

                // Get vehicle details
                var vehicle = await _parkingService.GetVehicleById(request.VehicleId);
                if (vehicle == null)
                {
                    return NotFound(new { error = $"Vehicle with ID {request.VehicleId} not found" });
                }

                // Calculate parking fee if not provided
                decimal amount = request.Amount;
                if (amount <= 0 && vehicle.ExitTime.HasValue)
                {
                    amount = _parkingFeeService.CalculateParkingFee(
                        vehicle.VehicleType,
                        vehicle.EntryTime,
                        vehicle.ExitTime.Value,
                        vehicle.IsMonthlyRegistered
                    );
                }
                else if (amount <= 0)
                {
                    amount = _parkingFeeService.CalculateParkingFee(
                        vehicle.VehicleType,
                        vehicle.EntryTime,
                        DateTime.Now,
                        vehicle.IsMonthlyRegistered
                    );
                }

                // Create transaction
                var transaction = await _transactionService.CreateCashTransactionAsync(
                    request.VehicleId,
                    amount,
                    "PARKING_FEE",
                    $"Parking fee for {vehicle.VehicleType} with license plate {vehicle.LicensePlate}"
                );

                return Ok(new
                {
                    message = "Cash payment processed successfully",
                    transaction = transaction,
                    vehicle = vehicle
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error processing cash payment for vehicle {request.VehicleId}");
                return StatusCode(500, new { error = ex.Message });
            }
        }

        [HttpGet("calculate-fee/{vehicleId}")]
        public async Task<IActionResult> CalculateParkingFee(string vehicleId)
        {
            try
            {
                // Get vehicle details
                var vehicle = await _parkingService.GetVehicleById(vehicleId);
                if (vehicle == null)
                {
                    return NotFound(new { error = $"Vehicle with ID {vehicleId} not found" });
                }

                // Calculate parking fee
                var exitTime = vehicle.ExitTime ?? DateTime.Now;
                var fee = _parkingFeeService.CalculateParkingFee(
                    vehicle.VehicleType,
                    vehicle.EntryTime,
                    exitTime,
                    vehicle.IsMonthlyRegistered
                );

                // Calculate parking duration
                var duration = _parkingFeeService.FormatParkingDuration(
                    vehicle.EntryTime,
                    exitTime
                );

                return Ok(new
                {
                    vehicleId = vehicle.VehicleId,
                    licensePlate = vehicle.LicensePlate,
                    vehicleType = vehicle.VehicleType,
                    entryTime = vehicle.EntryTime,
                    exitTime = exitTime,
                    parkingDuration = duration,
                    fee = fee
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error calculating parking fee for vehicle {vehicleId}");
                return StatusCode(500, new { error = ex.Message });
            }
        }

        [HttpPost("momo")]
        public async Task<IActionResult> CreateMomoPayment([FromBody] PaymentRequest request)
        {
            try
            {
                // Validate request
                if (string.IsNullOrEmpty(request.VehicleId))
                {
                    return BadRequest(new { error = "Vehicle ID is required" });
                }

                // Get vehicle details
                var vehicle = await _parkingService.GetVehicleById(request.VehicleId);
                if (vehicle == null)
                {
                    return NotFound(new { error = $"Vehicle with ID {request.VehicleId} not found" });
                }

                // Calculate parking fee if not provided
                decimal amount = request.Amount;
                if (amount <= 0 && vehicle.ExitTime.HasValue)
                {
                    amount = _parkingFeeService.CalculateParkingFee(
                        vehicle.VehicleType,
                        vehicle.EntryTime,
                        vehicle.ExitTime.Value,
                        vehicle.IsMonthlyRegistered
                    );
                }
                else if (amount <= 0)
                {
                    amount = _parkingFeeService.CalculateParkingFee(
                        vehicle.VehicleType,
                        vehicle.EntryTime,
                        DateTime.Now,
                        vehicle.IsMonthlyRegistered
                    );
                }

                // Generate idempotency key
                string idempotencyKey = $"momo_payment_{request.VehicleId}_{DateTime.UtcNow.Ticks}";

                // Create transaction (pending status)
                var transaction = await _transactionService.CreateMomoTransactionAsync(
                    request.VehicleId,
                    amount,
                    "PARKING_FEE",
                    $"Parking fee for {vehicle.VehicleType} with license plate {vehicle.LicensePlate}",
                    null, // transactionReference
                    idempotencyKey
                );

                // Create Momo payment request
                var orderInfo = $"Parking fee for {vehicle.LicensePlate}";
                var momoResponse = await _momoPaymentService.CreatePaymentAsync(
                    transaction.TransactionId,
                    orderInfo,
                    amount,
                    transaction.Id, // Store transaction ID as extra data
                    idempotencyKey // Pass idempotency key to Momo service
                );

                if (momoResponse.ResultCode != 0)
                {
                    return BadRequest(new { error = momoResponse.Message });
                }

                return Ok(new
                {
                    message = "Momo payment request created successfully",
                    transaction = transaction,
                    paymentUrl = momoResponse.PayUrl,
                    qrCodeUrl = momoResponse.QrCodeUrl,
                    deeplink = momoResponse.Deeplink
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error creating Momo payment for vehicle {request.VehicleId}");
                return StatusCode(500, new { error = ex.Message });
            }
        }

        [HttpPost("stripe")]
        public async Task<IActionResult> CreateStripePayment([FromBody] PaymentRequest request)
        {
            try
            {
                // Validate request
                if (string.IsNullOrEmpty(request.VehicleId))
                {
                    return BadRequest(new { error = "Vehicle ID is required" });
                }

                // Get vehicle details
                var vehicle = await _parkingService.GetVehicleById(request.VehicleId);
                if (vehicle == null)
                {
                    return NotFound(new { error = $"Vehicle with ID {request.VehicleId} not found" });
                }

                // Calculate parking fee if not provided
                decimal amount = request.Amount;
                if (amount <= 0 && vehicle.ExitTime.HasValue)
                {
                    amount = _parkingFeeService.CalculateParkingFee(
                        vehicle.VehicleType,
                        vehicle.EntryTime,
                        vehicle.ExitTime.Value,
                        vehicle.IsMonthlyRegistered
                    );
                }
                else if (amount <= 0)
                {
                    amount = _parkingFeeService.CalculateParkingFee(
                        vehicle.VehicleType,
                        vehicle.EntryTime,
                        DateTime.Now,
                        vehicle.IsMonthlyRegistered
                    );
                }

                // Generate idempotency key
                string idempotencyKey = $"stripe_payment_{request.VehicleId}_{DateTime.UtcNow.Ticks}";

                // Create transaction (pending status)
                var transaction = await _transactionService.CreateStripeTransactionAsync(
                    request.VehicleId,
                    amount,
                    "PARKING_FEE",
                    $"Parking fee for {vehicle.VehicleType} with license plate {vehicle.LicensePlate}",
                    idempotencyKey
                );

                // Create Stripe payment intent
                var description = $"Parking fee for {vehicle.LicensePlate}";
                var stripeResponse = await _stripePaymentService.CreatePaymentIntentAsync(
                    transaction.TransactionId,
                    description,
                    amount,
                    transaction.Id, // Store transaction ID as metadata
                    idempotencyKey // Pass idempotency key to Stripe service
                );

                // Update transaction with payment intent ID
                transaction.PaymentDetails.StripePaymentIntentId = stripeResponse.Id;
                await _transactionService.UpdateTransactionAsync(transaction);

                // Use the service's effective mode (it auto-falls back to mock when no
                // usable key is configured), not just the raw config flag.
                bool mockMode = _stripePaymentService.IsMockMode;
                if (mockMode)
                {
                    // Simulate a webhook notification for testing
                    _logger.LogInformation($"Mock mode enabled. Simulating Stripe webhook notification for payment intent {stripeResponse.Id}");

                    // For mock mode, directly update the transaction status
                    transaction.Status = "COMPLETED";
                    transaction.PaymentDetails.PaymentTime = DateTime.UtcNow;
                    transaction.PaymentDetails.CardLast4 = "4242";

                    // Save transaction
                    await _transactionService.UpdateTransactionAsync(transaction);

                    _logger.LogInformation($"Mock Stripe payment completed for transaction: {transaction.TransactionId}");
                }

                return Ok(new
                {
                    message = "Stripe payment intent created successfully",
                    transaction = transaction,
                    clientSecret = stripeResponse.ClientSecret,
                    paymentIntentId = stripeResponse.Id,
                    mockMode = mockMode
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error creating Stripe payment for vehicle {request.VehicleId}");
                return StatusCode(500, new { error = ex.Message });
            }
        }

        [HttpPost("webhook/momo")]
        public async Task<IActionResult> ProcessMomoWebhook()
        {
            try
            {
                // Read request body
                using var reader = new StreamReader(Request.Body);
                var requestBody = await reader.ReadToEndAsync();
                _logger.LogInformation($"Received Momo webhook notification: {requestBody}");

                // Parse notification
                var notification = JsonSerializer.Deserialize<MomoPaymentNotification>(requestBody);

                // Verify signature
                if (!_momoPaymentService.VerifyPaymentNotification(notification))
                {
                    _logger.LogWarning("Invalid Momo payment notification signature");
                    return BadRequest(new { error = "Invalid signature" });
                }

                // Check result code
                if (notification.ResultCode != 0)
                {
                    _logger.LogWarning($"Momo payment failed: {notification.Message}");
                    return Ok(new { message = "Notification received but payment failed" });
                }

                // Find transaction by order ID (which is our transaction ID)
                var transaction = await _transactionService.GetTransactionByTransactionId(notification.OrderId);
                if (transaction == null)
                {
                    _logger.LogWarning($"Transaction not found for order ID: {notification.OrderId}");
                    return NotFound(new { error = $"Transaction not found for order ID: {notification.OrderId}" });
                }

                try
                {
                    // Update transaction status
                    transaction.Status = "COMPLETED";
                    transaction.PaymentDetails.PaymentTime = DateTime.UtcNow;
                    transaction.PaymentDetails.MomoTransactionId = notification.TransId.ToString();
                    transaction.PaymentDetails.TransactionReference = notification.ExtraData; // Store the transaction reference

                    // Save transaction with optimistic concurrency control
                    await _transactionService.UpdateTransactionAsync(transaction);

                    _logger.LogInformation($"Momo payment completed for transaction: {transaction.TransactionId}");
                }
                catch (TransactionService.ConcurrencyException ex)
                {
                    // Handle concurrency conflict - the transaction was already updated
                    _logger.LogWarning(ex, $"Concurrency conflict when updating Momo transaction {transaction.TransactionId}. This may be a duplicate webhook notification.");

                    // Get the latest version of the transaction
                    var updatedTransaction = await _transactionService.GetTransactionByIdAsync(transaction.Id);
                    if (updatedTransaction != null && updatedTransaction.Status == "COMPLETED")
                    {
                        // Transaction is already completed, this is likely a duplicate webhook
                        _logger.LogInformation($"Transaction {transaction.TransactionId} is already completed. Ignoring duplicate webhook.");
                    }
                    else if (updatedTransaction != null)
                    {
                        // Transaction exists but is not completed, try updating it again
                        updatedTransaction.Status = "COMPLETED";
                        updatedTransaction.PaymentDetails.PaymentTime = DateTime.UtcNow;
                        updatedTransaction.PaymentDetails.MomoTransactionId = notification.TransId.ToString();
                        updatedTransaction.PaymentDetails.TransactionReference = notification.ExtraData;

                        await _transactionService.UpdateTransactionAsync(updatedTransaction);
                        _logger.LogInformation($"Successfully updated Momo transaction {updatedTransaction.TransactionId} after resolving concurrency conflict");
                    }
                }

                return Ok(new { message = "Payment notification processed successfully" });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing Momo webhook notification");
                return StatusCode(500, new { error = ex.Message });
            }
        }

        [HttpPost("webhook/stripe")]
        public async Task<IActionResult> ProcessStripeWebhook()
        {
            try
            {
                // Read request body
                using var reader = new StreamReader(Request.Body);
                var requestBody = await reader.ReadToEndAsync();
                _logger.LogInformation($"Received Stripe webhook notification: {requestBody}");

                // Get Stripe signature from header
                var signature = Request.Headers["Stripe-Signature"];

                // Verify signature
                if (!_stripePaymentService.VerifyWebhookSignature(requestBody, signature))
                {
                    _logger.LogWarning("Invalid Stripe webhook signature");
                    return BadRequest(new { error = "Invalid signature" });
                }

                // Parse event
                var stripeEvent = JsonSerializer.Deserialize<StripeWebhookEvent>(requestBody);

                // Handle different event types
                if (stripeEvent.Type == "payment_intent.succeeded")
                {
                    var paymentIntent = stripeEvent.Data.Object;
                    await ProcessStripePaymentSucceeded(paymentIntent);
                }
                else if (stripeEvent.Type == "payment_intent.payment_failed")
                {
                    var paymentIntent = stripeEvent.Data.Object;
                    _logger.LogWarning($"Stripe payment failed for payment intent ID: {paymentIntent.Id}");
                }

                return Ok(new { message = "Webhook received and processed" });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing Stripe webhook notification");
                return StatusCode(500, new { error = ex.Message });
            }
        }

        /// <summary>
        /// Process a successful Stripe payment
        /// </summary>
        private async Task ProcessStripePaymentSucceeded(StripePaymentIntent paymentIntent)
        {
            try
            {
                // Find transaction by payment intent ID - use a more efficient query
                var transactions = await _transactionService.GetAllTransactionsAsync();
                var transaction = transactions.FirstOrDefault(t =>
                    t.PaymentMethod == "STRIPE" &&
                    t.PaymentDetails?.StripePaymentIntentId == paymentIntent.Id);

                if (transaction == null && paymentIntent.Metadata != null && paymentIntent.Metadata.TryGetValue("orderId", out var orderId))
                {
                    // Try to find by order ID from metadata
                    transaction = await _transactionService.GetTransactionByTransactionId(orderId);
                }

                if (transaction == null)
                {
                    _logger.LogWarning($"Transaction not found for payment intent ID: {paymentIntent.Id}");
                    return;
                }

                // Check if transaction is already completed
                if (transaction.Status == "COMPLETED")
                {
                    _logger.LogInformation($"Transaction {transaction.TransactionId} is already completed. Ignoring duplicate webhook.");
                    return;
                }

                try
                {
                    // Update transaction status
                    transaction.Status = "COMPLETED";
                    transaction.PaymentDetails.PaymentTime = DateTime.UtcNow;
                    transaction.PaymentDetails.StripePaymentIntentId = paymentIntent.Id;

                    // Add card details if available
                    if (paymentIntent.PaymentMethodDetails?.Card != null)
                    {
                        transaction.PaymentDetails.CardLast4 = paymentIntent.PaymentMethodDetails.Card.Last4;
                    }

                    // Save transaction with optimistic concurrency control
                    await _transactionService.UpdateTransactionAsync(transaction);

                    _logger.LogInformation($"Stripe payment completed for transaction: {transaction.TransactionId}");
                }
                catch (TransactionService.ConcurrencyException ex)
                {
                    // Handle concurrency conflict - the transaction was already updated
                    _logger.LogWarning(ex, $"Concurrency conflict when updating Stripe transaction {transaction.TransactionId}. This may be a duplicate webhook notification.");

                    // Get the latest version of the transaction
                    var updatedTransaction = await _transactionService.GetTransactionByIdAsync(transaction.Id);
                    if (updatedTransaction != null && updatedTransaction.Status == "COMPLETED")
                    {
                        // Transaction is already completed, this is likely a duplicate webhook
                        _logger.LogInformation($"Transaction {transaction.TransactionId} is already completed. Ignoring duplicate webhook.");
                    }
                    else if (updatedTransaction != null)
                    {
                        // Transaction exists but is not completed, try updating it again
                        updatedTransaction.Status = "COMPLETED";
                        updatedTransaction.PaymentDetails.PaymentTime = DateTime.UtcNow;
                        updatedTransaction.PaymentDetails.StripePaymentIntentId = paymentIntent.Id;

                        if (paymentIntent.PaymentMethodDetails?.Card != null)
                        {
                            updatedTransaction.PaymentDetails.CardLast4 = paymentIntent.PaymentMethodDetails.Card.Last4;
                        }

                        await _transactionService.UpdateTransactionAsync(updatedTransaction);
                        _logger.LogInformation($"Successfully updated Stripe transaction {updatedTransaction.TransactionId} after resolving concurrency conflict");
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error processing Stripe payment success for payment intent ID: {paymentIntent.Id}");
            }
        }

        [HttpGet("transactions")]
        public async Task<IActionResult> GetTransactions()
        {
            try
            {
                // Get transactions one by one to avoid deserialization issues
                var transactionIds = await _transactionService.GetAllTransactionIdsAsync();
                var transactions = new List<Transaction>();

                foreach (var id in transactionIds)
                {
                    try
                    {
                        var transaction = await _transactionService.GetTransactionByIdAsync(id);
                        if (transaction != null)
                        {
                            transactions.Add(transaction);
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, $"Error deserializing transaction {id}");
                        // Continue with next transaction
                    }
                }

                return Ok(transactions);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting transactions");
                return StatusCode(500, new { error = ex.Message });
            }
        }

        [HttpGet("transactions/{transactionId}")]
        public async Task<IActionResult> GetTransaction(string transactionId)
        {
            try
            {
                var transaction = await _transactionService.GetTransactionByTransactionId(transactionId);
                if (transaction == null)
                {
                    return NotFound(new { error = $"Transaction with ID {transactionId} not found" });
                }

                return Ok(transaction);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error getting transaction {transactionId}");
                return StatusCode(500, new { error = ex.Message });
            }
        }

        [HttpGet("invoice/generate/{transactionId}")]
        public async Task<IActionResult> GenerateInvoice(string transactionId)
        {
            try
            {
                // Check if transaction exists
                var transaction = await _transactionService.GetTransactionByTransactionId(transactionId);
                if (transaction == null)
                {
                    return NotFound(new { error = $"Transaction with ID {transactionId} not found" });
                }

                // Check if transaction is completed
                if (transaction.Status != "COMPLETED")
                {
                    return BadRequest(new { error = "Cannot generate invoice for incomplete transaction" });
                }

                // Generate invoice
                string invoiceUrl = await _invoiceService.GenerateInvoiceAsync(transactionId);

                return Ok(new { invoiceUrl = invoiceUrl });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error generating invoice for transaction {transactionId}");
                return StatusCode(500, new { error = ex.Message });
            }
        }

        [HttpGet("invoice/{fileName}")]
        public IActionResult DownloadInvoice(string fileName)
        {
            try
            {
                // Get file path
                string filePath = _invoiceService.GetInvoiceFilePath(fileName);

                // Check if file exists
                if (!System.IO.File.Exists(filePath))
                {
                    return NotFound(new { error = $"Invoice file {fileName} not found" });
                }

                // Get content type
                var provider = new FileExtensionContentTypeProvider();
                if (!provider.TryGetContentType(filePath, out var contentType))
                {
                    contentType = "application/octet-stream";
                }

                // Return file
                var fileStream = new FileStream(filePath, FileMode.Open, FileAccess.Read);
                return File(fileStream, contentType, fileName);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error downloading invoice {fileName}");
                return StatusCode(500, new { error = ex.Message });
            }
        }
    }

    public class PaymentRequest
    {
        public string VehicleId { get; set; } = string.Empty;
        public decimal Amount { get; set; }
        public string PaymentMethod { get; set; } = string.Empty;
    }
}
