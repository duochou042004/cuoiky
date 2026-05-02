# Copilot Instructions - Quy tắc BẮT BUỘC (Dự án ASP.NET Core + React)

Bạn phải tuân thủ nghiêm ngặt tất cả quy tắc dưới đây để đảm bảo code chính xác, production-ready và phù hợp với yêu cầu người dùng ngay từ lần generate đầu tiên. Ưu tiên độ chính xác cao nhất, giảm tối đa việc sửa chữa nhiều lần.

## Nguyên tắc cốt lõi (Áp dụng mọi lúc)
- Tuân thủ chính xác yêu cầu của người dùng. Nếu thiếu thông tin hoặc không rõ → **hỏi lại ngay** thay vì assume.
- Code phải chính xác, hoàn chỉnh và usable ngay từ đầu. Tránh code nửa vời hoặc generic.
- **Tuyệt đối KHÔNG sử dụng emoji** ở bất kỳ đâu (code, comment, string, docs).
- Comment chỉ khi thực sự cần: giải thích "tại sao" hoặc phần phức tạp, ngắn gọn và tự nhiên.
- Ưu tiên readability thực tế, performance, security và maintainability.
- Luôn xử lý error, validation, logging và edge-case một cách thực tế.

## Project Structure & Setup (BẮT BUỘC)
- **Backend (ASP.NET Core)**: Clean Architecture (Domain, Application, Infrastructure, Presentation/API).
- **Frontend (React)**: Feature-based organization (không theo file type), App Router nếu dùng Next.js, hoặc Vite + React.
- Root project: .gitignore chuẩn, .env.example, docker-compose.yml (nếu áp dụng), appsettings*.json.
- **KHÔNG tự động tạo bất kỳ file .md nào** (CHANGELOG, CONTRIBUTING, docs folder, v.v.) trừ khi người dùng yêu cầu rõ ràng.
- Luôn tuân thủ cấu trúc và hướng dẫn hiện có trong README.md hoặc tài liệu người dùng cung cấp.

## ASP.NET Core Best Practices (BẮT BUỘC)
- .NET 8+, 9+, 10+ mới nhất (ưu tiên LTS), Minimal API hoặc Controller tùy theo yêu cầu.
- Sử dụng Clean Architecture + CQRS (MediatR) khi phù hợp, FluentValidation, AutoMapper hoặc Mapster.
- Security: JWT + HttpOnly Refresh Token, CORS chặt chẽ, rate limiting, input validation.
- Database: EF Core với migrations, Repository/UnitOfWork nếu cần, transactions đúng scope.
- Logging: Microsoft.Extensions.Logging + structured logging (Serilog khuyến khích).
- Testing: xUnit + Moq + Testcontainers cho integration tests.
- Error handling: ProblemDetails (RFC 7807), global exception middleware.

## React Best Practices (BẮT BUỘC)
- Functional components + Hooks, TypeScript strict mode.
- Feature-based folder structure, components nhỏ và tập trung một trách nhiệm.
- State: TanStack Query cho server data, Zustand cho client state.
- API client: centralized axios/fetch wrapper với interceptors (auth, refresh token).
- Performance: memoization hợp lý, tránh waterfall, dynamic import khi cần.
- UI: Modern, clean, responsive (Tailwind + shadcn/ui hoặc custom), dark mode nếu phù hợp.
- Accessibility và UX feedback tốt (loading, error states rõ ràng).

## Git Branching Strategy & Workflow (BẮT BUỢC)
- main (hoặc master) → Chỉ dành cho production (code ổn định, đã test kỹ).
- develop → Branch chính để tích hợp feature (development branch).
- feature/* → Phát triển tính năng mới (ví dụ: feature/payment-vnpay).
- hotfix/* → Vá lỗi khẩn cấp trên production.
- release/* → Chuẩn bị release (bump version, final testing).

**Workflow hàng ngày bắt buộc tuân theo**:
- Luôn tạo feature branch từ develop:
git checkout develop
git checkout -b feature/tên-tính-năng

- Commit message theo Conventional Commits (feat:, fix:, chore:, refactor:, etc.).
- Khi hoàn thành: merge vào develop (Pull Request + review).
- Release process:
- Tạo release branch từ develop → test → merge vào main + tag version.
- Merge release branch trở lại develop để đồng bộ.

Khi generate code hoặc script liên quan đến Git, luôn nhắc nhở và tuân thủ workflow này.

## Integration & Professional Practices
- Backend ↔ Frontend: API contract rõ ràng (DTO consistent), auth flow an toàn (JWT + refresh token).
- Docker & deployment ready (multi-stage Dockerfile cho backend, build script cho frontend).
- Code phải production-grade: async đúng cách, caching hợp lý, monitoring ready (Sentry, Application Insights).
- Luôn ưu tiên security best practices và performance.

## Cách hành xử
- Generate code: chính xác, hoàn chỉnh, theo đúng yêu cầu người dùng ngay lần đầu.
- Review/Refactor: liệt kê vấn đề + đề xuất fix rõ ràng.
- Nếu người dùng nói “theo clean architecture”, “production ready”, “theo git workflow” → áp dụng ngay 100%.
- Luôn tập trung vào tính thực tế và độ chính xác cao nhất.

Áp dụng NGHIÊM NGẶT tất cả quy tắc này cho mọi suggestion, completion, chat và file generation trong repository.