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
- `src/Pos.Domain` — Sales (Sale/SaleLine/Tender, CreditNote), Inventory (StockMovement), Catalog (Product),
  Payments (MpesaPayment), Identity (User/UserRole), Cash (RegisterSession/CashMovement), Tenancy (MerchantProfile/Branch, Mpesa/EtimsSettings, Entitlements, Register, PrinterProfile)
- `src/Pos.Application` — ports (repositories, IUnitOfWork, IClock, **IMpesaClient**, IPasswordHasher,
  ITokenIssuer, IUserRepository) + use cases `CheckoutService`, `MpesaPaymentService`, `AuthService`,
  and `ProductService` + `StockService` (single home for product/stock orchestration, shared by the
  API and the back-office); `Receipts/`; `Fiscalization/`; `Identity/` (AuthService, StoreServerOptions, PosClaims);
  `Tenancy/` (SetupService, SettingsService, IEntitlements); `Licensing/` (signed-licence verify/sign + codec);
  `Cash/` (CashOfficeService open/close/movement + CashOfficeReportService X/Z + DaySummary projections)
- `src/Pos.Infrastructure` — PosDbContext, EF configurations, repositories, outbox interceptor,
  **DarajaMpesaClient** (Mpesa/, per-tenant via MpesaSettingsResolver), **FakeEtimsProvider** (Fiscalization/),
  **JwtTokenIssuer + AspNetPasswordHasher** (Identity/), **DataProtectionSecretProtector** (Security/),
  **Printing/** (EscPosBuilder, MonoBitmap raster, ReceiptPreviewRenderer, Network/File/Null printers;
  ImageSharp + QRCoder), DI; `Persistence/Migrations`
- `src/Pos.Api` — ASP.NET Core 10 store-server host: minimal-API (auth + catalog + checkout + inventory,
  JWT bearer + role policies + dev-header bypass, `Auth/`) PLUS the **Blazor Server back-office**
  (`Components/` static-SSR pages, `BackOffice/` form-post endpoints, cookie auth, `wwwroot/`)
- `src/Pos.Till` — Avalonia (.NET 10, MVVM/CommunityToolkit) desktop till. A **pure HTTP client**
  of `Pos.Api`: it references no domain/application/infrastructure assembly and owns its own wire
  DTOs. PIN login screen → JWT held for the session; till screen (catalogue + scan + cart + tender +
  checkout) gated behind an **open cash shift** (open-float overlay + drawer/X/close toolbar with
  Supervisor/Manager PIN overrides — see cash management); "Lock / switch cashier" returns to login.
  Shell swaps Login↔Till via a ContentControl
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
dotnet test                                          # domain + API integration tests (110)
```
Receipt header + currency come from the tenant's DB-backed `MerchantProfile` (set in the /setup wizard),
NOT appsettings. A fresh install routes to `/setup`; you can't transact until provisioned. M-Pesa + eTIMS
credentials are per-tenant (DB, encrypted at rest via **ASP.NET Core Data Protection** — the install-level
key ring on disk, no app-config key), editable later in back-office **Settings**; `Mpesa:UseFake` /
`POS_MPESA_USEFAKE` still swaps in the dev fake client. Only the receipt-NUMBER prefix
(`Receipt:NumberPrefix`, generic) remains host config.
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
  NOT real fiscalization), step 8 (back-office data core: product/price management + stock
  receiving), step 9 (authentication & identity: custom User aggregate + JWT, role policies,
  till PIN login), step 10 (Blazor Server **back-office** admin, manager-gated, hosted in the
  store-server process), step 11 (returns / voids / refunds — immutable credit notes), and step 12
  (per-client multi-tenant install: DB-backed merchant profile + per-tenant integration settings +
  entitlements + first-run setup wizard), and step 13 (thermal printing pipeline: per-register
  PrinterProfile, ESC/POS builder, logo/QR rasterizer, Network/File/Null printers, PNG preview), and
  step 14 (cash management + close-of-day: register shifts, drawer movements, X/Z reports), and step 15
  (deployment/ops app-side foundation: Windows Service host + self-contained publish + safe auto-migration).
  All six projects target `net10.0`; `dotnet test` is green at 110 (29 domain + 81 API).
- **Deployment / ops (app-side; installer + scheduled backups come next):** the store server runs headless
  as a **Windows Service** (`builder.Host.UseWindowsService()` — a no-op in console/dev, same binary).
  Both apps publish **self-contained win-x64, single-folder** (runtime bundled; client has no .NET) via
  `deploy/publish-server.ps1` + `deploy/publish-till.ps1` → `dist/`. Install-level config (DB connection
  string with a dedicated non-default port + generated password, `Urls` LAN bind, `StoreServer` identity,
  `Jwt:Key`, `Ops:*` paths) is written by the installer as **`appsettings.Production.json`** (gitignored;
  template `appsettings.Production.json.template`). **Safe startup auto-migration** (`Pos.Infrastructure/Ops`):
  `MigrationPlan.Decide` + `StartupMigrator` — in Production, if pending migrations exist on a POPULATED DB
  it takes a timestamped `pg_dump` backup (`PgDumpBackup`, `Ops:PgDumpPath`) to `Ops:BackupDirectory`
  FIRST; if the backup fails it REFUSES to migrate and fails the service start (fatal log); a fresh/empty
  DB migrates straight through. Applied schema version → `schema-version.json`. **Serilog** rolling file
  logs (`Ops:LogDirectory`, 31-day/50 MB retention) + console. Data-Protection key ring path is
  per-install (`Ops:DataProtectionKeysPath`). Verified live: published self-contained, ran as a console in
  Production, bound `0.0.0.0:5081`, backed-up-then-migrated a one-behind populated DB, a LAN client reached
  the API. See `deploy/README.md`.
- **Cash management + close-of-day (S3):** the spine is a `RegisterSession` (shift) per register
  (`Pos.Domain/Cash`): OpenedBy/At + OpeningFloat → ClosedBy/At + CountedCash/ExpectedCash/Variance;
  one OPEN session per register (filtered unique index); a closed session is an immutable end-of-day
  fact (raises `RegisterSessionClosed` to the outbox). Sales + credit notes capture `RegisterSessionId`
  at creation; **checkout/returns are BLOCKED with no open session** (`CheckoutService`/`MpesaPaymentService`/
  `ReturnService` throw → 409). `CashMovement` (PayIn/PayOut/Drop, immutable, tied to the session) records
  DRAWER events only — NOT sale cash or cash refunds (those are Tender/CreditNote facts; single source).
  **X/Z reports are read-side projections** (`CashOfficeReportService`) over the session's facts — no
  running counters: gross + count + items, totals BY TENDER (gross) with counts, VAT by rate, returns +
  voids (a void = a full reversal, `CreditNote.IsVoid`). Expected drawer cash = OpeningFloat + net cash
  sales (cash tendered − change) − cash refunds + PayIns − PayOuts − Drops (cash only; M-Pesa reported,
  never reconciled). `CashOfficeService` opens/closes/records; a close variance beyond
  `CashOfficeOptions.VarianceAckThreshold` (config `Cash:VarianceAckThreshold`, default 500) needs a
  **Manager** acknowledgement. **Auth:** open/close own session Cashier+; PayIn/Out/Drop Supervisor+.
  `POST /api/v1/sessions/{open,current,movements,{id}/report,{id}/close,{id}/print}` + `GET /sales-summary`
  (store/day aggregate). X/Z print via the ESC/POS pipeline (`ReceiptOutputService.PrintShiftReportAsync`,
  reuses `IReceiptPrinter`) and view in back-office (`/sessions`, `/sessions/{id}`, `/day-summary`).
  **Till (Avalonia):** after login the till checks `GET /sessions/current`; with no shift it shows a
  blocking **Open-shift** overlay (opening float) and selling stays disabled until opened; the header
  shows a shift indicator. A toolbar runs Pay-in/out / Drop (each via a **Supervisor PIN override** —
  a one-off `pin-login` used as the bearer for that call, session token untouched), an **X report**, and
  **Close shift** (counted → Expected/Counted/Variance → Z, a large variance prompts a **Manager PIN**;
  after close it returns to the Open-shift prompt). `MainViewModel.Shift.cs` + `TillView` overlays.
- **Thermal printing (per-register, hardware-free to build/test):** `PrinterProfile` per Register
  (`Pos.Domain/Tenancy`): Transport (Null/File/Network), PaperWidth (80mm/576 dots → 48 cols, 58mm/384 →
  32), HasCutter/HasCashDrawer/NativeQrSupported. `EscPosBuilder` (`Pos.Infrastructure/Printing`) turns
  the persisted `ReceiptModel` (sale OR credit note) into ESC/POS: init, the CLIENT logo raster (from
  MerchantProfile; text header if none — never Corebalt's), text body, QR (native `GS ( k` if
  NativeQrSupported else a rasterized image), cut (`GS V` only if HasCutter, else feed), drawer-kick
  (`ESC p` only if HasCashDrawer AND a cash tender). `MonoBitmap` rasterizes any logo (downscale+threshold)
  or a QR (QRCoder) to 1-bit `GS v 0`. `IReceiptPrinter` impls: `EscPosNetworkPrinter` (TCP :9100),
  `EscPosFilePrinter` (.escpos file), `NullPrinter` (default) — selected by `ReceiptPrinterRouter` per
  transport. `ReceiptPreviewRenderer` draws the SAME model to a PNG at the dot width (ImageSharp + real
  QR) — `GET /api/v1/sales/{id}/receipt/preview.png` (+ returns), `?paper=58`. `ReceiptOutputService`
  ties build→print→preview; checkout + returns print via the register's profile (Null in dev). The
  Corebalt mark (`wwwroot/assets/corebalt-mark-black-mono.png`, `BrandAssets`) prints ONLY in the
  Powered-by footer. Only the final on-device acceptance check remains. Shared `ReceiptText` helpers.
  Each `Register` (per store; UUIDv7 + Number/Name, auto-numbered "Lane N" on first checkout) is captured
  on the `Sale` at sale time, so the receipt prints the lane label ("Till: Lane 1") not the GUID and
  reprints read the captured value. The client logo is a graphic — never the literal "[LOGO]" (omitted
  from the text + HTML receipts; rendered at the top in the image preview + ESC/POS, or cleanly absent).
- **Vendor/tenant model (per-client installs):** Corebalt is the VENDOR; each retailer is a TENANT
  (one tenant per on-prem install, `TenantId` everywhere so it consolidates into shared cloud later).
  `MerchantProfile` (DB, `Pos.Domain/Tenancy`) is the CLIENT's identity (legal/trading name, KRA PIN,
  VAT status, contacts, currency, branches, logo, receipt footer) — the receipt header reads THIS, never
  Corebalt's; an optional "Powered by Corebalt POS" footer is the only vendor mark. Per-tenant
  `MpesaSettings`/`EtimsSettings` (secrets encrypted at rest via `ISecretProtector` →
  `DataProtectionSecretProtector`, the install-level Data Protection key ring + an EF value converter) —
  the Daraja client (`MpesaSettingsResolver`) and fiscalization read them PER TENANT, not appsettings;
  editable in back-office **Settings** (Manager). **Entitlements are VENDOR-controlled:** `Entitlements`
  (Edition + `Feature` flags + limits + ValidUntil) come ONLY from a Corebalt-signed **licence key**
  (ECDSA P-256; `Pos.Application/Licensing` — the app embeds the PUBLIC key and only VERIFIES, never
  signs). The client APPLIES a key (setup or Settings) but cannot edit flags/limits; `EntitlementsService`
  re-verifies the signed key on every read (signature + expiry + tenant) and derives features from the
  PAYLOAD — so editing the DB columns grants nothing. Invalid/expired/wrong-tenant key → the unlicensed
  baseline (Retail, no features). `IEntitlements` gates modules (e.g. `POST /api/v1/branches` needs
  MultiBranch → else 403); `POST /api/v1/license` applies a key. **First-run wizard** at `/setup`
  (anonymous until provisioned; `SetupRedirectMiddleware` routes a fresh install there) provisions profile
  + settings + entitlements (from a licence key, or the baseline) + the first manager via `SetupService`;
  transacting is blocked by `ISetupGuard` until complete. No client-specific values hardcoded.
- **Returns / voids / refunds (NEVER mutate a completed sale):** a `CreditNote` aggregate (+ owned
  `CreditNoteLine`s) is a NEW immutable transaction referencing the original sale — UUIDv7 (client-
  generated for offline-replay idempotency), tenant+store scoped, store-authoritative number
  "MB-CN-000124". Holds returned lines (original unit price + tax, VAT backed out), a `ReturnReason`
  (Damaged/WrongItem/CustomerChangedMind/CashierError), the authorizing user, and the refund
  (`RefundMethod` Cash/Mpesa/Other + `RefundStatus`: Cash = Refunded now; M-Pesa/Other = PendingManual,
  NEVER auto-reversed). A **void** = a full-quantity return (no separate concept). `ReturnService`
  validates the **over-return guard** (sum(prior + this) ≤ sold), writes reversing **IN** `StockMovement`
  rows + the credit note in one transaction (on-hand stays SUM of movements), then fiscalizes via
  `IFiscalizationProvider.SignCreditNoteAsync` (stub: CUIN "TEST-CN-…", references the original CUIN).
  `POST /api/v1/sales/{id}/returns` (Supervisor+; Cashier → 403) and `GET /api/v1/returns/{id}/receipt`
  → a "CREDIT NOTE / REFUND" receipt (negative qty/amounts, original receipt referenced) via the reused
  renderer (`ReceiptModel.FromCreditNote`).
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
- **Authentication & identity (lightweight custom JWT — NOT ASP.NET Core Identity):** a `User`
  aggregate (tenant+store scoped; Name/Username/StaffCode/PinHash/PasswordHash/Role/IsActive,
  unique `(TenantId,Username)`); PIN/password stored only as hashes via `IPasswordHasher`
  (`PasswordHasher<>`, PBKDF2). `POST /api/v1/auth/pin-login` (StaffCode+PIN, fast till) and
  `/auth/login` (username+password, back office) return a JWT carrying tenant/store/user/role/name/
  staff-code, signed with a local store-server HMAC key from config (`Jwt:Key`). JWT bearer auth +
  role policies: **Manager** for back-office product/stock writes, any authenticated (Cashier+) for
  checkout. `ICurrentContext` reads the claims (`ClaimsCurrentContext`); the cashier's real name +
  staff code are stamped on the `Sale` at checkout and printed on the receipt. The store-server's
  tenant/store come from `StoreServer` config; a bootstrap **Manager** is seeded on first run
  (`Auth:Bootstrap`, must-change-password). **Dev bypass** (`Auth:AllowDevHeaders=true`, off by
  default, never in Production): `DevHeaderAuthMiddleware` turns `X-Tenant/Store/User-Id` headers into
  a Manager principal — used by the tests + local curling. `GET /healthz` + the M-Pesa callback are
  the only anonymous routes. Domain-rule violations surface as 409, argument validation as 400.
- **Back-office (Blazor Server, manager-gated, hosted IN `Pos.Api`):** static-SSR Razor Components
  (`Components/Pages` — Products / Stock / Cashiers, + Login / ChangePassword) over **cookie** auth
  (scheme `Cookies`, separate from the API's JWT). Login via username+password (`AuthService.
  ValidatePasswordAsync`); pages gated by the `BackOfficeManager` policy (cookie + Manager role) →
  non-managers bounce to `/login`; bootstrap manager's force-password-change still applies. Forms post
  to `/backoffice/*` endpoints (`BackOffice/BackOfficeEndpoints.cs`, antiforgery on) that call the SAME
  `ProductService`/`StockService`/`AuthService` as the API — NO duplicated logic. Brand: navy #16223f /
  accent #4D8BFF, corebalt favicon + logo (`wwwroot/`). It's one on-prem deployable; can be split later.
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
