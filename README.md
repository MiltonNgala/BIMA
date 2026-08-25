# BIMA Core Insurance System

BIMA is a modular core insurance system foundation inspired by Openkoda's insurance capabilities. This repository uses React and TypeScript for the operations experience and ASP.NET Core with C# for backend services.

## Workspace

- `frontend`: Vite, React, TypeScript operations dashboard
- `backend`: ASP.NET Core Web API with health and policy portfolio endpoints
- `BIMA.slnx`: .NET solution

The operations UI includes authenticated navigation for policies, claims, billing, customers, users, audit history, and session settings. The backend uses EF Core aggregates with tenant and audit metadata. When `ConnectionStrings:InsuranceDatabase` is configured, records use PostgreSQL; otherwise selected policy, customer, and claim reads use local sample services.

## Run locally

Backend (HTTP development profile):

```powershell
dotnet run --project .\backend\backend.csproj --launch-profile http
```

The HTTP profile listens on `http://localhost:5180`. The frontend defaults to this URL. Override it with `VITE_API_BASE_URL` when using another backend URL.

Apply the PostgreSQL schema after configuring the local connection:

```powershell
dotnet ef database update --project .\backend\backend.csproj --startup-project .\backend\backend.csproj
```

Frontend, in a second terminal:

```powershell
Set-Location .\frontend
npm.cmd run dev
```

Open the Vite URL shown in the terminal. The API exposes:

- Health: `GET /api/health` and `GET /health/ready`
- Policies: `GET/POST/PATCH/DELETE /api/policies` and `/api/policies/{number}`
- Customers: `GET/POST/DELETE /api/customers` and `/api/customers/{customerId}`
- Claims: `GET/POST/PATCH/DELETE /api/claims` and `/api/claims/{claimNumber}`
- Claim approval: `POST /api/claims/{claimNumber}/approve`
- Claim attachments: `GET/POST /api/claims/{claimNumber}/attachments` and `GET/DELETE /api/claims/{claimNumber}/attachments/{attachmentId}`
- Billing: `GET/POST/DELETE /api/billing/invoices` and `/api/billing/invoices/{invoiceNumber}`
- Payments: `GET/POST /api/billing/invoices/{invoiceNumber}/payments`
- Authentication: `POST /api/auth/register`, `/login`, `/refresh`, `/logout`, `/password-reset/request`, and `/password-reset/confirm`
- Sessions: `GET /api/auth/sessions` and `DELETE /api/auth/sessions/{id}`
- User administration: `GET /api/users`, `POST /api/users`, and `PATCH /api/users/{id}/role`
- Audit: administrator-only `GET /api/audit`
- Organization and permissions: `GET/PATCH /api/organization`, `GET /api/users/{id}/permissions`, and `PUT/DELETE /api/users/{id}/permissions/{permission}`

Authenticated endpoints require a bearer token and tenant identity comes only from validated JWT claims. CORS defaults to `http://localhost:5173` and can be changed with `Frontend:Origin`. Claim transitions are controlled by the service: `Open` -> `Under Review` -> `Approved` or `Rejected` -> `Settled` or `Closed`. Access tokens last 15 minutes; refresh tokens last 30 days, are stored only as SHA-256 hashes, rotate on use, and are consumed transactionally. Payment allocation is also transactionally protected. Password reset tokens last 30 minutes, are hashed, and are one-time use. Auth endpoints are limited to five requests per minute per client IP. Failed logins are audited and lock the account for 15 minutes after five attempts. Registration stores a securely hashed password: the first user in a tenant becomes `admin`, and later users receive the safe default `viewer` role. Roles are `admin`, `underwriter`, `agent`, and `viewer`.

## Implemented capabilities

- React/Vite operations workspace with responsive login, registration, password recovery, and reset flows.
- Policy, customer, claim, invoice, and payment record views with creation and guarded deletion actions.
- Claim detail workflow with status updates, approval, attachment upload, download, and deletion.
- Search across record tables, policy sorting, and policy pagination.
- Administrator user management, audit history, organization naming, and persisted per-user permission grants.
- Tenant-scoped persistence and audit events across business operations.
- Fail-closed JWT authentication, restricted CORS, transaction-protected refresh-token rotation, and transaction-protected invoice payment allocation.
- PostgreSQL readiness health check and Docker Compose database provisioning.

## Development status

- `docker-compose.yml` provisions PostgreSQL database `BIMA`.
- `.github/workflows/ci.yml` builds the backend and frontend and runs the backend test project on pushes and pull requests.
- `/health/ready` checks PostgreSQL connectivity.
- `backend.Tests` contains initial authorization coverage; broader integration and frontend coverage is still needed.
- A typed frontend API client is available in `frontend/src/api/client.ts`, while the current workspace orchestration uses authenticated request helpers in `App.tsx`.

The `AddCustomerPaymentsAndAttachments` migration adds the optional policy customer foreign key, invoice payments, and claim attachment metadata. The generated `AddOrganizationsAndPermissions` migration adds persisted organization and user-permission tables. Apply all migrations with `dotnet ef database update` before using PostgreSQL-backed organization or permission administration.

In Development, password-reset requests log and return the token for local testing. Production uses a fail-closed email sender until a provider adapter is registered; the token must be delivered by email and never exposed in the HTTP response.

Remaining work is tracked in `TODO.md` and includes a production email provider, customer backfill and required foreign-key enforcement, claim adjusters and settlement records, payment-provider integration, richer organization membership management, database-managed privilege policies beyond the current permission grants, frontend and integration test coverage, and production observability/deployment hardening.
