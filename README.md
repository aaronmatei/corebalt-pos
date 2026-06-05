# Pos — Point of Sale (store-tier core)

The foundation of a retail/supermarket POS designed to grow from a single on-prem store
to a multi-branch chain (and later SaaS) **without rewrites**. This step delivers the
domain core and the four structural invariants everything else depends on.

> Sandbox note: this was scaffolded where `nuget.org` is unreachable, so step 1 is
> intentionally dependency-free and builds fully offline. From step 2 (EF Core + Postgres)
> onward, run `dotnet restore`/`build` on a machine with normal internet.

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
  Pos.SharedKernel/   building blocks: Entity, AggregateRoot, ValueObject, Money, IDs, invariants
  Pos.Domain/         bounded contexts: Sales (Sale/SaleLine/Tender), Inventory (StockMovement)
samples/
  Pos.Smoke/          runnable console proof of the domain (no infrastructure)
```

## Intended full layout (built over the coming steps)
```
src/
  Pos.SharedKernel/      (done)
  Pos.Domain/            Sales, Inventory, Catalog, Pricing, Payments, Tax, Identity ...
  Pos.Application/       use cases / ports (step 2)
  Pos.Infrastructure/    EF Core + PostgreSQL, repositories, outbox, eTIMS/M-Pesa (step 2+)
  Pos.Api/               ASP.NET Core store server (step 3)
tests/
  Pos.Domain.Tests/      xUnit (step 2)
```

## Build & run
```bash
dotnet build
dotnet run --project samples/Pos.Smoke
```
