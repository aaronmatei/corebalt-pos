# Pos — Point of Sale (store-tier core)

A .NET POS designed to grow from a single on-prem retail store → single-store
supermarket → multi-branch chain (and later SaaS) **without rewrites**. Step 1
delivered the domain core and the four structural invariants. Step 2 adds the
Catalog bounded context, the `CheckoutService` use case (Application), and the
EF Core 10 + PostgreSQL persistence with a transactional outbox (Infrastructure),
plus xUnit tests pinning the invariants.

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
samples/
  Pos.Smoke/             domain-only console (no infrastructure)
  Pos.Persistence.Demo/  saves a sale to Postgres, reloads it, prints the outbox row
tests/
  Pos.Domain.Tests/      xUnit invariant tests (UUIDv7, store/tenant scoping, append-only, Money)
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
  same database transaction. `OutboxDispatcher` ships them at least once; the step-2
  stub logs them and the real HQ transport lands in step 3.

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

## Roadmap (anticipated in design choices)
- Single-store supermarket: S1 multi-lane foundation, S2 weighed goods + scales, S3 cash office,
  S4 promotions + loyalty, S5 procurement.
- Multi-branch chain: M1 HQ/cloud tier + store↔HQ sync (reads the outbox), M2 central
  catalog/pricing push, M3 distribution + inter-branch transfers. M1 also unlocks SaaS.
