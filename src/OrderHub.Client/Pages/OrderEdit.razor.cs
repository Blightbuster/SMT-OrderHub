using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Components;
using OrderHub.Client.ApiClient;
using OrderHub.Client.Components;

namespace OrderHub.Client.Pages;

/// <summary>
/// Create / edit form for an order, including board assignments
/// (BoardQuantity per board) via the reusable AssignmentList.
/// </summary>
public partial class OrderEdit : ComponentBase
{
    [Inject] private IOrderHubApiClient Api { get; set; } = null!;
    [Inject] private NavigationManager Navigation { get; set; } = null!;

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

    private Task StateChangedAsync() => Task.CompletedTask;

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
}
