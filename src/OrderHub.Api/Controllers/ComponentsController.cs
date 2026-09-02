using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using OrderHub.Application.Dtos;
using OrderHub.Application.Interfaces;
using OrderHub.Domain;

namespace OrderHub.Api.Controllers;

/// <summary>CRUD endpoints for components.</summary>
[ApiController]
[Route("api/[controller]")]
public class ComponentsController : ControllerBase
{
    private readonly IComponentRepository _repository;

    public ComponentsController(IComponentRepository repository) => _repository = repository;

    /// <summary>Paged, searchable component list.</summary>
    [HttpGet]
    public async Task<ActionResult<PagedResult<ComponentResponse>>> GetAll(
        [FromQuery] string? searchTerm, [FromQuery] int page = 1, [FromQuery] int pageSize = 20,
        CancellationToken cancellationToken = default)
    {
        var (items, totalCount) = await _repository.SearchAsync(searchTerm, page, pageSize, cancellationToken);

        return Ok(new PagedResult<ComponentResponse>(
            Items: items.Select(c => new ComponentResponse(c.Id, c.Name, c.Description, c.Quantity, c.RowVersion)).ToList(),
            TotalCount: totalCount,
            Page: Math.Max(1, page),
            PageSize: Math.Clamp(pageSize, 1, 100)));
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<ComponentResponse>> GetById(Guid id, CancellationToken cancellationToken)
    {
        var component = await _repository.GetByIdAsync(id, cancellationToken);
        if (component is null) return NotFound();

        return Ok(new ComponentResponse(component.Id, component.Name, component.Description, component.Quantity, component.RowVersion));
    }

    [HttpPost]
    public async Task<ActionResult<ComponentResponse>> Create(CreateComponentRequest request, CancellationToken cancellationToken)
    {
        var component = new Component
        {
            Name = request.Name,
            Description = request.Description,
            Quantity = request.Quantity
        };

        await _repository.AddAsync(component, cancellationToken);
        await _repository.SaveChangesAsync(cancellationToken);

        var response = new ComponentResponse(component.Id, component.Name, component.Description, component.Quantity, component.RowVersion);
        return CreatedAtAction(nameof(GetById), new { id = component.Id }, response);
    }

    [HttpPut("{id:guid}")]
    public async Task<IActionResult> Update(Guid id, UpdateComponentRequest request, CancellationToken cancellationToken)
    {
        var component = await _repository.GetByIdAsync(id, cancellationToken);
        if (component is null) return NotFound();

        // Client round-trips the RowVersion it last saw — this drives optimistic concurrency.
        component.Name = request.Name;
        component.Description = request.Description;
        component.Quantity = request.Quantity;
        _repository.MarkModified(component, request.RowVersion);

        try
        {
            await _repository.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            return await ConflictWithCurrentState(id, cancellationToken);
        }

        return NoContent();
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken cancellationToken)
    {
        var component = await _repository.GetByIdAsync(id, cancellationToken);
        if (component is null) return NotFound();

        await _repository.DeleteAsync(component, cancellationToken);
        await _repository.SaveChangesAsync(cancellationToken);

        return NoContent();
    }

    private async Task<IActionResult> ConflictWithCurrentState(Guid id, CancellationToken cancellationToken)
    {
        var current = await _repository.GetByIdAsync(id, cancellationToken);
        if (current is null) return NotFound();

        return Conflict(new ComponentResponse(current.Id, current.Name, current.Description, current.Quantity, current.RowVersion));
    }
}
