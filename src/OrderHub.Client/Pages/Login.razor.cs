using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using OrderHub.Client.ApiClient;

namespace OrderHub.Client.Pages;

/// <summary>Login / registration page backed by the API's Identity endpoints.</summary>
public partial class Login : ComponentBase
{
    [Inject] private IOrderHubApiClient Api { get; set; } = null!;
    [Inject] private AuthenticationStateProvider AuthState { get; set; } = null!;
    [Inject] private NavigationManager Navigation { get; set; } = null!;

    private string _mode = "login"; // "login" | "register"
    private string _email = string.Empty;
    private string _password = string.Empty;
    private string? _error;
    private bool _busy;

    private string Mode
    {
        get => _mode;
        set { _mode = value; _error = null; }
    }

    private void SetMode(string mode) => Mode = mode;

    private async Task SubmitAsync()
    {
        _error = null;
        _busy = true;
        try
        {
            if (Mode == "register")
            {
                await Api.Auth.RegisterAsync(_email, _password);
                // Identity register does not sign in; log in immediately afterwards.
            }

            await Api.Auth.LoginAsync(_email, _password);
            ((Auth.CookieAuthStateProvider)AuthState).NotifyStateChanged();
            Navigation.NavigateTo("orders");
        }
        catch (ApiValidationException ex)
        {
            _error = ex.Message;
        }
        catch (Exception ex) when (ex.Message.Contains("400"))
        {
            _error = "Registration failed — check the password requirements (8+ chars, upper, lower, digit, symbol).";
        }
        catch
        {
            _error = "Invalid credentials or server unreachable.";
        }
        finally
        {
            _busy = false;
        }
    }
}
