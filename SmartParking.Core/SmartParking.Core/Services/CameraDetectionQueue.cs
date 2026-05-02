using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;

namespace SmartParking.Core.Services
{
    public sealed record CameraDetectionWorkItem(
        string CameraId,
        string LicensePlate,
        bool IsEntryCamera,
        float Confidence,
        int Attempt = 0);

    public interface ICameraDetectionQueue
    {
        ValueTask QueueAsync(CameraDetectionWorkItem workItem, CancellationToken cancellationToken = default);

        ValueTask<CameraDetectionWorkItem> DequeueAsync(CancellationToken cancellationToken = default);
    }

    public class CameraDetectionQueue : ICameraDetectionQueue
    {
        private readonly Channel<CameraDetectionWorkItem> _queue;

        public CameraDetectionQueue(IConfiguration configuration)
        {
            var capacity = Math.Max(10, configuration.GetValue<int?>("CameraProcessing:QueueCapacity") ?? 500);
            _queue = Channel.CreateBounded<CameraDetectionWorkItem>(new BoundedChannelOptions(capacity)
            {
                FullMode = BoundedChannelFullMode.Wait,
                SingleReader = false,
                SingleWriter = false
            });
        }

        public ValueTask QueueAsync(CameraDetectionWorkItem workItem, CancellationToken cancellationToken = default)
        {
            return _queue.Writer.WriteAsync(workItem, cancellationToken);
        }

        public ValueTask<CameraDetectionWorkItem> DequeueAsync(CancellationToken cancellationToken = default)
        {
            return _queue.Reader.ReadAsync(cancellationToken);
        }
    }
}