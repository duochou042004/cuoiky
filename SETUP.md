# Smart Parking System — Local Development Setup

## Prerequisites

| Tool | Version | Install |
|------|---------|---------|
| Node.js | ≥ 18 | https://nodejs.org |
| .NET SDK | 8.0 | https://dotnet.microsoft.com/download |
| Python | 3.9 – 3.11 | https://www.python.org |
| Docker + Compose | any recent | https://docs.docker.com/get-docker |
| Git | any | https://git-scm.com |

## 1. Clone and enter the repo

```bash
git clone <repo-url>
cd cuoiky
```

## 2. Start MongoDB (Docker)

The only containerized service. Run it first — both the backend and dev experience depend on it.

```bash
docker compose up -d
```

Verify: `docker compose ps` should show `smartparking-mongo` as healthy.

MongoDB will persist data in a named volume (`smartparking_mongo_data`) across restarts.

## 3. Configure secrets (local override)

Copy the example file and fill in your values. This file is **gitignored**.

```bash
cp SmartParking.Core/SmartParking.Core/appsettings.Development.json.example \
   SmartParking.Core/SmartParking.Core/appsettings.Development.json
```

Edit `appsettings.Development.json` and set:

```json
{
  "EmailSettings": {
    "SmtpUsername": "your-gmail@gmail.com",
    "SmtpPassword": "your-gmail-app-password",
    "SenderEmail": "your-gmail@gmail.com"
  },
  "PaymentGateways": {
    "Momo": {
      "AccessKey": "your-momo-access-key",
      "SecretKey": "your-momo-secret-key"
    }
  },
  "JwtSettings": {
    "Secret": "change-this-to-a-random-32-char-string!!"
  },
  "AdminUser": {
    "Password": "YourSecureAdminPassword123!"
  }
}
```

> For local development without email/payment features, you can leave dummy values — those features will fail gracefully.

## 4. Obtain ML models

The models are **not in the repository** (large binary files). You need them for the Python license plate service and optional ML.NET vehicle classifier.

**YOLOv5 models** (required for any license plate recognition):
- Download from the project shared drive (ask maintainer)
- Place as:
  ```
  License-Plate-Recognition-main/model/LP_detector_nano_61.pt
  License-Plate-Recognition-main/model/LP_ocr_nano_62.pt
  ```
- The `api.py` service also accepts `LP_detector.pt` and `LP_ocr.pt` — original full-size versions

**ML.NET vehicle classifier** (optional — system falls back to plate-format heuristics if absent):
- Download `VehicleClassification.zip` from shared drive
- Place as:
  ```
  SmartParking.Core/SmartParking.Core/MLModels/VehicleClassification.zip
  ```

## 5. Set up Python environment

```bash
cd License-Plate-Recognition-main
python -m venv .venv

# Linux / macOS
source .venv/bin/activate

# Windows
.venv\Scripts\activate

pip install -r requirements.txt
```

> **Important**: Python 3.12+ is not supported by some YOLOv5 dependencies. Use 3.9–3.11.

## 6. Install frontend dependencies

```bash
cd smart-parking-frontend
npm install
```

## 7. Start all services

Open **four terminal windows** (or use a terminal multiplexer):

### Terminal 1 — License Plate OCR API (port 4050)
```bash
cd License-Plate-Recognition-main
source .venv/bin/activate   # or .venv\Scripts\activate on Windows
python api.py
```
Health check: `curl http://localhost:4050/health`

### Terminal 2 — Camera Streaming API (port 4051)
```bash
cd License-Plate-Recognition-main
source .venv/bin/activate
python stream_api.py
```
Health check: `curl http://localhost:4051/health`

> The streaming API requires a physical webcam. If no webcam is attached, cameras will fail to start (the service itself still runs).

### Terminal 3 — .NET Backend (port 5125)
```bash
cd SmartParking.Core
dotnet run --project SmartParking.Core
```
Health check: `curl http://localhost:5125/swagger`

On first run, the backend will:
- Create MongoDB collections and indexes
- Initialize parking slots (200 motorbike + 50 car slots by default)
- Create the default admin user

> **One-time database maintenance.** Schema migrations and duplicate-record cleanup
> no longer run on every startup. If you are upgrading an older database that needs
> these fixes, run the backend once with the maintenance flag:
> ```bash
> dotnet run --project SmartParking.Core -- --run-maintenance
> ```
> A fresh install does not need this.

### Terminal 4 — React Frontend (port 3000)
```bash
cd smart-parking-frontend
npm run dev
```
Open: http://localhost:3000

## 8. First login

Default admin credentials (set in `appsettings.Development.json`):
- Username: `admin`
- Password: whatever you set in `AdminUser.Password`

To create staff users, log in as admin and go to **Settings → User Management**.

## Service Dependencies

```
Browser (port 3000)
  ↓ /api/* proxied to
.NET Backend (port 5125)
  ↓ calls
  ├── MongoDB (port 27017)
  ├── License Plate API (port 4050)  — for image-upload check-in/check-out
  └── Streaming API (port 4051)      — for camera snapshot capture
      └── Physical webcam (via OpenCV)

Browser also connects directly to:
  └── Streaming API (port 4051)      — MJPEG video stream (direct cross-origin)
```

## Common Issues

### Backend crashes on startup with `FileNotFoundException`
The ML.NET model is missing. Either:
- Place `VehicleClassification.zip` at `SmartParking.Core/SmartParking.Core/MLModels/` (see step 4), or
- The backend is designed to fall back to heuristic classification — check the version (after P0-2 fix is applied)

### Camera stream shows nothing / black screen
- The Python streaming API uses camera index 0 by default. Verify a webcam is attached.
- Check: `python -c "import cv2; cap = cv2.VideoCapture(0); print(cap.isOpened())"`

### MongoDB connection refused
- Ensure Docker is running: `docker compose ps`
- Restart: `docker compose restart mongo`

### Port 5125 already in use
```bash
# Linux
lsof -i :5125
kill -9 <PID>
```

### License plate recognition returns "Unknown"
- Check the YOLOv5 models are in the correct `model/` directory
- Verify the image has a clear, well-lit license plate
- Check Python API logs for errors

## Building for Production

```bash
# Build frontend static files
cd smart-parking-frontend
npm run build
# Output in smart-parking-frontend/dist/

# Build backend
cd SmartParking.Core
dotnet publish SmartParking.Core -c Release -o ./publish
```

For production deployment, set environment variables rather than using `appsettings.json`:
```bash
export ConnectionStrings__MongoDb="mongodb://user:pass@host:27017"
export JwtSettings__Secret="your-production-secret-at-least-32-chars"
# etc.
```

## Scripts (Linux)

```bash
# Start everything (convenience wrapper — create this yourself or use tmux)
# There is no run_smart_parking.sh yet — run the four terminals manually

# Check if all services are up
curl -s http://localhost:4050/health | python -m json.tool
curl -s http://localhost:4051/health | python -m json.tool
curl -s http://localhost:5125/swagger/v1/swagger.json | head -5
curl -s http://localhost:3000 | head -3
```
