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
    private readonly TillOptions _options;

    [ObservableProperty] private ObservableObject _current = default!;

    public ShellViewModel(IPosApiClient api, TillOptions options)
    {
        _api = api;
        _options = options;
        ShowLogin();
    }

    private void ShowLogin()
    {
        _api.ClearAccessToken();
        var login = new LoginViewModel(_api);
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
