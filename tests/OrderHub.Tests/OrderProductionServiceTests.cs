using Moq;
using OrderHub.Application.Interfaces;
using OrderHub.Application.ProductionExport;
using OrderHub.Domain;

namespace OrderHub.Tests;

/// <summary>
/// Unit tests for the production export service using a mocked repository.
/// Validates JSON structure, aggregate mapping, and error handling.
/// </summary>
public class OrderProductionServiceTests
{
    private readonly Mock<IOrderRepository> _orderRepositoryMock = new();
    private readonly OrderProductionService _sut;

    public OrderProductionServiceTests()
    {
        _sut = new OrderProductionService(_orderRepositoryMock.Object);
    }

    [Fact]
    public async Task Export_WithValidOrder_ReturnsJsonWithAggregateData()
    {
        // Arrange
        var order = CreateFullyPopulatedOrder();
        _orderRepositoryMock
            .Setup(r => r.GetDetailByIdAsync(order.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(order);

        // Act
        var json = await _sut.ExportOrderForProductionAsync(order.Id);

        // Assert
        Assert.False(string.IsNullOrWhiteSpace(json));

        using var document = System.Text.Json.JsonDocument.Parse(json);
        var root = document.RootElement;

        Assert.Equal(order.Id, root.GetProperty("orderId").GetGuid());
        Assert.Equal(order.Name, root.GetProperty("name").GetString());

        var boards = root.GetProperty("boards");
        Assert.Equal(1, boards.GetArrayLength());

        var board = boards[0];
        Assert.Equal("MCU-Mainboard-v2", board.GetProperty("name").GetString());
        Assert.Equal(160.5, board.GetProperty("lengthMm").GetDouble());
        Assert.Equal(100.0, board.GetProperty("widthMm").GetDouble());
        Assert.Equal(5, board.GetProperty("quantity").GetInt32());

        var placements = board.GetProperty("placements");
        Assert.Equal(2, placements.GetArrayLength());

        var resistor = placements[0];
        Assert.Equal("Resistor 10k 0805", resistor.GetProperty("name").GetString());
        Assert.Equal(24, resistor.GetProperty("placementCount").GetInt32());

        var mcu = placements[1];
        Assert.Equal("STM32F407", mcu.GetProperty("name").GetString());
        Assert.Equal(1, mcu.GetProperty("placementCount").GetInt32());
    }

    [Fact]
    public async Task Export_WithUnknownOrder_ThrowsEntityNotFoundException()
    {
        var unknownId = Guid.NewGuid();
        _orderRepositoryMock
            .Setup(r => r.GetDetailByIdAsync(unknownId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((Order?)null);

        var exception = await Assert.ThrowsAsync<EntityNotFoundException>(
            () => _sut.ExportOrderForProductionAsync(unknownId));

        Assert.Equal(unknownId, exception.Id);
        Assert.Equal("Order", exception.EntityName);
    }

    [Fact]
    public async Task Export_WithEmptyOrder_ThrowsInvalidOperationException()
    {
        var order = new Order { Name = "SMT-RUN-2026-EMPTY" };
        _orderRepositoryMock
            .Setup(r => r.GetDetailByIdAsync(order.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(order);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => _sut.ExportOrderForProductionAsync(order.Id));
    }

    [Fact]
    public async Task Export_JsonIsCamelCaseAndParsesWithoutCycles()
    {
        var order = CreateFullyPopulatedOrder();
        _orderRepositoryMock
            .Setup(r => r.GetDetailByIdAsync(order.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(order);

        var json = await _sut.ExportOrderForProductionAsync(order.Id);

        // Camel-case contract properties must exist (round-trip through DTOs, not raw entities).
        using var document = System.Text.Json.JsonDocument.Parse(json);
        Assert.True(document.RootElement.TryGetProperty("orderDateUtc", out _));
        Assert.False(json.Contains("\"orderBoards\"", StringComparison.Ordinal), "Raw entity graph must not leak into the payload.");
    }

    private static Order CreateFullyPopulatedOrder()
    {
        var resistor = new Component { Name = "Resistor 10k 0805", Description = "SKU-R-10K-0805", Quantity = 5000 };
        var mcu = new Component { Name = "STM32F407", Description = "SKU-MCU-32F407", Quantity = 250 };
        var board = new Board { Name = "MCU-Mainboard-v2", Description = "Rev C", Length = 160.5, Width = 100.0 };

        board.BoardComponents.Add(new BoardComponent { Board = board, Component = resistor, PlacementCount = 24 });
        board.BoardComponents.Add(new BoardComponent { Board = board, Component = mcu, PlacementCount = 1 });

        var order = new Order { Name = "SMT-RUN-2026-001", Description = "Batch parameters: reflow profile 3" };
        order.OrderBoards.Add(new OrderBoard { Order = order, Board = board, BoardQuantity = 5 });

        return order;
    }
}
