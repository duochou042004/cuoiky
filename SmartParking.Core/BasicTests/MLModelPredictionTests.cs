using Microsoft.Extensions.Logging.Abstractions;
using SmartParking.Core.Services;

namespace SmartParking.Core.Tests
{
    public class MLModelPredictionTests
    {
        [Fact]
        public void Missing_Model_Does_Not_Throw_And_Reports_Not_Loaded()
        {
            // Point at a path that does not exist — construction must degrade gracefully
            // (this is the P0-2 fix: no hard crash when the model file is absent).
            var prediction = new MLModelPrediction(
                NullLogger<MLModelPrediction>.Instance,
                modelPath: "/nonexistent/path/VehicleClassification.zip");

            Assert.False(prediction.IsModelLoaded);
        }

        [Fact]
        public void Predict_Without_Model_Returns_Unknown_Instead_Of_Throwing()
        {
            var prediction = new MLModelPrediction(
                NullLogger<MLModelPrediction>.Instance,
                modelPath: "/nonexistent/path/VehicleClassification.zip");

            // Even with a bogus image path, an unloaded model must not throw; it
            // returns the sentinel UNKNOWN result so callers can fall back to heuristics.
            var result = prediction.PredictVehicleType("/nonexistent/image.jpg");

            Assert.NotNull(result);
            Assert.Equal("UNKNOWN", result.PredictedLabel);
        }
    }
}
