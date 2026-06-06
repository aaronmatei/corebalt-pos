using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Pos.Till.Api;

namespace Pos.Till.ViewModels;

/// <summary>
/// PIN login screen. The cashier enters StaffCode + PIN; on success the JWT is handed to the API
/// client (held for the session) and <see cref="LoggedIn"/> tells the shell to show the till.
/// </summary>
public partial class LoginViewModel : ObservableObject
{
    private readonly IPosApiClient _api;
    public LoginViewModel(IPosApiClient api) => _api = api;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(LoginCommand))]
    private string _staffCode = "";

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(LoginCommand))]
    private string _pin = "";

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(LoginCommand))]
    private bool _isBusy;

    [ObservableProperty] private string? _error;

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
}
