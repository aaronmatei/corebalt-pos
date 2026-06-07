using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Pos.Till.Api;

namespace Pos.Till.ViewModels;

/// <summary>
/// Cash-shift wiring for the till: open a shift (opening float) before selling, run drawer movements
/// (Supervisor-PIN gated), pull an X report, and close (cash-up → Z). Selling stays disabled until a
/// shift is open; after close the till returns to the open-shift prompt.
/// </summary>
public partial class MainViewModel
{
    // ── Shift state ───────────────────────────────────────────────────────────────────────────
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(CompleteSaleCommand), nameof(PayWithMpesaCommand),
        nameof(BeginDrawerCommand), nameof(BeginCloseCommand), nameof(ShowXReportCommand))]
    [NotifyPropertyChangedFor(nameof(AnyOverlay), nameof(ShowOpenShiftPanel))]
    private bool _hasOpenShift;

    private Guid _sessionId;

    [ObservableProperty] private string _shiftIndicator = "No shift open";
    [ObservableProperty] private decimal _openingFloat;

    // Mutually exclusive overlay panels (report wins, then drawer/close, then the open-shift prompt).
    public bool ShowOpenShiftPanel => !HasOpenShift && !ShowReport && !ShowDrawer && !ShowClose;
    public bool AnyOverlay => ShowOpenShiftPanel || ShowDrawer || ShowClose || ShowReport;

    // ── Drawer movement panel ───────────────────────────────────────────────────────────────────
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(AnyOverlay), nameof(ShowOpenShiftPanel))] private bool _showDrawer;
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(DrawerTitle))] private string _drawerKind = "PayIn";
    [ObservableProperty] private decimal _drawerAmount;
    [ObservableProperty] private string _drawerReason = "";
    [ObservableProperty] private string _supervisorStaff = "";
    [ObservableProperty] private string _supervisorPin = "";

    public string DrawerTitle => DrawerKind switch { "PayOut" => "Pay out", "Drop" => "Cash drop", _ => "Pay in" };

    // ── Close panel ─────────────────────────────────────────────────────────────────────────────
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(AnyOverlay), nameof(ShowOpenShiftPanel))] private bool _showClose;
    [ObservableProperty] private decimal _expectedCash;
    [ObservableProperty] private decimal _countedCash;
    [ObservableProperty] private bool _needsManagerAck;
    [ObservableProperty] private string _managerStaff = "";
    [ObservableProperty] private string _managerPin = "";

    public string ExpectedCashDisplay => $"{_options.Currency} {ExpectedCash:0.00}";
    public string CountedVarianceDisplay
    {
        get
        {
            var v = CountedCash - ExpectedCash;
            var tag = v == 0 ? "" : v > 0 ? " (OVER)" : " (SHORT)";
            return $"{_options.Currency} {v:0.00}{tag}";
        }
    }

    partial void OnExpectedCashChanged(decimal value) => OnPropertyChanged(nameof(ExpectedCashDisplay));
    partial void OnCountedCashChanged(decimal value) => OnPropertyChanged(nameof(CountedVarianceDisplay));

    // ── Report panel (X or Z) ─────────────────────────────────────────────────────────────────
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(AnyOverlay), nameof(ShowOpenShiftPanel))] private bool _showReport;
    [ObservableProperty] private string? _reportText;

    // ── Load / open ─────────────────────────────────────────────────────────────────────────────
    private async Task LoadShiftAsync()
    {
        var result = await _api.GetCurrentSessionAsync(_options.RegisterId);
        if (result.Ok) ApplyOpenSession(result.Value!);
        else if (result.StatusCode == 404) SetNoShift("No shift open — enter the opening float to start selling.");
        else StatusMessage = result.Error ?? "Could not check the shift.";
    }

    [RelayCommand]
    private async Task OpenShiftAsync()
    {
        await RunBusy(async () =>
        {
            var result = await _api.OpenSessionAsync(new OpenSessionRequestDto(_options.RegisterId, OpeningFloat));
            if (result.Ok)
            {
                ApplyOpenSession(result.Value!);
                StatusMessage = $"Shift opened with {_options.Currency} {OpeningFloat:0.00} float.";
                OpeningFloat = 0m;
            }
            else
            {
                StatusMessage = $"Could not open the shift ({result.StatusCode}): {result.Error}";
            }
        });
    }

    private void ApplyOpenSession(SessionDto s)
    {
        _sessionId = s.Id;
        HasOpenShift = true;
        var time = s.OpenedAtEat.Length >= 16 ? s.OpenedAtEat[11..16] : s.OpenedAtEat; // HH:mm
        ShiftIndicator = $"Shift open · {s.RegisterLabel} · opened {time}";
    }

    private void SetNoShift(string message)
    {
        HasOpenShift = false;
        _sessionId = Guid.Empty;
        ShiftIndicator = "No shift open";
        StatusMessage = message;
    }

    // ── Drawer movements (Supervisor+) ──────────────────────────────────────────────────────────
    [RelayCommand(CanExecute = nameof(HasOpenShift))]
    private void BeginDrawer(string kind)
    {
        DrawerKind = kind;
        DrawerAmount = 0m;
        DrawerReason = "";
        SupervisorStaff = "";
        SupervisorPin = "";
        ShowDrawer = true;
    }

    [RelayCommand]
    private void CancelDrawer() => ShowDrawer = false;

    [RelayCommand]
    private async Task ConfirmDrawerAsync()
    {
        if (DrawerAmount <= 0m) { StatusMessage = "Enter an amount greater than zero."; return; }
        if (string.IsNullOrWhiteSpace(SupervisorStaff) || string.IsNullOrWhiteSpace(SupervisorPin))
        {
            StatusMessage = "A supervisor PIN is required for drawer movements."; return;
        }

        await RunBusy(async () =>
        {
            // Supervisor override: log in as the supervisor for this one call (session token untouched).
            var login = await _api.PinLoginAsync(SupervisorStaff.Trim(), SupervisorPin.Trim());
            if (!login.Ok) { StatusMessage = "Supervisor sign-in failed — check the staff code + PIN."; return; }

            var request = new CashMovementRequestDto(_options.RegisterId, DrawerKind, DrawerAmount, DrawerReason.Trim());
            var result = await _api.RecordCashMovementAsync(request, bearerOverride: login.Value!.AccessToken);
            if (result.Ok)
            {
                StatusMessage = $"{DrawerTitle} {_options.Currency} {DrawerAmount:0.00} recorded.";
                ShowDrawer = false;
            }
            else
            {
                StatusMessage = result.StatusCode == 403
                    ? "That user is not a supervisor — drawer movements need Supervisor or above."
                    : $"Drawer movement failed ({result.StatusCode}): {result.Error}";
            }
        });
    }

    // ── X report ────────────────────────────────────────────────────────────────────────────────
    [RelayCommand(CanExecute = nameof(HasOpenShift))]
    private async Task ShowXReportAsync()
    {
        await RunBusy(async () =>
        {
            var result = await _api.GetSessionReportAsync(_sessionId);
            if (result.Ok) { ReportText = result.Value!.Text; ShowReport = true; }
            else StatusMessage = $"Could not fetch the X report ({result.StatusCode}): {result.Error}";
        });
    }

    [RelayCommand]
    private async Task PrintReportAsync()
    {
        var result = await _api.PrintSessionReportAsync(_sessionId);
        StatusMessage = result.Ok ? "Report sent to the printer." : $"Print failed: {result.Error}";
    }

    [RelayCommand]
    private void CloseReport() => ShowReport = false;

    // ── Close shift (cash-up → Z) ─────────────────────────────────────────────────────────────
    [RelayCommand(CanExecute = nameof(HasOpenShift))]
    private async Task BeginCloseAsync()
    {
        await RunBusy(async () =>
        {
            var report = await _api.GetSessionReportAsync(_sessionId);
            ExpectedCash = report.Ok ? report.Value!.Report.Cash.Expected : 0m;
            CountedCash = 0m;
            NeedsManagerAck = false;
            ManagerStaff = "";
            ManagerPin = "";
            ShowClose = true;
        });
    }

    [RelayCommand]
    private void CancelClose() => ShowClose = false;

    [RelayCommand]
    private async Task ConfirmCloseAsync()
    {
        await RunBusy(async () =>
        {
            string? managerToken = null;
            if (NeedsManagerAck)
            {
                if (string.IsNullOrWhiteSpace(ManagerStaff) || string.IsNullOrWhiteSpace(ManagerPin))
                {
                    StatusMessage = "A manager PIN is required to acknowledge this variance."; return;
                }
                var mgr = await _api.PinLoginAsync(ManagerStaff.Trim(), ManagerPin.Trim());
                if (!mgr.Ok) { StatusMessage = "Manager sign-in failed — check the staff code + PIN."; return; }
                managerToken = mgr.Value!.AccessToken;
            }

            var request = new CloseSessionRequestDto(CountedCash, Acknowledged: NeedsManagerAck);
            var result = await _api.CloseSessionAsync(_sessionId, request, bearerOverride: managerToken);

            if (result.Ok)
            {
                ReportText = result.Value!.Text; // the Z report (also printed server-side)
                ShowClose = false;
                ShowReport = true;
                SetNoShift("Shift closed. Open a new shift to keep selling.");
                return;
            }

            // 409 = variance beyond the threshold → a manager must acknowledge.
            if (result.StatusCode == 409 && !NeedsManagerAck)
            {
                NeedsManagerAck = true;
                StatusMessage = "Variance exceeds the threshold — a manager PIN is required to acknowledge the close.";
                return;
            }
            StatusMessage = $"Could not close the shift ({result.StatusCode}): {result.Error}";
        });
    }
}
