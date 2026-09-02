using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;
using OrderHub.Client.ApiClient;

namespace OrderHub.Client.Pages;

/// <summary>
/// Read-only order view with board assignments and the production-line
/// JSON export download (single click).
/// </summary>
public partial class OrderDetail : ComponentBase
{
    [Inject] private IOrderHubApiClient Api { get; set; } = null!;
    [Inject] private NavigationManager Navigation { get; set; } = null!;
    [Inject] private IJSRuntime JS { get; set; } = null!;

    [Parameter] public Guid Id { get; set; }

    private OrderDetailDto? _order;
    private bool _loading = true;
    private bool _notFound;
    private bool _exporting;
    private string? _exportError;

    protected override async Task OnParametersSetAsync()
    {
        _loading = true;
        try
        {
            _order = await Api.Orders.GetByIdAsync(Id);
            _notFound = _order is null;
        }
        catch (Exception ex)
        {
            _exportError = ex.Message;
        }
        finally
        {
            _loading = false;
        }
    }

    private async Task ExportAsync()
    {
        if (_order is null) return;
        _exporting = true;
        _exportError = null;
        try
        {
            using var response = await Api.Orders.ExportForProductionAsync(_order.Id);
            var bytes = await response.Content.ReadAsByteArrayAsync();

            // Trigger a browser download of the production JSON.
            var fileName = $"order-{_order.Name.Replace(' ', '-')}-production.json";
            await using var fileRef = await JS.InvokeAsync<IJSObjectReference>(
                "import", "./js/download.js");
            await fileRef.InvokeVoidAsync("downloadFile", fileName, "application/json",
                Convert.ToBase64String(bytes));
        }
        catch (Exception ex)
        {
            // Includes API 422 (order has no boards) surfaced as a validation message.
            _exportError = ex.Message;
        }
        finally
        {
            _exporting = false;
        }
    }
}
