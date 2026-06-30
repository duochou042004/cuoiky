<div align="center">

# 🅿️ Smart Parking System

**An intelligent parking-management platform with automatic license-plate recognition,
vehicle classification, monthly subscriptions, online payments, and real-time monitoring.**

[![CI](https://github.com/tduo1404pty1802/cuoiky/actions/workflows/ci.yml/badge.svg)](https://github.com/tduo1404pty1802/cuoiky/actions/workflows/ci.yml)
[![License](https://img.shields.io/badge/license-MIT-green.svg)](#-license)

### Built with

![.NET](https://img.shields.io/badge/.NET%208-512BD4?style=for-the-badge&logo=dotnet&logoColor=white)
![C#](https://img.shields.io/badge/C%23-239120?style=for-the-badge&logo=csharp&logoColor=white)
![React](https://img.shields.io/badge/React%2019-20232A?style=for-the-badge&logo=react&logoColor=61DAFB)
![Vite](https://img.shields.io/badge/Vite-646CFF?style=for-the-badge&logo=vite&logoColor=white)
![Python](https://img.shields.io/badge/Python-3776AB?style=for-the-badge&logo=python&logoColor=white)
![Flask](https://img.shields.io/badge/Flask-000000?style=for-the-badge&logo=flask&logoColor=white)

![MongoDB](https://img.shields.io/badge/MongoDB-47A248?style=for-the-badge&logo=mongodb&logoColor=white)
![SignalR](https://img.shields.io/badge/SignalR-512BD4?style=for-the-badge&logo=dotnet&logoColor=white)
![OpenCV](https://img.shields.io/badge/OpenCV-5C3EE8?style=for-the-badge&logo=opencv&logoColor=white)
![YOLOv5](https://img.shields.io/badge/YOLOv5-00FFFF?style=for-the-badge&logo=yolo&logoColor=black)
![Stripe](https://img.shields.io/badge/Stripe-635BFF?style=for-the-badge&logo=stripe&logoColor=white)
![Docker](https://img.shields.io/badge/Docker-2496ED?style=for-the-badge&logo=docker&logoColor=white)

</div>

---

## 📖 Overview

Smart Parking System automates the full life cycle of a Vietnamese parking lot: it recognises
license plates from images or live camera frames, classifies the vehicle (car / motorbike),
assigns a slot, tracks occupancy in real time, handles monthly subscriptions, collects fees
through multiple payment channels, and surfaces revenue analytics — all behind a role-based
operator console.

> 🌐 The operator UI is in **Vietnamese**; code, comments, and documentation are in **English**.

---

## ✨ Features

- 🔍 **Automatic license-plate recognition** (YOLOv5 detector + OCR)
- 🚗 **Vehicle classification** (car / motorbike) via ML.NET, with a heuristic fallback
- 🅿️ **Real-time slot management** pushed to the dashboard over SignalR
- 🎫 **Monthly subscriptions** — registration, renewal, and auto check-out
- 💳 **Online payments** — Cash, MoMo e-wallet, and Stripe (card)
- 📊 **Revenue & transaction reports** with CSV / PDF export
- 👥 **Role-based access** (Admin / Operator) with JWT authentication
- ⚙️ **Configurable settings** — fees, slot counts, and zones

---

## 🏗️ Architecture

The system is composed of four runtime services plus a MongoDB database:

| Component | Technology | Port | Responsibility |
|-----------|------------|------|----------------|
| **Frontend** | React 19 + Vite | `3000` | Operator console |
| **Backend API** | .NET 8 Web API + SignalR | `5125` | Business logic, auth, real-time hub |
| **License Plate API** | Python + Flask | `4050` | Image-based OCR (static images) |
| **Streaming API** | Python + Flask + OpenCV | `4051` | Live webcam feed + real-time OCR |
| **Database** | MongoDB (Docker) | `27017` | Persistence |

```
                ┌──────────────┐   REST + SignalR   ┌────────────────────┐
                │  React (Vite)│ ◀────────────────▶ │  .NET 8 Web API     │
                │   :3000      │                    │   :5125             │
                └──────────────┘                    └─────────┬──────────┘
                                                              │ HTTP
                                          ┌───────────────────┼───────────────────┐
                                          ▼                   ▼                   ▼
                                 ┌─────────────────┐ ┌─────────────────┐ ┌──────────────┐
                                 │ Plate OCR :4050 │ │ Streaming :4051 │ │ MongoDB      │
                                 │ (Flask + YOLO)  │ │ (Flask + OpenCV)│ │  :27017      │
                                 └─────────────────┘ └─────────────────┘ └──────────────┘
```

---

## 🛠️ Tech Stack

| Layer | Technologies |
|-------|--------------|
| Frontend | React 19, Vite, React-Bootstrap, Chart.js, SignalR client, Stripe Elements |
| Backend | ASP.NET Core 8, SignalR, MongoDB.Driver, ML.NET, JWT, Stripe.NET |
| Recognition | Python, Flask, YOLOv5 (PyTorch), OpenCV |
| Database | MongoDB 7 |
| Payments | Cash, MoMo, Stripe |
| Tooling | Docker, GitHub Actions (CI) |

---

## 🚀 Getting Started

> A complete, step-by-step guide — including secrets, ML models, and the Python
> virtual-environment setup — lives in **[SETUP.md](SETUP.md)**. The quickstart below is a summary.

### Prerequisites

- [.NET SDK 8.0+](https://dotnet.microsoft.com/download)
- [Node.js 20+](https://nodejs.org/)
- [Python 3.10+](https://www.python.org/)
- [Docker](https://www.docker.com/) (for MongoDB)

### 1. Start MongoDB

```bash
docker compose up -d
```

### 2. Configure secrets

```bash
cp SmartParking.Core/SmartParking.Core/appsettings.Development.json.example \
   SmartParking.Core/SmartParking.Core/appsettings.Development.json
# then fill in your values (this file is gitignored)
```

### 3. Run each service (four terminals)

```bash
# Terminal 1 — License Plate OCR API  →  http://localhost:4050
cd License-Plate-Recognition-main && python api.py

# Terminal 2 — Streaming API          →  http://localhost:4051
cd License-Plate-Recognition-main && python stream_api.py

# Terminal 3 — Backend API            →  http://localhost:5125
cd SmartParking.Core && dotnet run --project SmartParking.Core

# Terminal 4 — Frontend               →  http://localhost:3000
cd smart-parking-frontend && npm install && npm run dev
```

Then open **http://localhost:3000**. The default admin account is created on first run from
`appsettings.Development.json` (`AdminUser` section) — never commit real credentials.

---

## 🔌 API Overview

A selection of the most-used endpoints (see Swagger at `/swagger` for the full contract).

<details>
<summary><strong>Backend API (.NET, :5125)</strong></summary>

| Method | Endpoint | Description |
|--------|----------|-------------|
| `POST` | `/api/vehicle/checkin` | Check a vehicle into the lot |
| `POST` | `/api/vehicle/checkout/{vehicleId}` | Check a vehicle out |
| `GET`  | `/api/parking/slots` | List all parking slots |
| `POST` | `/api/monthlyvehicle/register` | Register a monthly subscription |
| `POST` | `/api/payment/{cash\|momo\|stripe}` | Create a payment |
| `POST` | `/api/payment/webhook/stripe` | Stripe webhook (signature-verified) |
| `GET`  | `/api/reports/revenue` | Revenue report |

</details>

<details>
<summary><strong>License Plate &amp; Streaming APIs (Flask, :4050 / :4051)</strong></summary>

| Method | Endpoint | Description |
|--------|----------|-------------|
| `POST` | `:4050/recognize` | Recognise a plate from an uploaded image |
| `POST` | `:4051/cameras/{id}/start` | Start a camera stream |
| `GET`  | `:4051/cameras/{id}/stream` | MJPEG live stream |
| `GET`  | `:4051/cameras/{id}/raw-frame` | Single raw JPEG frame |

</details>

---

## 🎥 Recognition Modes

- **Image upload** — the operator uploads a photo; the backend runs OCR and classification,
  then assigns a slot.
- **Camera snapshot (supported live mode)** — the operator captures a snapshot from a live
  feed and confirms the action. This deliberate, one-shot flow avoids the duplicate
  detections and OCR load that continuous polling caused.

> ℹ️ Fully automatic, always-on detection is intentionally **disabled** (`CameraMonitoringService`
> runs in *manual-capture mode*). The rationale and the path to re-enable it are documented in
> the service and in [IMPROVEMENT_ROADMAP.md](IMPROVEMENT_ROADMAP.md).

---

## 📁 Project Structure

```
cuoiky/
├── smart-parking-frontend/         # React 19 + Vite operator console
├── SmartParking.Core/              # .NET 8 Web API + SignalR + ML.NET
├── License-Plate-Recognition-main/ # Flask OCR (api.py) + streaming (stream_api.py)
├── docker-compose.yml              # MongoDB
├── .github/workflows/ci.yml        # CI pipeline
├── CLAUDE.md                       # Architecture notes for contributors
├── SETUP.md                        # Full local setup guide
└── IMPROVEMENT_ROADMAP.md          # Roadmap & progress tracking
```

---

## ✅ Continuous Integration

Every push and pull request to `develop` / `main` runs **[GitHub Actions](.github/workflows/ci.yml)**:

| Job | Steps |
|-----|-------|
| **Backend (.NET 8)** | restore → build (Release) → `dotnet test` |
| **Frontend (React/Vite)** | `npm ci` → `tsc` type-check → `vite build` |
| **Python (License Plate APIs)** | `py_compile` → `flake8` (error checks) |

---

## 🗺️ Roadmap

The codebase review and prioritised improvement plan (P0–P3, all completed) are tracked in
**[IMPROVEMENT_ROADMAP.md](IMPROVEMENT_ROADMAP.md)**.

---

## 🤝 Contributing

- `develop` is the integration branch; feature work happens on `feature/*` branches via PR.
- Commit messages follow `type: description` (`feat`, `fix`, `chore`, `docs`, `merge`).
- See [CLAUDE.md](CLAUDE.md) for architecture conventions before contributing.

---

## 📄 License

Released under the **MIT License**.

---

<div align="center">
<sub>Smart Parking System — license-plate recognition, real-time monitoring, and online payments.</sub>
</div>
