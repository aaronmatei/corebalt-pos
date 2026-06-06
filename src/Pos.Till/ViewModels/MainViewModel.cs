using System.Collections.ObjectModel;
using System.Collections.Specialized;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Pos.Till.Api;
using Pos.Till.Scanning;

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

    // Full catalogue; FilteredProducts is the search-narrowed view bound by the list.
    public ObservableCollection<ProductRowViewModel> Products { get; } = new();
    public ObservableCollection<CartLineViewModel> Cart { get; } = new();

    [ObservableProperty] private string _searchText = "";
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

    [ObservableProperty] private bool _isBusy;
    [ObservableProperty] private string _statusMessage = "Starting…";
    [ObservableProperty] private string? _lastSaleSummary;

    private CancellationTokenSource? _mpesaCts;

    public MainViewModel(IPosApiClient api, TillOptions options)
    {
        _api = api;
        _options = options;
        Cart.CollectionChanged += OnCartChanged;
    }

    // ── Derived (preview) values ──────────────────────────────────────────────────────────
    public IEnumerable<ProductRowViewModel> FilteredProducts =>
        string.IsNullOrWhiteSpace(SearchText)
            ? Products
            : Products.Where(p => p.Matches(SearchText.Trim()));

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
    public async Task InitializeAsync() => await RefreshAsync();

    [RelayCommand(CanExecute = nameof(NotBusy))]
    private async Task RefreshAsync()
    {
        await RunBusy(async () =>
        {
            var result = await _api.ListProductsAsync();
            if (!result.Ok) { StatusMessage = result.Error ?? "Failed to load products."; return; }

            Products.Clear();
            foreach (var p in result.Value!) Products.Add(new ProductRowViewModel(p));
            OnPropertyChanged(nameof(FilteredProducts));
            StatusMessage = $"Loaded {Products.Count} product(s) from {_options.BaseUrl}.";
        });
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
                var row = new ProductRowViewModel(result.Value!);
                BeginAdd(row);
                StatusMessage = $"Found {row.Name}.";
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
        CancelPending();
        StatusMessage = "Sale cleared.";
    }

    // ── Cash checkout (synchronous) ─────────────────────────────────────────────────────────
    private bool CanComplete() => !IsBusy && !MpesaInProgress && Cart.Count > 0;

    [RelayCommand(CanExecute = nameof(CanComplete))]
    private async Task CompleteSaleAsync()
    {
        // Cash-only path. M-Pesa is asynchronous and goes through PayWithMpesa, never here.
        var lines = Cart.Select(l => new CheckoutLineDto(l.ProductId, l.Quantity)).ToList();
        var tenders = new List<CheckoutTenderDto>();
        if (CashAmount > 0m) tenders.Add(new CheckoutTenderDto(TenderType.Cash, CashAmount, null));

        var request = new CheckoutRequestDto(_options.RegisterId, lines, tenders, _options.Currency);

        await RunBusy(async () =>
        {
            var result = await _api.CheckoutAsync(request);
            if (result.Ok)
            {
                var sale = result.Value!;
                // Server response is authoritative — display ITS total/change, not the preview.
                LastSaleSummary =
                    $"✔ Sale {sale.SaleId}\n" +
                    $"   Total  {sale.Currency} {sale.Total:0.00}\n" +
                    $"   Change {sale.Currency} {sale.ChangeDue:0.00}";
                StatusMessage = "Cash sale completed.";
                ClearSaleAfterSuccess();
            }
            else
            {
                StatusMessage = $"Checkout failed ({result.StatusCode}): {result.Error}";
            }
        });
    }

    // ── M-Pesa checkout (asynchronous STK push → poll) ──────────────────────────────────────
    private bool CanPayWithMpesa() =>
        !IsBusy && !MpesaInProgress && Cart.Count > 0 && MpesaAmount > 0m && !string.IsNullOrWhiteSpace(MpesaPhone);

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

    private void ClearSaleAfterSuccess()
    {
        Cart.Clear();
        CashAmount = 0m;
        MpesaAmount = 0m;
        MpesaPhone = "";
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
