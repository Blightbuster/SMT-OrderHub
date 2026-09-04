using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using OrderHub.Api.RealTime;
using OrderHub.Application.Dtos;
using OrderHub.Application.Interfaces;
using OrderHub.Application.ProductionExport;
using OrderHub.Domain;
using Serilog;

namespace OrderHub.Api.Controllers;

/// <summary>CRUD endpoints for orders plus the production-line export.</summary>
[ApiController]
[Authorize]
[Route("api/[controller]")]
public class OrdersController : ControllerBase
{
    private static readonly Serilog.ILogger AuditLog = Log.ForContext<OrdersController>();

    private readonly IOrderRepository _repository;
    private readonly IBoardRepository _boardRepository;
    private readonly IOrderProductionService _productionService;
    private readonly IHubContext<RealTime.OrderHub> _hubContext;

    public OrdersController(
        IOrderRepository repository,
        IBoardRepository boardRepository,
        IOrderProductionService productionService,
        IHubContext<RealTime.OrderHub> hubContext)
    {
        _repository = repository;
        _boardRepository = boardRepository;
        _productionService = productionService;
        _hubContext = hubContext;
    }

    [HttpGet]
    public async Task<ActionResult<PagedResult<OrderResponse>>> GetAll(
        [FromQuery] string? searchTerm, [FromQuery] int page = 1, [FromQuery] int pageSize = 20,
        CancellationToken cancellationToken = default)
    {
        var (items, totalCount) = await _repository.SearchAsync(searchTerm, page, pageSize, cancellationToken);

        return Ok(new PagedResult<OrderResponse>(
            Items: items.Select(o => new OrderResponse(o.Id, o.Name, o.Description, o.OrderDate, o.RowVersion)).ToList(),
            TotalCount: totalCount,
            Page: Math.Max(1, page),
            PageSize: Math.Clamp(pageSize, 1, 100)));
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<OrderDetailResponse>> GetById(Guid id, CancellationToken cancellationToken)
    {
        var order = await _repository.GetDetailByIdAsync(id, cancellationToken);
        if (order is null) return NotFound();

        return Ok(ToDetailResponse(order));
    }

    /// <summary>Production-line JSON export (Order → Boards → Components).</summary>
    [HttpGet("{id:guid}/export")]
    public async Task<IActionResult> ExportForProduction(Guid id, CancellationToken cancellationToken)
    {
        try
        {
            var json = await _productionService.ExportOrderForProductionAsync(id, cancellationToken);
            return File(System.Text.Encoding.UTF8.GetBytes(json), "application/json",
                fileDownloadName: $"order-{id}-production.json");
        }
        catch (EntityNotFoundException)
        {
            return NotFound();
        }
        catch (InvalidOperationException ex)
        {
            return UnprocessableEntity(new { error = ex.Message });
        }
    }

    [HttpPost]
    public async Task<ActionResult<OrderResponse>> Create(CreateOrderRequest request, CancellationToken cancellationToken)
    {
        var order = new Order
        {
            Name = request.Name,
            Description = request.Description,
            OrderDate = DateTime.UtcNow
        };

        await _repository.AddAsync(order, cancellationToken);
        try
        {
            await _repository.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex) when (ex.IsUniqueConstraintViolation())
        {
            return Conflict(new { error = $"An order named '{request.Name}' already exists." });
        }

        var response = new OrderResponse(order.Id, order.Name, order.Description, order.OrderDate, order.RowVersion);

        AuditLog.Information("Order {OrderId} ({OrderName}) created by {User}",
            order.Id, order.Name, User.Identity?.Name ?? "anonymous");

        return CreatedAtAction(nameof(GetById), new { id = order.Id }, response);
    }

    [HttpPut("{id:guid}")]
    public async Task<IActionResult> Update(Guid id, UpdateOrderRequest request, CancellationToken cancellationToken)
    {
        var order = await _repository.GetDetailByIdAsync(id, cancellationToken);
        if (order is null) return NotFound();

        var requestedIds = request.Boards.Select(b => b.BoardId).ToList();
        if (requestedIds.Count > 0 && !await _boardRepository.AllExistAsync(requestedIds, cancellationToken))
        {
            return BadRequest("One or more referenced boards do not exist.");
        }

        order.Name = request.Name;
        order.Description = request.Description;

        ReplaceBoardAssignments(order, request.Boards);
        _repository.MarkModified(order, request.RowVersion);

        try
        {
            await _repository.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            AuditLog.Warning("Concurrency conflict updating order {OrderId} (client RowVersion stale) by {User}",
                id, User.Identity?.Name ?? "anonymous");
            return await ConflictWithCurrentState(id, cancellationToken);
        }
        catch (DbUpdateException ex) when (ex.IsUniqueConstraintViolation())
        {
            return Conflict(new { error = $"An order named '{request.Name}' already exists." });
        }

        await BroadcastModificationAsync(order, cancellationToken);

        AuditLog.Information("Order {OrderId} ({OrderName}) updated by {User}",
            order.Id, order.Name, User.Identity?.Name ?? "anonymous");

        return NoContent();
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken cancellationToken)
    {
        var order = await _repository.GetDetailByIdAsync(id, cancellationToken);
        if (order is null) return NotFound();

        await _repository.DeleteAsync(order, cancellationToken);
        await _repository.SaveChangesAsync(cancellationToken);

        await BroadcastDeletedAsync(id, User.Identity?.Name ?? "unknown", cancellationToken);

        AuditLog.Information("Order {OrderId} ({OrderName}) deleted by {User}",
            order.Id, order.Name, User.Identity?.Name ?? "anonymous");

        return NoContent();
    }

    /// <summary>Notifies all watchers that this order was modified by the current user.</summary>
    private async Task BroadcastModificationAsync(Order order, CancellationToken cancellationToken)
    {
        await _hubContext.Clients
            .Group(RealTime.OrderHub.GroupNameFor(order.Id))
            .SendAsync("OrderModifiedByAnotherUser", new OrderModifiedEvent(
                order.Id,
                order.Name,
                order.RowVersion,
                User.Identity?.Name ?? "unknown",
                DateTimeOffset.UtcNow), cancellationToken);
    }

    private async Task BroadcastDeletedAsync(Guid orderId, string modifiedBy, CancellationToken cancellationToken)
    {
        await _hubContext.Clients
            .Group(RealTime.OrderHub.GroupNameFor(orderId))
            .SendAsync("OrderDeleted", new { orderId, modifiedBy, deletedAtUtc = DateTimeOffset.UtcNow }, cancellationToken);
    }

    private async Task<IActionResult> ConflictWithCurrentState(Guid id, CancellationToken cancellationToken)
    {
        var current = await _repository.GetCurrentStateAsync(id, cancellationToken);
        if (current is null) return NotFound();

        return Conflict(ToDetailResponse(current));
    }

    private static OrderDetailResponse ToDetailResponse(Order order) => new(
        order.Id, order.Name, order.Description, order.OrderDate, order.RowVersion,
        order.OrderBoards
            .Select(ob => new OrderBoardResponse(ob.BoardId, ob.Board.Name, ob.BoardQuantity))
            .ToList());

    private static void ReplaceBoardAssignments(Order order, IReadOnlyList<OrderBoardRequest> requests)
    {
        order.OrderBoards.Clear();
        foreach (var request in requests)
        {
            order.OrderBoards.Add(new OrderBoard
            {
                OrderId = order.Id,
                BoardId = request.BoardId,
                BoardQuantity = request.BoardQuantity
            });
        }
    }
}
