using BasicTests.Infrastructure;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using SmartParking.Core.Controllers;
using SmartParking.Core.Models;
using SmartParking.Core.Services;
using System.Text;
using System.Text.Json;

namespace BasicTests.Payment;

public sealed class PaymentWebhookIntegrationTests : IClassFixture<MongoIntegrationFixture>
{
    private readonly MongoIntegrationFixture _fixture;

    public PaymentWebhookIntegrationTests(MongoIntegrationFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task Momo_webhook_marks_pending_transaction_as_completed()
    {
        if (!_fixture.IsAvailable)
        {
            return;
        }

        var transactionService = TestServiceFactory.CreateTransactionService(_fixture);
        var transaction = await transactionService.CreateMomoTransactionAsync("M001", 10000, "PARKING_FEE", "Test parking fee", "momo_ref_001", "momo_idempotency_001");
        var controller = CreatePaymentController(transactionService);
        var notification = new MomoPaymentNotification
        {
            PartnerCode = "TEST_PARTNER",
            AccessKey = "TEST_ACCESS",
            RequestId = "request-001",
            OrderId = transaction.TransactionId,
            Amount = "10000",
            TransId = 123456789,
            OrderInfo = "Test parking fee",
            OrderType = "momo_wallet",
            ResultCode = 0,
            Message = "Successful",
            PayType = "qr",
            ResponseTime = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
            ExtraData = "momo_ref_001",
            Signature = "invalid-test-signature"
        };

        controller.ControllerContext = CreateControllerContext(notification);

        var result = await controller.ProcessMomoWebhook();

        Assert.IsType<OkObjectResult>(result);
        var updated = await transactionService.GetTransactionByIdAsync(transaction.Id);
        Assert.Equal("COMPLETED", updated.Status);
        Assert.Equal(notification.TransId.ToString(), updated.PaymentDetails.MomoTransactionId);
    }

    [Fact]
    public async Task Stripe_webhook_marks_pending_transaction_as_completed()
    {
        if (!_fixture.IsAvailable)
        {
            return;
        }

        var transactionService = TestServiceFactory.CreateTransactionService(_fixture);
        var transaction = await transactionService.CreateStripeTransactionAsync("C001", 30000, "PARKING_FEE", "Test parking fee", "stripe_idempotency_001");
        transaction.PaymentDetails.StripePaymentIntentId = "pi_test_001";
        await transactionService.UpdateTransactionAsync(transaction);

        var controller = CreatePaymentController(transactionService);
        var webhookEvent = new StripeWebhookEvent
        {
            Id = "evt_test_001",
            Type = "payment_intent.succeeded",
            Data = new StripeWebhookEventData
            {
                Object = new StripePaymentIntent
                {
                    Id = "pi_test_001",
                    Amount = 3000000,
                    Currency = "vnd",
                    Status = "succeeded",
                    Metadata = new Dictionary<string, string>
                    {
                        ["orderId"] = transaction.TransactionId,
                        ["transactionId"] = transaction.Id
                    },
                    PaymentMethod = "pm_card_visa",
                    PaymentMethodDetails = new StripePaymentMethodDetails
                    {
                        Card = new StripeCardDetails
                        {
                            Last4 = "4242",
                            Brand = "visa"
                        }
                    }
                }
            }
        };

        controller.ControllerContext = CreateControllerContext(webhookEvent, headers: new Dictionary<string, string>
        {
            ["Stripe-Signature"] = "test-signature"
        });

        var result = await controller.ProcessStripeWebhook();

        Assert.IsType<OkObjectResult>(result);
        var updated = await transactionService.GetTransactionByIdAsync(transaction.Id);
        Assert.Equal("COMPLETED", updated.Status);
        Assert.Equal("4242", updated.PaymentDetails.CardLast4);
    }

    private PaymentController CreatePaymentController(TransactionService transactionService)
    {
        var parkingService = TestServiceFactory.CreateParkingService(_fixture);
        var settingsService = TestServiceFactory.CreateSettingsService(_fixture);
        var parkingFeeService = new ParkingFeeService(_fixture.Configuration, settingsService, NullLogger<ParkingFeeService>.Instance);
        var momoService = new MomoPaymentService(_fixture.Configuration, NullLogger<MomoPaymentService>.Instance, new HttpClient());
        var stripeService = new StripePaymentService(_fixture.Configuration, NullLogger<StripePaymentService>.Instance, new HttpClient());
        var invoiceService = new InvoiceService(_fixture.Context, parkingService, _fixture.Configuration);

        return new PaymentController(
            transactionService,
            parkingService,
            parkingFeeService,
            momoService,
            stripeService,
            invoiceService,
            _fixture.Configuration,
            NullLogger<PaymentController>.Instance);
    }

    private static ControllerContext CreateControllerContext<T>(T body, Dictionary<string, string>? headers = null)
    {
        var json = JsonSerializer.Serialize(body);
        var httpContext = new DefaultHttpContext
        {
            Request =
            {
                Body = new MemoryStream(Encoding.UTF8.GetBytes(json)),
                ContentType = "application/json"
            }
        };

        if (headers != null)
        {
            foreach (var header in headers)
            {
                httpContext.Request.Headers[header.Key] = header.Value;
            }
        }

        return new ControllerContext
        {
            HttpContext = httpContext
        };
    }
}