using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Components;
using OrderHub.Client.ApiClient;

namespace OrderHub.Client.Pages;

/// <summary>Create / edit form for a single component.</summary>
public partial class ComponentEdit : ComponentBase
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

    private sealed class Form
    {
        [Required, MaxLength(200)]
        public string Name { get; set; } = string.Empty;

        [MaxLength(2000)]
        public string Description { get; set; } = string.Empty;

        [Range(0, int.MaxValue)]
        public int Quantity { get; set; }
    }

    protected override async Task OnParametersSetAsync()
    {
        if (_isNew) return;

        _loading = true;
        try
        {
            var component = await Api.Components.GetByIdAsync(Id!.Value);
            if (component is null)
            {
                _notFound = true;
                return;
            }

            _rowVersion = component.RowVersion;
            _form = new Form
            {
                Name = component.Name,
                Description = component.Description,
                Quantity = component.Quantity
            };
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

    private async Task SaveAsync()
    {
        _saving = true;
        _error = null;
        try
        {
            if (_isNew)
            {
                await Api.Components.CreateAsync(new CreateComponentRequest(_form.Name, _form.Description, _form.Quantity));
            }
            else
            {
                await Api.Components.UpdateAsync(Id!.Value,
                    new UpdateComponentRequest(_rowVersion, _form.Name, _form.Description, _form.Quantity));
            }

            Navigation.NavigateTo("components");
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
