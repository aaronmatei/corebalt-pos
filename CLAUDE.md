# CLAUDE.md — POS (Point of Sale)

## What this is
A .NET POS designed to grow from a single on-prem **retail** store → single-store
**supermarket** → multi-branch **chain** (and later SaaS) **without rewrites**.
Three tiers: terminal (till) → store server (per branch, authoritative) → cloud/HQ (added later).
Offline-first: a till/branch must keep selling when the network drops.

## Non-negotiable invariants (NEVER violate when editing)
1. **Edge-generated, time-ordered IDs** — UUIDv7 via `Pos.SharedKernel.Ids.Uuid7`
   (or `Guid.CreateVersion7()` on .NET 9+). No DB-generated or sequential keys.
2. **Store-authoritative ownership** — every fact carries `StoreId` (`IStoreScoped`).
   HQ aggregates; one branch never overwrites another.
3. **Append-only / immutable facts** — a completed `Sale` is immutable; stock-on-hand is the
   SUM of immutable `StockMovement` rows, never a mutable quantity column. Corrections are new rows.
4. **Tenant scoping** — every row carries `TenantId` (`ITenantScoped`), even though the first
   customer is single-tenant. This is what lets the SaaS/cloud tier be added with no migration.

## Stack
- .NET 10, C# 14. PostgreSQL via **Npgsql + EF Core 10**. xUnit tests (domain + API integration).
- Clean architecture: SharedKernel → Domain → Application → Infrastructure → Api.
- `Money` is a value object, mapped as an EF **owned type**. Aggregates record domain events.
- **Transactional outbox** (`outbox_messages`, written by `DomainEventsToOutboxInterceptor`
  inside the same `SaveChanges` transaction) is the seam the future multi-branch sync engine
  reads from. Do not bypass it.

## Layout
- `src/Pos.SharedKernel` — Entity, AggregateRoot, ValueObject, Money, Uuid7, invariant interfaces
- `src/Pos.Domain` — Sales (Sale/SaleLine/Tender), Inventory (StockMovement), Catalog (Product)
- `src/Pos.Application` — ports (repositories, IUnitOfWork, IClock) + `CheckoutService` use case
- `src/Pos.Infrastructure` — PosDbContext, EF configurations, repositories, outbox interceptor, DI;
  `Persistence/Migrations` holds the EF migration (`InitialCreate`) + model snapshot
- `src/Pos.Api` — ASP.NET Core 10 minimal-API store-server host: catalog + checkout + inventory over
  HTTP. Thin endpoints delegating to `CheckoutService`; header-based identity (`Auth/`)
- `samples/Pos.Smoke` — domain-only console (no infrastructure)
- `samples/Pos.Persistence.Demo` — saves a sale to Postgres, reloads it, prints the outbox
- `tests/Pos.Domain.Tests` — xUnit invariant tests (UUIDv7, store/tenant scoping, append-only, Money)
- `tests/Pos.Api.Tests` — `WebApplicationFactory<Program>` integration tests against `pos_test`

## Build / run
```bash
docker run --name pos-pg -e POSTGRES_PASSWORD=pos -e POSTGRES_DB=pos -p 5432:5432 -d postgres:17
# The InitialCreate migration already exists; this just applies it. (Add a new one only when the model changes.)
dotnet ef database update --project src/Pos.Infrastructure --startup-project samples/Pos.Persistence.Demo
dotnet run  --project samples/Pos.Persistence.Demo   # save→reload a sale, print the outbox row
dotnet run  --project src/Pos.Api                    # store-server host; OpenAPI at /openapi/v1.json
dotnet test                                          # domain + API integration tests
```
Connection string via `POS_DB` env var (default `Host=localhost;Port=5432;Database=pos;Username=postgres;Password=pos`).
`Pos.Api.Tests` uses `POS_DB_TEST`, or derives it from `POS_DB` by swapping `Database=pos` → `Database=pos_test`.

## Current state & immediate task
- **Done:** step 1 (domain core + invariants), step 2 (Catalog, Application, EF Core/Postgres
  persistence + transactional outbox, `InitialCreate` migration), and step 3 (`Pos.Api`
  store-server host + `Pos.Api.Tests` integration tests). All five projects target `net10.0`.
- **API identity (step-3 stand-in for the chain/SaaS JWT):** every `/api/v1` route requires three
  trusted headers — `X-Tenant-Id`, `X-Store-Id`, `X-User-Id` (all UUIDs), read by
  `HeaderCurrentContext` and enforced by `AuthEndpointFilter`. Missing/non-UUID → 401. `GET /healthz`
  is the only unauthenticated route. Domain-rule violations surface as 409, argument validation as 400.
  Identity and prices come from `ICurrentContext` / the `Product` record, never the client payload.
- **Caveat:** a clean `dotnet build` / `dotnet test` against a live Postgres has not been
  re-confirmed in this environment — run them before starting new work. Stale `net8.0` artifacts
  linger under some `bin`/`obj` folders and can be ignored (or cleaned).
- **Next (roadmap):** pick up the supermarket/chain roadmap below — e.g. S1 multi-lane foundation
  or M1 HQ/cloud sync (the outbox is already the seam M1 reads from).

## Roadmap (anticipate it in design choices)
- Single-store supermarket: S1 multi-lane foundation, S2 weighed goods + scales, S3 cash office,
  S4 promotions + loyalty, S5 procurement.
- Multi-branch chain: M1 HQ/cloud tier + store↔HQ sync (reads the outbox), M2 central catalog/pricing
  push, M3 distribution + inter-branch transfers. M1 also unlocks SaaS.

## Conventions
- Aggregates expose behavior; keep setters private and mutate via methods.
- Never add a mutable stock count — record a `StockMovement`.
- New persisted entity: carry `TenantId` (+ `StoreId` if branch-owned), use `Uuid7` for the id,
  and add an `IEntityTypeConfiguration<T>`.
