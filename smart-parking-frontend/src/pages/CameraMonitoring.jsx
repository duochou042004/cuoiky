import React, { useState, useEffect, useRef } from 'react';
import { Container, Row, Col, Card, Alert, Button, Badge, Spinner } from 'react-bootstrap';
import WebcamViewer from '../components/WebcamViewer';
import ParkingPaymentModal from './PaymentModal';
import axios from 'axios';
import * as signalR from '@microsoft/signalr';
import { toast } from 'react-toastify';

const CameraMonitoring = () => {
  const [cameras, setCameras] = useState([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState(null);
  const [recentDetections, setRecentDetections] = useState([]);
  const connectionRef = useRef(null);

  // Payment modal state for casual vehicle camera checkout
  const [showPaymentModal, setShowPaymentModal] = useState(false);
  const [pendingPaymentData, setPendingPaymentData] = useState(null);

  useEffect(() => {
    fetchCameras();

    const newConnection = new signalR.HubConnectionBuilder()
      .withUrl('/parkingHub')
      .withAutomaticReconnect()
      .build();

    newConnection.on('ReceiveVehicleEntry', (data) => {
      setRecentDetections(prev => [
        { type: 'ENTRY', timestamp: new Date(), ...data },
        ...prev.slice(0, 9)
      ]);
    });

    newConnection.on('ReceiveVehicleExit', (data) => {
      setRecentDetections(prev => [
        { type: 'EXIT', timestamp: new Date(), ...data },
        ...prev.slice(0, 9)
      ]);
    });

    newConnection.on('ReceiveManualSnapshot', (data) => {
      setRecentDetections(prev => [
        { type: 'SNAPSHOT', timestamp: new Date(), ...data },
        ...prev.slice(0, 9)
      ]);
    });

    // Casual vehicle detected at exit gate — open payment modal
    newConnection.on('ReceiveVehicleAtExit', (data) => {
      setRecentDetections(prev => [
        { type: 'AWAITING_PAYMENT', timestamp: new Date(), ...data },
        ...prev.slice(0, 9)
      ]);
      openPaymentModal(data);
    });

    newConnection.start()
      .then(() => {
        connectionRef.current = newConnection;
      })
      .catch(err => {
        console.error('SignalR Connection Error:', err);
        setError('Failed to connect to real-time updates');
      });

    return () => {
      connectionRef.current?.stop();
    };
  }, []);

  const openPaymentModal = (vehicleAtExitData) => {
    // Shape the data into the format ParkingPaymentModal expects
    setPendingPaymentData({
      vehicle: {
        vehicleId: vehicleAtExitData.vehicleId,
        licensePlate: vehicleAtExitData.licensePlate,
        vehicleType: vehicleAtExitData.vehicleType,
        entryTime: vehicleAtExitData.entryTime,
        slotId: vehicleAtExitData.slotId,
      },
      parkingFee: vehicleAtExitData.parkingFee,
      parkingDuration: vehicleAtExitData.parkingDuration,
      paymentRequired: true,
    });
    setShowPaymentModal(true);
  };

  const handlePaymentComplete = (checkoutData) => {
    setShowPaymentModal(false);
    setPendingPaymentData(null);
    toast.success(`Xe ${checkoutData?.vehicle?.licensePlate || ''} đã ra bãi thành công.`);
  };

  const fetchCameras = async () => {
    try {
      setLoading(true);
      const response = await axios.get('/api/cameras');
      setCameras(response.data.cameras || []);
      setError(null);
    } catch (err) {
      console.error('Error fetching cameras:', err);
      setError('Failed to load cameras. Please try again.');
    } finally {
      setLoading(false);
    }
  };

  const handleDetection = (cameraId, detection) => {
    setRecentDetections(prev => [
      { type: 'SNAPSHOT', timestamp: new Date(), cameraId, ...detection },
      ...prev.slice(0, 9)
    ]);
  };

  const handleProcessVehicle = async (cameraId, vehicleData) => {
    try {
      setLoading(true);
      setError(null);

      const response = await axios.post(`/api/cameras/${cameraId}/process-vehicle`, vehicleData);

      if (vehicleData.action === 'checkout' && response.data.requiresPayment) {
        // Casual vehicle — open payment modal with data from backend response
        openPaymentModal({
          vehicleId: response.data.vehicleId,
          licensePlate: vehicleData.licensePlate,
          vehicleType: vehicleData.vehicleType,
          entryTime: response.data.entryTime,
          slotId: response.data.slotId,
          parkingFee: response.data.parkingFee,
          parkingDuration: response.data.parkingDuration,
        });
      } else {
        const actionText = vehicleData.action === 'checkin' ? 'đã vào bãi' : 'đã ra bãi';
        toast.success(`Xe ${vehicleData.licensePlate} ${actionText} thành công.`);
      }
    } catch (err) {
      console.error(`Error processing vehicle for camera ${cameraId}:`, err);
      const msg = err.response?.data?.error || err.message;
      setError(msg);
      toast.error(`Lỗi: ${msg}`);
    } finally {
      setLoading(false);
    }
  };

  const setupDefaultCameras = async () => {
    try {
      setLoading(true);
      // Each camera gets its own index so they open different physical devices
      await axios.post('/api/cameras/IN-01/start', { cameraIndex: 0 });
      await axios.post('/api/cameras/IN-02/start', { cameraIndex: 1 });
      await axios.post('/api/cameras/OUT-01/start', { cameraIndex: 2 });
      await axios.post('/api/cameras/OUT-02/start', { cameraIndex: 3 });
      await fetchCameras();
      setError(null);
      toast.success('Cameras set up successfully.');
    } catch (err) {
      console.error('Error setting up default cameras:', err);
      const msg = 'Failed to set up default cameras. Please check if your webcam is connected.';
      setError(msg);
      toast.error(msg);
    } finally {
      setLoading(false);
    }
  };

  const getDetectionBadge = (type) => {
    switch (type) {
      case 'ENTRY': return <Badge bg="success">Vào</Badge>;
      case 'EXIT': return <Badge bg="danger">Ra</Badge>;
      case 'AWAITING_PAYMENT': return <Badge bg="warning" text="dark">Chờ thanh toán</Badge>;
      default: return <Badge bg="info">Snapshot</Badge>;
    }
  };

  const formatTimestamp = (ts) => {
    if (ts instanceof Date) return ts.toLocaleTimeString();
    if (typeof ts === 'string') return new Date(ts).toLocaleTimeString();
    return new Date().toLocaleTimeString();
  };

  return (
    <Container fluid>
      <h1 className="mb-4">Giám sát Camera</h1>

      {error && (
        <Alert variant="danger" className="mb-4" dismissible onClose={() => setError(null)}>
          {error}
        </Alert>
      )}

      {cameras.length === 0 && !loading && (
        <div className="text-center mb-4">
          <p>Chưa có camera nào được thiết lập.</p>
          <Button variant="primary" onClick={setupDefaultCameras}>
            Thiết lập Camera Mặc định
          </Button>
        </div>
      )}

      <Row>
        <Col md={8}>
          <h2 className="mb-3">Camera Trực tiếp</h2>
          <Row>
            <Col md={6}>
              <WebcamViewer
                cameraId="IN-01"
                title="Camera Vào 1"
                onDetection={(detection) => handleDetection('IN-01', detection)}
                onProcessVehicle={(vehicleData) => handleProcessVehicle('IN-01', vehicleData)}
              />
            </Col>
            <Col md={6}>
              <WebcamViewer
                cameraId="OUT-01"
                title="Camera Ra 1"
                onDetection={(detection) => handleDetection('OUT-01', detection)}
                onProcessVehicle={(vehicleData) => handleProcessVehicle('OUT-01', vehicleData)}
              />
            </Col>
          </Row>
        </Col>

        <Col md={4}>
          <h2 className="mb-3">Phát hiện gần đây</h2>
          <div className="detection-list">
            {recentDetections.length === 0 ? (
              <Alert variant="info">
                Chưa có phát hiện nào. Xe sẽ xuất hiện ở đây khi được camera phát hiện.
              </Alert>
            ) : (
              recentDetections.map((detection, index) => (
                <Card
                  key={index}
                  className="mb-2 detection-card"
                  border={detection.type === 'AWAITING_PAYMENT' ? 'warning' : undefined}
                >
                  <Card.Body className="py-2">
                    <div className="d-flex justify-content-between align-items-center">
                      <div>
                        <strong>{detection.licensePlate}</strong>
                        <span className="ms-2">{getDetectionBadge(detection.type)}</span>
                      </div>
                      <small className="text-muted">{formatTimestamp(detection.timestamp)}</small>
                    </div>
                    <div className="small mt-1">
                      <div>Mã xe: {detection.vehicleId}</div>
                      <div>Loại: {detection.vehicleType}</div>
                      <div>Vị trí: {detection.slotId}</div>
                      <div>Camera: {detection.cameraId}</div>
                      {detection.type === 'AWAITING_PAYMENT' && (
                        <div className="mt-1">
                          <span className="text-warning fw-bold">Phí đỗ xe: {detection.parkingFee?.toLocaleString()} VND</span>
                          <div className="mt-1">
                            <Button
                              size="sm"
                              variant="warning"
                              onClick={() => openPaymentModal(detection)}
                            >
                              Thanh toán
                            </Button>
                          </div>
                        </div>
                      )}
                      {detection.type === 'ENTRY' && detection.classificationMethod && (
                        <div>
                          Phân loại: {detection.classificationMethod === 'ml' ? 'ML Model' :
                                      detection.classificationMethod === 'format' ? 'Định dạng biển số' : 'Dự phòng'}
                          {detection.classificationMethod === 'ml' && (
                            <span className="ms-1 text-muted">
                              ({(detection.classificationConfidence * 100).toFixed(1)}%)
                            </span>
                          )}
                        </div>
                      )}
                      {detection.type === 'SNAPSHOT' && detection.confidence != null && (
                        <div>
                          Độ tin cậy: {(detection.confidence * 100).toFixed(1)}%
                        </div>
                      )}
                      {detection.type === 'EXIT' && detection.parkingDuration && (
                        <div>Thời gian đỗ: {detection.parkingDuration}</div>
                      )}
                      {detection.debugImage && (
                        <div className="mt-1">
                          <a href={`/DebugFrames/${detection.debugImage}`} target="_blank" rel="noopener noreferrer" className="text-primary">
                            Xem ảnh
                          </a>
                        </div>
                      )}
                    </div>
                  </Card.Body>
                </Card>
              ))
            )}
          </div>
        </Col>
      </Row>

      {showPaymentModal && pendingPaymentData && (
        <ParkingPaymentModal
          show={showPaymentModal}
          onHide={() => {
            setShowPaymentModal(false);
            setPendingPaymentData(null);
          }}
          vehicleData={pendingPaymentData}
          onPaymentComplete={handlePaymentComplete}
        />
      )}
    </Container>
  );
};

export default CameraMonitoring;
