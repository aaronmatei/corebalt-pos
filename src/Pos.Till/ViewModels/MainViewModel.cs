using System.Collections.ObjectModel;
using System.Collections.Specialized;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Pos.Till.Api;
using Pos.Till.Scanning;
using Pos.Till.Services.Local;

namespace Pos.Till.ViewModels;

/// <summary>
/// The single till screen. Strictly presentation + orchestration: it sequences API calls and
/// shapes data for the view. It computes a client-side subtotal/change PREVIEW for responsiveness,
/// but the server's checkout response is treated as the authoritative total and change.
/// </summary>
public partial class MainViewModel : ObservableObject
{
    private readonly IPosApiClient _api;
    private readonly TillOptions _options;
    private readonly LocalStore _local;
    private readonly Connectivity _net;

    // Full catalogue; FilteredProducts is the search- and category-narrowed view bound by the list.
    public ObservableCollection<ProductRowViewModel> Products { get; } = new();
    public ObservableCollection<CartLineViewModel> Cart { get; } = new();

    /// <summary>Category filter options shown above the catalogue (All / Uncategorized / each category).</summary>
    public ObservableCollection<CategoryFilterViewModel> Categories { get; } = new();

    [ObservableProperty] private string _searchText = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(FilteredProducts))]
    private CategoryFilterViewModel? _selectedCategory;

    [ObservableProperty] private string _barcodeText = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasPending), nameof(PendingHeader), nameof(PendingQuantityLabel))]
    private ProductRowViewModel? _selectedProduct;

    [ObservableProperty] private ProductRowViewModel? _pendingProduct;
    [ObservableProperty] private decimal _pendingQuantity = 1m;

    [ObservableProperty] private CartLineViewModel? _selectedCartLine;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(TenderedTotal), nameof(ChangePreview))]
    [NotifyCanExecuteChangedFor(nameof(CompleteSaleCommand), nameof(PayWithMpesaCommand))]
    private decimal _cashAmount;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(TenderedTotal), nameof(ChangePreview))]
    [NotifyCanExecuteChangedFor(nameof(PayWithMpesaCommand))]
    private decimal _mpesaAmount;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(PayWithMpesaCommand))]
    private string _mpesaPhone = "";

    // Set while an STK push is awaiting the customer's PIN; gates the other actions and shows the
    // waiting UI. Distinct from IsBusy so the Cancel button stays live during the (long) poll.
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(PayWithMpesaCommand), nameof(CompleteSaleCommand),
        nameof(CancelMpesaCommand), nameof(RefreshCommand), nameof(LookupBarcodeCommand))]
    private bool _mpesaInProgress;

    // ── Loyalty customer attached to the sale (optional; null = walk-in) ──
    [ObservableProperty] private string _customerPhone = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasCustomer), nameof(CustomerLabel))]
    private CustomerDto? _attachedCustomer;

    public bool HasCustomer => AttachedCustomer is not null;
    public string CustomerLabel => AttachedCustomer is null ? "" : $"{AttachedCustomer.Name} · {AttachedCustomer.LoyaltyPoints} pts";

    [ObservableProperty] private bool _isBusy;
    [ObservableProperty] private string _statusMessage = "Starting…";
    [ObservableProperty] private string? _lastSaleSummary;
    [ObservableProperty] private string? _receiptText;

    /// <summary>The signed-in cashier (staff code), shown in the header.</summary>
    [ObservableProperty] private string _cashierLabel = "";

    /// <summary>Raised by "Lock / switch cashier" — the shell returns to the login screen.</summary>
    public event Action? LockRequested;

    [RelayCommand]
    private void Lock()
    {
        _mpesaCts?.Cancel();
        _drainCts?.Cancel();              // stop the offline drain loop for this cashier's session
        _net.Changed -= OnConnectivityChanged;
        LockRequested?.Invoke();
    }

    private CancellationTokenSource? _mpesaCts;

    public MainViewModel(IPosApiClient api, TillOptions options, LocalStore local, Connectivity net)
    {
        _api = api;
        _options = options;
        _local = local;
        _net = net;
        _isOnline = net.IsOnline;
        _net.Changed += OnConnectivityChanged;
        Cart.CollectionChanged += OnCartChanged;
    }

    // ── Derived (preview) values ──────────────────────────────────────────────────────────
    public IEnumerable<ProductRowViewModel> FilteredProducts
    {
        get
        {
            IEnumerable<ProductRowViewModel> items = Products;
            if (SelectedCategory is { IsAll: false } cat)
                items = items.Where(cat.Matches);
            if (!string.IsNullOrWhiteSpace(SearchText))
                items = items.Where(p => p.Matches(SearchText.Trim()));
            return items;
        }
    }

    public decimal Subtotal => Cart.Sum(l => l.LineTotal);
    public decimal TenderedTotal => CashAmount + MpesaAmount;
    public decimal ChangePreview => TenderedTotal > Subtotal ? TenderedTotal - Subtotal : 0m;

    public string SubtotalDisplay => $"{_options.Currency} {Subtotal:0.00}";
    public string ChangePreviewDisplay => $"{_options.Currency} {ChangePreview:0.00}";

    public bool HasPending => PendingProduct is not null;
    public string PendingHeader => PendingProduct is null ? "" : $"{PendingProduct.Name}  ({PendingProduct.PriceDisplay})";
    public string PendingQuantityLabel => PendingProduct?.IsWeighed == true ? "Weight (kg)" : "Quantity";

    // ── Change reactions ──────────────────────────────────────────────────────────────────
    partial void OnSearchTextChanged(string value) => OnPropertyChanged(nameof(FilteredProducts));

    partial void OnSelectedProductChanged(ProductRowViewModel? value)
    {
        if (value is not null) BeginAdd(value);
    }

    partial void OnPendingProductChanged(ProductRowViewModel? value)
    {
        OnPropertyChanged(nameof(HasPending));
        OnPropertyChanged(nameof(PendingHeader));
        OnPropertyChanged(nameof(PendingQuantityLabel));
        ConfirmAddCommand.NotifyCanExecuteChanged();
    }

    partial void OnPendingQuantityChanged(decimal value) => ConfirmAddCommand.NotifyCanExecuteChanged();

    partial void OnIsBusyChanged(bool value)
    {
        RefreshCommand.NotifyCanExecuteChanged();
        LookupBarcodeCommand.NotifyCanExecuteChanged();
        CompleteSaleCommand.NotifyCanExecuteChanged();
    }

    private void OnCartChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        OnPropertyChanged(nameof(Subtotal));
        OnPropertyChanged(nameof(SubtotalDisplay));
        OnPropertyChanged(nameof(ChangePreview));
        OnPropertyChanged(nameof(ChangePreviewDisplay));
        CompleteSaleCommand.NotifyCanExecuteChanged();
        PayWithMpesaCommand.NotifyCanExecuteChanged();
    }

    // ── Lifecycle ─────────────────────────────────────────────────────────────────────────
    public async Task InitializeAsync()
    {
        QueuedCount = await _local.QueuedCountAsync(); // surface any sales left queued from a prior run
        await LoadCategoriesAsync();
        await RefreshAsync();
        await LoadShiftAsync(); // gate selling on an open shift; prompt to open one if none
        StartDrainLoop();       // replay queued offline sales as soon as the server is reachable
    }

    [RelayCommand(CanExecute = nameof(NotBusy))]
    private async Task RefreshAsync()
    {
        await RunBusy(async () =>
        {
            var result = await _api.ListProductsAsync();
            if (result.Ok)
            {
                _net.Report(true);
                await _local.CacheProductsAsync(result.Value!); // refresh the offline catalogue
                ShowProducts(result.Value!);
                StatusMessage = $"Loaded {Products.Count} product(s) from {_options.BaseUrl}.";
                return;
            }

            // A transport failure (StatusCode 0 = server unreachable) means we're offline: fall back to the
            // last cached catalogue so the cashier can keep selling. A real HTTP error is shown as-is.
            if (result.StatusCode == 0)
            {
                _net.Report(false);
                var cached = await _local.GetCachedProductsAsync();
                ShowProducts(cached);
                StatusMessage = cached.Count > 0
                    ? $"Offline — showing {cached.Count} cached product(s). Sales will sync when reconnected."
                    : "Offline and no cached catalogue yet. Connect once to download products.";
            }
            else
            {
                StatusMessage = result.Error ?? "Failed to load products.";
            }
        });
    }

    private void ShowProducts(IReadOnlyList<ProductDto> products)
    {
        Products.Clear();
        foreach (var p in products) Products.Add(new ProductRowViewModel(p));
        OnPropertyChanged(nameof(FilteredProducts));
    }

    /// <summary>Load the active categories into the catalogue filter (All + Uncategorized + each).
    /// Best-effort: a failure just leaves the default "All" filter so selling is never blocked.</summary>
    private async Task LoadCategoriesAsync()
    {
        var result = await _api.ListCategoriesAsync();
        Categories.Clear();
        Categories.Add(CategoryFilterViewModel.All);
        Categories.Add(CategoryFilterViewModel.Uncategorized);
        if (result.Ok)
            foreach (var c in result.Value!.Where(c => c.IsActive))
                Categories.Add(CategoryFilterViewModel.For(c));
        SelectedCategory = CategoryFilterViewModel.All;
    }

    // ── Scanning ──────────────────────────────────────────────────────────────────────────
    [RelayCommand(CanExecute = nameof(NotBusy))]
    private async Task LookupBarcodeAsync()
    {
        var scan = ScannedCode.Parse(BarcodeText);
        if (scan.Kind == ScanKind.Unknown && scan.Raw.Length == 0)
        {
            StatusMessage = "Enter or scan a barcode first.";
            return;
        }

        // Scale labels (price-embedded EAN-13) carry weight/price, not a catalogue barcode.
        // Full PLU+weight decoding lands with the scales feature; for now we flag and stop so we
        // don't fire a GTIN lookup that can only 404.
        if (scan.Kind == ScanKind.PriceEmbeddedEan13)
        {
            StatusMessage = $"Price-embedded scale label (PLU {scan.EmbeddedItemCode}). " +
                            "Weighed-goods decoding arrives with scales support — add the item from the list for now.";
            BarcodeText = "";
            return;
        }

        await RunBusy(async () =>
        {
            var result = await _api.FindByBarcodeAsync(scan.Raw);
            if (result.Ok)
            {
                _net.Report(true);
                BeginAdd(new ProductRowViewModel(result.Value!));
                StatusMessage = $"Found {result.Value!.Name}.";
            }
            else if (result.StatusCode == 0)
            {
                // Offline — resolve from the cached catalogue instead.
                _net.Report(false);
                var cached = await _local.FindByBarcodeAsync(scan.Raw);
                if (cached is not null)
                {
                    BeginAdd(new ProductRowViewModel(cached));
                    StatusMessage = $"Found {cached.Name} (offline).";
                }
                else
                {
                    StatusMessage = $"Offline — barcode {scan.Raw} isn't in the cached catalogue.";
                }
            }
            else
            {
                StatusMessage = result.StatusCode == 404
                    ? $"No product with barcode {scan.Raw}."
                    : result.Error ?? "Lookup failed.";
            }
            BarcodeText = "";
        });
    }

    // ── Add-to-cart (weight vs quantity prompt via the inline panel) ───────────────────────
    private void BeginAdd(ProductRowViewModel product)
    {
        PendingProduct = product;
        PendingQuantity = product.IsWeighed ? 0m : 1m; // weighed items start blank — cashier keys the weight
    }

    private bool CanConfirmAdd() => PendingProduct is not null && PendingQuantity > 0m;

    [RelayCommand(CanExecute = nameof(CanConfirmAdd))]
    private void ConfirmAdd()
    {
        if (PendingProduct is null || PendingQuantity <= 0m) return;
        Cart.Add(new CartLineViewModel(PendingProduct, PendingQuantity));
        StatusMessage = $"Added {PendingProduct.Name}.";
        CancelPending();
    }

    [RelayCommand]
    private void CancelPending()
    {
        PendingProduct = null;
        PendingQuantity = 1m;
        SelectedProduct = null;
    }

    [RelayCommand]
    private void RemoveSelectedLine()
    {
        if (SelectedCartLine is not null) Cart.Remove(SelectedCartLine);
    }

    [RelayCommand]
    private void ClearSale()
    {
        _mpesaCts?.Cancel();
        Cart.Clear();
        CashAmount = 0m;
        MpesaAmount = 0m;
        MpesaPhone = "";
        ReceiptText = null;
        CancelPending();
        StatusMessage = "Sale cleared.";
    }

    // ── Loyalty customer (attach by phone) ──────────────────────────────────────────────────
    [RelayCommand(CanExecute = nameof(NotBusy))]
    private async Task AttachCustomerAsync()
    {
        if (string.IsNullOrWhiteSpace(CustomerPhone)) { StatusMessage = "Enter the customer's phone first."; return; }
        await RunBusy(async () =>
        {
            var result = await _api.FindCustomerByPhoneAsync(CustomerPhone.Trim());
            if (result.Ok)
            {
                _net.Report(true);
                AttachedCustomer = result.Value;
                StatusMessage = $"Customer {result.Value!.Name} attached ({result.Value.LoyaltyPoints} pts).";
            }
            else if (result.StatusCode == 404) StatusMessage = $"No customer with phone {CustomerPhone.Trim()}.";
            else if (result.StatusCode == 0) StatusMessage = "Can't look up customers while offline.";
            else StatusMessage = result.Error ?? "Lookup failed.";
        });
    }

    [RelayCommand]
    private void DetachCustomer()
    {
        AttachedCustomer = null;
        CustomerPhone = "";
    }

    // ── Cash checkout (synchronous) ─────────────────────────────────────────────────────────
    private bool CanComplete() => !IsBusy && !MpesaInProgress && Cart.Count > 0 && HasOpenShift;

    [RelayCommand(CanExecute = nameof(CanComplete))]
    private async Task CompleteSaleAsync()
    {
        // Cash-only path. M-Pesa is asynchronous and goes through PayWithMpesa, never here.
        var lines = Cart.Select(l => new CheckoutLineDto(l.ProductId, l.Quantity)).ToList();
        var tenders = new List<CheckoutTenderDto>();
        if (CashAmount > 0m) tenders.Add(new CheckoutTenderDto(TenderType.Cash, CashAmount, null));

        // Stamp the sale with an edge-generated UUIDv7 NOW, before sending. If the server is unreachable the
        // same id lets us queue the sale and replay it idempotently on reconnect (no double-charge).
        var saleId = Guid.CreateVersion7();
        var subtotal = Subtotal;
        var request = new CheckoutRequestDto(_options.RegisterId, lines, tenders, _options.Currency, saleId, AttachedCustomer?.Id);

        await RunBusy(async () =>
        {
            // Already known to be offline → don't even attempt; queue straight away.
            if (!_net.IsOnline)
            {
                await QueueOfflineSaleAsync(request, subtotal);
                return;
            }

            var result = await _api.CheckoutAsync(request);
            if (result.Ok)
            {
                _net.Report(true);
                var sale = result.Value!;
                // Server response is authoritative — display ITS total/change, not the preview.
                LastSaleSummary =
                    $"✔ Sale {sale.SaleId}\n" +
                    $"   Total  {sale.Currency} {sale.Total:0.00}\n" +
                    $"   Change {sale.Currency} {sale.ChangeDue:0.00}";
                StatusMessage = "Cash sale completed.";
                await ShowReceiptAsync(sale.SaleId);
                ClearSaleAfterSuccess();
            }
            else if (result.StatusCode == 0)
            {
                // The server dropped mid-checkout — fall back to the offline queue rather than losing the sale.
                _net.Report(false);
                await QueueOfflineSaleAsync(request, subtotal);
            }
            else
            {
                // A real rejection (e.g. 409 no open shift, 400 bad request) — surface it, never queue it.
                StatusMessage = $"Checkout failed ({result.StatusCode}): {result.Error}";
            }
        });
    }

    // ── M-Pesa checkout (asynchronous STK push → poll) ──────────────────────────────────────
    private bool CanPayWithMpesa() =>
        !IsBusy && !MpesaInProgress && Cart.Count > 0 && MpesaAmount > 0m && !string.IsNullOrWhiteSpace(MpesaPhone)
        && HasOpenShift && IsOnline; // STK push can't work offline — cash only when disconnected

    [RelayCommand(CanExecute = nameof(CanPayWithMpesa))]
    private async Task PayWithMpesaAsync()
    {
        var lines = Cart.Select(l => new CheckoutLineDto(l.ProductId, l.Quantity)).ToList();
        var cash = new List<CheckoutTenderDto>();
        if (CashAmount > 0m) cash.Add(new CheckoutTenderDto(TenderType.Cash, CashAmount, null)); // split payment

        var request = new MpesaCheckoutRequestDto(
            _options.RegisterId, lines, MpesaAmount, MpesaPhone.Trim(),
            AccountReference: null, CashTenders: cash, Currency: _options.Currency);

        MpesaInProgress = true;
        StatusMessage = "Sending M-Pesa STK push…";
        try
        {
            var init = await _api.InitiateMpesaAsync(request);
            if (!init.Ok)
            {
                StatusMessage = $"M-Pesa could not start ({init.StatusCode}): {init.Error}";
                return;
            }
            if (!string.Equals(init.Value!.Status, "Pending", StringComparison.OrdinalIgnoreCase))
            {
                StatusMessage = $"M-Pesa rejected: {init.Value.Message}. Retry or use cash.";
                return;
            }

            StatusMessage = "Waiting for customer to enter M-Pesa PIN…";
            await PollMpesaAsync(init.Value.SaleId);
        }
        catch (Exception ex)
        {
            StatusMessage = $"Unexpected error: {ex.Message}";
        }
        finally
        {
            MpesaInProgress = false;
        }
    }

    private async Task PollMpesaAsync(Guid saleId)
    {
        _mpesaCts?.Dispose();
        _mpesaCts = new CancellationTokenSource();
        var ct = _mpesaCts.Token;

        const int maxPolls = 30; // ~60s at 2s intervals
        for (var i = 0; i < maxPolls; i++)
        {
            try { await Task.Delay(TimeSpan.FromSeconds(2), ct); }
            catch (TaskCanceledException)
            {
                StatusMessage = "Stopped waiting. Query again later or switch to cash.";
                return;
            }

            var status = await _api.GetMpesaStatusAsync(saleId, ct);
            if (!status.Ok) { StatusMessage = $"Status check failed: {status.Error}"; continue; }

            var s = status.Value!;
            switch (s.PaymentStatus)
            {
                case "Confirmed":
                    LastSaleSummary =
                        $"✔ M-Pesa sale {s.SaleId}\n" +
                        $"   Total   {s.Currency} {s.Total:0.00}\n" +
                        $"   Change  {s.Currency} {s.ChangeDue:0.00}\n" +
                        $"   Receipt {s.Receipt ?? "(pending callback)"}";
                    StatusMessage = "M-Pesa confirmed — sale completed.";
                    await ShowReceiptAsync(s.SaleId);
                    ClearSaleAfterSuccess();
                    return;
                case "Failed":
                    StatusMessage = $"M-Pesa failed: {s.ResultDescription ?? "cancelled or declined"}. Retry or use cash.";
                    return;
                default:
                    StatusMessage = $"Waiting for customer to enter M-Pesa PIN… ({(i + 1) * 2}s)";
                    break;
            }
        }

        StatusMessage = "M-Pesa timed out. Query again later or switch to cash.";
    }

    private bool CanCancelMpesa() => MpesaInProgress;

    [RelayCommand(CanExecute = nameof(CanCancelMpesa))]
    private void CancelMpesa() => _mpesaCts?.Cancel();

    /// <summary>Fetch the rendered receipt for a completed sale and show it in the till.</summary>
    private async Task ShowReceiptAsync(Guid saleId)
    {
        var r = await _api.GetReceiptAsync(saleId);
        ReceiptText = r.Ok ? r.Value!.Text : $"(receipt unavailable: {r.Error})";
    }

    private void ClearSaleAfterSuccess()
    {
        Cart.Clear();
        CashAmount = 0m;
        MpesaAmount = 0m;
        MpesaPhone = "";
        AttachedCustomer = null;
        CustomerPhone = "";
        CancelPending();
    }

    // ── helpers ───────────────────────────────────────────────────────────────────────────
    private bool NotBusy() => !IsBusy && !MpesaInProgress;

    private async Task RunBusy(Func<Task> action)
    {
        if (IsBusy) return;
        IsBusy = true;
        try { await action(); }
        catch (Exception ex) { StatusMessage = $"Unexpected error: {ex.Message}"; }
        finally { IsBusy = false; }
    }
}
