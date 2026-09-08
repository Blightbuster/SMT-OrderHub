using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.SignalR.Client;
using OrderHub.Client.ApiClient;
using OrderHub.Client.Components;
using OrderHub.Client.RealTime;

namespace OrderHub.Client.Pages;

/// <summary>
/// Create / edit form for a board, including component placements
/// (PlacementCount per component) via the reusable AssignmentList.
/// While editing an existing board, subscribes to the SignalR watch channel:
/// when another user saves changes, a non-blocking conflict banner appears
/// offering "Discard & Reload" or a "Side-by-Side Review".
/// </summary>
public partial class BoardEdit : ComponentBase, IDisposable
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
    private List<AssignmentRow> _placements = [];
    private List<AvailableOption> _availableComponents = [];

    // Real-time conflict state (also set by the save-time 409 path).
    private EntityModifiedEvent? _conflict;
    private bool _reviewing;
    private BoardDetailDto? _conflictState;

    private sealed class Form
    {
        [Required, MaxLength(200)]
        public string Name { get; set; } = string.Empty;

        [MaxLength(2000)]
        public string Description { get; set; } = string.Empty;

        [Range(0.1, 10000)]
        public double Length { get; set; } = 100;

        [Range(0.1, 10000)]
        public double Width { get; set; } = 100;
    }

    protected override async Task OnParametersSetAsync()
    {
        _loading = true;
        try
        {
            // Pick list = all components (first page, large enough for the demo scope).
            var components = await Api.Components.SearchAsync(null, 1, 100);
            _availableComponents = components.Items
                .Select(c => new AvailableOption(c.Id, c.Name))
                .ToList();

            if (_isNew) return;

            var board = await Api.Boards.GetByIdAsync(Id!.Value);
            if (board is null)
            {
                _notFound = true;
                return;
            }

            _rowVersion = board.RowVersion;
            _form = new Form
            {
                Name = board.Name,
                Description = board.Description,
                Length = board.Length,
                Width = board.Width
            };
            _placements = board.Components
                .Select(p => new AssignmentRow
                {
                    ItemId = p.ComponentId,
                    Label = p.ComponentName,
                    Count = p.PlacementCount
                })
                .ToList();

            // Subscribe to real-time modifications of THIS board.
            await OrderHub.StartAsync();
            OrderHub.Connection.Remove("EntityModifiedByAnotherUser");
            OrderHub.Connection.On<EntityModifiedEvent>("EntityModifiedByAnotherUser", (evt) =>
            {
                _ = HandleConflictAsync(evt);
                return Task.CompletedTask;
            });
            await OrderHub.WatchBoardAsync(Id.Value);
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

    private async Task HandleConflictAsync(EntityModifiedEvent evt)
    {
        if (evt.Id != Id.Value || _saving) return;
        _conflict = evt;
        try
        {
            _conflictState = await Api.Boards.GetByIdAsync(Id.Value);
        }
        catch
        {
            _conflictState = null;
        }
        _reviewing = false;
        await InvokeAsync(StateHasChanged);
    }

    private void DismissConflict() => _conflict = null;

    /// <summary>Timestamp suffix for the banner when the modifier is known.</summary>
    private string ConflictWhenSuffix =>
        _conflict is null ? "." : $" {T["OrderEdit_ConflictAt"]} {_conflict.ModifiedAtUtc.ToLocalTime():HH:mm:ss}.";

    private Task StateChangedAsync()
    {
        // Called by AssignmentList when rows are added/removed/re-counted.
        return InvokeAsync(StateHasChanged);
    }

    private async Task SaveAsync()
    {
        _saving = true;
        _error = null;
        try
        {
            var placements = _placements
                .Select(r => new PlacementRequest(r.ItemId, r.Count))
                .ToList();

            if (_isNew)
            {
                await Api.Boards.CreateAsync(new CreateBoardRequest(_form.Name, _form.Description, _form.Length, _form.Width));
            }
            else
            {
                await Api.Boards.UpdateAsync(Id!.Value,
                    new UpdateBoardRequest(_rowVersion, _form.Name, _form.Description, _form.Length, _form.Width, placements));
            }

            Navigation.NavigateTo("boards");
        }
        catch (ConcurrencyConflictException<BoardDetailDto> ex)
        {
            // Another user saved first: offer side-by-side review / discard / overwrite.
            _conflictState = ex.CurrentState;
            _error = null;
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

    /// <summary>Summary of the local placement assignments for the diff table.</summary>
    private string PlacementsSummary =>
        string.Join(", ", _placements.Select(p => $"{p.Label} ×{p.Count}"));

    /// <summary>Summary of the database placement assignments for the diff table.</summary>
    private string? ConflictPlacementsSummary =>
        _conflictState is null ? null
            : string.Join(", ", _conflictState.Components.Select(c => $"{c.ComponentName} ×{c.PlacementCount}"));

    /// <summary>Reloads the current database state, discarding local edits.</summary>
    private async Task DiscardAndReloadAsync()
    {
        _conflictState = null;
        _conflict = null;
        _reviewing = false;
        _error = null;
        _loading = true;
        try
        {
            var board = await Api.Boards.GetByIdAsync(Id!.Value);
            if (board is null)
            {
                _notFound = true;
                return;
            }
            _rowVersion = board.RowVersion;
            _form = new Form
            {
                Name = board.Name,
                Description = board.Description,
                Length = board.Length,
                Width = board.Width
            };
            _placements = board.Components
                .Select(p => new AssignmentRow { ItemId = p.ComponentId, Label = p.ComponentName, Count = p.PlacementCount })
                .ToList();
        }
        finally
        {
            _loading = false;
        }
    }

    /// <summary>Rebases onto the latest RowVersion and saves anyway (last-writer-wins).</summary>
    private async Task OverwriteAnywayAsync()
    {
        if (_conflictState is null) return;
        _rowVersion = _conflictState.RowVersion;
        _conflictState = null;
        _conflict = null;
        _reviewing = false;
        await SaveAsync();
    }

    public void Dispose()
    {
        if (Id is not null)
        {
            _ = OrderHub.UnwatchBoardAsync(Id.Value);
        }
    }
}
