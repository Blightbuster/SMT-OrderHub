using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.SignalR.Client;
using OrderHub.Client.ApiClient;
using OrderHub.Client.Components;
using OrderHub.Client.RealTime;

namespace OrderHub.Client.Pages;

/// <summary>
/// Create / edit form for an order, including board assignments
/// (BoardQuantity per board) via the reusable AssignmentList.
/// While editing an existing order, subscribes to the SignalR watch channel:
/// when another user saves changes, a non-blocking conflict banner appears
/// offering "Discard & Reload" or a "Side-by-Side Review".
/// </summary>
public partial class OrderEdit : ComponentBase, IDisposable
{
    [Inject] private IOrderHubApiClient Api { get; set; } = null!;
    [Inject] private NavigationManager Navigation { get; set; } = null!;
    [Inject] private IOrderHubClient OrderHub { get; set; } = null!;

    [Parameter] public Guid? Id { get; set; }

    private bool _isNew => Id is null;
    private bool _loading;
    private bool _saving;
    private bool _notFound;
    private string? _error;

    private Guid _rowVersion;
    private Form _form = new();
    private List<AssignmentRow> _boardAssignments = [];
    private List<AvailableOption> _availableBoards = [];

    // Real-time conflict state.
    private OrderModifiedEvent? _conflict;
    private bool _reviewing;
    private OrderDetailDto? _conflictState;

    private string _conflictBoardSummary =>
        _boardAssignments.Count == 0 ? "—" : string.Join(", ", _boardAssignments.Select(r => $"{r.Label} ×{r.Count}"));

    private string? _conflictStateSummary =>
        _conflictState is null || _conflictState.Boards.Count == 0
            ? "—"
            : string.Join(", ", _conflictState.Boards.Select(b => $"{b.BoardName} ×{b.BoardQuantity}"));

    private sealed class Form
    {
        [Required, MaxLength(200)]
        public string Name { get; set; } = string.Empty;

        [MaxLength(2000)]
        public string Description { get; set; } = string.Empty;
    }

    protected override async Task OnParametersSetAsync()
    {
        _loading = true;
        try
        {
            // Pick list = all boards (first page, large enough for the demo scope).
            var boards = await Api.Boards.SearchAsync(null, 1, 100);
            _availableBoards = boards.Items
                .Select(b => new AvailableOption(b.Id, b.Name))
                .ToList();

            if (_isNew) return;

            var order = await Api.Orders.GetByIdAsync(Id!.Value);
            if (order is null)
            {
                _notFound = true;
                return;
            }

            _rowVersion = order.RowVersion;
            _form = new Form { Name = order.Name, Description = order.Description };
            _boardAssignments = order.Boards
                .Select(ba => new AssignmentRow
                {
                    ItemId = ba.BoardId,
                    Label = ba.BoardName,
                    Count = ba.BoardQuantity
                })
                .ToList();

            // Subscribe to real-time modifications of THIS order.
            await OrderHub.StartAsync();
            OrderHub.Connection.Remove("OrderModifiedByAnotherUser");
            OrderHub.Connection.On<OrderModifiedEvent>("OrderModifiedByAnotherUser", (evt) =>
            {
                _ = HandleConflictAsync(evt);
                return Task.CompletedTask;
            });
            await OrderHub.WatchOrderAsync(Id.Value);
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

    private void DismissConflict() => _conflict = null;

    private async Task HandleConflictAsync(OrderModifiedEvent evt)
    {
        if (evt.OrderId != Id.Value) return;
        _conflict = evt;
        try
        {
            _conflictState = await Api.Orders.GetByIdAsync(Id.Value);
        }
        catch
        {
            _conflictState = null;
        }
        _reviewing = false;
        await InvokeAsync(StateHasChanged);
    }

    private async Task DiscardAndReloadAsync()
    {
        _conflict = null;
        _reviewing = false;
        _loading = true;
        try
        {
            var order = await Api.Orders.GetByIdAsync(Id!.Value);
            if (order is null)
            {
                _notFound = true;
                return;
            }
            _rowVersion = order.RowVersion;
            _form = new Form { Name = order.Name, Description = order.Description };
            _boardAssignments = order.Boards
                .Select(ba => new AssignmentRow { ItemId = ba.BoardId, Label = ba.BoardName, Count = ba.BoardQuantity })
                .ToList();
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

    private async Task OverwriteAnywayAsync()
    {
        // Re-apply our form state on top of the latest RowVersion.
        if (_conflictState is null) return;
        _rowVersion = _conflictState.RowVersion;
        await SaveAsync();
    }

    private Task StateChangedAsync()
    {
        // Called by AssignmentList when rows are added/removed/re-counted.
        // Re-render so the conflict banner's side-by-side diff reflects the
        // current form state immediately.
        return InvokeAsync(StateHasChanged);
    }

    private async Task SaveAsync()
    {
        _saving = true;
        _error = null;
        try
        {
            var boards = _boardAssignments
                .Select(r => new BoardAssignmentRequest(r.ItemId, r.Count))
                .ToList();

            if (_isNew)
            {
                // Create, then immediately assign boards via PUT (the update contract
                // owns the relationship; matches the API surface).
                var created = await Api.Orders.CreateAsync(new CreateOrderRequest(_form.Name, _form.Description));
                await Api.Orders.UpdateAsync(created.Id,
                    new UpdateOrderRequest(created.RowVersion, _form.Name, _form.Description, boards));
            }
            else
            {
                await Api.Orders.UpdateAsync(Id!.Value,
                    new UpdateOrderRequest(_rowVersion, _form.Name, _form.Description, boards));
            }

            Navigation.NavigateTo("orders");
        }
        catch (Exception ex)
        {
            _error = ex.Message;
        }
        finally
        {
            _saving = false;
        }
    }

    public void Dispose()
    {
        if (Id is not null)
        {
            _ = OrderHub.UnwatchOrderAsync(Id.Value);
        }
    }
}
