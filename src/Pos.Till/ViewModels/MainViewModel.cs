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
    private decimal _cashAmount;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(TenderedTotal), nameof(ChangePreview))]
    private decimal _mpesaAmount;

    [ObservableProperty] private string _mpesaReference = "";

    [ObservableProperty] private bool _isBusy;
    [ObservableProperty] private string _statusMessage = "Starting…";
    [ObservableProperty] private string? _lastSaleSummary;

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
        Cart.Clear();
        CashAmount = 0m;
        MpesaAmount = 0m;
        MpesaReference = "";
        CancelPending();
        StatusMessage = "Sale cleared.";
    }

    // ── Checkout ──────────────────────────────────────────────────────────────────────────
    private bool CanComplete() => !IsBusy && Cart.Count > 0;

    [RelayCommand(CanExecute = nameof(CanComplete))]
    private async Task CompleteSaleAsync()
    {
        var lines = Cart.Select(l => new CheckoutLineDto(l.ProductId, l.Quantity)).ToList();

        var tenders = new List<CheckoutTenderDto>();
        if (CashAmount > 0m) tenders.Add(new CheckoutTenderDto(TenderType.Cash, CashAmount, null));
        if (MpesaAmount > 0m)
            tenders.Add(new CheckoutTenderDto(TenderType.Mpesa, MpesaAmount,
                string.IsNullOrWhiteSpace(MpesaReference) ? null : MpesaReference.Trim()));

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
                StatusMessage = "Sale completed.";
                ClearSaleAfterSuccess();
            }
            else
            {
                StatusMessage = $"Checkout failed ({result.StatusCode}): {result.Error}";
            }
        });
    }

    private void ClearSaleAfterSuccess()
    {
        Cart.Clear();
        CashAmount = 0m;
        MpesaAmount = 0m;
        MpesaReference = "";
        CancelPending();
    }

    // ── helpers ───────────────────────────────────────────────────────────────────────────
    private bool NotBusy() => !IsBusy;

    private async Task RunBusy(Func<Task> action)
    {
        if (IsBusy) return;
        IsBusy = true;
        try { await action(); }
        catch (Exception ex) { StatusMessage = $"Unexpected error: {ex.Message}"; }
        finally { IsBusy = false; }
    }
}
