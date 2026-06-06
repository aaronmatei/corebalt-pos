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
- `src/Pos.Domain` — Sales (Sale/SaleLine/Tender), Inventory (StockMovement), Catalog (Product),
  Payments (MpesaPayment — the M-Pesa reconciliation ledger)
- `src/Pos.Application` — ports (repositories, IUnitOfWork, IClock, **IMpesaClient**) + use cases
  `CheckoutService` (cash) and `MpesaPaymentService` (async M-Pesa: initiate/query/callback);
  `Receipts/` (ReceiptModel projection + renderers); `Fiscalization/` (IFiscalizationProvider,
  FiscalizationService, FiscalSyncService, EtimsOptions)
- `src/Pos.Infrastructure` — PosDbContext, EF configurations, repositories, outbox interceptor,
  **DarajaMpesaClient** (Mpesa/), **FakeEtimsProvider** (Fiscalization/), DI; `Persistence/Migrations`
- `src/Pos.Api` — ASP.NET Core 10 minimal-API store-server host: catalog + checkout + inventory over
  HTTP. Thin endpoints delegating to `CheckoutService`; header-based identity (`Auth/`)
- `src/Pos.Till` — Avalonia (.NET 10, MVVM/CommunityToolkit) desktop till. A **pure HTTP client**
  of `Pos.Api`: it references no domain/application/infrastructure assembly and owns its own wire
  DTOs. Single till screen (catalogue + barcode scan + cart + cash/M-Pesa tender + checkout)
- `samples/Pos.Smoke` — domain-only console (no infrastructure)
- `samples/Pos.Persistence.Demo` — saves a sale to Postgres, reloads it, prints the outbox
- `tests/Pos.Domain.Tests` — xUnit invariant tests (UUIDv7, store/tenant scoping, append-only, Money)
- `tests/Pos.Api.Tests` — `WebApplicationFactory<Program>` integration tests against `pos_test`

## Build / run
```bash
# Port 5544 avoids clashing with any native Postgres on 5432; --restart survives reboots; the named
# volume survives container RECREATION (without it, docker rm/recreate wipes the schema).
docker run --name pos-pg --restart unless-stopped -v pos-pg-data:/var/lib/postgresql/data -e POSTGRES_PASSWORD=pos -e POSTGRES_DB=pos -p 5544:5432 -d postgres:17
# The InitialCreate migration already exists; this just applies it. (Add a new one only when the model changes.)
dotnet ef database update --project src/Pos.Infrastructure --startup-project samples/Pos.Persistence.Demo
dotnet run  --project samples/Pos.Persistence.Demo   # save→reload a sale, print the outbox row
dotnet run  --project src/Pos.Api                    # store-server host on http://localhost:5080; auto-applies migrations in Development
dotnet run  --project src/Pos.Till                   # Avalonia till (pure API client); talks to :5080
dotnet test                                          # domain + API integration tests (63)
```
Receipt header comes from the `Store` config section (LegalName/KraPin/BranchName/BranchAddress/
Phone/VatNumber/Currency) — config-swappable, defaults to Corebalt Technologies.
M-Pesa (Daraja) secrets are read from the `Mpesa` config section / user-secrets / `POS_MPESA_*` env
(see README). Without them, M-Pesa initiate fails gracefully (tender → Failed); cash is unaffected.
Dev/demo: `POS_MPESA_USEFAKE=true` (or `Mpesa:UseFake`) swaps in an auto-confirming in-memory M-Pesa
client so the till's Pay-with-M-Pesa completes without Daraja/a device; ignored in Production.
Connection string via `POS_DB` env var (default `Host=localhost;Port=5544;Database=pos;Username=postgres;Password=pos`).
`Pos.Api.Tests` uses `POS_TEST_DB` (default same as above but `Database=pos_test`); the `pos_test` DB is created on first run if absent.

## Current state & immediate task
- **Done:** step 1 (domain core + invariants), step 2 (Catalog, Application, EF Core/Postgres
  persistence + transactional outbox, `InitialCreate` migration), step 3 (`Pos.Api`
  store-server host + `Pos.Api.Tests` integration tests), step 4 (`Pos.Till` Avalonia client), and
  step 5 (real M-Pesa via Daraja STK push, as an async pending→confirmed flow), and step 6
  (fiscal receipt incl. human receipt numbers), step 7 (eTIMS fiscalization SEAM — eTIMS-ready,
  NOT real fiscalization), and step 8 (back-office data core: product/price management + stock
  receiving). All six projects target `net10.0`; `dotnet test` is green at 63 (29 domain + 34 API).
- **eTIMS seam (mirrors the M-Pesa provider pattern):** `IFiscalizationProvider.SignAsync/SyncAsync`
  with `FakeEtimsProvider` (training mode — deterministic "TEST-…" CUIN from the receipt number, fake
  signature, KRA-shaped QR URL; SyncAsync a logged no-op). Config "Etims": Enabled, Mode (Vscu/Oscu),
  blank DeviceSerial/BranchId/CmcKey/BaseUrl placeholders (seller PIN from Store). `Sale.FiscalStatus`
  (NotRequired/Signed/Synced/Failed) + EtimsCuin/Signature/QrUrl/SignedAt/TransmittedAt/SyncAttempts.
  After checkout commits, `FiscalizationService.FiscalizeAsync` signs (if Enabled) → Signed and
  persists CUIN/QR (idempotent — never re-signs on reprint); disabled → NotRequired. `EtimsSyncWorker`
  (BackgroundService over `FiscalSyncService`) transmits Signed→Synced with retry, Failed after N.
  The receipt fiscal block renders CUIN + QR when signed, else a "NON-FISCAL / TRAINING" note. The
  real KRA VSCU/OSCU client drops in behind the interface (selected by `Etims.HasRealCredentials`).
- **Receipt number:** `Sale.ReceiptNumber` (e.g. "MB-000123") — store-authoritative, from a
  per-(TenantId,StoreId) counter row (`receipt_counters`) incremented atomically via upsert inside
  the completion transaction (`IUnitOfWork.ExecuteInTransactionAsync` + `SaleCompletion`), NOT a
  global sequence. Formatted in one place (`ReceiptNumberFormatter`, branch code from `Store:BranchCode`).
  The UUIDv7 stays the internal id/idempotency key (printed as a small "Ref" only in the model/HTML).
- **VAT + receipt (KRA):** `Product.TaxClass` (StandardRated 16% / ZeroRated / Exempt; prices are
  VAT-INCLUSIVE). At completion `Sale.Complete()` backs VAT out of each line and STORES it (per-line
  TaxClass + VatAmount + TaxableAmount, a per-class VAT summary, and the grand total) inside the
  checkout transaction — immutable facts, never recomputed at print time. `Sale` also has nullable
  eTIMS fields (CUIN/signature/QR/transmittedAt), all null until the Tax module transmits.
  `GET /api/v1/sales/{id}/receipt?cols=48|32` projects the persisted sale + the "Store" config section
  into a `ReceiptModel` and renders deterministic (byte-identical) thermal text + an HTML preview; the
  fiscal block renders the CUIN + QR once signed (see the eTIMS seam above).
- **M-Pesa (async, NEVER faked as synchronous):** a `Tender` has a `Status` (Pending/Confirmed/Failed)
  + `ProviderReference`; cash is Confirmed on creation; `Paid` counts only Confirmed tenders and
  `Sale.Complete()` refuses while any tender is Pending. STK flow: `POST /api/v1/sales/mpesa/checkout`
  (opens the sale + pending tender + STK push → 202), `GET /api/v1/sales/mpesa/{saleId}/status`
  (till polls; reconciles via STK query), `POST /mpesa/callback` (unauthenticated, idempotent,
  reconciled by CheckoutRequestID + amount). The sale finalizes (complete + stock movements + outbox)
  only when the pending tender confirms. Daraja secrets come from user-secrets / `POS_MPESA_*` env —
  never hardcoded; tests use a fake `IMpesaClient` (no network). See README "M-Pesa (Daraja)".
- **Back-office (products/pricing/stock):** `POST /products` (SKU unique + barcode unique when present,
  **per tenant** — enforced by unique indexes `ux_products_tenant_sku` and `ux_products_tenant_barcode`
  [filtered `WHERE barcode IS NOT NULL`], with app-level checks returning a clean 409 and a 23505 DB backstop), `PUT /products/{id}` (name/barcode/unit/tax-class/active),
  `POST /products/{id}/deactivate` (SOFT — never hard-delete; list defaults to active, `?includeInactive=true`),
  `PUT /products/{id}/price` (raises `ProductPriceChanged` to the outbox — audit + central-pricing seam;
  current Price stays for fast lookup). Stock is the append-only `StockMovement` ledger: `POST /inventory/receive`
  (positive qty, reason Purchase/OpeningBalance/Adjustment, optional Reference) and `POST /inventory/adjust`
  (signed qty, reason Adjustment) each write ONE immutable movement; `GET /inventory/{id}/on-hand` and
  `GET /inventory/report` are always SUM(movements) — on-hand is NEVER a stored column.
- **Catalog/checkout endpoints (`/api/v1`):** `GET /products` (list; `?includeInactive`), `GET /products/{id}`,
  `GET /products/by-sku/{sku}`, `GET /products/barcode/{barcode}`;
  `POST /sales/checkout` (atomic one-shot: start+lines+tenders+complete
  in a single transaction, returns 201), plus the incremental `POST /sales` → `/lines` → `/tenders`
  → `/complete` flow; `GET /sales/{id}`; `GET /sales/{id}/receipt`.
- **Product.Barcode:** nullable scan code (GTIN/EAN-13), distinct from `Sku`, indexed per tenant
  (`AddProductBarcode` migration). The till's scan handler (`Scanning/ScannedCode`) already
  classifies price-embedded EAN-13 (number-system digit `2`) so weighed-goods PLU+weight decoding
  can slot in with the scales feature (roadmap S2) without changing the contract.
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
