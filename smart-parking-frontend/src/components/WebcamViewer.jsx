import React, { useState, useEffect, useRef } from 'react';
import { Card, Button, Badge, Spinner } from 'react-bootstrap';
import axios from 'axios';
import { STREAMING_API_BASE_URL } from '../config/api';

const WebcamViewer = ({ cameraId, title, onDetection, onProcessVehicle = () => {} }) => {
  const [status, setStatus] = useState('LOADING');
  const [metrics, setMetrics] = useState(null);
  const [error, setError] = useState(null);
  const [isCapturing, setIsCapturing] = useState(false);
  const [snapshotResult, setSnapshotResult] = useState(null);
  const streamRef = useRef(null);
  const metricsIntervalRef = useRef(null);

  useEffect(() => {
    // Start fetching metrics
    fetchMetrics();
    metricsIntervalRef.current = setInterval(fetchMetrics, 2000);

    return () => {
      if (metricsIntervalRef.current) {
        clearInterval(metricsIntervalRef.current);
      }
    };
  }, [cameraId]);

  const fetchMetrics = async () => {
    try {
      const response = await axios.get(`/api/cameras/${cameraId}/metrics`);
      setMetrics(response.data);
      setStatus('RUNNING');
    } catch (error) {
      // Only log once per minute to avoid console spam
      if (Math.random() < 0.05) {
        console.error(`Error fetching metrics for camera ${cameraId}:`, error);
      }

      // Handle 404 errors (camera not found or not started)
      if (error.response && error.response.status === 404) {
        setStatus('STOPPED');
      } else {
        // For other errors, don't change the status if it's already RUNNING
        // This prevents flickering when there are temporary network issues
        if (status !== 'RUNNING') {
          setStatus('ERROR');
          setError(error.message);
        }
      }
    }
  };

  const startCamera = async () => {
    try {
      setStatus('STARTING');
      await axios.post(`/api/cameras/${cameraId}/start`, { cameraIndex: getCameraIndex() });

      // Start vehicle classification
      try {
        await axios.post(`/api/vehicle-classification/cameras/${cameraId}/start-classification`);
        console.log(`Started vehicle classification for camera ${cameraId}`);
      } catch (classificationError) {
        console.warn(`Could not start vehicle classification: ${classificationError.message}`);
      }

      setStatus('RUNNING');
      // Refresh the stream
      if (streamRef.current) {
        // Add a small delay to ensure the camera is fully started
        setTimeout(() => {
          if (streamRef.current) {
            streamRef.current.src = `${STREAMING_API_BASE_URL}/cameras/${cameraId}/stream?t=${Date.now()}`;
          }
        }, 1000);
      }
    } catch (error) {
      console.error(`Error starting camera ${cameraId}:`, error);
      setStatus('ERROR');
      setError(error.response?.data?.error || error.message);
    }
  };

  const stopCamera = async () => {
    try {
      setStatus('STOPPING');

      // Stop vehicle classification
      try {
        await axios.post(`/api/vehicle-classification/cameras/${cameraId}/stop-classification`);
        console.log(`Stopped vehicle classification for camera ${cameraId}`);
      } catch (classificationError) {
        console.warn(`Could not stop vehicle classification: ${classificationError.message}`);
      }

      await axios.post(`/api/cameras/${cameraId}/stop`);
      setStatus('STOPPED');
      // Clear the stream
      if (streamRef.current) {
        streamRef.current.src = '';
      }
    } catch (error) {
      console.error(`Error stopping camera ${cameraId}:`, error);
      setStatus('ERROR');
      setError(error.response?.data?.error || error.message);
    }
  };

  const getCameraIndex = () => {
    // Extract camera index from ID (e.g., IN-01 -> 0, OUT-02 -> 1)
    const match = cameraId.match(/(\d+)$/);
    if (match) {
      return parseInt(match[1]) - 1;
    }
    return 0;
  };

  const captureSnapshot = async () => {
    try {
      setIsCapturing(true);
      setSnapshotResult(null);
      setError(null);

      const response = await axios.post(`/api/cameras/${cameraId}/capture-snapshot`);

      setSnapshotResult(response.data);

      // Call the onDetection callback if provided
      if (onDetection) {
        onDetection(response.data);
      }
    } catch (error) {
      console.error(`Error capturing snapshot from camera ${cameraId}:`, error);
      setError(error.response?.data?.error || error.message);
    } finally {
      setIsCapturing(false);
    }
  };

  const getStatusBadge = () => {
    switch (status) {
      case 'RUNNING':
        return <Badge bg="success">Running</Badge>;
      case 'STOPPED':
        return <Badge bg="secondary">Stopped</Badge>;
      case 'STARTING':
      case 'STOPPING':
        return <Badge bg="warning">Processing...</Badge>;
      case 'ERROR':
        return <Badge bg="danger">Error</Badge>;
      default:
        return <Badge bg="info">Loading...</Badge>;
    }
  };

  return (
    <Card className="mb-4 webcam-card">
      <Card.Header className="d-flex justify-content-between align-items-center">
        <div>
          {title || `Camera ${cameraId}`} {getStatusBadge()}
        </div>
        <div>
          {status === 'RUNNING' && metrics && (
            <span className="me-2">FPS: {metrics.fps || 0}</span>
          )}
          {status === 'RUNNING' && (
            <Button variant="danger" size="sm" onClick={stopCamera}>
              Stop
            </Button>
          )}
          {status === 'STOPPED' && (
            <Button variant="success" size="sm" onClick={startCamera}>
              Start
            </Button>
          )}
          {(status === 'STARTING' || status === 'STOPPING') && (
            <Button variant="secondary" size="sm" disabled>
              <Spinner
                as="span"
                animation="border"
                size="sm"
                role="status"
                aria-hidden="true"
              />
              <span className="visually-hidden">Loading...</span>
            </Button>
          )}
        </div>
      </Card.Header>
      <Card.Body className="p-0 webcam-container">
        {status === 'RUNNING' ? (
          <div className="position-relative">
            <img
              ref={streamRef}
              src={`${STREAMING_API_BASE_URL}/cameras/${cameraId}/stream?t=${Date.now()}`}
              alt={`Stream from camera ${cameraId}`}
              className="img-fluid w-100"
              style={{ maxHeight: '300px', objectFit: 'cover' }}
              onError={(e) => {
                console.error(`Error loading stream for camera ${cameraId}`);
                // Don't show the broken image icon
                e.target.onerror = null;
                // Try to reload the stream with a new timestamp
                setTimeout(() => {
                  if (streamRef.current) {
                    streamRef.current.src = `${STREAMING_API_BASE_URL}/cameras/${cameraId}/stream?t=${Date.now()}`;
                  }
                }, 2000);
              }}
            />
          </div>
        ) : status === 'ERROR' ? (
          <div className="text-center p-4">
            <div className="text-danger mb-2">Error: {error}</div>
            <Button variant="primary" size="sm" onClick={startCamera}>
              Try Again
            </Button>
          </div>
        ) : status === 'STOPPED' ? (
          <div className="text-center p-4">
            <div className="text-muted mb-2">Camera is stopped</div>
            <Button variant="primary" size="sm" onClick={startCamera}>
              Start Camera
            </Button>
          </div>
        ) : (
          <div className="text-center p-4">
            <Spinner animation="border" role="status">
              <span className="visually-hidden">Loading...</span>
            </Spinner>
          </div>
        )}
      </Card.Body>
      {status === 'RUNNING' && (
        <Card.Footer>
          <div className="d-flex justify-content-between align-items-center">
            <div className="text-muted small">
              {metrics && (
                <>
                  <div>Detection Time: {metrics.detection_time || 0}ms</div>
                  <div>Queue Size: {metrics.queue_size || 0}</div>
                </>
              )}
            </div>
            <Button
              variant="primary"
              size="sm"
              onClick={captureSnapshot}
              disabled={isCapturing}
            >
              {isCapturing ? (
                <>
                  <Spinner
                    as="span"
                    animation="border"
                    size="sm"
                    role="status"
                    aria-hidden="true"
                    className="me-1"
                  />
                  Capturing...
                </>
              ) : (
                'Capture Snapshot'
              )}
            </Button>
          </div>
          {snapshotResult && (
            <div className="mt-2 p-2 bg-light rounded small">
              <div className="fw-bold">Snapshot Result:</div>
              <div>License Plate: {snapshotResult.licensePlate}</div>
              <div>Vehicle Type: {snapshotResult.vehicleType} (Confidence: {(snapshotResult.confidence * 100).toFixed(1)}%)</div>

              {snapshotResult.isParked && (
                <div className="mt-1 text-success">
                  <strong>Vehicle is currently parked</strong>
                  <div>Vehicle ID: {snapshotResult.vehicleId}</div>
                  <div>Slot: {snapshotResult.slotId}</div>
                  <div>Entry Time: {new Date(snapshotResult.entryTime).toLocaleString()}</div>
                </div>
              )}

              <div className="mt-2 d-flex gap-2">
                {snapshotResult.canCheckIn && (
                  <Button
                    size="sm"
                    variant="success"
                    onClick={() => onProcessVehicle({
                      action: 'checkin',
                      licensePlate: snapshotResult.licensePlate,
                      vehicleType: snapshotResult.vehicleType,
                      confidence: snapshotResult.confidence,
                      classificationMethod: snapshotResult.classificationMethod,
                      debugImage: snapshotResult.debugImage
                    })}
                  >
                    Check In Vehicle
                  </Button>
                )}

                {snapshotResult.canCheckOut && (
                  <Button
                    size="sm"
                    variant="danger"
                    onClick={() => onProcessVehicle({
                      action: 'checkout',
                      licensePlate: snapshotResult.licensePlate,
                      vehicleType: snapshotResult.vehicleType,
                      confidence: snapshotResult.confidence,
                      classificationMethod: snapshotResult.classificationMethod,
                      debugImage: snapshotResult.debugImage
                    })}
                  >
                    Check Out Vehicle
                  </Button>
                )}

                {snapshotResult.debugImage && (
                  <a
                    href={`/DebugFrames/${snapshotResult.debugImage}`}
                    target="_blank"
                    rel="noopener noreferrer"
                    className="btn btn-sm btn-outline-primary"
                  >
                    View Image
                  </a>
                )}
              </div>
            </div>
          )}
        </Card.Footer>
      )}
    </Card>
  );
};

export default WebcamViewer;
