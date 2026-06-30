from flask import Flask, Response, jsonify, request
import cv2
import torch
import time
import threading
import queue
import numpy as np
import function.utils_rotate as utils_rotate
import function.helper as helper
from flask_cors import CORS
import json
import os
import logging
import tempfile

# Configure logging
logging.basicConfig(level=logging.INFO, format='%(asctime)s - %(name)s - %(levelname)s - %(message)s')
logger = logging.getLogger(__name__)

app = Flask(__name__)
CORS(app, resources={
    r"/*": {
        "origins": ["http://localhost:3000"],
        "methods": ["GET", "POST", "OPTIONS"],
        "allow_headers": ["Content-Type", "Authorization"]
    }
})

# Global variables
camera_streams = {}
detection_results = {}
frame_queues = {}
processing_threads = {}
camera_statuses = {}
performance_metrics = {}

# Camera stream class
class CameraStream:
    def __init__(self, camera_id, camera_index=0):
        self.camera_id = camera_id
        self.camera_index = camera_index
        self.status = "INITIALIZING"
        self.cap = None
        self.frame_queue = queue.Queue(maxsize=10)
        self.last_frame = None
        self.fps = 0
        self.prev_frame_time = 0
        self.new_frame_time = 0
        self.running = False
        self.thread = None

    def start(self):
        if self.running:
            return

        try:
            self.cap = cv2.VideoCapture(self.camera_index)
            if not self.cap.isOpened():
                self.status = "ERROR"
                logger.error(f"Failed to open camera {self.camera_id}")
                return False

            self.status = "RUNNING"
            self.running = True
            self.thread = threading.Thread(target=self._update, daemon=True)
            self.thread.start()
            logger.info(f"Camera {self.camera_id} started")
            return True
        except Exception as e:
            self.status = "ERROR"
            logger.error(f"Error starting camera {self.camera_id}: {str(e)}")
            return False

    def _update(self):
        while self.running:
            try:
                ret, frame = self.cap.read()
                if not ret:
                    self.status = "ERROR"
                    logger.error(f"Failed to read frame from camera {self.camera_id}")
                    time.sleep(1)
                    continue

                # Calculate FPS
                self.new_frame_time = time.time()
                self.fps = 1 / (self.new_frame_time - self.prev_frame_time) if (self.new_frame_time - self.prev_frame_time) > 0 else 0
                self.prev_frame_time = self.new_frame_time

                # Update performance metrics
                performance_metrics[self.camera_id] = {
                    "timestamp": time.time(),
                    "fps": round(self.fps, 2),
                    "queue_size": self.frame_queue.qsize(),
                    "status": self.status
                }

                # Put frame in queue, discard if queue is full
                if not self.frame_queue.full():
                    self.frame_queue.put(frame)
                self.last_frame = frame
            except Exception as e:
                self.status = "ERROR"
                logger.error(f"Error in camera {self.camera_id} update loop: {str(e)}")
                time.sleep(1)

    def get_frame(self):
        if self.last_frame is None:
            return None
        return self.last_frame

    def stop(self):
        self.running = False
        if self.thread is not None:
            self.thread.join(timeout=1.0)
        if self.cap is not None:
            self.cap.release()
        self.status = "STOPPED"
        logger.info(f"Camera {self.camera_id} stopped")

# Frame processor class
class FrameProcessor:
    def __init__(self, camera_id, model_path_detector='model/LP_detector_nano_61.pt', model_path_ocr='model/LP_ocr_nano_62.pt'):
        self.camera_id = camera_id
        self.running = False
        self.thread = None
        self.last_detection_time = 0
        self.detection_interval = 0.2  # seconds between detections

        # Load models
        try:
            self.yolo_LP_detect = torch.hub.load('yolov5', 'custom', path=model_path_detector, force_reload=True, source='local')
            self.yolo_license_plate = torch.hub.load('yolov5', 'custom', path=model_path_ocr, force_reload=True, source='local')
            self.yolo_license_plate.conf = 0.60
            logger.info(f"Models loaded for camera {camera_id}")
        except Exception as e:
            logger.error(f"Error loading models for camera {camera_id}: {str(e)}")
            raise

    def start(self):
        if self.running:
            return

        self.running = True
        self.thread = threading.Thread(target=self._process, daemon=True)
        self.thread.start()
        logger.info(f"Frame processor started for camera {self.camera_id}")

    def _process(self):
        while self.running:
            try:
                if self.camera_id not in frame_queues or frame_queues[self.camera_id].empty():
                    time.sleep(0.01)
                    continue

                # Check if enough time has passed since last detection
                current_time = time.time()
                if current_time - self.last_detection_time < self.detection_interval:
                    time.sleep(0.01)
                    continue

                # Get frame from queue
                frame = frame_queues[self.camera_id].get()

                # Process frame
                start_time = time.time()
                plates = self.yolo_LP_detect(frame, size=640)
                list_plates = plates.pandas().xyxy[0].values.tolist()
                list_read_plates = []

                for plate in list_plates:
                    flag = 0
                    x = int(plate[0])  # xmin
                    y = int(plate[1])  # ymin
                    w = int(plate[2] - plate[0])  # xmax - xmin
                    h = int(plate[3] - plate[1])  # ymax - ymin

                    crop_img = frame[y:y+h, x:x+w]

                    for cc in range(0, 2):
                        for ct in range(0, 2):
                            deskewed_img = utils_rotate.deskew(crop_img, cc, ct)
                            lp = helper.read_plate(self.yolo_license_plate, deskewed_img)
                            if lp != "unknown":
                                list_read_plates.append({
                                    "license_plate": lp,
                                    "confidence": float(plate[4]),
                                    "bbox": [x, y, w, h]
                                })
                                flag = 1
                                break
                        if flag == 1:
                            break

                # Update detection results
                detection_time = time.time() - start_time
                detection_results[self.camera_id] = {
                    "timestamp": time.time(),
                    "plates": list_read_plates,
                    "detection_time": round(detection_time, 3)
                }

                # Update performance metrics
                if self.camera_id in performance_metrics:
                    performance_metrics[self.camera_id]["detection_time"] = round(detection_time, 3)

                self.last_detection_time = current_time

            except Exception as e:
                logger.error(f"Error in frame processor for camera {self.camera_id}: {str(e)}")
                time.sleep(1)

    def stop(self):
        self.running = False
        if self.thread is not None:
            self.thread.join(timeout=1.0)
        logger.info(f"Frame processor stopped for camera {self.camera_id}")

# Routes
@app.route('/health', methods=['GET'])
def health_check():
    return jsonify({
        "status": "ok",
        "message": "Video Stream API is running",
        "cameras": {camera_id: camera_statuses.get(camera_id, "UNKNOWN") for camera_id in camera_streams}
    })

@app.route('/cameras', methods=['GET'])
def get_cameras():
    return jsonify({
        "cameras": [
            {
                "id": camera_id,
                "status": camera_streams[camera_id].status,
                "fps": round(camera_streams[camera_id].fps, 2) if hasattr(camera_streams[camera_id], 'fps') else 0
            } for camera_id in camera_streams
        ]
    })

@app.route('/cameras/<camera_id>/start', methods=['POST'])
def start_camera(camera_id):
    try:
        data = request.get_json() or {}
        camera_index = data.get('camera_index', 0)

        if camera_id in camera_streams:
            return jsonify({"error": f"Camera {camera_id} already exists"}), 400

        # Create camera stream
        camera = CameraStream(camera_id, camera_index)
        if not camera.start():
            return jsonify({"error": f"Failed to start camera {camera_id}"}), 500

        camera_streams[camera_id] = camera
        camera_statuses[camera_id] = "RUNNING"
        frame_queues[camera_id] = camera.frame_queue

        # Create and start frame processor
        processor = FrameProcessor(camera_id)
        processor.start()
        processing_threads[camera_id] = processor

        return jsonify({"message": f"Camera {camera_id} started successfully"})
    except Exception as e:
        logger.error(f"Error starting camera {camera_id}: {str(e)}")
        return jsonify({"error": str(e)}), 500

@app.route('/cameras/<camera_id>/stop', methods=['POST'])
def stop_camera(camera_id):
    if camera_id not in camera_streams:
        return jsonify({"error": f"Camera {camera_id} not found"}), 404

    try:
        # Stop frame processor
        if camera_id in processing_threads:
            processing_threads[camera_id].stop()
            del processing_threads[camera_id]

        # Stop camera stream
        camera_streams[camera_id].stop()
        camera_statuses[camera_id] = "STOPPED"

        # Clean up
        if camera_id in frame_queues:
            del frame_queues[camera_id]
        if camera_id in detection_results:
            del detection_results[camera_id]

        return jsonify({"message": f"Camera {camera_id} stopped successfully"})
    except Exception as e:
        logger.error(f"Error stopping camera {camera_id}: {str(e)}")
        return jsonify({"error": str(e)}), 500

@app.route('/cameras/<camera_id>/stream')
def stream_camera(camera_id):
    if camera_id not in camera_streams:
        return jsonify({"error": f"Camera {camera_id} not found"}), 404

    def generate():
        while camera_id in camera_streams and camera_streams[camera_id].running:
            frame = camera_streams[camera_id].get_frame()
            if frame is None:
                continue

            # Draw detection results if available
            if camera_id in detection_results:
                for plate in detection_results[camera_id].get("plates", []):
                    x, y, w, h = plate["bbox"]
                    cv2.rectangle(frame, (x, y), (x+w, y+h), (0, 0, 255), 2)
                    cv2.putText(frame, plate["license_plate"], (x, y-10), cv2.FONT_HERSHEY_SIMPLEX, 0.9, (36, 255, 12), 2)

            # Draw FPS
            fps = camera_streams[camera_id].fps
            cv2.putText(frame, f"FPS: {int(fps)}", (10, 30), cv2.FONT_HERSHEY_SIMPLEX, 1, (100, 255, 0), 2)

            # Encode frame to JPEG
            ret, buffer = cv2.imencode('.jpg', frame)
            if not ret:
                continue

            frame_bytes = buffer.tobytes()
            yield (b'--frame\r\n'
                   b'Content-Type: image/jpeg\r\n\r\n' + frame_bytes + b'\r\n')

            # Limit frame rate to reduce CPU usage
            time.sleep(0.03)  # ~30 FPS max

    return Response(generate(), mimetype='multipart/x-mixed-replace; boundary=frame')

@app.route('/cameras/<camera_id>/detections', methods=['GET'])
def get_detections(camera_id):
    if camera_id not in camera_streams:
        return jsonify({"error": f"Camera {camera_id} not found"}), 404

    if camera_id not in detection_results:
        return jsonify({"plates": []})

    return jsonify(detection_results[camera_id])

@app.route('/cameras/<camera_id>/metrics', methods=['GET'])
def get_metrics(camera_id):
    if camera_id not in camera_streams:
        return jsonify({"error": f"Camera {camera_id} not found"}), 404

    if camera_id not in performance_metrics:
        return jsonify({"error": "No metrics available"}), 404

    return jsonify(performance_metrics[camera_id])

@app.route('/cameras/<camera_id>/frame', methods=['GET'])
def get_camera_frame(camera_id):
    if camera_id not in camera_streams:
        return jsonify({"error": f"Camera {camera_id} not found"}), 404

    frame = camera_streams[camera_id].get_frame()
    if frame is None:
        return jsonify({"error": "No frame available"}), 404

    # Make a copy of the frame to avoid modifying the original
    frame_copy = frame.copy()

    # Draw detection results if available
    if camera_id in detection_results:
        for plate in detection_results[camera_id].get("plates", []):
            x, y, w, h = plate["bbox"]
            cv2.rectangle(frame_copy, (x, y), (x+w, y+h), (0, 0, 255), 2)
            cv2.putText(frame_copy, plate["license_plate"], (x, y-10), cv2.FONT_HERSHEY_SIMPLEX, 0.9, (36, 255, 12), 2)

            # Log detection for debugging
            print(f"Camera {camera_id} detected license plate: {plate['license_plate']} at {x},{y},{w},{h}")

    # Encode frame to JPEG with high quality
    ret, buffer = cv2.imencode('.jpg', frame_copy, [cv2.IMWRITE_JPEG_QUALITY, 95])
    if not ret:
        return jsonify({"error": "Failed to encode frame"}), 500

    # Add CORS headers
    response = Response(buffer.tobytes(), mimetype='image/jpeg')
    response.headers.add('Access-Control-Allow-Origin', '*')
    return response

@app.route('/cameras/<camera_id>/raw-frame', methods=['GET'])
def get_raw_camera_frame(camera_id):
    """Get raw frame without any annotations for ML processing"""
    if camera_id not in camera_streams:
        return jsonify({"error": f"Camera {camera_id} not found"}), 404

    frame = camera_streams[camera_id].get_frame()
    if frame is None:
        return jsonify({"error": "No frame available"}), 404

    # Encode frame to JPEG with high quality
    ret, buffer = cv2.imencode('.jpg', frame, [cv2.IMWRITE_JPEG_QUALITY, 95])
    if not ret:
        return jsonify({"error": "Failed to encode frame"}), 500

    # Add CORS headers
    response = Response(buffer.tobytes(), mimetype='image/jpeg')
    response.headers.add('Access-Control-Allow-Origin', '*')
    return response

@app.route('/metrics', methods=['GET'])
def get_all_metrics():
    return jsonify(performance_metrics)

# Initialize default cameras
def init_cameras():
    # Initialize entry cameras
    for i in range(2):
        camera_id = f"IN-0{i+1}"
        try:
            camera = CameraStream(camera_id, i)
            if camera.start():
                camera_streams[camera_id] = camera
                camera_statuses[camera_id] = "RUNNING"
                frame_queues[camera_id] = camera.frame_queue

                # Create and start frame processor
                processor = FrameProcessor(camera_id)
                processor.start()
                processing_threads[camera_id] = processor

                logger.info(f"Initialized camera {camera_id}")
            else:
                logger.warning(f"Failed to initialize camera {camera_id}")
        except Exception as e:
            logger.error(f"Error initializing camera {camera_id}: {str(e)}")

if __name__ == '__main__':
    # Initialize cameras on startup
    # Uncomment to auto-start cameras
    # init_cameras()

    # Start the Flask app
    app.run(host='0.0.0.0', port=4051, debug=True, threaded=True)
