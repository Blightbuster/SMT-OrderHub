using Microsoft.AspNetCore.Components;

namespace OrderHub.Client.Layout;

/// <summary>Redirects unauthenticated users to the login page.</summary>
public partial class RedirectToLogin : ComponentBase
{
    [Inject] private NavigationManager Navigation { get; set; } = null!;

    protected override void OnInitialized() =>
        Navigation.NavigateTo("login");
}
