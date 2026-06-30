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

- [x] **P1-1** Fix SignalR connection cleanup bug in `CameraMonitoring.jsx` ✅
  - Completed as part of P0-1: the connection is held in a `useRef`, so the unmount
    cleanup stops the live connection instead of a stale `null`.

- [x] **P1-2** Remove duplicate/dead-code files ✅
  - Deleted `src/components/ParkingSpaceStatus.tsx` (the `.jsx` version is the one imported)
  - Deleted `src/pages/Transactions.jsx`, `src/pages/MonthlyVehicles.jsx`,
    `src/pages/CheckoutComplete.jsx` — none had a route in `App.jsx` or any importer
  - Note: `src/pages/PaymentModal.jsx` is **kept** — it is imported by `CameraMonitoring.jsx`
    and `CheckOut.jsx` (only the `components/PaymentModal.jsx` duplicate was suspected dead,
    but it is used by `MonthlyRegistration.jsx`, so both are live and were left in place).
  - Removed ~1160 lines of dead code.

- [x] **P1-3** Standardize vehicle type constant to `MOTORBIKE` ✅
  - Removed the `MOTORBIKE → MOTORCYCLE` remap in `VehicleImageUpload.jsx` (both the success
    and the warning-with-data paths) — the component now passes the backend value through.
  - `MonthlyRegistration.jsx`: form state, badge rendering, `<option>` values, and the help
    text all use `MOTORBIKE`; deleted the now-dead `vehicleType` mapping helper.
  - `Reports.jsx`: `vehicleTypeDistribution` keys aligned to `MOTORBIKE`.

- [x] **P1-4** Make TypeScript real instead of silently bypassed ✅
  - The 4 `.tsx` components were compiled by Vite with **no** type-checking and TypeScript
    was not even a dependency. Rather than hand-strip 1145 lines of typed code (bug risk),
    TypeScript was added as a dev dependency with a `strict: true` `tsconfig.json`.
  - Added a `typecheck` npm script (`tsc --noEmit`) and wired it into the frontend CI job.
  - All `.tsx` files type-check clean under strict mode (verified locally and in CI).

- [x] **P1-5** Fix Dashboard mock data behavior ✅
  - Initial state is now zeroed/empty, not fabricated mock records.
  - On API failure the existing error banner is shown instead of silently swapping in mock data.
  - `loading` starts `true` so the user sees a spinner rather than a flash of empty content.

---

### P2 — Functional improvements

- [x] **P2-1** Fix multi-camera `cameraIndex` assignment in `setupDefaultCameras()` ✅
  - Completed as part of P0-1: IN-01→0, IN-02→1, OUT-01→2, OUT-02→3 (no longer all index 0)

- [x] **P2-2** Replace `alert()` with toast notifications in `CameraMonitoring.jsx` ✅
  - Completed as part of P0-1: `react-toastify` is used for both success and error feedback

- [x] **P2-3** Single source of truth for axios configuration ✅
  - Discovery: `main.jsx` already configured the **global** `axios` instance with the
    same token-injection + 401-redirect interceptors, so every `import axios from 'axios'`
    was already covered — `utils/axiosConfig.js` was a redundant, never-imported duplicate.
  - Consolidated: `axiosConfig.js` now configures the global instance (one place), `main.jsx`
    imports it for its side effects, and the duplicated block in `main.jsx` was removed.
  - Improvements: the 401 handler now shows a toast and guards against redirect loops on `/login`.

- [ ] **P2-4** Add JWT session expiry handling
  - JWT expires after 8 hours; the current 401 interceptor redirects to login, which is correct
  - Add a visual countdown or notification before expiry so operators aren't caught mid-action

- [ ] **P2-5** Move database startup cleanup to a one-time migration command
  - Extract `FixM001DuplicateAsync`, `CleanupDuplicateVehiclesAsync` out of `Program.cs`
  - Make them a CLI tool or a one-shot script that operators run once

- [x] **P2-6** Fix `stream_api.py` CORS for the MJPEG/frame endpoints ✅
  - Allowed origins are now configurable via the `STREAM_CORS_ORIGINS` env var, defaulting
    to `*` (read-only local-network camera endpoints; several routes already emitted `*`).
  - Resolves the inconsistency where the global CORS allowed only `localhost:3000` while
    `/frame` and `/raw-frame` manually added `Access-Control-Allow-Origin: *`.

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

## CI/CD

A GitHub Actions workflow (`.github/workflows/ci.yml`) runs on every push and PR to
`develop`/`main`. Three parallel jobs:

| Job | Steps |
|-----|-------|
| **Backend (.NET 8)** | restore + build `SmartParking.Core.sln` (Release) → `dotnet test BasicTests` |
| **Frontend (React/Vite)** | `npm ci` → `npm run typecheck` (tsc strict) → `npm run build` |
| **Python (License Plate APIs)** | install flask/flake8 → `py_compile` both APIs → flake8 errors-only (E9,F63,F7,F82) |

This is the project's first automated pipeline — prior to this, the system was only ever
verified by a developer running it locally.

## Progress Tracking

| Item | Status | Branch | PR |
|------|--------|--------|----|
| P0-1: Camera checkout fix | ✅ done | feature/initial-improvements | #2 |
| P0-2: ML model graceful degradation | ✅ done | feature/initial-improvements | #2 |
| P0-3: Externalize secrets | ✅ done | feature/initial-improvements | #2 |
| CI/CD: GitHub Actions pipeline | ✅ done | feature/initial-improvements | #2 |
| P1-1: SignalR cleanup bug | ✅ done (in P0-1) | feature/initial-improvements | #2 |
| P1-2: Remove dead code | ✅ done | feature/p1-improvements | #3 |
| P1-3: Vehicle type constants | ✅ done | feature/p1-improvements | #3 |
| P1-4: TypeScript strict + typecheck CI | ✅ done | feature/p1-improvements | #3 |
| P1-5: Dashboard error states | ✅ done | feature/p1-improvements | #3 |
| P2-1: Camera index assignment | ✅ done (in P0-1) | feature/initial-improvements | #2 |
| P2-2: Toast notifications | ✅ done (in P0-1) | feature/initial-improvements | #2 |
| P2-3: Single axios config | ✅ done | feature/p2-improvements | (in progress) |
| P2-6: stream_api CORS | ✅ done | feature/p2-improvements | (in progress) |
| P2-4, P2-5, P2-7 | pending | — | — |
| P3-1 … P3-7 | pending | — | — |

_Last updated: 2026-06-30 — P0 + P1 merged to develop (CI-green). P2 in progress on feature/p2-improvements (P2-3, P2-6 done)._
