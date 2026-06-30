# Smart Parking System — CLAUDE.md

Project context for Claude Code sessions.

## Project Overview

A smart parking management system for Vietnamese parking lots. It handles vehicle check-in/check-out via license plate recognition, monthly vehicle subscriptions, fee collection, and real-time dashboard monitoring.

## Architecture

Four runtime components that must all be running:

| Component | Tech | Port | Purpose |
|-----------|------|------|---------|
| Frontend | React 19 + Vite | 3000 | Operator UI |
| Backend API | .NET 8 Web API | 5125 | Business logic, SignalR hub |
| License Plate API | Python Flask | 4050 | Image-based OCR (static images) |
| Streaming API | Python Flask | 4051 | Live webcam feed + real-time OCR |

**Database**: MongoDB on port 27017 (run via Docker — see SETUP.md).

**ML models** (not in repo — must be obtained separately):
- YOLOv5 detector: `License-Plate-Recognition-main/model/LP_detector.pt` (or `LP_detector_nano_61.pt`)
- YOLOv5 OCR: `License-Plate-Recognition-main/model/LP_ocr.pt` (or `LP_ocr_nano_62.pt`)
- ML.NET vehicle classifier: `SmartParking.Core/SmartParking.Core/MLModels/VehicleClassification.zip`

## Key File Locations

```
cuoiky/
├── CLAUDE.md                          # This file
├── SETUP.md                           # Local dev setup guide
├── IMPROVEMENT_ROADMAP.md             # Issue tracking and roadmap
├── docker-compose.yml                 # MongoDB only
├── smart-parking-frontend/
│   ├── src/
│   │   ├── pages/                     # Route-level page components
│   │   │   ├── CheckIn.jsx            # Image-upload check-in flow
│   │   │   ├── CheckOut.jsx           # Image-upload check-out flow
│   │   │   ├── CameraMonitoring.jsx   # Live camera feed page
│   │   │   ├── Dashboard.jsx          # Main dashboard
│   │   │   ├── Reports.jsx            # Revenue and transaction reports
│   │   │   ├── MonthlyRegistration.jsx # Monthly subscription management
│   │   │   ├── AccessControl.jsx      # Unified vehicle management hub
│   │   │   └── Settings.jsx           # Admin settings + user management
│   │   ├── components/
│   │   │   ├── WebcamViewer.jsx       # Camera widget (per-camera)
│   │   │   ├── VehicleImageUpload.jsx # Image-upload recognition widget
│   │   │   └── PaymentModal.jsx       # Cash / Momo payment dialog
│   │   ├── contexts/AuthContext.jsx   # JWT auth state
│   │   └── utils/axiosConfig.js       # Axios instance with 401 interceptor
│   └── vite.config.js                 # Dev proxy: /api → :5125, /parkingHub → :5125 (ws)
├── SmartParking.Core/SmartParking.Core/
│   ├── Controllers/
│   │   ├── CheckInOutController.cs    # POST /api/vehicle/checkin|verify-checkout|checkout
│   │   ├── CameraController.cs        # Camera management + snapshot processing
│   │   ├── PaymentController.cs       # Cash / Momo / Stripe payment endpoints
│   │   ├── MonthlyVehicleController.cs
│   │   ├── ReportController.cs
│   │   └── SettingsController.cs
│   ├── Services/
│   │   ├── ParkingService.cs          # Slot assignment, park/exit vehicle
│   │   ├── LicensePlateService.cs     # Calls Python :4050 for OCR + ML.NET classify
│   │   ├── ParkingFeeService.cs       # Fee calculation (casual + monthly)
│   │   ├── MLModelPrediction.cs       # ML.NET vehicle type classifier
│   │   ├── VehicleClassificationService.cs  # Camera frame classifier
│   │   ├── CameraMonitoringService.cs # Background service (manual-capture mode)
│   │   ├── MomoPaymentService.cs      # Momo QR payment integration
│   │   ├── StripePaymentService.cs    # Stripe via official SDK (auto mock fallback w/o keys)
│   │   └── AuthService.cs             # JWT auth + user management
│   ├── Hubs/ParkingHub.cs             # SignalR hub
│   ├── appsettings.json               # Config (DO NOT commit real secrets)
│   └── Program.cs                     # App startup + DI registration
└── License-Plate-Recognition-main/
    ├── api.py                         # Static image OCR — port 4050
    ├── stream_api.py                  # Webcam streaming + OCR — port 4051
    └── function/
        ├── helper.py                  # OCR helper
        └── utils_rotate.py            # Image deskew
```

## Environment Variables / Secrets

**Never commit real credentials.** The backend reads from `appsettings.json` which should be overridden by environment-specific files or environment variables. For local dev, use `appsettings.Development.json` (gitignored). Required keys:

```
SMTP_USERNAME, SMTP_PASSWORD          # Email service
MOMO_ACCESS_KEY, MOMO_SECRET_KEY      # Momo payment
STRIPE_API_KEY, STRIPE_WEBHOOK_SECRET # Stripe payment
JWT_SECRET                            # Must be ≥32 chars
MONGO_CONNECTION_STRING               # MongoDB URI
```

## Vehicle Type Constants

Canonical values, used end-to-end: `CAR` | `MOTORBIKE`

As of P1-3 the frontend uses `MOTORBIKE` everywhere — the old `MOTORBIKE → MOTORCYCLE`
remap in `VehicleImageUpload.jsx` and the monthly registration form has been removed.
Backend values are now passed straight through. Do not reintroduce `MOTORCYCLE`.

## Data Flow: Check-In (Image Upload)

```
User uploads photo
  → POST /api/vehicle/checkin (multipart)
    → LicensePlateService: saves temp file → calls :4050/recognize (Python OCR)
    → MLModelPrediction: classifies vehicle type from image
    → ParkingService.ParkVehicle(plate, type) → assigns slot → writes to MongoDB
  ← returns vehicle record + slot assignment
```

## Data Flow: Check-In (Camera)

```
Operator clicks "Capture Snapshot" on WebcamViewer
  → POST /api/cameras/{id}/capture-snapshot
    → .NET: GET :4051/cameras/{id}/raw-frame (gets JPEG bytes)
    → .NET: POST :4050/recognize (Python OCR)
    → VehicleClassificationService: classifies from frame bytes
    → emits ReceiveManualSnapshot via SignalR
  ← returns { licensePlate, vehicleType, canCheckIn, canCheckOut }
Operator clicks "Check In Vehicle" button
  → POST /api/cameras/{id}/process-vehicle { action: "checkin", ... }
    → ParkingService.ParkVehicle → assigns slot
    → emits ReceiveVehicleEntry via SignalR
```

## Data Flow: Check-Out (Camera) — Casual Vehicle

```
Operator clicks "Capture Snapshot"
  → snapshot returns { canCheckOut: true, vehicleId }
Operator clicks "Check Out Vehicle"
  → POST /api/cameras/{id}/process-vehicle { action: "checkout", ... }
    → Casual vehicle: calculates fee, emits ReceiveVehicleAtExit via SignalR
    ← returns { requiresPayment: true, parkingFee, vehicleId }
  (!) Frontend must intercept this and open PaymentModal → then POST /api/vehicle/checkout/{id}
```

## SignalR Events

The backend emits these events on `ParkingHub`:

| Event | Trigger | Frontend listener |
|-------|---------|-------------------|
| `ReceiveVehicleEntry` | Vehicle checked in | CameraMonitoring: adds to recentDetections |
| `ReceiveVehicleExit` | Vehicle checked out | CameraMonitoring: adds to recentDetections |
| `ReceiveManualSnapshot` | Snapshot captured | CameraMonitoring: adds to recentDetections |
| `ReceiveCameraUpdate` | Camera started/stopped | (currently unused in UI) |
| `ReceiveVehicleAtExit` | Casual vehicle at exit gate | CameraMonitoring: opens ParkingPaymentModal (fixed in P0-1) |
| `ParkingUpdated` | Slot state changed | Dashboard: refreshes parking slots |

## Known Architectural Issues

See `IMPROVEMENT_ROADMAP.md` for the full list and progress tracking.

**Resolved (P0 + P1):**
- ✅ Camera checkout for casual vehicles — `ReceiveVehicleAtExit` now has a frontend listener that opens the payment modal (P0-1)
- ✅ ML model path no longer hardcoded — missing model degrades gracefully instead of crashing startup (P0-2)
- ✅ Real credentials removed from `appsettings.json` — moved to gitignored `appsettings.Development.json` (P0-3)
- ✅ Dead code removed; vehicle type standardized to `MOTORBIKE`; TypeScript made strict with a CI typecheck (P1)
- ✅ Single axios config; gated DB maintenance (`--run-maintenance`); login rate limiting; JWT expiry UX; configurable stream CORS (P2)
- ✅ React error boundary; `/DebugFrames` Development-only; JWT issuer/audience + CORS allow-list; real Stripe integration; unit tests (P3)

**P0–P3 of the roadmap are complete.** See `IMPROVEMENT_ROADMAP.md` for the full record.
Possible future work: real coverage for MongoDB-backed services, code-splitting the large
frontend bundle, and replacing the `manual capture` camera mode with a debounced auto-mode.

## Running Tests

CI (`.github/workflows/ci.yml`) runs all of the below on every push/PR to `develop`/`main`.

```bash
# Backend — build the solution then run the test project
cd SmartParking.Core
dotnet build SmartParking.Core.sln -c Release
dotnet test BasicTests/BasicTests.csproj   # currently placeholder tests only

# Frontend — type-check (strict) then build
cd smart-parking-frontend
npm run typecheck   # tsc --noEmit, strict mode over the .tsx files
npm run build       # vite production build

# Python — syntax + lint the license-plate services
cd License-Plate-Recognition-main
python -m py_compile api.py stream_api.py
flake8 api.py stream_api.py --select=E9,F63,F7,F82
```

## Commit Message Style

Follow existing commits: `type: description`
- `feat:` new feature
- `fix:` bug fix
- `chore:` non-functional (deps, tooling)
- `merge:` merge commits (auto-generated)
- `docs:` documentation

## Branch Strategy

- `develop` — integration branch (default)
- `feature/*` — feature branches, PR into develop
- Do not push directly to develop; use PRs

## Language Note

UI strings and user-facing text are in Vietnamese. Code, comments, commit messages, and documentation are in English.
