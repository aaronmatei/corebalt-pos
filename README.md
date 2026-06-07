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
`Host=localhost;Port=5544;Database=pos;Username=postgres;Password=pos`.

> Port **5544** (not 5432) is deliberate: it avoids clashing with any native
> PostgreSQL service already bound to 5432. `--restart unless-stopped` keeps the
> container across reboots; the named volume `pos-pg-data` keeps the data across
> container **recreation** (without it, `docker rm`/recreate wipes the schema and
> the API would 500 with `relation "products" does not exist`).

End-to-end verification:
```bash
docker run --name pos-pg --restart unless-stopped -v pos-pg-data:/var/lib/postgresql/data -e POSTGRES_PASSWORD=pos -e POSTGRES_DB=pos -p 5544:5432 -d postgres:17
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
$env:POS_DB = "Host=localhost;Port=5544;Database=pos;Username=postgres;Password=pos"
dotnet run --project src/Pos.Api
# OpenAPI document at: http://localhost:5xxx/openapi/v1.json
```

### Identity & authentication (step 9)
Lightweight custom identity — own `User` aggregate + JWT, **not** ASP.NET Core Identity.
Sign in for a token, then send it as `Authorization: Bearer <jwt>` on every call:

- `POST /api/v1/auth/pin-login` `{ staffCode, pin }` → JWT (fast till login)
- `POST /api/v1/auth/login` `{ username, password }` → JWT (back office)

The JWT carries tenant/store/user/role/name/staff-code, signed with a local store-server HMAC key
(`Jwt:Key`, works offline on the LAN). PINs/passwords are stored only as PBKDF2 hashes. **Role
policies:** Manager for back-office product/stock writes; Cashier+ (any authenticated) for checkout.
A bootstrap Manager is seeded on first run (`Auth:Bootstrap`, forced password change). `GET /healthz`
and the M-Pesa callback are the only anonymous routes.

**Dev bypass** (`Auth:AllowDevHeaders=true`, off by default, never in Production): send
`X-Tenant-Id`/`X-Store-Id`/`X-User-Id` (UUIDs) and the API treats you as a Manager in that scope —
keeps the surface curlable and the tests simple. In Development a demo cashier is seeded for the till
(staff code `1001`, PIN `1234`).

### Endpoints

| Method | Path                                             | Notes                                                |
|--------|--------------------------------------------------|------------------------------------------------------|
| POST   | `/api/v1/auth/pin-login`                         | StaffCode + PIN → JWT (anonymous)                    |
| POST   | `/api/v1/auth/login`                             | Username + password → JWT (anonymous)                |
| POST   | `/api/v1/auth/change-password`                   | Change own password (clears forced change)           |
| POST   | `/api/v1/users`                                  | Create staff (Manager only)                          |
| GET    | `/api/v1/products`                               | List catalogue (active only; `?includeInactive=true`) |
| POST   | `/api/v1/products`                               | Create (SKU unique; barcode unique when present)     |
| PUT    | `/api/v1/products/{id}`                          | Update name/barcode/unit/tax-class/active            |
| POST   | `/api/v1/products/{id}/deactivate`               | Soft-delete (sets IsActive=false)                    |
| GET    | `/api/v1/products/{id}`                          |                                                      |
| GET    | `/api/v1/products/by-sku/{sku}`                  |                                                      |
| GET    | `/api/v1/products/barcode/{barcode}`             | Look up by printed GTIN/EAN-13 scan code             |
| PUT    | `/api/v1/products/{id}/price`                    | Reprice — emits ProductPriceChanged to the outbox    |
| POST   | `/api/v1/sales/checkout`                          | **Atomic** cash start+lines+tenders+complete → 201   |
| POST   | `/api/v1/sales/mpesa/checkout`                    | Initiate STK push (async); sale stays pending → 202   |
| GET    | `/api/v1/sales/mpesa/{saleId}/status`             | Poll: reconciles via STK query (Pending/Confirmed/Failed) |
| POST   | `/mpesa/callback`                                 | Daraja result callback — **no auth**, idempotent     |
| POST   | `/api/v1/sales`                                  | StartAsync, returns saleId                           |
| POST   | `/api/v1/sales/{saleId}/lines`                   | Body supplies productId + quantity; price from catalog |
| POST   | `/api/v1/sales/{saleId}/tenders`                 | Cash / Mpesa / Card / AirtelMoney                    |
| POST   | `/api/v1/sales/{saleId}/complete`                | Writes -delta StockMovements in the same UoW         |
| GET    | `/api/v1/sales/{saleId}`                         |                                                      |
| GET    | `/api/v1/sales/{saleId}/receipt`                 | Fiscal receipt (`?cols=48`/`32`): model + text + HTML |
| POST   | `/api/v1/inventory/receive`                      | Receive IN: +qty, reason Purchase/OpeningBalance/Adjustment |
| POST   | `/api/v1/inventory/adjust`                       | Adjustment: signed qty (stock take / shrinkage)      |
| POST   | `/api/v1/sales/{id}/returns`                     | Return / void (Supervisor+); client-generated returnId |
| GET    | `/api/v1/returns/{id}/receipt`                   | Credit-note receipt (negative amounts)               |
| GET    | `/api/v1/inventory/{productId}/on-hand`          | SUM of stock_movements (never a mutable column)      |
| GET    | `/api/v1/inventory/report`                       | Store stock report (every product, derived on-hand)  |
| GET    | `/healthz`                                       | No auth                                              |

Domain rule violations (`Sale not fully paid`, `Currency mismatch`, …) surface
as `409 Conflict`. Argument-validation errors are `400 Bad Request`.

### API integration tests
`tests/Pos.Api.Tests` boots the host via `WebApplicationFactory<Program>` against
a dedicated `pos_test` database (created on first run if absent). Tests mint a
fresh `TenantId` per case to stay isolated. The connection string is taken from
`POS_TEST_DB`, defaulting to
`Host=localhost;Port=5544;Database=pos_test;Username=postgres;Password=pos`.

```bash
dotnet test tests/Pos.Api.Tests
```

## Till client (step 4)

`src/Pos.Till` is an Avalonia (.NET 10, MVVM via CommunityToolkit.Mvvm) desktop till. It is a
**pure HTTP client** of `Pos.Api` — it references none of the domain/application/infrastructure
assemblies and carries its own wire DTOs. One screen: browse/search the catalogue, scan or type a
barcode, add lines (weighed goods prompt for kg, others for quantity), tender cash and/or M-Pesa
(reference), and complete the sale. The client shows a local subtotal/change preview but treats the
checkout response as authoritative for the final total and change.

Config (`appsettings.json` section `Till`, or `POS_TILL_*` env vars): `BaseUrl`
(default `http://localhost:5080`) and the identity GUIDs (`TenantId`/`StoreId`/`UserId` sent as the
`X-*` headers, plus `RegisterId` for the checkout body) — defaulting to the dev scope the
persistence demo seeds, so the till sees the same store.

```bash
dotnet run --project src/Pos.Api      # serves http://localhost:5080
dotnet run --project src/Pos.Till     # launches the till against it
```

The scan handler (`Scanning/ScannedCode`) already distinguishes a normal GTIN from a price-embedded
EAN-13 (number-system digit `2`, the in-store/scale range); decoding its PLU+weight payload arrives
with the weighed-goods/scales feature (roadmap S2).

## M-Pesa (Daraja STK push) — step 5

M-Pesa is **asynchronous** and is modelled as a pending→confirmed flow, never a synchronous tender:

1. A `Tender` carries a `Status` (Pending / Confirmed / Failed) and a `ProviderReference`
   (Daraja CheckoutRequestID). Cash is Confirmed on creation; only Confirmed tenders count toward
   `Paid`, and a sale **cannot complete while any tender is Pending**.
2. `POST /api/v1/sales/mpesa/checkout` opens the sale, attaches a **pending** M-Pesa tender, fires
   the STK push, and returns `202` with the `checkoutRequestId` (sale stays Open). Optional
   `cashTenders` allow a split payment.
3. The till **polls** `GET /api/v1/sales/mpesa/{saleId}/status`, which runs Daraja's STK *query* and
   reconciles. On success the tender is Confirmed and — once the basket is fully paid — the sale is
   finalized (completed + stock movements + outbox), all in one transaction.
4. `POST /mpesa/callback` is also wired (Daraja's result callback). It's **unauthenticated** (Safaricom
   can't send our identity headers), **idempotent**, and reconciled strictly by CheckoutRequestID +
   amount, so a replayed callback can't double-confirm. In dev we confirm by polling (no public URL
   needed); point `POS_MPESA_CALLBACKURL` at a tunnel if you want live callbacks.

A `MpesaPayment` ledger row (unique on CheckoutRequestID) is the durable reconciliation record.

### Configuring sandbox credentials

Get a sandbox app's **Consumer Key/Secret** and the **Lipa na M-Pesa Online Passkey** from the
[Daraja portal](https://developer.safaricom.co.ke/). Secrets are **never committed** — supply them via
.NET user-secrets (dev) or `POS_MPESA_*` environment variables (CI/containers). `POS_MPESA_*` wins.

```bash
# dev: user-secrets (scoped to Pos.Api)
dotnet user-secrets --project src/Pos.Api set "Mpesa:ConsumerKey"    "<your-key>"
dotnet user-secrets --project src/Pos.Api set "Mpesa:ConsumerSecret" "<your-secret>"
dotnet user-secrets --project src/Pos.Api set "Mpesa:Passkey"        "<your-passkey>"
# ShortCode defaults to the sandbox test till 174379; BaseUrl to https://sandbox.safaricom.co.ke
```
```powershell
# or environment variables
$env:POS_MPESA_CONSUMERKEY="<your-key>"; $env:POS_MPESA_CONSUMERSECRET="<your-secret>"
$env:POS_MPESA_PASSKEY="<your-passkey>"; $env:POS_MPESA_SHORTCODE="174379"
```

Sandbox STK pushes go to the test MSISDN you request a prompt for (use Safaricom's sandbox test
number). Without credentials, `mpesa/checkout` fails gracefully (the tender is marked Failed and the
till offers retry / cash) — cash checkout is unaffected. The test suite uses a fake `IMpesaClient`, so
`dotnet test` needs **no** credentials and makes **no** network calls.

### Dev fake provider (no Daraja, no device)

The Daraja **sandbox** test number can't enter a PIN, so a real STK push there always ends in "DS
timeout". For demos / UI testing, flip on the in-memory fake provider — it auto-confirms, so the
till's **Pay with M-Pesa** completes on its own (initiate → poll → Confirmed → completed → fiscalized):

```powershell
$env:POS_MPESA_USEFAKE = "true"     # or "Mpesa:UseFake": true in appsettings.Development.json / user-secrets
dotnet run --project src/Pos.Api
```

It's **dev-only** — `UseFake` is ignored when the environment is Production. A startup log warns when
it's active, and every fake call logs a warning. Leave it off (default) to exercise real Daraja.

## Fiscal receipt (step 6)

The receipt is a **deterministic projection over the completed, persisted sale** — never recomputed
from the cart, so reprints are byte-identical. VAT is computed and **stored at checkout** (immutable
fact): Kenyan retail prices are VAT-inclusive, so `Sale.Complete()` backs VAT out of each line
(standard-rated: `VAT = total × 16/116`; zero-rated/exempt: 0) and stores the per-line figures, a
per-class VAT summary, and the grand total — inside the existing checkout transaction.

- **Receipt number** (`Sale.ReceiptNumber`, e.g. `MB-000123`): human-readable and
  **store-authoritative** — a per-`(TenantId, StoreId)` counter row incremented atomically (upsert)
  *inside the completion transaction*, never a global sequence (which would break store ownership /
  offline operation). Formatted in one place (`ReceiptNumberFormatter`; branch code from
  `Store:BranchCode`). The UUIDv7 stays the internal id / idempotency key and is kept as a small "Ref"
  in the model/HTML for support lookups.
- `Product.TaxClass`: `StandardRated` (16%) / `ZeroRated` / `Exempt` (default standard).
- `GET /api/v1/sales/{id}/receipt?cols=48` (80mm; `32` = 58mm) returns the `ReceiptModel`, a
  fixed-width monospace **text** render for ESC/POS, and a simple **HTML** preview for the till.
- Header from the `Store` config section (LegalName, KraPin, BranchName, address, Phone, VatNumber).
  Line items carry a configurable tax-code letter (A/B/C — provisional, confirm KRA codes at eTIMS
  integration); a VAT breakdown + legend and totals (subtotal, total VAT, grand total) follow.
- **eTIMS** fields (CUIN, signature, QR, transmittedAt) are nullable and null for now; the fiscal
  block prints `eTIMS: PENDING TRANSMISSION` and is isolated in its own renderer method so wiring the
  Tax module later only fills the fields. CUIN + QR are minted by eTIMS on transmission.

The till fetches and shows the rendered receipt after each completed sale.

## eTIMS fiscalization seam (step 7)

This makes the POS **eTIMS-ready** — it is NOT real KRA fiscalization. The real VSCU/OSCU client
drops in behind `IFiscalizationProvider` later; until then a **fake/training provider** runs.

- Config section **"Etims"**: `Enabled`, `Mode` (Vscu/Oscu), and blank onboarding placeholders
  `DeviceSerial`/`BranchId`/`CmcKey`/`BaseUrl`. The seller PIN comes from `Store:KraPin`. The fake is
  used while `Enabled` and no real credentials are present (`HasRealCredentials`).
- `FakeEtimsProvider.SignAsync` mints a clearly-fake **deterministic** CUIN (`TEST-` + receipt
  number), a fake signature, and a QR payload shaped like a KRA verification URL containing the CUIN.
  `SyncAsync` is a logged no-op success.
- `Sale.FiscalStatus` (`NotRequired` / `Signed` / `Synced` / `Failed`) + the eTIMS fields. After a
  sale is committed, `FiscalizationService` signs it (if enabled) → **Signed**, persisting the CUIN/QR
  so a receipt fetched right after shows the fiscal block. It's **idempotent** — reprints never
  re-sign. Disabled → **NotRequired** and the receipt prints a **"NON-FISCAL / TRAINING"** note.
- `EtimsSyncWorker` (a `BackgroundService` over `FiscalSyncService`) is the seam for the real KRA
  batch upload: it transmits Signed-but-not-Synced sales → **Synced**, retrying on the interval and
  flipping to **Failed** after `SyncMaxAttempts`. With the fake it's instant success.
- The thermal receipt prints the CUIN + the QR payload string (native ESC/POS QR raster comes with
  the printer driver); the HTML preview renders the QR target as a link.

## Back-office data core (step 8)

Product/price management and stock receiving — API + domain only (no admin UI yet), holding the
invariants (UUIDv7 ids, TenantId+StoreId on every row, append-only stock, on-hand always derived).

- **Products:** SKU unique and barcode unique (when present) **per tenant** — backed by unique
  indexes (`ux_products_tenant_sku`; `ux_products_tenant_barcode` filtered `WHERE barcode IS NOT NULL`
  so many products may have no barcode), with app-level checks returning a clean `409` (and a
  Postgres `23505` backstop in the exception handler if the check loses a race). Price ≥ 0.
  Update edits name/barcode/unit/tax-class/active. **Deactivate is soft** (`IsActive=false`,
  never a hard delete); list/search default to active, `?includeInactive=true` shows all.
- **Pricing is never silent:** `PUT /products/{id}/price` raises a **`ProductPriceChanged`** domain
  event (old/new price, who, when) to the outbox — audit + the seam for central pricing (M2). The
  current `Price` stays on the product for fast lookup. (Effective-dated price lists arrive with S4.)
- **Stock receiving:** `POST /inventory/receive` (positive qty; reason Purchase/OpeningBalance/Adjustment;
  optional supplier/GRN Reference) and `POST /inventory/adjust` (signed qty, reason Adjustment) each
  append exactly **one immutable `StockMovement`** — never an edit. Sales already write the OUT rows.
- **On-hand is always derived:** `GET /inventory/{id}/on-hand` and `GET /inventory/report` are
  `SUM(QuantityDelta)` over the ledger; there is no stored on-hand/quantity column anywhere.

## Returns, voids & refunds (step 11)

A reversal is a **new immutable `CreditNote`** referencing the original sale — the completed sale is
never mutated or deleted.

- `POST /api/v1/sales/{id}/returns` `{ returnId, reason, lines[{productId, quantity}], refundMethod }`
  → creates the credit note and returns its id + a credit-note receipt. **Supervisor or Manager only**
  (a Cashier gets `403`). Idempotent on the client-generated `returnId` (offline replay).
- **Reason** is required (Damaged / WrongItem / CustomerChangedMind / CashierError). Returned lines
  carry the **original** unit price + tax (VAT backed out), so the credit is exact.
- **Over-return guard:** `sum(prior returns + this) ≤ original qty` per line, else `409`.
- A **void** is simply a full-quantity return — no separate concept.
- **Stock:** returned goods append new **IN** `StockMovement` rows (reason `Return`) reversing the
  sale's OUT; on-hand stays `SUM(movements)`. Existing movements are never edited.
- **Refund:** Cash is recorded as **Refunded** immediately. M-Pesa / other are recorded as
  **PendingManual** — flagged for an out-of-band refund, never auto-reversed (the Daraja reversal is a
  separate, operationally-gated API).
- **Fiscal:** a credit note is signed through the same eTIMS seam (`SignCreditNoteAsync`, stub CUIN
  `TEST-CN-…`) referencing the original receipt's CUIN.
- `GET /api/v1/returns/{id}/receipt` renders a **"CREDIT NOTE / REFUND"** receipt — negative
  quantities/amounts, the original receipt referenced, the refund tender, the credit-note fiscal block.

## Back office — Blazor Server admin (step 10)

A manager-gated admin UI **hosted inside the store-server process** (`Pos.Api`) — one on-prem
deployable, splittable later. Static server-side-rendered Razor Components + form posts; it reuses the
**same Application services** as the JSON API (`ProductService`, `StockService`, `AuthService`), so
there's no duplicated business logic.

- Browse to the store server root (e.g. `http://localhost:5080/`). Sign in at `/login` with a Manager
  **username + password** (the bootstrap `manager` / `ChangeMe!123`; first login forces a change).
- **Cookie auth** (scheme `Cookies`), separate from the till/API's JWT bearer. Every page is gated by
  the `BackOfficeManager` policy (cookie + Manager role); a non-manager is bounced to `/login`.
- **Screens:** Products (search, create/edit, price change → `ProductPriceChanged` to the outbox,
  soft activate/deactivate with include-inactive), Stock (receive / signed adjustments → immutable
  `StockMovement`, on-hand report = SUM of movements), Cashiers (create with PIN + role, reset PIN,
  deactivate/reactivate).
- **Brand:** navy `#16223f`, accent `#4D8BFF`, corebalt favicon + sidebar logo.
- Form posts go to `/backoffice/*` endpoints (antiforgery protected). On-prem on a LAN; the cookie
  session lasts a shift.

## Per-client installs — vendor/tenant model (step 12)

Corebalt is the **vendor**; each retailer we install for is a **tenant** (one tenant per on-prem
deployment + DB). `TenantId` is on every row, so the same code consolidates into a shared multi-tenant
cloud later. **Nothing client-specific is hardcoded.**

- **Merchant profile (DB):** the client's own identity — legal/trading name, KRA PIN, VAT status,
  contacts, currency, branches, optional logo + receipt footer. The receipt header reads THIS, never
  Corebalt's; an optional **"Powered by Corebalt POS"** footer is the only place the vendor appears.
- **Per-tenant integration settings (CLIENT-editable, encrypted):** M-Pesa (shortcode/passkey/consumer
  key+secret/environment) and eTIMS (device/branch creds, mode, enabled) live in the DB, encrypted at rest
  with **ASP.NET Core Data Protection** (the install-level key ring on disk — store ciphertext, never
  plaintext). The Daraja client and fiscalization read them per tenant — not appsettings. Editable by the
  client's Manager in back-office **Settings**. Dev user-secrets / `Mpesa:UseFake` are for Corebalt's own
  development only.
- **Entitlements / licence (VENDOR-controlled, offline):** Edition, feature flags (MultiBranch,
  Promotions, Loyalty…), limits and expiry come ONLY from a **Corebalt-signed licence key** — a token
  Corebalt signs with its private key (ECDSA P-256) that the app verifies with an embedded public key.
  The client *applies* a key (in setup or Settings) but **cannot edit flags/limits** — a tampered,
  expired, or wrong-tenant key is rejected, and entitlements are re-derived from the verified key on every
  read (so editing the DB rows grants nothing). `IEntitlements` gates optional modules (e.g.
  `POST /api/v1/branches` requires MultiBranch → otherwise `403`); no key → unlicensed baseline (Retail).
- **First-run setup wizard (`/setup`):** a fresh install (no merchant profile) is routed to a guided
  setup — business profile, branch, currency, **licence key**, payment + eTIMS (or skip → stub), and the
  **first manager** (no seeded-credential hunting). You can't transact until setup is complete.

## Thermal printing (step 13)

Drives ESC/POS from the persisted `ReceiptModel` (sales AND credit notes), fully verifiable without a
printer — everything is per-tenant/per-register config.

- **`PrinterProfile` per register (DB):** Transport (Network `host:9100` / File / Null), PaperWidth
  (80mm → 576 dots / 48 cols, 58mm → 384 / 32), and capability flags HasCutter, HasCashDrawer,
  NativeQrSupported. Defaults to Null / 80mm (safe in dev).
- **`EscPosBuilder`:** init → CLIENT logo raster (from `MerchantProfile`; text header if none — never
  Corebalt's) → text body → QR (native `GS ( k` if NativeQrSupported, else a rasterized image) → cut
  (`GS V`, only with a cutter, else feed) → cash-drawer kick (`ESC p`, only with a drawer AND a cash
  tender). `MonoBitmap` downscales+thresholds any logo and rasterizes the QR (QRCoder) to 1-bit `GS v 0`.
- **`IReceiptPrinter`:** `EscPosNetworkPrinter` (raw TCP), `EscPosFilePrinter` (writes a `.escpos` file —
  on Windows `copy /b file PRN`), `NullPrinter` (logs length; the dev/test default). A router picks the
  implementation from the profile's transport.
- **Visual preview (verify without hardware):** `ReceiptPreviewRenderer` draws the same model to a PNG
  at the dot width with the client logo and a real rendered QR. `GET /api/v1/sales/{id}/receipt/preview.png`
  and `GET /api/v1/returns/{id}/receipt/preview.png` (`?paper=58` for 58mm). What you see is what prints.
- Checkout and returns build the bytes from the register's profile and send to the configured printer;
  reprints use the persisted model so output is byte-identical. The Corebalt mark
  (`wwwroot/assets/corebalt-mark-black-mono.png`) prints ONLY in the optional Powered-by footer.
- Only the final on-physical-device acceptance check remains.

## Cash management + close-of-day (S3)

The spine is a register **shift** (`RegisterSession`); cash handling and the X/Z reports hang off it.
Everything is immutable facts, tenant/store/register-scoped; reports are read-side projections (no
mutable running counters).

- **Shift:** open with an opening float, sell, then close (cash-up). One open session per register; a
  closed session is a permanent end-of-day fact. **Selling requires an open session** — checkout and
  returns are blocked (409) until a shift is opened, so the till prompts the cashier to open one.
- **Cash movements:** `PayIn` / `PayOut` / `Drop` (Supervisor+), immutable, tied to the session. Sale
  cash and cash refunds are NOT movements — they already exist as tender / credit-note facts.
- **Expected drawer cash** = opening float + cash sales (net of change) − cash refunds + pay-ins −
  pay-outs − drops. Cash only; M-Pesa and other tenders are reported but never reconciled to the drawer.
- **X report** — a read of the current open session at any time (changes nothing). **Z report** — produced
  on close: the same totals (by tender, VAT by rate, returns, voids) PLUS the cash reconciliation
  (expected vs counted → variance over/short). Closing freezes counted/expected/variance and starts the
  next session clean.
- **Authorization:** open/close own session — Cashier+; pay-in/out/drop — Supervisor+; a close variance
  beyond `Cash:VarianceAckThreshold` (default 500) requires a **Manager** to acknowledge it.
- **Print / view:** X/Z print via the ESC/POS pipeline (`POST /api/v1/sessions/{id}/print`) and are
  viewable in the back-office (`/sessions`, `/sessions/{id}`), with a store/day sales summary
  (`/day-summary`, `GET /api/v1/sales-summary`).

## Roadmap (anticipated in design choices)
- Single-store supermarket: S1 multi-lane foundation, S2 weighed goods + scales, S3 cash office,
  S4 promotions + loyalty, S5 procurement.
- Multi-branch chain: M1 HQ/cloud tier + store↔HQ sync (reads the outbox), M2 central
  catalog/pricing push, M3 distribution + inter-branch transfers. M1 also unlocks SaaS.
