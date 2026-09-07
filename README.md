# SMT OrderHub

SMT OrderHub is a web application for managing production orders in a
Surface-Mount Technology (SMT) manufacturing environment. It tracks
**Orders**, the **Boards** they contain, and the **Components** placed on
those boards. It includes full CRUD, server-side search with pagination,
real-time concurrency notifications, and a production-line export.

[![Live demo](https://img.shields.io/badge/Live%20demo-orderhub.scheve.org-blue?logo=github)](https://orderhub.scheve.org/)

## What's inside

The repository contains two deployable services:

| Service    | Project               | Description                                                                                                                                                                                                      |
| ---------- | --------------------- | ---------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| **API**    | `src/OrderHub.Api`    | ASP.NET Core Web API (REST + OpenAPI). Serves all data endpoints, authentication (ASP.NET Core Identity with cookie sessions), the SignalR hub for real-time notifications, and the production-line JSON export. |
| **Client** | `src/OrderHub.Client` | Blazor WebAssembly SPA served by nginx. Communicates with the API via typed JSON-over-HTTP calls.                                                                                                                |

Supporting projects (not deployed independently):

- `src/OrderHub.Domain` — entities and domain logic (Order, Board, Component + join entities).
- `src/OrderHub.Application` — DTOs, repository interfaces, production export service.
- `src/OrderHub.Infrastructure` — EF Core `SmtDbContext`, migrations, repository implementations.

## Architecture

Clean Architecture with four layers, dependencies pointing inward:

```
OrderHub.Client (Blazor WASM, nginx)
        │  JSON over HTTPS + SignalR
        ▼
OrderHub.Api (Controllers, Identity, SignalR hub)
        ▼
OrderHub.Application (DTOs, repository interfaces, export service)
        ▼
OrderHub.Infrastructure (EF Core repositories, SQLite)
        ▼
OrderHub.Domain (entities — referenced by all layers)
```

- **Domain** holds the entities and their invariants. `Order ↔ Board` and
  `Board ↔ Component` are many-to-many relationships via `OrderBoard`
  (with `BoardQuantity`) and `BoardComponent` (with `PlacementCount`) join entities.
- **Application** defines repository interfaces and use-case services such as
  `OrderProductionService`, which assembles the JSON payload for the production line.
- **Infrastructure** implements the repositories with EF Core against SQLite
  (file-based, persisted on a Docker volume; swap-friendly for other providers).
- **API** exposes REST controllers, cookie-based auth via Identity, optimistic
  concurrency through RowVersion round-tripping, per-request CSRF header checks,
  rate limiting on auth endpoints, CORS for the WASM client, and a SignalR hub
  (`/hubs/orders`) that notifies other users when an order is modified.
- **Client** is a Blazor WebAssembly SPA with server-side search + pagination,
  localization (resx), and real-time conflict banners driven by the SignalR hub.
- **Logging** is centralized via Serilog (console sink; also visible in Azure Log Stream).

## Deployment (Docker)

Both services ship as multi-stage Docker images; `docker-compose.yml` runs them
together with a persistent SQLite volume.

### Quick start

```bash
docker compose up -d --build
```

Then open:

- **Client:** http://localhost:5106 (register an account on first use)
- **API:** http://localhost:5053 (OpenAPI at `/openapi/v1.json`)

The API applies EF Core migrations automatically at startup. Data persists
across restarts in the `orderhub-data` volume.

### Environment variables

| Variable                         | Service          | Default (compose)               | Purpose                                                                                                                                                                   |
| -------------------------------- | ---------------- | ------------------------------- | ------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| `ASPNETCORE_ENVIRONMENT`         | API              | `Production`                    | .NET hosting environment. In `Development`, HTTPS redirection is skipped and dev settings apply.                                                                          |
| `ConnectionStrings__SmtDatabase` | API              | `Data Source=/data/orderhub.db` | SQLite connection string. The file lives on the persisted volume.                                                                                                         |
| `Cors__AllowedOrigins`           | API              | `http://localhost:5106`         | Comma-separated list of client origins allowed by the CORS policy (credentials included). Set your production client origin(s) here, e.g. `https://orderhub.example.org`. |
| `DisableHttpsRedirection`        | API              | _(unset)_                       | Set to `true` in HTTP-only local deployments to skip `UseHttpsRedirection` (behind a TLS-terminating reverse proxy, redirection is normally handled there).               |
| `ASPNETCORE_URLS`                | API (Dockerfile) | `http://+:8080`                 | HTTP bind address inside the container (the ASP.NET Core web server listens here).                                                                                        |

Client-side configuration is static JSON served by nginx:

- `ApiBaseUrl` in `src/OrderHub.Client/wwwroot/appsettings.json` — the API origin the
  browser talks to (`https://api.…` in production).

## Local development (without Docker)

```bash
dotnet restore OrderHub.slnx
dotnet run --project src/OrderHub.Api      # API on http://localhost:5053
dotnet run --project src/OrderHub.Client   # Client dev server
dotnet test OrderHub.slnx                  # xUnit test suite
```

## CI/CD

GitHub Actions run on every push/PR: **CI** builds and tests the solution;
**CD** chains off CI (`workflow_run`) and — only for green runs on `main` —
builds and pushes both images to GHCR and deploys them to Azure App Service.
