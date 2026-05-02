from flask import Flask, request, jsonify
import cv2
import torch
import os
import tempfile
import time
import function.helper as helper
import function.utils_rotate as utils_rotate
from werkzeug.utils import secure_filename

app = Flask(__name__)

# Load YOLO models
try:
    yolo_LP_detect = torch.hub.load('yolov5', 'custom', path='model/LP_detector.pt', force_reload=True, source='local')
    yolo_license_plate = torch.hub.load('yolov5', 'custom', path='model/LP_ocr.pt', force_reload=True, source='local')
    yolo_license_plate.conf = 0.60
    print("Models loaded successfully")
except Exception as e:
    print(f"Error loading models: {e}")

@app.route('/health', methods=['GET'])
def health_check():
    return jsonify({"status": "ok", "message": "License Plate Recognition API is running"})

@app.route('/recognize', methods=['POST'])
def recognize_license_plate():
    if 'image' not in request.files:
        return jsonify({"success": False, "error": "No image provided"}), 400

    file = request.files['image']
    if file.filename == '':
        return jsonify({"success": False, "error": "No image selected"}), 400

    # Save the uploaded file temporarily
    filename = secure_filename(file.filename)
    temp_dir = tempfile.gettempdir()  # Get system's temp directory
    # Create a unique filename to avoid collisions
    unique_filename = f"{int(time.time())}_{filename}"
    temp_path = os.path.join(temp_dir, unique_filename)
    file.save(temp_path)

    try:
        # Read the image
        img = cv2.imread(temp_path)
        if img is None:
            return jsonify({"success": False, "error": "Could not read image"}), 400

        # Detect license plates
        plates = yolo_LP_detect(img, size=640)
        list_plates = plates.pandas().xyxy[0].values.tolist()
        license_plate = "Unknown"

        if len(list_plates) == 0:
            # Try direct OCR on the image if no plate is detected
            lp = helper.read_plate(yolo_license_plate, img)
            if lp != "unknown":
                license_plate = lp
        else:
            # Process each detected plate
            for plate in list_plates:
                flag = 0
                x = int(plate[0])  # xmin
                y = int(plate[1])  # ymin
                w = int(plate[2] - plate[0])  # xmax - xmin
                h = int(plate[3] - plate[1])  # ymax - ymin
                crop_img = img[y:y+h, x:x+w]

                # Try different rotations to get the best OCR result
                for cc in range(0, 2):
                    for ct in range(0, 2):
                        lp = helper.read_plate(yolo_license_plate, utils_rotate.deskew(crop_img, cc, ct))
                        if lp != "unknown":
                            license_plate = lp
                            flag = 1
                            break
                    if flag == 1:
                        break

        # Clean up
        if os.path.exists(temp_path):
            os.remove(temp_path)

        return jsonify({
            "success": True,
            "licensePlate": license_plate
        })

    except Exception as e:
        # Clean up in case of error
        if os.path.exists(temp_path):
            os.remove(temp_path)
        return jsonify({"success": False, "error": str(e)}), 500

if __name__ == '__main__':
    host = os.getenv('FLASK_HOST', '0.0.0.0')
    port = int(os.getenv('FLASK_PORT', '4050'))
    debug = os.getenv('FLASK_DEBUG', 'false').lower() == 'true'
    app.run(host=host, port=port, debug=debug)
