using BasicTests.Infrastructure;

namespace BasicTests.Parking;

public sealed class ParkingFlowIntegrationTests : IClassFixture<MongoIntegrationFixture>
{
    private readonly MongoIntegrationFixture _fixture;

    public ParkingFlowIntegrationTests(MongoIntegrationFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task Vehicle_check_in_and_check_out_updates_vehicle_and_slot_state()
    {
        if (!_fixture.IsAvailable)
        {
            return;
        }

        var parkingService = TestServiceFactory.CreateParkingService(_fixture);
        await parkingService.InitializeParkingSlots();

        var parkedVehicle = await parkingService.ParkVehicle("59A-12345", "CAR");

        Assert.Equal("PARKED", parkedVehicle.Status);
        Assert.Equal("CAR", parkedVehicle.VehicleType);
        Assert.False(string.IsNullOrWhiteSpace(parkedVehicle.SlotId));

        var occupiedSlot = (await parkingService.GetAllParkingSlots()).Single(slot => slot.SlotId == parkedVehicle.SlotId);
        Assert.Equal("OCCUPIED", occupiedSlot.Status);
        Assert.Equal(parkedVehicle.VehicleId, occupiedSlot.CurrentVehicleId);

        var exitedVehicle = await parkingService.ExitVehicle(parkedVehicle.VehicleId);

        Assert.Equal("LEFT", exitedVehicle.Status);
        Assert.NotNull(exitedVehicle.ExitTime);

        var availableSlot = (await parkingService.GetAllParkingSlots()).Single(slot => slot.SlotId == parkedVehicle.SlotId);
        Assert.Equal("AVAILABLE", availableSlot.Status);
        Assert.Null(availableSlot.CurrentVehicleId);
    }
}