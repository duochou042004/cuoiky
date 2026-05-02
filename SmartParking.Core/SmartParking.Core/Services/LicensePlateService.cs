using Microsoft.Extensions.Configuration;
using System;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using SmartParking.Core.Abstractions;

namespace SmartParking.Core.Services
{
    public class LicensePlateService
    {
        private readonly ILicensePlateRecognitionClient _recognitionClient;
        private readonly MLModelPrediction _mlModelPrediction;
        private readonly ILogger<LicensePlateService> _logger;

        public LicensePlateService(ILicensePlateRecognitionClient recognitionClient, IConfiguration configuration, MLModelPrediction mlModelPrediction, ILogger<LicensePlateService> logger)
        {
            _recognitionClient = recognitionClient;
            _mlModelPrediction = mlModelPrediction;
            _logger = logger;
        }

        public async Task<(string LicensePlate, string VehicleType)> ProcessVehicleImage(IFormFile image)
        {
            // Save the image to a temporary file
            string tempFilePath = Path.GetTempFileName();
            try
            {
                using (var stream = new FileStream(tempFilePath, FileMode.Create))
                {
                    await image.CopyToAsync(stream);
                }

                // Use ML.NET to classify the vehicle type
                var prediction = _mlModelPrediction.PredictVehicleType(tempFilePath);
                string vehicleType = prediction.PredictedLabel;
                float confidence = prediction.GetHighestScore();

                // Call the Python API to recognize the license plate
                string licensePlate = await RecognizeLicensePlate(tempFilePath);

                // If ML classification has low confidence or returns "MOTORBIKE" for a car,
                // use license plate format to determine vehicle type
                if (confidence < 0.65f || vehicleType == "UNKNOWN")
                {
                    // Vietnamese car plates typically have a dash and are longer
                    if (licensePlate.Length >= 9 && licensePlate.Contains("-"))
                    {
                        vehicleType = "CAR";
                        Console.WriteLine($"License plate format suggests CAR: {licensePlate}");
                    }
                    // Vietnamese motorcycle plates are typically shorter and don't have dashes
                    else if (licensePlate.Length <= 8 && !licensePlate.Contains("-"))
                    {
                        vehicleType = "MOTORBIKE";
                        Console.WriteLine($"License plate format suggests MOTORBIKE: {licensePlate}");
                    }
                    // For plates that don't match either pattern clearly
                    else
                    {
                        // Default to CAR for plates with more than 8 characters
                        if (licensePlate.Length > 8)
                        {
                            vehicleType = "CAR";
                            Console.WriteLine($"License plate length suggests CAR: {licensePlate}");
                        }
                        else
                        {
                            vehicleType = "MOTORBIKE";
                            Console.WriteLine($"License plate length suggests MOTORBIKE: {licensePlate}");
                        }
                    }
                }

                return (licensePlate, vehicleType);
            }
            finally
            {
                // Clean up the temporary file
                if (File.Exists(tempFilePath))
                {
                    File.Delete(tempFilePath);
                }
            }
        }

        private async Task<string> RecognizeLicensePlate(string imagePath)
        {
            try
            {
                return await _recognitionClient.RecognizeAsync(imagePath);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error recognizing license plate from Python API");
                return "Error";
            }
        }

    }
}
