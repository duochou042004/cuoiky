using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace SmartParking.Core.Services
{
    public class CameraDetectionQueueWorker : BackgroundService
    {
        private readonly ICameraDetectionQueue _queue;
        private readonly IServiceScopeFactory _serviceScopeFactory;
        private readonly IConfiguration _configuration;
        private readonly ILogger<CameraDetectionQueueWorker> _logger;

        public CameraDetectionQueueWorker(
            ICameraDetectionQueue queue,
            IServiceScopeFactory serviceScopeFactory,
            IConfiguration configuration,
            ILogger<CameraDetectionQueueWorker> logger)
        {
            _queue = queue;
            _serviceScopeFactory = serviceScopeFactory;
            _configuration = configuration;
            _logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("Camera detection queue worker is starting");

            while (!stoppingToken.IsCancellationRequested)
            {
                CameraDetectionWorkItem workItem;
                try
                {
                    workItem = await _queue.DequeueAsync(stoppingToken);
                }
                catch (OperationCanceledException)
                {
                    break;
                }

                await ProcessWorkItem(workItem, stoppingToken);
            }
        }

        private async Task ProcessWorkItem(CameraDetectionWorkItem workItem, CancellationToken stoppingToken)
        {
            var maxAttempts = Math.Max(1, _configuration.GetValue<int?>("CameraProcessing:MaxAttempts") ?? 3);
            try
            {
                using var scope = _serviceScopeFactory.CreateScope();
                var processingService = scope.ServiceProvider.GetRequiredService<CameraVehicleProcessingService>();
                await processingService.ProcessDetectionAsync(workItem, stoppingToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException && workItem.Attempt + 1 < maxAttempts)
            {
                var nextAttempt = workItem.Attempt + 1;
                var delay = TimeSpan.FromMilliseconds(250 * Math.Pow(2, workItem.Attempt));
                _logger.LogWarning(ex, "Camera detection processing failed for {LicensePlate} from {CameraId}. Retrying attempt {Attempt}/{MaxAttempts} after {DelayMs} ms", workItem.LicensePlate, workItem.CameraId, nextAttempt + 1, maxAttempts, delay.TotalMilliseconds);
                await Task.Delay(delay, stoppingToken);
                await _queue.QueueAsync(workItem with { Attempt = nextAttempt }, stoppingToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogError(ex, "Camera detection processing permanently failed for {LicensePlate} from {CameraId} after {Attempts} attempts", workItem.LicensePlate, workItem.CameraId, workItem.Attempt + 1);
            }
        }
    }
}