using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using SmartParking.Core.Services;
using System;
using System.IO;
using System.Net.Http;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;

namespace SmartParking.Core.Tests
{
    public class LicensePlateServiceTest
    {
        private readonly LicensePlateService _licensePlateService;
        private readonly MLModelPrediction _mlModelPrediction;
        private readonly IConfiguration _configuration;
        private readonly ILogger<LicensePlateService> _logger;

        public LicensePlateServiceTest()
        {
            // Create configuration
            var configBuilder = new ConfigurationBuilder()
                .AddJsonFile("appsettings.json")
                .AddEnvironmentVariables();
            
            _configuration = configBuilder.Build();
            _mlModelPrediction = new MLModelPrediction();
            _logger = LoggerFactory.Create(builder => builder.AddConsole()).CreateLogger<LicensePlateService>();
            _licensePlateService = new LicensePlateService(new HttpClient(), _configuration, _mlModelPrediction, _logger);
        }

        public async Task TestLicensePlateRecognition(string imagePath)
        {
            if (!File.Exists(imagePath))
            {
                Console.WriteLine($"Error: Image file not found at {imagePath}");
                return;
            }

            Console.WriteLine($"Testing license plate recognition with image: {imagePath}");

            try
            {
                // Create a FormFile from the image
                using var stream = new FileStream(imagePath, FileMode.Open);
                var formFile = new FormFile(stream, 0, stream.Length, "image", Path.GetFileName(imagePath))
                {
                    Headers = new HeaderDictionary(),
                    ContentType = "image/jpeg"
                };

                // Process the image
                var (licensePlate, vehicleType) = await _licensePlateService.ProcessVehicleImage(formFile);

                Console.WriteLine($"License Plate: {licensePlate}");
                Console.WriteLine($"Vehicle Type: {vehicleType}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error: {ex.Message}");
                if (ex.InnerException != null)
                {
                    Console.WriteLine($"Inner Exception: {ex.InnerException.Message}");
                }
            }
        }
    }
}
