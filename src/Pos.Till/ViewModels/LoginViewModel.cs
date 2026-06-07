using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Pos.Till.Api;

namespace Pos.Till.ViewModels;

/// <summary>
/// Cashier sign-in. Two paths to the SAME session: StaffCode + PIN (always available), or an OPTIONAL
/// fingerprint scan (when a reader is present). On success the JWT is handed to the API client and
/// <see cref="LoggedIn"/> tells the shell to show the till. This screen is also the fast "switch cashier"
/// path mid-shift (the till's Lock returns here).
/// </summary>
public partial class LoginViewModel : ObservableObject
{
    private readonly IPosApiClient _api;
    private readonly IFingerprintScanner _scanner;

    public LoginViewModel(IPosApiClient api, IFingerprintScanner scanner)
    {
        _api = api;
        _scanner = scanner;
    }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(LoginCommand))]
    private string _staffCode = "";

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(LoginCommand))]
    private string _pin = "";

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(LoginCommand), nameof(ScanFingerprintCommand))]
    private bool _isBusy;

    [ObservableProperty] private string? _error;

    /// <summary>Fingerprint sign-in is only offered when a reader is available on this till.</summary>
    public bool FingerprintAvailable => _scanner.IsAvailable;

    /// <summary>Raised on a successful login with the staff code (shown in the till header).</summary>
    public event Action<string>? LoggedIn;

    private bool CanLogin() => !IsBusy && !string.IsNullOrWhiteSpace(StaffCode) && !string.IsNullOrWhiteSpace(Pin);

    [RelayCommand(CanExecute = nameof(CanLogin))]
    private async Task LoginAsync()
    {
        IsBusy = true;
        Error = null;
        try
        {
            var result = await _api.PinLoginAsync(StaffCode.Trim(), Pin.Trim());
            if (!result.Ok)
            {
                Error = result.StatusCode == 401 ? "Invalid staff code or PIN." : result.Error ?? "Login failed.";
                return;
            }

            _api.SetAccessToken(result.Value!.AccessToken);
            var staff = StaffCode.Trim();
            Pin = "";
            LoggedIn?.Invoke(staff);
        }
        finally
        {
            IsBusy = false;
        }
    }

    private bool CanScan() => !IsBusy && FingerprintAvailable;

    [RelayCommand(CanExecute = nameof(CanScan))]
    private async Task ScanFingerprintAsync()
    {
        IsBusy = true;
        Error = null;
        try
        {
            // Capture the probe at the reader (dev stub seeds from the staff-code box). Only the template
            // travels to the server; the match happens there and resolves to a cashier.
            var probe = await _scanner.CaptureAsync(StaffCode.Trim());
            if (probe is null || probe.Length == 0)
            {
                Error = "No fingerprint captured. Place your finger on the reader, or sign in with your PIN.";
                return;
            }

            var result = await _api.FingerprintLoginAsync(Convert.ToBase64String(probe));
            if (!result.Ok)
            {
                Error = result.StatusCode == 401
                    ? "Fingerprint not recognised. Try again, or sign in with your PIN."
                    : result.Error ?? "Login failed.";
                return;
            }

            _api.SetAccessToken(result.Value!.AccessToken);
            Pin = "";
            LoggedIn?.Invoke(result.Value.StaffCode); // server resolved who it is
        }
        finally
        {
            IsBusy = false;
        }
    }
}
