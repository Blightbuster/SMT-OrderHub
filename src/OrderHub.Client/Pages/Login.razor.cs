using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.Extensions.Localization;
using OrderHub.Client.ApiClient;
using OrderHub.Client.Resources;

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

    private void OnPasswordInput(ChangeEventArgs e)
    {
        _password = e.Value?.ToString() ?? string.Empty;
    }

    /// <summary>
    /// Client-side password requirement check (localized). Shown live in
    /// register mode and enforced before submitting, so Identity password
    /// errors from the API never need translating or matching.
    /// </summary>
    private IEnumerable<(Func<string> Label, bool Met)> PasswordRules => new (Func<string>, bool)[]
    {
        (() => T["PasswordRule_Length"], _password.Length >= 6),
        (() => T["PasswordRule_Upper"], _password.Any(char.IsUpper)),
        (() => T["PasswordRule_Lower"], _password.Any(char.IsLower)),
        (() => T["PasswordRule_Digit"], _password.Any(char.IsDigit)),
        (() => T["PasswordRule_Special"], _password.Any(c => !char.IsLetterOrDigit(c))),
    };

    private bool AllPasswordRulesMet => PasswordRules.All(r => r.Met);

    private async Task SubmitAsync()
    {
        _error = null;
        _busy = true;
        try
        {
            if (Mode == "register")
            {
                if (_email.Contains('\'') || _email.Contains('"') || _email.Contains(' '))
                {
                    _error = T["Register_InvalidEmail"];
                    return;
                }

                if (!AllPasswordRulesMet)
                {
                    // Show the FIRST unmet requirement as a single localized message.
                    _error = PasswordRules.First(r => !r.Met).Label();
                    return;
                }

                await Api.Auth.RegisterAsync(_email, _password);
                // Identity register does not sign in; log in immediately afterwards.
            }

            await Api.Auth.LoginAsync(_email, _password);
            ((Auth.CookieAuthStateProvider)AuthState).NotifyStateChanged();
            Navigation.NavigateTo("");
        }
        catch (ApiValidationException ex)
        {
            _error = ex.Message;
        }
        catch (Exception ex) when (ex.Message.Contains("400") || ex.Message.Contains("409"))
        {
            _error = T["Register_Failed"];
        }
        catch (Exception ex)
        {
            _error = ComponentEdit.FriendlyError(ex, T["Login_InvalidCredentials"]);
        }
        finally
        {
            _busy = false;
        }
    }
}
