# Pos — Point of Sale (store-tier core)

A .NET POS designed to grow from a single on-prem retail store → single-store
supermarket → multi-branch chain (and later SaaS) **without rewrites**. Step 1
delivered the domain core and the four structural invariants. Step 2 adds the
Catalog bounded context, the `CheckoutService` use case (Application), and the
EF Core 10 + PostgreSQL persistence with a transactional outbox (Infrastructure),
plus xUnit tests pinning the invariants. Step 3 adds the ASP.NET Core
store-server API + WebApplicationFactory integration tests.

> Stack: **.NET 10, C# 14, EF Core 10, PostgreSQL via Npgsql.** Step 2 onward
> needs `dotnet restore` against the public NuGet feed.

## The four invariants (and where they live)

1. **Edge-generated, time-ordered IDs** — `src/Pos.SharedKernel/Ids/Uuid7.cs`
   UUIDv7 (`Guid.CreateVersion7()` on .NET 9+) so every till/branch mints unique,
   sortable IDs with no central sequence.
2. **Store-authoritative ownership** — `src/Pos.SharedKernel/IStoreScoped.cs`
   Every fact carries the `StoreId` that owns it; HQ aggregates.
3. **Append-only / immutable facts** — `src/Pos.Domain/Inventory/StockMovement.cs`
   and completed `Sale`s. Stock-on-hand = SUM of movements; no overwriting → safe to
   reconcile across branches without last-write-wins.
4. **Tenant scoping from row one** — `src/Pos.SharedKernel/ITenantScoped.cs`
   So the multi-tenant cloud/SaaS tier is an addition, never a migration.

## Current layout
```
src/
  Pos.SharedKernel/      building blocks: Entity, AggregateRoot, ValueObject, Money, Uuid7, invariants
  Pos.Domain/            bounded contexts: Sales, Inventory, Catalog (Product)
  Pos.Application/       ports (IClock, ICurrentContext, IUnitOfWork, repositories) + CheckoutService
  Pos.Infrastructure/    PosDbContext, EF configurations, repositories, outbox interceptor, DI
  Pos.Api/               ASP.NET Core store-server host: catalog + checkout + inventory over HTTP
samples/
  Pos.Smoke/             domain-only console (no infrastructure)
  Pos.Persistence.Demo/  saves a sale to Postgres, reloads it, prints the outbox row
tests/
  Pos.Domain.Tests/      xUnit invariant tests (UUIDv7, store/tenant scoping, append-only, Money)
  Pos.Api.Tests/         WebApplicationFactory integration tests against pos_test
```

## Step 2 design notes

- **Ports stay in `Pos.Application`**, implementations in `Pos.Infrastructure` — the
  domain references neither, so the bounded contexts stay framework-free.
- **`CheckoutService`** sources tenant/store/user from `ICurrentContext` — never the
  client payload — so the store-authoritative and tenant-scoping invariants survive a
  hostile or buggy request. `AddLineAsync` looks up the `Product` by id; description
  and unit price come from the product record, not the till payload.
- **`CompleteAsync`** writes one negative-delta `StockMovement` per line in the SAME
  unit of work that completes the sale, so a crash during checkout commits both or
  neither — the append-only inventory invariant survives partial failure.
- **Transactional outbox** (`DomainEventsToOutboxInterceptor`) drains every aggregate's
  `DomainEvents` collection into `outbox_messages` during `SaveChangesAsync`, in the
  same database transaction. `OutboxDispatcher` ships them at least once; the current
  stub logs each row and the real HQ transport lands with the chain/M1 milestone.

## Build & run
```bash
dotnet restore
dotnet build
dotnet test
dotnet run --project samples/Pos.Smoke
```

## Postgres + Persistence.Demo (step 2)
Wire the infrastructure into a host via:
```csharp
services.AddInfrastructure(connectionString);
```

Connection string comes from the `POS_DB` env var; the default targets
`Host=localhost;Port=5432;Database=pos;Username=postgres;Password=pos`.

End-to-end verification:
```bash
docker run --name pos-pg -e POSTGRES_PASSWORD=pos -e POSTGRES_DB=pos -p 5432:5432 -d postgres:17
dotnet tool install --global dotnet-ef
dotnet ef migrations add InitialCreate --project src/Pos.Infrastructure --startup-project samples/Pos.Persistence.Demo
dotnet ef database update              --project src/Pos.Infrastructure --startup-project samples/Pos.Persistence.Demo
dotnet run --project samples/Pos.Persistence.Demo
```
`Pos.Persistence.Demo` migrates the database, seeds a Product, runs a full checkout
via `CheckoutService`, reloads the sale via `ISaleRepository`, and prints the
`SaleCompleted` outbox row written by the interceptor.

## HTTP API (step 3)

The store-server host is `Pos.Api` (ASP.NET Core 10, minimal APIs). It calls
`AddInfrastructure(POS_DB)` at boot, and exposes catalog + checkout + on-hand
over HTTP. Endpoints are thin — orchestration stays in `CheckoutService`.

```bash
$env:POS_DB = "Host=localhost;Port=5432;Database=pos;Username=postgres;Password=YOURPASS"
dotnet run --project src/Pos.Api
# OpenAPI document at: http://localhost:5xxx/openapi/v1.json
```

### Identity
Every business endpoint requires three trusted headers — this is the step-3
stand-in for the chain/SaaS-tier JWT bearer (kept curlable for now):

| Header        | Type | Source of truth (later)                  |
|---------------|------|------------------------------------------|
| `X-Tenant-Id` | UUID | JWT claim `tenant`                       |
| `X-Store-Id`  | UUID | JWT claim `store` (this branch)          |
| `X-User-Id`   | UUID | JWT claim `sub` (the cashier)            |

Missing or non-UUID values → `401 Unauthorized` (`ProblemDetails`). `GET /healthz`
is the only route that doesn't need them.

### Endpoints

| Method | Path                                             | Notes                                                |
|--------|--------------------------------------------------|------------------------------------------------------|
| POST   | `/api/v1/products`                               | Create a Product                                     |
| GET    | `/api/v1/products/{id}`                          |                                                      |
| GET    | `/api/v1/products/by-sku/{sku}`                  |                                                      |
| PUT    | `/api/v1/products/{id}/price`                    | Reprice                                              |
| POST   | `/api/v1/sales`                                  | StartAsync, returns saleId                           |
| POST   | `/api/v1/sales/{saleId}/lines`                   | Body supplies productId + quantity; price from catalog |
| POST   | `/api/v1/sales/{saleId}/tenders`                 | Cash / Mpesa / Card / AirtelMoney                    |
| POST   | `/api/v1/sales/{saleId}/complete`                | Writes -delta StockMovements in the same UoW         |
| GET    | `/api/v1/sales/{saleId}`                         |                                                      |
| GET    | `/api/v1/inventory/{productId}/on-hand`          | SUM of stock_movements (never a mutable column)      |
| GET    | `/healthz`                                       | No auth                                              |

Domain rule violations (`Sale not fully paid`, `Currency mismatch`, …) surface
as `409 Conflict`. Argument-validation errors are `400 Bad Request`.

### API integration tests
`tests/Pos.Api.Tests` boots the host via `WebApplicationFactory<Program>` against
a dedicated `pos_test` database. Tests mint a fresh `TenantId` per case to stay
isolated. The connection string is taken from `POS_DB_TEST`, or — if that's
unset — derived from `POS_DB` by swapping `Database=pos` → `Database=pos_test`.

```bash
dotnet test tests/Pos.Api.Tests
```

## Roadmap (anticipated in design choices)
- Single-store supermarket: S1 multi-lane foundation, S2 weighed goods + scales, S3 cash office,
  S4 promotions + loyalty, S5 procurement.
- Multi-branch chain: M1 HQ/cloud tier + store↔HQ sync (reads the outbox), M2 central
  catalog/pricing push, M3 distribution + inter-branch transfers. M1 also unlocks SaaS.
