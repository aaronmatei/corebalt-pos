using CommunityToolkit.Mvvm.ComponentModel;
using Pos.Till.Api;

namespace Pos.Till.ViewModels;

/// <summary>
/// Root of the till: swaps the <see cref="Current"/> view-model between the PIN login and the till
/// screen. Login → till on success; "lock / switch cashier" → back to a fresh login (token cleared).
/// </summary>
public partial class ShellViewModel : ObservableObject
{
    private readonly IPosApiClient _api;
    private readonly IFingerprintScanner _scanner;
    private readonly TillOptions _options;

    [ObservableProperty] private ObservableObject _current = default!;

    public ShellViewModel(IPosApiClient api, IFingerprintScanner scanner, TillOptions options)
    {
        _api = api;
        _scanner = scanner;
        _options = options;
        ShowLogin();
    }

    private void ShowLogin()
    {
        _api.ClearAccessToken();
        var login = new LoginViewModel(_api, _scanner);
        login.LoggedIn += OnLoggedIn;
        Current = login;
    }

    private void OnLoggedIn(string staffCode)
    {
        var till = new MainViewModel(_api, _options) { CashierLabel = staffCode };
        till.LockRequested += ShowLogin;
        Current = till;
        _ = till.InitializeAsync();
    }
}
