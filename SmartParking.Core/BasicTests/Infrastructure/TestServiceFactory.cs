using Microsoft.Extensions.Logging.Abstractions;
using SmartParking.Core.Services;

namespace BasicTests.Infrastructure;

public static class TestServiceFactory
{
    public static SettingsService CreateSettingsService(MongoIntegrationFixture fixture)
    {
        return new SettingsService(fixture.Context, fixture.Configuration, NullLogger<SettingsService>.Instance);
    }

    public static ParkingService CreateParkingService(MongoIntegrationFixture fixture)
    {
        var settingsService = CreateSettingsService(fixture);
        return new ParkingService(
            fixture.Context,
            new IDGeneratorService(fixture.Context),
            fixture.Configuration,
            new NoOpHubContext(),
            NullLogger<ParkingService>.Instance,
            settingsService);
    }

    public static TransactionService CreateTransactionService(MongoIntegrationFixture fixture)
    {
        return new TransactionService(fixture.Context, NullLogger<TransactionService>.Instance);
    }
}