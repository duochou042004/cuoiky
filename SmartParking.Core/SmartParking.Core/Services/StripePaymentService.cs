using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using SmartParking.Core.Models;
using Stripe;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace SmartParking.Core.Services
{
    /// <summary>
    /// Stripe integration backed by the official Stripe.NET SDK.
    ///
    /// Runs in one of two modes:
    ///   • Live   — when a real secret API key is configured. Talks to the Stripe API
    ///              and verifies webhook signatures with the webhook signing secret.
    ///   • Mock   — when no usable key is configured (empty/placeholder) or
    ///              PaymentGateways:Stripe:MockMode is true. Returns simulated
    ///              PaymentIntents and skips signature verification so the system is
    ///              fully runnable in local dev / CI without Stripe credentials.
    /// </summary>
    public class StripePaymentService
    {
        private readonly ILogger<StripePaymentService> _logger;
        private readonly StripePaymentConfig _stripeConfig;
        private readonly bool _mockMode;

        public bool IsMockMode => _mockMode;

        public StripePaymentService(IConfiguration configuration, ILogger<StripePaymentService> logger)
        {
            _logger = logger;

            _stripeConfig = new StripePaymentConfig
            {
                ApiKey = configuration["PaymentGateways:Stripe:ApiKey"],
                WebhookSecret = configuration["PaymentGateways:Stripe:WebhookSecret"]
            };

            // Explicit override.
            bool explicitMock = bool.TryParse(configuration["PaymentGateways:Stripe:MockMode"], out bool m) && m;

            // Auto-fall back to mock when the key is missing or a placeholder, so a
            // misconfigured environment never tries to hit the live API with junk.
            _mockMode = explicitMock || !IsUsableApiKey(_stripeConfig.ApiKey);

            if (!_mockMode)
            {
                StripeConfiguration.ApiKey = _stripeConfig.ApiKey;
                _logger.LogInformation("Stripe running in LIVE mode (key ending …{Suffix}).",
                    _stripeConfig.ApiKey[^4..]);
            }
            else
            {
                _logger.LogInformation("Stripe running in MOCK mode (no usable key configured or MockMode=true).");
            }
        }

        // A real Stripe secret key starts with "sk_" and is not the committed placeholder.
        private static bool IsUsableApiKey(string apiKey)
        {
            if (string.IsNullOrWhiteSpace(apiKey)) return false;
            if (!apiKey.StartsWith("sk_")) return false;
            if (apiKey.Contains("XXXX")) return false; // placeholder in appsettings
            return true;
        }

        /// <summary>
        /// Create a Stripe PaymentIntent. In mock mode returns a simulated intent.
        /// </summary>
        public async Task<StripeCreatePaymentResponse> CreatePaymentIntentAsync(
            string orderId, string description, decimal amount, string transactionId, string idempotencyKey = null)
        {
            // Stripe expects the amount in the smallest currency unit. VND is zero-decimal,
            // but the existing app convention multiplied by 100 — keep that consistent so
            // amounts already stored in transactions continue to line up.
            long amountInSmallestUnit = (long)(amount * 100);

            var metadata = new Dictionary<string, string>
            {
                { "orderId", orderId },
                { "transactionId", transactionId }
            };

            if (_mockMode)
            {
                _logger.LogInformation("Mock mode: returning simulated Stripe PaymentIntent for order {OrderId}", orderId);
                var mockId = $"pi_mock_{Guid.NewGuid():N}";
                return new StripeCreatePaymentResponse
                {
                    Id = mockId,
                    ClientSecret = $"{mockId}_secret_{Guid.NewGuid():N}",
                    Amount = amountInSmallestUnit,
                    Currency = "vnd",
                    Status = "requires_payment_method"
                };
            }

            try
            {
                var options = new PaymentIntentCreateOptions
                {
                    Amount = amountInSmallestUnit,
                    Currency = "vnd",
                    Description = description,
                    Metadata = metadata,
                    PaymentMethodTypes = new List<string> { "card" }
                };

                var requestOptions = new RequestOptions();
                if (!string.IsNullOrEmpty(idempotencyKey))
                {
                    requestOptions.IdempotencyKey = idempotencyKey;
                }

                var service = new PaymentIntentService();
                var intent = await service.CreateAsync(options, requestOptions);

                _logger.LogInformation("Created Stripe PaymentIntent {Id} ({Status})", intent.Id, intent.Status);

                return new StripeCreatePaymentResponse
                {
                    Id = intent.Id,
                    ClientSecret = intent.ClientSecret,
                    Amount = intent.Amount,
                    Currency = intent.Currency,
                    Status = intent.Status
                };
            }
            catch (StripeException ex)
            {
                _logger.LogError(ex, "Stripe API error creating PaymentIntent for order {OrderId}", orderId);
                throw;
            }
        }

        /// <summary>
        /// Verify a webhook signature. In live mode this uses Stripe's signing-secret
        /// based verification; in mock mode there is no real signature so it is skipped.
        /// </summary>
        public bool VerifyWebhookSignature(string payload, string signature)
        {
            if (_mockMode)
            {
                _logger.LogWarning("Mock mode: skipping Stripe webhook signature verification.");
                return true;
            }

            if (string.IsNullOrEmpty(_stripeConfig.WebhookSecret))
            {
                _logger.LogError("Cannot verify Stripe webhook: no webhook signing secret configured.");
                return false;
            }

            try
            {
                // Throws StripeException if the signature does not match.
                EventUtility.ConstructEvent(payload, signature, _stripeConfig.WebhookSecret);
                return true;
            }
            catch (StripeException ex)
            {
                _logger.LogWarning(ex, "Stripe webhook signature verification failed.");
                return false;
            }
        }

        /// <summary>
        /// Build a simulated successful payment event (mock mode only).
        /// </summary>
        public StripeWebhookEvent CreateMockSuccessfulPaymentEvent(
            string paymentIntentId, string orderId, string transactionId, decimal amount)
        {
            long amountInSmallestUnit = (long)(amount * 100);

            return new StripeWebhookEvent
            {
                Id = $"evt_mock_{Guid.NewGuid():N}",
                Type = "payment_intent.succeeded",
                Data = new StripeWebhookEventData
                {
                    Object = new Models.StripePaymentIntent
                    {
                        Id = paymentIntentId,
                        Amount = amountInSmallestUnit,
                        Currency = "vnd",
                        Status = "succeeded",
                        Metadata = new Dictionary<string, string>
                        {
                            { "orderId", orderId },
                            { "transactionId", transactionId }
                        },
                        PaymentMethod = "pm_card_visa",
                        PaymentMethodDetails = new StripePaymentMethodDetails
                        {
                            Card = new StripeCardDetails { Last4 = "4242", Brand = "visa" }
                        }
                    }
                }
            };
        }
    }
}
