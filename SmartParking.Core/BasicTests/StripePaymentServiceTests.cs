using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using SmartParking.Core.Services;

namespace SmartParking.Core.Tests
{
    public class StripePaymentServiceTests
    {
        private static StripePaymentService BuildService(Dictionary<string, string?> settings)
        {
            var config = new ConfigurationBuilder()
                .AddInMemoryCollection(settings)
                .Build();
            return new StripePaymentService(config, NullLogger<StripePaymentService>.Instance);
        }

        [Fact]
        public void Defaults_To_MockMode_When_No_Key_Configured()
        {
            var service = BuildService(new Dictionary<string, string?>());
            Assert.True(service.IsMockMode);
        }

        [Fact]
        public void Placeholder_Key_Is_Treated_As_MockMode()
        {
            var service = BuildService(new Dictionary<string, string?>
            {
                ["PaymentGateways:Stripe:ApiKey"] = "sk_test_51OvXXXXXXXXXXXXXXXXXXXXX",
                ["PaymentGateways:Stripe:MockMode"] = "false"
            });
            Assert.True(service.IsMockMode);
        }

        [Fact]
        public void Explicit_MockMode_Overrides_Real_Key()
        {
            var service = BuildService(new Dictionary<string, string?>
            {
                ["PaymentGateways:Stripe:ApiKey"] = "sk_live_realLookingKey1234567890",
                ["PaymentGateways:Stripe:MockMode"] = "true"
            });
            Assert.True(service.IsMockMode);
        }

        [Fact]
        public async Task CreatePaymentIntent_In_MockMode_Returns_Mock_Intent_With_Scaled_Amount()
        {
            var service = BuildService(new Dictionary<string, string?>());

            var result = await service.CreatePaymentIntentAsync(
                orderId: "ORDER-1", description: "Test", amount: 30000m, transactionId: "TX-1");

            Assert.NotNull(result);
            Assert.StartsWith("pi_mock_", result.Id);
            Assert.False(string.IsNullOrEmpty(result.ClientSecret));
            Assert.Equal(3000000, result.Amount); // 30000 * 100 (app convention)
            Assert.Equal("vnd", result.Currency);
            Assert.Equal("requires_payment_method", result.Status);
        }

        [Fact]
        public void VerifyWebhookSignature_In_MockMode_Returns_True()
        {
            var service = BuildService(new Dictionary<string, string?>());
            Assert.True(service.VerifyWebhookSignature("{}", "any-signature"));
        }

        [Fact]
        public void CreateMockSuccessfulPaymentEvent_Has_Succeeded_Shape()
        {
            var service = BuildService(new Dictionary<string, string?>());

            var evt = service.CreateMockSuccessfulPaymentEvent("pi_123", "ORDER-1", "TX-1", 30000m);

            Assert.Equal("payment_intent.succeeded", evt.Type);
            Assert.Equal("pi_123", evt.Data.Object.Id);
            Assert.Equal("succeeded", evt.Data.Object.Status);
            Assert.Equal(3000000, evt.Data.Object.Amount);
            Assert.Equal("TX-1", evt.Data.Object.Metadata["transactionId"]);
            Assert.Equal("4242", evt.Data.Object.PaymentMethodDetails.Card.Last4);
        }
    }
}
