using MongoDB.Driver;
using SmartParking.Core.Data;
using SmartParking.Core.Models;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Linq;

namespace SmartParking.Core.Services
{
    public class TransactionService
    {
        private readonly MongoDBContext _context;
        private readonly ILogger<TransactionService> _logger;
        private readonly TimeSpan _transactionTimeout = TimeSpan.FromMinutes(30); // Default timeout for pending transactions

        public TransactionService(MongoDBContext context, ILogger<TransactionService> logger)
        {
            _context = context;
            _logger = logger;
        }

        public async Task<List<Transaction>> GetAllTransactionsAsync()
        {
            return await _context.Transactions.Find(_ => true).ToListAsync();
        }

        public async Task<List<string>> GetAllTransactionIdsAsync()
        {
            var projection = Builders<Transaction>.Projection.Include(t => t.Id);
            var transactions = await _context.Transactions.Find(_ => true)
                .Project<Transaction>(projection)
                .ToListAsync();

            return transactions.Select(t => t.Id).ToList();
        }

        public async Task<Transaction> GetTransactionByIdAsync(string id)
        {
            return await _context.Transactions.Find(t => t.Id == id).FirstOrDefaultAsync();
        }

        public async Task<Transaction> GetTransactionByTransactionId(string transactionId)
        {
            return await _context.Transactions.Find(t => t.TransactionId == transactionId).FirstOrDefaultAsync();
        }

        public async Task<Transaction?> GetStripeTransactionByPaymentIntentIdAsync(string paymentIntentId)
        {
            var filter = Builders<Transaction>.Filter.And(
                Builders<Transaction>.Filter.Eq(t => t.PaymentMethod, "STRIPE"),
                Builders<Transaction>.Filter.Eq(t => t.PaymentDetails.StripePaymentIntentId, paymentIntentId)
            );

            return await _context.Transactions.Find(filter).FirstOrDefaultAsync();
        }

        public async Task<List<Transaction>> GetTransactionsByVehicleIdAsync(string vehicleId)
        {
            return await _context.Transactions.Find(t => t.VehicleId == vehicleId).ToListAsync();
        }

        public async Task<List<Transaction>> GetTransactionsByTypeAsync(string type)
        {
            return await _context.Transactions.Find(t => t.Type == type).ToListAsync();
        }

        public async Task<List<Transaction>> GetTransactionsByDateRangeAsync(DateTime startDate, DateTime endDate)
        {
            return await _context.Transactions
                .Find(t => t.Timestamp >= startDate && t.Timestamp <= endDate)
                .ToListAsync();
        }

        public async Task<List<Transaction>> GetMonthlySubscriptionTransactionsAsync(DateTime startDate, DateTime endDate)
        {
            return await _context.Transactions
                .Find(t => (t.Type == "MONTHLY_SUBSCRIPTION" || t.Type == "MONTHLY_RENEWAL") &&
                           t.Timestamp >= startDate &&
                           t.Timestamp <= endDate)
                .ToListAsync();
        }

        public async Task<Transaction> CreateTransactionAsync(Transaction transaction)
        {
            transaction.Timestamp = DateTime.UtcNow;
            await _context.Transactions.InsertOneAsync(transaction);
            return transaction;
        }

        public async Task<Transaction> UpdateTransactionStatusAsync(string id, string status)
        {
            try
            {
                // Get the current transaction
                var transaction = await GetTransactionByIdAsync(id);
                if (transaction == null)
                {
                    throw new Exception($"Transaction {id} not found");
                }

                // Update the transaction
                transaction.Status = status;
                transaction.PaymentDetails.PaymentTime = DateTime.UtcNow;

                // Use optimistic concurrency control to update
                return await UpdateTransactionAsync(transaction);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error updating transaction status for {id}");
                throw;
            }
        }

        public async Task<Transaction> UpdateTransactionAsync(Transaction transaction)
        {
            try
            {
                // Create a filter that matches the document by ID and version
                var filter = Builders<Transaction>.Filter.And(
                    Builders<Transaction>.Filter.Eq(t => t.Id, transaction.Id),
                    Builders<Transaction>.Filter.Eq(t => t.Version, transaction.Version)
                );

                // Increment the version and update the updatedAt timestamp
                transaction.Version++;
                transaction.UpdatedAt = DateTime.UtcNow;

                // Attempt to replace the document
                var result = await _context.Transactions.ReplaceOneAsync(filter, transaction);

                // Check if the document was updated
                if (result.ModifiedCount == 0)
                {
                    // If no document was modified, it means the version has changed
                    _logger.LogWarning($"Optimistic concurrency control failed for transaction {transaction.Id}. Document was modified by another process.");

                    // Fetch the latest version of the document
                    var currentTransaction = await GetTransactionByIdAsync(transaction.Id);
                    if (currentTransaction != null)
                    {
                        throw new ConcurrencyException($"Transaction {transaction.Id} was modified. Current version: {currentTransaction.Version}, attempted update version: {transaction.Version - 1}");
                    }
                    else
                    {
                        throw new Exception($"Transaction {transaction.Id} not found during update.");
                    }
                }

                return transaction;
            }
            catch (ConcurrencyException)
            {
                // Re-throw concurrency exceptions
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error updating transaction {transaction.Id}");
                throw;
            }
        }

        // Custom exception for concurrency issues
        public class ConcurrencyException : Exception
        {
            public ConcurrencyException(string message) : base(message) { }
        }

        public async Task<Transaction> CreateCashTransactionAsync(string vehicleId, decimal amount, string type, string description, string idempotencyKey = null)
        {
            // Generate idempotency key if not provided
            if (string.IsNullOrEmpty(idempotencyKey))
            {
                idempotencyKey = $"cash_{vehicleId}_{DateTime.UtcNow.Ticks}";
            }

            // Check for existing transaction with this idempotency key
            var existingTransaction = await GetTransactionByIdempotencyKeyAsync(idempotencyKey);
            if (existingTransaction != null)
            {
                _logger.LogInformation($"Cash transaction with idempotency key {idempotencyKey} already exists. Returning existing transaction.");
                return existingTransaction;
            }

            var transaction = new Transaction
            {
                TransactionId = GenerateTransactionId(),
                IdempotencyKey = idempotencyKey,
                VehicleId = vehicleId,
                Amount = amount,
                Type = type,
                PaymentMethod = "CASH",
                Status = "COMPLETED",
                Timestamp = DateTime.UtcNow,
                Description = description,
                PaymentDetails = new PaymentDetails
                {
                    CashierName = "Staff", // This could be updated with actual staff name if authentication is implemented
                    PaymentTime = DateTime.UtcNow
                }
            };

            await _context.Transactions.InsertOneAsync(transaction);
            _logger.LogInformation($"Created cash transaction {transaction.TransactionId} with idempotency key {idempotencyKey}");
            return transaction;
        }

        public async Task<Transaction> CreateStripeTransactionAsync(string vehicleId, decimal amount, string type, string description, string idempotencyKey = null)
        {
            // Generate idempotency key if not provided
            if (string.IsNullOrEmpty(idempotencyKey))
            {
                idempotencyKey = $"stripe_{vehicleId}_{DateTime.UtcNow.Ticks}";
            }

            // Check for existing transaction with this idempotency key
            var existingTransaction = await GetTransactionByIdempotencyKeyAsync(idempotencyKey);
            if (existingTransaction != null)
            {
                _logger.LogInformation($"Stripe transaction with idempotency key {idempotencyKey} already exists. Returning existing transaction.");
                return existingTransaction;
            }

            var transaction = new Transaction
            {
                TransactionId = GenerateTransactionId(),
                IdempotencyKey = idempotencyKey,
                VehicleId = vehicleId,
                Amount = amount,
                Type = type,
                PaymentMethod = "STRIPE",
                Status = "PENDING", // Will be updated when payment is completed
                Timestamp = DateTime.UtcNow,
                ExpiresAt = DateTime.UtcNow.Add(_transactionTimeout),
                Description = description,
                PaymentDetails = new PaymentDetails
                {
                    PaymentTime = null, // Will be updated when payment is completed
                    StripePaymentIntentId = null, // Will be updated when payment is created
                    CardLast4 = null // Will be updated when payment is completed
                }
            };

            await _context.Transactions.InsertOneAsync(transaction);
            _logger.LogInformation($"Created Stripe transaction {transaction.TransactionId} with idempotency key {idempotencyKey}");
            return transaction;
        }

        public async Task<Transaction> CreateMomoTransactionAsync(string vehicleId, decimal amount, string type, string description, string? transactionReference = null, string idempotencyKey = null)
        {
            // Generate idempotency key if not provided
            if (string.IsNullOrEmpty(idempotencyKey))
            {
                idempotencyKey = $"momo_{vehicleId}_{DateTime.UtcNow.Ticks}";
            }

            // Check for existing transaction with this idempotency key
            var existingTransaction = await GetTransactionByIdempotencyKeyAsync(idempotencyKey);
            if (existingTransaction != null)
            {
                _logger.LogInformation($"Momo transaction with idempotency key {idempotencyKey} already exists. Returning existing transaction.");
                return existingTransaction;
            }

            var transaction = new Transaction
            {
                TransactionId = GenerateTransactionId(),
                IdempotencyKey = idempotencyKey,
                VehicleId = vehicleId,
                Amount = amount,
                Type = type,
                PaymentMethod = "MOMO",
                Status = "PENDING", // Will be updated when payment is completed
                Timestamp = DateTime.UtcNow,
                ExpiresAt = DateTime.UtcNow.Add(_transactionTimeout),
                Description = description,
                PaymentDetails = new PaymentDetails
                {
                    PaymentTime = null, // Will be updated when payment is completed
                    TransactionReference = transactionReference,
                    MomoTransactionId = null // Will be updated when payment is completed
                }
            };

            await _context.Transactions.InsertOneAsync(transaction);
            _logger.LogInformation($"Created Momo transaction {transaction.TransactionId} with idempotency key {idempotencyKey}");
            return transaction;
        }

        public async Task<Dictionary<string, decimal>> GetRevenueByPaymentMethodAsync(DateTime startDate, DateTime endDate)
        {
            var transactions = await _context.Transactions
                .Find(t => t.Timestamp >= startDate && t.Timestamp <= endDate && t.Status == "COMPLETED")
                .ToListAsync();

            var revenue = new Dictionary<string, decimal>
            {
                { "CASH", 0 },
                { "STRIPE", 0 },
                { "MOMO", 0 },
                { "TOTAL", 0 }
            };

            foreach (var transaction in transactions)
            {
                if (revenue.ContainsKey(transaction.PaymentMethod))
                {
                    revenue[transaction.PaymentMethod] += transaction.Amount;
                }
                revenue["TOTAL"] += transaction.Amount;
            }

            return revenue;
        }

        public async Task<Dictionary<string, decimal>> GetRevenueByVehicleTypeAsync(DateTime startDate, DateTime endDate)
        {
            var transactions = await _context.Transactions
                .Find(t => t.Timestamp >= startDate && t.Timestamp <= endDate && t.Status == "COMPLETED")
                .ToListAsync();

            var revenue = new Dictionary<string, decimal>
            {
                { "CAR", 0 },
                { "MOTORCYCLE", 0 },
                { "TOTAL", 0 }
            };

            foreach (var transaction in transactions)
            {
                if (transaction.VehicleId.StartsWith("C"))
                {
                    revenue["CAR"] += transaction.Amount;
                }
                else if (transaction.VehicleId.StartsWith("M"))
                {
                    revenue["MOTORCYCLE"] += transaction.Amount;
                }
                revenue["TOTAL"] += transaction.Amount;
            }

            return revenue;
        }

        public async Task<Dictionary<string, decimal>> GetMonthlySubscriptionRevenueAsync(DateTime startDate, DateTime endDate)
        {
            var transactions = await _context.Transactions
                .Find(t => (t.Type == "MONTHLY_SUBSCRIPTION" || t.Type == "MONTHLY_RENEWAL") &&
                           t.Timestamp >= startDate &&
                           t.Timestamp <= endDate &&
                           t.Status == "COMPLETED")
                .ToListAsync();

            var revenue = new Dictionary<string, decimal>
            {
                { "NEW_SUBSCRIPTION", 0 },
                { "RENEWAL", 0 },
                { "CAR", 0 },
                { "MOTORCYCLE", 0 },
                { "CASH", 0 },
                { "MOMO", 0 },
                { "STRIPE", 0 },
                { "TOTAL", 0 }
            };

            foreach (var transaction in transactions)
            {
                // Categorize by transaction type
                if (transaction.Type == "MONTHLY_SUBSCRIPTION")
                {
                    revenue["NEW_SUBSCRIPTION"] += transaction.Amount;
                }
                else if (transaction.Type == "MONTHLY_RENEWAL")
                {
                    revenue["RENEWAL"] += transaction.Amount;
                }

                // Categorize by vehicle type
                if (transaction.VehicleId.StartsWith("C"))
                {
                    revenue["CAR"] += transaction.Amount;
                }
                else if (transaction.VehicleId.StartsWith("M"))
                {
                    revenue["MOTORCYCLE"] += transaction.Amount;
                }

                // Categorize by payment method
                if (revenue.ContainsKey(transaction.PaymentMethod))
                {
                    revenue[transaction.PaymentMethod] += transaction.Amount;
                }

                revenue["TOTAL"] += transaction.Amount;
            }

            return revenue;
        }

        public async Task<List<object>> GetDailyMonthlySubscriptionRevenueAsync(DateTime startDate, DateTime endDate)
        {
            var transactions = await _context.Transactions
                .Find(t => (t.Type == "MONTHLY_SUBSCRIPTION" || t.Type == "MONTHLY_RENEWAL") &&
                           t.Timestamp >= startDate &&
                           t.Timestamp <= endDate &&
                           t.Status == "COMPLETED")
                .ToListAsync();

            var dailyRevenue = transactions
                .GroupBy(t => t.Timestamp.Date)
                .Select(g => new
                {
                    Date = g.Key,
                    Total = g.Sum(t => t.Amount),
                    NewSubscription = g.Where(t => t.Type == "MONTHLY_SUBSCRIPTION").Sum(t => t.Amount),
                    Renewal = g.Where(t => t.Type == "MONTHLY_RENEWAL").Sum(t => t.Amount),
                    Car = g.Where(t => t.VehicleId.StartsWith("C")).Sum(t => t.Amount),
                    Motorcycle = g.Where(t => t.VehicleId.StartsWith("M")).Sum(t => t.Amount),
                    Cash = g.Where(t => t.PaymentMethod == "CASH").Sum(t => t.Amount),
                    Momo = g.Where(t => t.PaymentMethod == "MOMO").Sum(t => t.Amount),
                    Stripe = g.Where(t => t.PaymentMethod == "STRIPE").Sum(t => t.Amount)
                })
                .OrderBy(d => d.Date)
                .ToList<object>();

            return dailyRevenue;
        }

        private string GenerateTransactionId()
        {
            return $"TRX{DateTime.Now:yyyyMMddHHmmss}{new Random().Next(1000, 9999)}";
        }

        /// <summary>
        /// Check for and handle timed-out transactions
        /// </summary>
        public async Task HandleTimedOutTransactionsAsync()
        {
            try
            {
                var cutoffTime = DateTime.UtcNow.Subtract(_transactionTimeout);

                // Find pending transactions that have exceeded the timeout
                var timedOutTransactions = await _context.Transactions
                    .Find(t => t.Status == "PENDING" && t.Timestamp < cutoffTime)
                    .ToListAsync();

                _logger.LogInformation($"Found {timedOutTransactions.Count} timed-out transactions");

                foreach (var transaction in timedOutTransactions)
                {
                    try
                    {
                        // Update the transaction status to TIMEOUT
                        transaction.Status = "TIMEOUT";
                        transaction.UpdatedAt = DateTime.UtcNow;
                        transaction.Version++;

                        // Use a filter that doesn't include version to avoid concurrency issues
                        // since we're handling old transactions that might have been modified
                        var filter = Builders<Transaction>.Filter.Eq(t => t.Id, transaction.Id);
                        var update = Builders<Transaction>.Update
                            .Set(t => t.Status, "TIMEOUT")
                            .Set(t => t.UpdatedAt, DateTime.UtcNow)
                            .Inc(t => t.Version, 1);

                        var result = await _context.Transactions.UpdateOneAsync(filter, update);

                        if (result.ModifiedCount > 0)
                        {
                            _logger.LogInformation($"Updated timed-out transaction {transaction.TransactionId}");
                        }
                        else
                        {
                            _logger.LogWarning($"Failed to update timed-out transaction {transaction.TransactionId}");
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, $"Error handling timed-out transaction {transaction.TransactionId}");
                        // Continue with next transaction
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error handling timed-out transactions");
            }
        }

        /// <summary>
        /// Get a transaction by idempotency key
        /// </summary>
        public async Task<Transaction> GetTransactionByIdempotencyKeyAsync(string idempotencyKey)
        {
            return await _context.Transactions.Find(t => t.IdempotencyKey == idempotencyKey).FirstOrDefaultAsync();
        }

        /// <summary>
        /// Get all monthly subscription transactions (both new registrations and renewals)
        /// </summary>
        public async Task<List<Transaction>> GetAllMonthlySubscriptionTransactionsAsync()
        {
            return await _context.Transactions
                .Find(t => t.Type == "MONTHLY_SUBSCRIPTION" || t.Type == "MONTHLY_RENEWAL")
                .ToListAsync();
        }

        /// <summary>
        /// Create transaction with idempotency check
        /// </summary>
        public async Task<Transaction> CreateTransactionWithIdempotencyAsync(Transaction transaction, string idempotencyKey)
        {
            // Check if a transaction with this idempotency key already exists
            var existingTransaction = await GetTransactionByIdempotencyKeyAsync(idempotencyKey);
            if (existingTransaction != null)
            {
                _logger.LogInformation($"Transaction with idempotency key {idempotencyKey} already exists. Returning existing transaction.");
                return existingTransaction;
            }

            // Set the idempotency key
            transaction.IdempotencyKey = idempotencyKey;

            // Set expiration time for pending transactions
            if (transaction.Status == "PENDING")
            {
                transaction.ExpiresAt = DateTime.UtcNow.Add(_transactionTimeout);
            }

            // Create the transaction
            await _context.Transactions.InsertOneAsync(transaction);
            return transaction;
        }
    }
}
