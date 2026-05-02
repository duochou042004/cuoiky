using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using MongoDB.Driver;
using SmartParking.Core.Data;

namespace BasicTests.Infrastructure;

public sealed class MongoIntegrationFixture : IAsyncLifetime
{
    public string DatabaseName { get; } = $"SmartParkingTest_{Guid.NewGuid():N}";

    public IConfiguration Configuration { get; private set; } = default!;

    public MongoDBContext Context { get; private set; } = default!;

    public bool IsAvailable { get; private set; }

    public async Task InitializeAsync()
    {
        var connectionString = Environment.GetEnvironmentVariable("SMARTPARKING_TEST_MONGODB") ?? "mongodb://localhost:27017";
        Configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:MongoDb"] = connectionString,
                ["DatabaseSettings:DatabaseName"] = DatabaseName,
                ["ParkingSettings:MotorcycleSlots"] = "2",
                ["ParkingSettings:CarSlots"] = "1",
                ["ParkingFees:CasualMotorbikeFee"] = "10000",
                ["ParkingFees:CasualCarFee"] = "30000",
                ["PaymentGateways:Momo:PartnerCode"] = "TEST_PARTNER",
                ["PaymentGateways:Momo:AccessKey"] = "TEST_ACCESS",
                ["PaymentGateways:Momo:SecretKey"] = "TEST_SECRET",
                ["PaymentGateways:Momo:ApiEndpoint"] = "https://example.invalid/momo",
                ["PaymentGateways:Momo:ReturnUrl"] = "http://localhost/momo-return",
                ["PaymentGateways:Momo:NotifyUrl"] = "http://localhost/api/payment/webhook/momo",
                ["PaymentGateways:Stripe:ApiKey"] = "sk_test_placeholder",
                ["PaymentGateways:Stripe:WebhookSecret"] = "whsec_placeholder",
                ["PaymentGateways:Stripe:MockMode"] = "true"
            })
            .Build();

        Context = new MongoDBContext(Configuration, NullLogger<MongoDBContext>.Instance);

        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(3));
        try
        {
            await Context.GetClient().GetDatabase("admin").RunCommandAsync<MongoDB.Bson.BsonDocument>(
                new MongoDB.Bson.BsonDocument("ping", 1), cancellationToken: timeout.Token);
            IsAvailable = true;
        }
        catch
        {
            IsAvailable = false;
        }
    }

    public async Task DisposeAsync()
    {
        if (IsAvailable)
        {
            await Context.GetClient().DropDatabaseAsync(DatabaseName);
        }
    }
}