# Smart Parking System — Improvement Roadmap

Generated from full codebase review on 2026-06-30.

---

## Current State Summary

### What Works
- Image-upload check-in/check-out pipeline (full flow, including payment modal)
- Monthly vehicle registration, renewal, auto-checkout
- Dashboard real-time slot updates via SignalR
- Auth (JWT), admin user management, password reset via email
- Reports: revenue charts, transaction history, CSV/PDF export
- Settings: configurable fees, parking space counts
- MoMo payment integration (test environment)

### Major Broken Features
1. **Camera checkout for casual vehicles is non-functional.** The backend emits `ReceiveVehicleAtExit` via SignalR when an exit camera detects a casual vehicle, but the frontend has no listener for that event. The vehicle remains permanently "parked" in the system.
2. **ML model will crash the backend on a fresh machine.** `MLModelPrediction.cs` has a hardcoded absolute path `/home/user/ProjectITS/...` from the original developer's machine. If the model is absent from all expected paths, startup throws `FileNotFoundException` rather than degrading gracefully.
3. **Real credentials are committed to version control.** `appsettings.json` contains live Gmail SMTP password, Momo payment keys, and the JWT signing secret.

### Significant Quality Issues
- Unit tests (`BasicTests/UnitTest1.cs`) are placeholder-only — 5 tests that verify 2+3=5. Zero coverage of any service or controller.
- Dashboard displays hardcoded mock data silently when the backend is unreachable, misleading operators.
- `ParkingSpaceStatus.tsx` and `/pages/PaymentModal.jsx` are dead code (unused duplicates of their `.jsx` / `/components/` counterparts).
- Three pages (`Transactions.jsx`, `MonthlyVehicles.jsx`, `CheckoutComplete.jsx`) exist but have no route in App.jsx.
- Vehicle type string inconsistency: backend uses `MOTORBIKE`, frontend uses `MOTORCYCLE` in the monthly registration path. Mapped in `VehicleImageUpload.jsx` but leaks in display logic.
- `axiosConfig.js` creates a configured Axios instance (with 401 redirect + timeout) that is never imported — pages use raw `axios` directly.
- `CameraMonitoring.jsx` uses native `alert()` for feedback instead of toast notifications.
- `setupDefaultCameras()` starts all 4 cameras with `cameraIndex: 0` (same physical device).
- SignalR connection is never stopped on component unmount in `CameraMonitoring.jsx` (closure over stale `null`).
- `.tsx` components exist without a `tsconfig.json`, so TypeScript checking is silently bypassed.
- `stream_api.py` CORS restricts to `localhost:3000` but `WebcamViewer.jsx` fetches the MJPEG stream directly from `localhost:4051` (cross-origin).
- Database cleanup and schema migration scripts (`FixM001DuplicateAsync`, `CleanupDuplicateVehiclesAsync`) run on every application startup.
- Stripe is permanently in mock mode (`"MockMode": true`) with a placeholder API key.
- `/DebugFrames/` endpoint serves license plate images without authentication.

---

## Prioritized Improvement Roadmap

### P0 — Blockers (must fix before the system is usable)

- [x] **P0-1** Fix camera checkout for casual vehicles ✅
  - Added `ReceiveVehicleAtExit` SignalR listener in `CameraMonitoring.jsx`
  - Opens `ParkingPaymentModal` (from `pages/PaymentModal.jsx`) when event fires
  - `handleProcessVehicle` also intercepts HTTP `requiresPayment: true` response
  - Fixed SignalR cleanup bug (replaced `useState` with `useRef`)
  - Replaced `alert()` with `toast` notifications
  - Fixed `setupDefaultCameras` camera indices (0,1,2,3 instead of all 0)
  - Backend: added `entryTime` and `slotId` to the `requiresPayment` HTTP response

- [x] **P0-2** Fix ML model path — graceful degradation when model is missing ✅
  - Removed hardcoded `/home/user/ProjectITS/...` path from `MLModelPrediction.cs`
  - Model loading is now non-fatal; logs a warning and falls back to heuristic classification
  - `MLModelPrediction.IsModelLoaded` property allows callers to check availability
  - Updated DI registration in `Program.cs` to inject `ILogger<MLModelPrediction>`
  - Fixed `VehicleController` and `LicensePlateServiceTest` to use DI instead of direct construction

- [x] **P0-3** Externalize all secrets from `appsettings.json` ✅
  - Cleared SMTP, Momo, JWT secret, admin password from `appsettings.json` (now empty strings)
  - Secrets live in `appsettings.Development.json` (gitignored)
  - Created `appsettings.Development.json.example` as a template
  - Added `appsettings.Development.json`, `.env*`, and ML model `.zip`/`.pt` files to `.gitignore`
  - **Action required**: rotate the Gmail app password and Momo API keys that were previously in git history

---

### P1 — Critical quality fixes

- [ ] **P1-1** Fix SignalR connection cleanup bug in `CameraMonitoring.jsx`
  - Use `useRef` to hold the connection so the cleanup function accesses current value, not stale null

- [ ] **P1-2** Remove duplicate/dead-code files
  - Delete `src/components/ParkingSpaceStatus.tsx` (`.jsx` version is used)
  - Delete `src/pages/PaymentModal.jsx` (component version in `components/` is used)
  - Route or delete `Transactions.jsx`, `MonthlyVehicles.jsx`, `CheckoutComplete.jsx`

- [ ] **P1-3** Standardize vehicle type constant
  - Define `VEHICLE_TYPES = { CAR: 'CAR', MOTORBIKE: 'MOTORBIKE' }` in a shared constants file
  - Remove the `MOTORBIKE → MOTORCYCLE` remap in `VehicleImageUpload.jsx`
  - Fix display logic in `CheckIn.jsx`/`CheckOut.jsx` to handle all type values

- [ ] **P1-4** Add `tsconfig.json` or remove TypeScript from frontend
  - If keeping TypeScript: add `tsconfig.json` with `strict: true`, ensure all `.tsx` files type-check
  - If removing: rename `.tsx` → `.jsx` and delete `vite-env.d.ts`

- [ ] **P1-5** Fix Dashboard mock data behavior
  - Show a clear error banner when API fetch fails; do not silently retain mock data
  - Set initial state to `null`/empty, not fabricated mock records
  - Add a "Reload" button on error state

---

### P2 — Functional improvements

- [ ] **P2-1** Fix multi-camera `cameraIndex` assignment in `setupDefaultCameras()`
  - IN-01 → index 0, IN-02 → index 1, OUT-01 → index 2, OUT-02 → index 3 (or make it configurable)

- [ ] **P2-2** Replace `alert()` with toast notifications in `CameraMonitoring.jsx`
  - Use `react-toastify` (already a dependency) for both success and error feedback

- [ ] **P2-3** Use the configured Axios instance across all page components
  - Import `axiosInstance` from `utils/axiosConfig.js` instead of raw `axios`
  - This ensures 401 redirect and 30s timeout apply uniformly

- [ ] **P2-4** Add JWT session expiry handling
  - JWT expires after 8 hours; the current 401 interceptor redirects to login, which is correct
  - Add a visual countdown or notification before expiry so operators aren't caught mid-action

- [ ] **P2-5** Move database startup cleanup to a one-time migration command
  - Extract `FixM001DuplicateAsync`, `CleanupDuplicateVehiclesAsync` out of `Program.cs`
  - Make them a CLI tool or a one-shot script that operators run once

- [ ] **P2-6** Fix `stream_api.py` CORS for MJPEG stream endpoint
  - Add `*` CORS or restrict to the actual backend origin for the `/cameras/<id>/stream` route
  - Or proxy the stream through the .NET backend to avoid browser cross-origin issues

- [ ] **P2-7** Add rate limiting on `/api/auth/login`
  - Prevent brute-force password attacks; ASP.NET has built-in rate limiting middleware

---

### P3 — Architecture and testing

- [ ] **P3-1** Write real unit tests for critical services
  - `ParkingFeeService`: fee calculation edge cases (midnight, monthly vehicles, discounts)
  - `ParkingService.AssignParkingSlot`: concurrent slot assignment, no-slot-available case
  - `LicensePlateService`: fallback behavior when Python API is down
  - `AuthService`: token generation, password hashing

- [ ] **P3-2** Add React Error Boundary
  - Wrap route-level components so one broken page doesn't crash the entire app

- [ ] **P3-3** Restrict `/DebugFrames/` static file serving
  - Require auth middleware on the debug frames path, or disable it in production

- [ ] **P3-4** Narrow JWT validation and CORS policy
  - Enable `ValidateIssuer` and `ValidateAudience` in JWT validation
  - Replace `SetIsOriginAllowed(origin => true)` with an explicit list of allowed origins

- [ ] **P3-5** Document or enable automatic camera detection mode
  - `CameraMonitoringService` is intentionally in "manual capture mode" — document why
  - If auto-mode is desired, re-enable the polling loop with debounce + duplicate prevention

- [ ] **P3-6** Implement or remove Stripe
  - Either connect a real Stripe test account and implement webhook handling
  - Or remove Stripe from the UI and simplify to Cash + Momo only

- [ ] **P3-7** Add `.gitignore` entries for sensitive files
  - `appsettings.Development.json`
  - `appsettings.Production.json`
  - `.env`, `.env.local`
  - `*.zip` in MLModels directories (ML model files)

---

## Progress Tracking

| Item | Status | Branch | PR |
|------|--------|--------|----|
| P0-1: Camera checkout fix | ✅ done | feature/initial-improvements | pending PR |
| P0-2: ML model graceful degradation | ✅ done | feature/initial-improvements | pending PR |
| P0-3: Externalize secrets | ✅ done | feature/initial-improvements | pending PR |
| P1-1: SignalR cleanup bug | pending | — | — |
| P1-2: Remove dead code | pending | — | — |
| P1-3: Vehicle type constants | pending | — | — |
| P1-4: TypeScript config | pending | — | — |
| P1-5: Dashboard error states | pending | — | — |

_Last updated: 2026-06-30 — P0 items complete, awaiting review_
