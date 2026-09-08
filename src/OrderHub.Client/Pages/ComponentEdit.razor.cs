using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.SignalR.Client;
using OrderHub.Client.ApiClient;
using OrderHub.Client.RealTime;

namespace OrderHub.Client.Pages;

/// <summary>
/// Create / edit form for a single component. While editing an existing
/// component, subscribes to the SignalR watch channel: when another user
/// saves changes, a non-blocking conflict banner appears.
/// </summary>
public partial class ComponentEdit : ComponentBase, IDisposable
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

    // Real-time conflict state (also set by the save-time 409 path).
    private EntityModifiedEvent? _conflict;
    private bool _reviewing;
    private ComponentDto? _conflictState;

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

            // Subscribe to real-time modifications of THIS component.
            await OrderHub.StartAsync();
            OrderHub.Connection.Remove("EntityModifiedByAnotherUser");
            OrderHub.Connection.On<EntityModifiedEvent>("EntityModifiedByAnotherUser", (evt) =>
            {
                _ = HandleConflictAsync(evt);
                return Task.CompletedTask;
            });
            await OrderHub.WatchComponentAsync(Id.Value);
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
            _conflictState = await Api.Components.GetByIdAsync(Id.Value);
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
        catch (ConcurrencyConflictException<ComponentDto> ex)
        {
            // Another user saved first: offer side-by-side review / discard / overwrite.
            _conflictState = ex.CurrentState;
            _error = null;
        }
        catch (ApiConflictException ex)
        {
            _error = ex.Message;
        }
        catch (ApiValidationException ex)
        {
            _error = ex.Message;
        }
        catch (Exception ex)
        {
            _error = FriendlyError(ex, "Could not save the component.");
        }
        finally
        {
            _saving = false;
        }
    }

    /// <summary>Reloads the current database state, discarding local edits.</summary>
    private async Task DiscardAndReloadAsync()
    {
        _conflictState = null;
        _reviewing = false;
        _error = null;
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
            _ = OrderHub.UnwatchComponentAsync(Id.Value);
        }
    }

    /// <summary>Maps raw transport/unknown exceptions to a readable message.</summary>
    internal static string FriendlyError(Exception ex, string fallback)
    {
        // WASM HttpClient surfaces connectivity problems as TypeError/HttpRequestException.
        if (ex is HttpRequestException
            || ex.Message.Contains("NetworkError", StringComparison.OrdinalIgnoreCase)
            || ex.Message.Contains("Failed to fetch", StringComparison.OrdinalIgnoreCase)
            || ex.Message.Contains("TypeError", StringComparison.OrdinalIgnoreCase))
        {
            return $"{fallback} The API could not be reached — is it running?";
        }
        return ex.Message.Length > 0 ? ex.Message : fallback;
    }
}
