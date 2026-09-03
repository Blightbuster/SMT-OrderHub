using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Components;
using OrderHub.Client.ApiClient;
using OrderHub.Client.Components;

namespace OrderHub.Client.Pages;

/// <summary>
/// Create / edit form for a board, including component placements
/// (PlacementCount per component) via the reusable AssignmentList.
/// </summary>
public partial class BoardEdit : ComponentBase
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
    private List<AssignmentRow> _placements = [];
    private List<AvailableOption> _availableComponents = [];

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
