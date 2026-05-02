using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using Microsoft.ML;
using Microsoft.ML.Data;
using Microsoft.ML.Vision;

namespace SmartParking.Core.Services
{
    public class MLModelPrediction
    {
        private readonly MLContext _mlContext;
        private readonly ITransformer? _model;
        private readonly string? _modelPath;

        public MLModelPrediction(string? modelPath = null)
        {
            _mlContext = new MLContext(seed: 1);

            // Danh sách các đường dẫn có thể chứa mô hình
            var possiblePaths = new List<string>();

            // Nếu có đường dẫn được cung cấp, đó là ưu tiên hàng đầu
            if (!string.IsNullOrEmpty(modelPath))
            {
                possiblePaths.Add(modelPath);
            }

            // Thêm các đường dẫn khác có thể
            possiblePaths.Add(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "MLModels", "VehicleClassification.zip"));
            possiblePaths.Add(Path.Combine(Directory.GetCurrentDirectory(), "MLModels", "VehicleClassification.zip"));

            // Đường dẫn tương đối từ thư mục bin
            string projectPath = Path.GetFullPath(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", "..", ".."));
            possiblePaths.Add(Path.Combine(projectPath, "MLModels", "VehicleClassification.zip"));

            // Đường dẫn tuyệt đối
            possiblePaths.Add("/home/user/ProjectITS/SmartParking.Core/SmartParking.Core/MLModels/VehicleClassification.zip");

            // Kiểm tra từng đường dẫn
            bool modelFound = false;
            foreach (var path in possiblePaths)
            {
                Console.WriteLine($"Trying to load model from: {path}");
                if (File.Exists(path))
                {
                    _modelPath = path;
                    Console.WriteLine($"Model file exists at: {_modelPath}");
                    try
                    {
                        _model = LoadModel();
                        InspectModelSchema();
                        modelFound = true;
                        break;
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"Vehicle classification model at {_modelPath} is not usable: {ex.Message}");
                    }
                }
            }

            if (!modelFound)
            {
                Console.WriteLine($"Vehicle classification model not available. Classification will fall back to UNKNOWN. Checked paths: {string.Join(", ", possiblePaths)}");
            }
        }

        private ITransformer LoadModel()
        {
            if (string.IsNullOrWhiteSpace(_modelPath))
            {
                throw new FileNotFoundException("ML model path is not configured.");
            }

            DataViewSchema inputSchema;
            ITransformer loadedModel = _mlContext.Model.Load(_modelPath, out inputSchema);

            // Xuất thông tin về schema để debug
            Console.WriteLine("Model schema columns:");
            foreach (var column in inputSchema)
            {
                Console.WriteLine($"- Column: {column.Name}, Type: {column.Type}");
            }

            return loadedModel;
        }

        private void InspectModelSchema()
        {
            try
            {
                DataViewSchema modelSchema;
                _mlContext.Model.Load(_modelPath, out modelSchema);

                Console.WriteLine("Detailed model schema inspection:");
                Console.WriteLine("Input columns:");
                foreach (var column in modelSchema)
                {
                    Console.WriteLine($"- Column: {column.Name}, Type: {column.Type}");
                }

                // Thử kiểm tra schema đầu ra (có thể gây lỗi trong một số trường hợp)
                try
                {
                    var outputSchema = (_model as ITransformer).GetOutputSchema(modelSchema);
                    Console.WriteLine("Output columns:");
                    foreach (var column in outputSchema)
                    {
                        Console.WriteLine($"- Output Column: {column.Name}, Type: {column.Type}");
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Could not get output schema: {ex.Message}");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error inspecting schema: {ex.Message}");
            }
        }

        public ImagePredictionResult PredictVehicleType(string imagePath)
        {
            if (!File.Exists(imagePath))
            {
                throw new FileNotFoundException("Image file not found!", imagePath);
            }

            if (_model == null)
            {
                return CreateFallbackPrediction();
            }

            try
            {
                // Đọc byte array từ file hình ảnh
                byte[] imageBytes = File.ReadAllBytes(imagePath);
                return PredictVehicleType(imageBytes);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error in prediction from file: {ex.Message}");
                Console.WriteLine($"Stack trace: {ex.StackTrace}");

                if (ex.InnerException != null)
                {
                    Console.WriteLine($"Inner exception: {ex.InnerException.Message}");
                    Console.WriteLine($"Inner stack trace: {ex.InnerException.StackTrace}");
                }

                return new ImagePredictionResult
                {
                    PredictedLabel = $"Error: {ex.Message}",
                    Score = new float[] { 0 }
                };
            }
        }

        public ImagePredictionResult PredictVehicleType(byte[] imageBytes)
        {
            if (_model == null)
            {
                return CreateFallbackPrediction();
            }

            try
            {
                // Tạo đối tượng dữ liệu đầu vào phù hợp với cấu trúc của mô hình
                var imageData = new ImageDataForPrediction
                {
                    ImageBytes = imageBytes
                };

                // Tạo prediction engine để dự đoán
                var predictionEngine = _mlContext.Model.CreatePredictionEngine<ImageDataForPrediction, ImagePredictionResult>(_model);

                // Thực hiện dự đoán
                var prediction = predictionEngine.Predict(imageData);

                // Ghi log chi tiết về kết quả dự đoán
                Console.WriteLine($"Prediction result: {prediction.PredictedLabel}");
                if (prediction.Score != null)
                {
                    Console.WriteLine($"Confidence scores: {string.Join(", ", prediction.Score)}");
                    Console.WriteLine($"Highest confidence: {prediction.Score.Max()}");
                }

                // Trả về kết quả dự đoán chi tiết
                return prediction;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error in prediction from bytes: {ex.Message}");
                Console.WriteLine($"Stack trace: {ex.StackTrace}");

                if (ex.InnerException != null)
                {
                    Console.WriteLine($"Inner exception: {ex.InnerException.Message}");
                    Console.WriteLine($"Inner stack trace: {ex.InnerException.StackTrace}");
                }

                return new ImagePredictionResult
                {
                    PredictedLabel = $"Error: {ex.Message}",
                    Score = new float[] { 0 }
                };
            }
        }

        private static ImagePredictionResult CreateFallbackPrediction()
        {
            return new ImagePredictionResult
            {
                PredictedLabel = "UNKNOWN",
                Score = new float[] { 0 }
            };
        }

        // Lớp dữ liệu đầu vào cho dự đoán
        public class ImageDataForPrediction
        {
            [ColumnName("ImageBytes")]
            public byte[] ImageBytes { get; set; }
        }

        // Lớp dữ liệu đầu ra cho kết quả dự đoán
        public class ImagePredictionResult
        {
            [ColumnName("PredictedLabel")]
            public string PredictedLabel { get; set; }

            [ColumnName("Score")]
            public float[] Score { get; set; }

            // Thuộc tính bổ sung để dễ sử dụng
            public float GetHighestScore()
            {
                if (Score == null || Score.Length == 0)
                    return 0;

                return Score.Max();
            }
        }
    }
}
