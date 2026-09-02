using Microsoft.AspNetCore.Components;
using OrderHub.Client.ApiClient;
using OrderHub.Client.Components;

namespace OrderHub.Client.Pages;

/// <summary>Order list with server-side search + pagination and delete confirmation.</summary>
public partial class Orders : ComponentBase, IDisposable
{
    [Inject] private IOrderHubApiClient Api { get; set; } = null!;

    private List<OrderDto> _items = [];
    private readonly PagerState _pager = new() { PageSize = 10 };
    private string _searchTerm = string.Empty;
    private string _lastLoadedSearchTerm = string.Empty;

    private bool _loading = true;
    private string? _error;

    private OrderDto? _pendingDelete;
    private bool _deleting;

    private System.Timers.Timer? _debounceTimer;

    protected override async Task OnInitializedAsync() => await LoadAsync();

    private async Task LoadAsync()
    {
        _loading = true;
        _error = null;
        try
        {
            var result = await Api.Orders.SearchAsync(_searchTerm, _pager.Page, _pager.PageSize);
            _items = result.Items.ToList();
            _pager.TotalCount = result.TotalCount;
            _lastLoadedSearchTerm = _searchTerm;
        }
        catch (Exception ex)
        {
            _error = ex.Message;
        }
        finally
        {
            _loading = false;
        }
    }

    private void OnSearchInput(ChangeEventArgs e)
    {
        _searchTerm = e.Value?.ToString() ?? string.Empty;

        _debounceTimer?.Stop();
        _debounceTimer = new System.Timers.Timer(400) { AutoReset = false };
        _debounceTimer.Elapsed += async (_, _) => await InvokeAsync(async () =>
        {
            _pager.Reset();
            if (_searchTerm != _lastLoadedSearchTerm) await LoadAsync();
        });
        _debounceTimer.Start();
    }

    private async Task ClearSearch()
    {
        _searchTerm = string.Empty;
        _pager.Reset();
        await LoadAsync();
    }

    private void ConfirmDeleteAsync(OrderDto item) => _pendingDelete = item;

    private void CancelDelete() => _pendingDelete = null;

    private async Task DeleteAsync()
    {
        if (_pendingDelete is null) return;
        _deleting = true;
        _error = null;
        try
        {
            await Api.Orders.DeleteAsync(_pendingDelete.Id);
            _pendingDelete = null;
            await LoadAsync();
        }
        catch (Exception ex)
        {
            _error = ex.Message;
            _pendingDelete = null;
        }
        finally
        {
            _deleting = false;
        }
    }

    public void Dispose() => _debounceTimer?.Dispose();
}
