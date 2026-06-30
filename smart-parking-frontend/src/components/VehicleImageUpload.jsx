import React, { useState } from 'react';
import { Form, Button, Spinner, Alert, Card, Row, Col } from 'react-bootstrap';
import { FaCamera, FaCheck, FaUpload, FaSync } from 'react-icons/fa';
import axios from 'axios';

const VehicleImageUpload = ({ onRecognitionComplete }) => {
  const [selectedFile, setSelectedFile] = useState(null);
  const [previewUrl, setPreviewUrl] = useState(null);
  const [loading, setLoading] = useState(false);
  const [error, setError] = useState(null);
  const [success, setSuccess] = useState(false);
  const [recognizedData, setRecognizedData] = useState(null);

  const handleFileSelect = (event) => {
    const file = event.target.files[0];
    if (file) {
      setSelectedFile(file);
      setPreviewUrl(URL.createObjectURL(file));
      setError(null);
      setSuccess(false);
      setRecognizedData(null);
    }
  };

  const handleUpload = async () => {
    if (!selectedFile) {
      setError('Vui lòng chọn ảnh');
      return;
    }

    try {
      setLoading(true);
      setError(null);
      setSuccess(false);

      const formData = new FormData();
      formData.append('image', selectedFile);

      const response = await axios.post('/api/monthlyvehicle/recognize-vehicle', formData, {
        headers: {
          'Content-Type': 'multipart/form-data'
        }
      });

      if (response.data.success) {
        const data = {
          licensePlate: response.data.licensePlate,
          vehicleType: response.data.vehicleType
        };

        console.log("Recognition successful:", data);
        setRecognizedData(data);
        setSuccess(true);

        // Call the parent component's callback with the recognized data
        if (onRecognitionComplete) {
          onRecognitionComplete(data);
        }
      } else {
        setError('Không thể nhận diện biển số xe. Vui lòng thử lại hoặc nhập thủ công.');
      }
    } catch (err) {
      if (err.response?.data?.error) {
        setError(err.response.data.error);
        // If the error includes license plate and vehicle type, still update the form
        if (err.response.data.licensePlate && err.response.data.vehicleType) {
          const data = {
            licensePlate: err.response.data.licensePlate,
            vehicleType: err.response.data.vehicleType
          };

          console.log("Recognition with warning:", data);
          setRecognizedData(data);

          // Call the parent component's callback with the recognized data
          if (onRecognitionComplete) {
            onRecognitionComplete(data);
          }
        }
      } else {
        setError('Đã xảy ra lỗi khi xử lý ảnh. Vui lòng thử lại hoặc nhập thủ công.');
      }
      console.error('Error uploading image:', err);
    } finally {
      setLoading(false);
    }
  };

  const resetUpload = () => {
    setSelectedFile(null);
    setPreviewUrl(null);
    setError(null);
    setSuccess(false);
    setRecognizedData(null);
  };

  return (
    <Card className="mb-4 border-0 shadow-sm">
      <Card.Header className="bg-light">
        <div className="d-flex align-items-center">
          <FaCamera className="me-2 text-primary" />
          <h6 className="mb-0">Nhận diện biển số xe</h6>
        </div>
      </Card.Header>
      <Card.Body>
        <Form.Group>
          <Form.Label>Tải ảnh phương tiện</Form.Label>
          <Form.Control
            type="file"
            accept="image/*"
            onChange={handleFileSelect}
            disabled={loading}
          />
          <Form.Text className="text-muted">
            Tải lên ảnh chụp biển số xe để tự động nhận diện
          </Form.Text>
        </Form.Group>

        {previewUrl && (
          <div className="mt-3 mb-3">
            <Card className="border">
              <Card.Body className="p-2 text-center">
                <img
                  src={previewUrl}
                  alt="Preview"
                  style={{ maxWidth: '100%', maxHeight: '200px', objectFit: 'contain' }}
                />
              </Card.Body>
            </Card>
          </div>
        )}

        {error && (
          <Alert variant="danger" className="mt-3">
            {error}
          </Alert>
        )}

        {success && recognizedData && (
          <Alert variant="success" className="mt-3">
            <div className="d-flex align-items-center">
              <FaCheck className="me-2" />
              <div>
                <strong>Nhận diện thành công!</strong>
                <div>Biển số: {recognizedData.licensePlate}</div>
                <div>Loại xe: {recognizedData.vehicleType === 'CAR' ? 'Ô tô' : recognizedData.vehicleType === 'MOTORBIKE' ? 'Xe máy' : recognizedData.vehicleType}</div>
              </div>
            </div>
          </Alert>
        )}

        <Row className="mt-3">
          <Col>
            {!selectedFile ? (
              <Button
                variant="primary"
                disabled={loading}
                className="w-100"
                onClick={() => document.querySelector('input[type="file"]').click()}
              >
                <FaUpload className="me-2" />
                Chọn ảnh
              </Button>
            ) : (
              <div className="d-flex gap-2">
                <Button
                  variant="primary"
                  onClick={handleUpload}
                  disabled={!selectedFile || loading}
                  className="flex-grow-1"
                >
                  {loading ? (
                    <>
                      <Spinner
                        as="span"
                        animation="border"
                        size="sm"
                        role="status"
                        aria-hidden="true"
                        className="me-2"
                      />
                      Đang xử lý...
                    </>
                  ) : (
                    <>
                      <FaCamera className="me-2" />
                      Nhận diện biển số
                    </>
                  )}
                </Button>
                <Button
                  variant="outline-secondary"
                  onClick={resetUpload}
                  disabled={loading}
                >
                  <FaSync />
                </Button>
              </div>
            )}
          </Col>
        </Row>
      </Card.Body>
    </Card>
  );
};

export default VehicleImageUpload;