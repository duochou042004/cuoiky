using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using Microsoft.ML;
using Microsoft.ML.Data;
using Microsoft.Extensions.Logging;

namespace SmartParking.Core.Services
{
    public class MLModelPrediction
    {
        private readonly MLContext _mlContext;
        private ITransformer _model;
        private readonly string _modelPath;
        private readonly ILogger<MLModelPrediction> _logger;

        public bool IsModelLoaded => _model != null;

        public MLModelPrediction(ILogger<MLModelPrediction> logger, string modelPath = null)
        {
            _logger = logger;
            _mlContext = new MLContext(seed: 1);

            var possiblePaths = new List<string>();

            if (!string.IsNullOrEmpty(modelPath))
                possiblePaths.Add(modelPath);

            possiblePaths.Add(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "MLModels", "VehicleClassification.zip"));
            possiblePaths.Add(Path.Combine(Directory.GetCurrentDirectory(), "MLModels", "VehicleClassification.zip"));

            string projectPath = Path.GetFullPath(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", "..", ".."));
            possiblePaths.Add(Path.Combine(projectPath, "MLModels", "VehicleClassification.zip"));

            foreach (var path in possiblePaths)
            {
                if (File.Exists(path))
                {
                    _modelPath = path;
                    try
                    {
                        _model = LoadModel();
                        _logger.LogInformation("ML model loaded from {Path}", _modelPath);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Found model file at {Path} but failed to load it. Falling back to heuristic classification.", path);
                        _model = null;
                    }
                    break;
                }
            }

            if (_model == null)
            {
                _logger.LogWarning(
                    "ML vehicle classification model not found. Checked paths: {Paths}. " +
                    "Vehicle type will be determined by license plate format only. " +
                    "To enable ML classification, place VehicleClassification.zip in the MLModels directory.",
                    string.Join("; ", possiblePaths));
            }
        }

        private ITransformer LoadModel()
        {
            DataViewSchema inputSchema;
            return _mlContext.Model.Load(_modelPath, out inputSchema);
        }

        public ImagePredictionResult PredictVehicleType(string imagePath)
        {
            if (_model == null)
                return UnavailableResult();

            if (!File.Exists(imagePath))
                return UnavailableResult();

            try
            {
                return PredictVehicleType(File.ReadAllBytes(imagePath));
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error predicting vehicle type from file {Path}", imagePath);
                return UnavailableResult();
            }
        }

        public ImagePredictionResult PredictVehicleType(byte[] imageBytes)
        {
            if (_model == null)
                return UnavailableResult();

            try
            {
                var imageData = new ImageDataForPrediction { ImageBytes = imageBytes };
                var predictionEngine = _mlContext.Model.CreatePredictionEngine<ImageDataForPrediction, ImagePredictionResult>(_model);
                var prediction = predictionEngine.Predict(imageData);

                _logger.LogDebug("ML prediction: {Label} (confidence: {Score:F2})",
                    prediction.PredictedLabel, prediction.GetHighestScore());

                return prediction;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error predicting vehicle type from bytes");
                return UnavailableResult();
            }
        }

        private static ImagePredictionResult UnavailableResult() =>
            new ImagePredictionResult { PredictedLabel = "UNKNOWN", Score = new float[] { 0 } };

        public class ImageDataForPrediction
        {
            [ColumnName("ImageBytes")]
            public byte[] ImageBytes { get; set; }
        }

        public class ImagePredictionResult
        {
            [ColumnName("PredictedLabel")]
            public string PredictedLabel { get; set; }

            [ColumnName("Score")]
            public float[] Score { get; set; }

            public float GetHighestScore()
            {
                if (Score == null || Score.Length == 0) return 0;
                return Score.Max();
            }
        }
    }
}
