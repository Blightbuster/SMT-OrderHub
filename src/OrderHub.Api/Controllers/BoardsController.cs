using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using OrderHub.Application.Dtos;
using OrderHub.Application.Interfaces;
using OrderHub.Domain;

namespace OrderHub.Api.Controllers;

/// <summary>CRUD endpoints for boards.</summary>
[ApiController]
[Route("api/[controller]")]
public class BoardsController : ControllerBase
{
    private readonly IBoardRepository _repository;
    private readonly IComponentRepository _componentRepository;

    public BoardsController(IBoardRepository repository, IComponentRepository componentRepository)
    {
        _repository = repository;
        _componentRepository = componentRepository;
    }

    [HttpGet]
    public async Task<ActionResult<PagedResult<BoardResponse>>> GetAll(
        [FromQuery] string? searchTerm, [FromQuery] int page = 1, [FromQuery] int pageSize = 20,
        CancellationToken cancellationToken = default)
    {
        var (items, totalCount) = await _repository.SearchAsync(searchTerm, page, pageSize, cancellationToken);

        return Ok(new PagedResult<BoardResponse>(
            Items: items.Select(b => new BoardResponse(b.Id, b.Name, b.Description, b.Length, b.Width, b.RowVersion)).ToList(),
            TotalCount: totalCount,
            Page: Math.Max(1, page),
            PageSize: Math.Clamp(pageSize, 1, 100)));
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<BoardDetailResponse>> GetById(Guid id, CancellationToken cancellationToken)
    {
        var board = await _repository.GetDetailByIdAsync(id, cancellationToken);
        if (board is null) return NotFound();

        return Ok(ToDetailResponse(board));
    }

    [HttpPost]
    public async Task<ActionResult<BoardResponse>> Create(CreateBoardRequest request, CancellationToken cancellationToken)
    {
        var board = new Board
        {
            Name = request.Name,
            Description = request.Description,
            Length = request.Length,
            Width = request.Width
        };

        await _repository.AddAsync(board, cancellationToken);
        await _repository.SaveChangesAsync(cancellationToken);

        var response = new BoardResponse(board.Id, board.Name, board.Description, board.Length, board.Width, board.RowVersion);
        return CreatedAtAction(nameof(GetById), new { id = board.Id }, response);
    }

    [HttpPut("{id:guid}")]
    public async Task<IActionResult> Update(Guid id, UpdateBoardRequest request, CancellationToken cancellationToken)
    {
        var board = await _repository.GetDetailByIdAsync(id, cancellationToken);
        if (board is null) return NotFound();

        // Validate all referenced components exist before mutating the aggregate.
        var requestedIds = request.Components.Select(c => c.ComponentId).ToList();
        if (!await _componentRepository.AllExistAsync(requestedIds, cancellationToken))
        {
            return BadRequest("One or more referenced components do not exist.");
        }

        board.Name = request.Name;
        board.Description = request.Description;
        board.Length = request.Length;
        board.Width = request.Width;

        ReplacePlacements(board, request.Components);
        _repository.MarkModified(board, request.RowVersion);

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
        var board = await _repository.GetDetailByIdAsync(id, cancellationToken);
        if (board is null) return NotFound();

        await _repository.DeleteAsync(board, cancellationToken);
        await _repository.SaveChangesAsync(cancellationToken);

        return NoContent();
    }

    private async Task<IActionResult> ConflictWithCurrentState(Guid id, CancellationToken cancellationToken)
    {
        var current = await _repository.GetCurrentStateAsync(id, cancellationToken);
        if (current is null) return NotFound();

        return Conflict(ToDetailResponse(current));
    }

    private static BoardDetailResponse ToDetailResponse(Board board) => new(
        board.Id, board.Name, board.Description, board.Length, board.Width, board.RowVersion,
        board.BoardComponents
            .Select(bc => new BoardComponentResponse(bc.ComponentId, bc.Component.Name, bc.PlacementCount))
            .ToList());

    private static void ReplacePlacements(Board board, IReadOnlyList<BoardComponentRequest> requests)
    {
        board.BoardComponents.Clear();
        foreach (var request in requests)
        {
            board.BoardComponents.Add(new BoardComponent
            {
                BoardId = board.Id,
                ComponentId = request.ComponentId,
                PlacementCount = request.PlacementCount
            });
        }
    }
}
