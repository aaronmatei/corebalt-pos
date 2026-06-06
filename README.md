# Pos — Point of Sale (store-tier core)

The foundation of a retail/supermarket POS designed to grow from a single on-prem store
to a multi-branch chain (and later SaaS) **without rewrites**. Step 1 delivered the
domain core and the four structural invariants. Step 2 wires the use cases (Application)
and the EF Core + Postgres persistence + transactional outbox (Infrastructure), plus
xUnit tests pinning the invariants.

> Sandbox note: step 1 was scaffolded fully offline. From step 2 onward there are
> NuGet dependencies (EF Core, Npgsql, xUnit), so run `dotnet restore` / `dotnet build` /
> `dotnet test` on a machine with normal internet access.

## The four invariants (and where they live)

1. **Edge-generated, time-ordered IDs** — `src/Pos.SharedKernel/Ids/Uuid7.cs`
   UUIDv7 so every till/branch mints unique, sortable IDs with no central sequence.
   (.NET 9+: swap for `Guid.CreateVersion7()`.)
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
  Pos.SharedKernel/      building blocks: Entity, AggregateRoot, ValueObject, Money, IDs, invariants
  Pos.Domain/            bounded contexts: Sales (Sale/SaleLine/Tender), Inventory (StockMovement)
  Pos.Application/       ports (IClock, ICurrentContext, IUnitOfWork, repositories) + use-case handlers
  Pos.Infrastructure/    EF Core + PostgreSQL, repositories, transactional outbox + dispatcher
samples/
  Pos.Smoke/             runnable console proof of the domain (no infrastructure)
tests/
  Pos.Domain.Tests/      xUnit invariant tests (UUIDv7, store/tenant scoping, append-only, Money)
```

## Intended full layout (built over the coming steps)
```
src/
  Pos.SharedKernel/      (done)
  Pos.Domain/            Sales, Inventory + catalog, pricing, payments, tax, identity ... (step 3+)
  Pos.Application/       (done — step 2)
  Pos.Infrastructure/    (done — step 2; eTIMS/M-Pesa adapters land in step 3+)
  Pos.Api/               ASP.NET Core store server (step 3)
```

## Step 2 design notes

- **Ports stay in `Pos.Application`**, implementations in `Pos.Infrastructure` — domain
  references neither, so the bounded contexts in step 1 stay framework-free.
- **Handlers** (`StartSaleHandler`, `AddSaleLineHandler`, `AddTenderHandler`,
  `CompleteSaleHandler`, `RecordStockMovementHandler`, `GetStockOnHandHandler`) source
  tenant / store / user from `ICurrentContext` — never the client payload — so the
  store-authoritative and tenant-scoping invariants survive a hostile request.
- **CompleteSaleHandler** records one negative-delta `StockMovement` per line in the
  SAME unit of work that completes the sale, so a crash during checkout either commits
  both or neither — the append-only inventory invariant survives partial failure.
- **Transactional outbox** (`DomainEventToOutboxInterceptor`) drains every aggregate's
  `DomainEvents` into an `outbox_messages` row during `SaveChangesAsync`, in the same
  database transaction. `OutboxDispatcher` ships them at least once (step-2 stub logs
  them; the real HQ transport lands in step 3).

## Build & run
```bash
dotnet restore
dotnet build
dotnet test
dotnet run --project samples/Pos.Smoke
```

## Postgres + migrations (step 2+)
The infrastructure DI extension is wired up via:
```csharp
services.AddPosInfrastructure(builder.Configuration.GetConnectionString("Pos")!);
```
Each branch's store server runs its own Postgres database; HQ aggregates from outbox
shipments. To scaffold the first migration on an internet-connected machine:
```bash
dotnet tool install --global dotnet-ef
dotnet ef migrations add Init --project src/Pos.Infrastructure --startup-project src/Pos.Infrastructure
```
