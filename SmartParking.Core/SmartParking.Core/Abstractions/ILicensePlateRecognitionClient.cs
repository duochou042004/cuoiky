using System.Threading;
using System.Threading.Tasks;

namespace SmartParking.Core.Abstractions
{
    public interface ILicensePlateRecognitionClient
    {
        Task<string> RecognizeAsync(string imagePath, CancellationToken cancellationToken = default);

        Task<bool> IsHealthyAsync(CancellationToken cancellationToken = default);
    }
}