using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace OrderHub.Client.ApiClient;

/// <summary>
/// Typed HTTP client for the OrderHub API.
/// Centralizes base address, cookie credentials, JSON options, the CSRF header
/// contract, and 409-conflict translation — so no page deals with raw HttpClient.
/// </summary>
public interface IOrderHubApiClient
{
    IComponentApi Components { get; }
    IBoardApi Boards { get; }
    IOrderApi Orders { get; }
    IAuthApi Auth { get; }
}

public interface IComponentApi
{
    Task<PagedResultDto<ComponentDto>> SearchAsync(string? searchTerm, int page, int pageSize, CancellationToken ct = default);
    Task<ComponentDto?> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task<ComponentDto> CreateAsync(CreateComponentRequest request, CancellationToken ct = default);
    Task UpdateAsync(Guid id, UpdateComponentRequest request, CancellationToken ct = default);
    Task DeleteAsync(Guid id, CancellationToken ct = default);
}

public interface IBoardApi
{
    Task<PagedResultDto<BoardDto>> SearchAsync(string? searchTerm, int page, int pageSize, CancellationToken ct = default);
    Task<BoardDetailDto?> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task<BoardDto> CreateAsync(CreateBoardRequest request, CancellationToken ct = default);
    Task UpdateAsync(Guid id, UpdateBoardRequest request, CancellationToken ct = default);
    Task DeleteAsync(Guid id, CancellationToken ct = default);
}

public interface IOrderApi
{
    Task<PagedResultDto<OrderDto>> SearchAsync(string? searchTerm, int page, int pageSize, CancellationToken ct = default);
    Task<OrderDetailDto?> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task<OrderDto> CreateAsync(CreateOrderRequest request, CancellationToken ct = default);
    Task UpdateAsync(Guid id, UpdateOrderRequest request, CancellationToken ct = default);
    Task DeleteAsync(Guid id, CancellationToken ct = default);
    Task<HttpResponseMessage> ExportForProductionAsync(Guid id, CancellationToken ct = default);
}

public interface IAuthApi
{
    Task RegisterAsync(string email, string password, CancellationToken ct = default);
    Task LoginAsync(string email, string password, CancellationToken ct = default);
    Task LogoutAsync(CancellationToken ct = default);

    /// <summary>Returns the signed-in user's info, or null when not authenticated.</summary>
    Task<UserInfoDto?> GetUserInfoAsync(CancellationToken ct = default);
}

/// <summary>Identity /manage/info response.</summary>
public sealed record UserInfoDto(string Email);

public class OrderHubApiClient : IOrderHubApiClient
{
    public IComponentApi Components { get; }
    public IBoardApi Boards { get; }
    public IOrderApi Orders { get; }
    public IAuthApi Auth { get; }

    public OrderHubApiClient(HttpClient http)
    {
        var call = new ApiCall(http);
        Components = new ComponentApi(call);
        Boards = new BoardApi(call);
        Orders = new OrderApi(call);
        Auth = new AuthApi(call);
    }

    /// <summary>Shared request wrapper: credentials, JSON options, status translation.</summary>
    private sealed class ApiCall(HttpClient http)
    {
        public static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

        public async Task<T?> GetFromJsonAsync<T>(string url, CancellationToken ct)
        {
            using var response = await http.GetAsync(url, ct);
            if (response.StatusCode == HttpStatusCode.NotFound) return default;
            await EnsureSuccessAsync(response);
            return await response.Content.ReadFromJsonAsync<T>(JsonOptions, ct);
        }

        public async Task<T> PostAsync<T>(string url, object request, CancellationToken ct)
        {
            using var response = await http.PostAsJsonAsync(url, request, JsonOptions, ct);
            await EnsureSuccessAsync(response);
            return (await response.Content.ReadFromJsonAsync<T>(JsonOptions, ct))!;
        }

        public async Task PostAsync(string url, object request, CancellationToken ct)
        {
            using var response = await http.PostAsJsonAsync(url, request, JsonOptions, ct);
            await EnsureSuccessAsync(response);
        }

        public async Task PostEmptyAsync(string url, CancellationToken ct)
        {
            using var response = await http.PostAsync(url, content: null, ct);
            await EnsureSuccessAsync(response);
        }

        public async Task PutAsync(string url, object request, Type conflictStateType, CancellationToken ct)
        {
            using var response = await http.PutAsJsonAsync(url, request, JsonOptions, ct);
            await EnsureSuccessAsync(response, conflictStateType);
        }

        public async Task DeleteAsync(string url, CancellationToken ct)
        {
            using var response = await http.DeleteAsync(url, ct);
            await EnsureSuccessAsync(response);
        }

        public async Task<HttpResponseMessage> GetRawAsync(string url, CancellationToken ct)
        {
            var response = await http.GetAsync(url, ct);
            if (!response.IsSuccessStatusCode)
            {
                using var _ = response;
                throw await ApiExceptionFactory.CreateAsync(response, null);
            }
            return response;
        }

        private async Task EnsureSuccessAsync(HttpResponseMessage response, Type? conflictStateType = null)
        {
            if (response.IsSuccessStatusCode) return;
            throw await ApiExceptionFactory.CreateAsync(response, conflictStateType);
        }
    }

    private static class ApiExceptionFactory
    {
        public static async Task<Exception> CreateAsync(HttpResponseMessage response, Type? conflictStateType)
        {
            var body = await response.Content.ReadAsStringAsync();

            if (response.StatusCode == HttpStatusCode.Conflict && conflictStateType is not null)
            {
                var state = JsonSerializer.Deserialize(body, conflictStateType, ApiCall.JsonOptions);
                return (Exception)Activator.CreateInstance(
                    typeof(ConcurrencyConflictException<>).MakeGenericType(conflictStateType), state)!;
            }

            if (response.StatusCode == HttpStatusCode.Unauthorized)
            {
                return new UnauthorizedAccessException("You are not signed in (or your session expired).");
            }

            if (response.StatusCode == HttpStatusCode.Forbidden)
            {
                return new ApiValidationException("Request rejected (missing CSRF header or insufficient rights).");
            }

            if (response.StatusCode == HttpStatusCode.NotFound)
            {
                return new ApiValidationException("The requested record no longer exists.");
            }

            return new ApiValidationException($"API error {(int)response.StatusCode}: {body}");
        }
    }

    private sealed class ComponentApi(ApiCall call) : IComponentApi
    {
        public async Task<PagedResultDto<ComponentDto>> SearchAsync(string? searchTerm, int page, int pageSize, CancellationToken ct = default) =>
            await call.GetFromJsonAsync<PagedResultDto<ComponentDto>>(
                $"api/components?searchTerm={Uri.EscapeDataString(searchTerm ?? "")}&page={page}&pageSize={pageSize}", ct)
            ?? new PagedResultDto<ComponentDto>([], 0, page, pageSize);

        public async Task<ComponentDto?> GetByIdAsync(Guid id, CancellationToken ct = default) =>
            await call.GetFromJsonAsync<ComponentDto>($"api/components/{id}", ct);

        public async Task<ComponentDto> CreateAsync(CreateComponentRequest request, CancellationToken ct = default) =>
            await call.PostAsync<ComponentDto>("api/components", request, ct);

        public Task UpdateAsync(Guid id, UpdateComponentRequest request, CancellationToken ct = default) =>
            call.PutAsync($"api/components/{id}", request, typeof(ComponentDto), ct);

        public Task DeleteAsync(Guid id, CancellationToken ct = default) =>
            call.DeleteAsync($"api/components/{id}", ct);
    }

    private sealed class BoardApi(ApiCall call) : IBoardApi
    {
        public async Task<PagedResultDto<BoardDto>> SearchAsync(string? searchTerm, int page, int pageSize, CancellationToken ct = default) =>
            await call.GetFromJsonAsync<PagedResultDto<BoardDto>>(
                $"api/boards?searchTerm={Uri.EscapeDataString(searchTerm ?? "")}&page={page}&pageSize={pageSize}", ct)
            ?? new PagedResultDto<BoardDto>([], 0, page, pageSize);

        public async Task<BoardDetailDto?> GetByIdAsync(Guid id, CancellationToken ct = default) =>
            await call.GetFromJsonAsync<BoardDetailDto>($"api/boards/{id}", ct);

        public async Task<BoardDto> CreateAsync(CreateBoardRequest request, CancellationToken ct = default) =>
            await call.PostAsync<BoardDto>("api/boards", request, ct);

        public Task UpdateAsync(Guid id, UpdateBoardRequest request, CancellationToken ct = default) =>
            call.PutAsync($"api/boards/{id}", request, typeof(BoardDetailDto), ct);

        public Task DeleteAsync(Guid id, CancellationToken ct = default) =>
            call.DeleteAsync($"api/boards/{id}", ct);
    }

    private sealed class OrderApi(ApiCall call) : IOrderApi
    {
        public async Task<PagedResultDto<OrderDto>> SearchAsync(string? searchTerm, int page, int pageSize, CancellationToken ct = default) =>
            await call.GetFromJsonAsync<PagedResultDto<OrderDto>>(
                $"api/orders?searchTerm={Uri.EscapeDataString(searchTerm ?? "")}&page={page}&pageSize={pageSize}", ct)
            ?? new PagedResultDto<OrderDto>([], 0, page, pageSize);

        public async Task<OrderDetailDto?> GetByIdAsync(Guid id, CancellationToken ct = default) =>
            await call.GetFromJsonAsync<OrderDetailDto>($"api/orders/{id}", ct);

        public async Task<OrderDto> CreateAsync(CreateOrderRequest request, CancellationToken ct = default) =>
            await call.PostAsync<OrderDto>("api/orders", request, ct);

        public Task UpdateAsync(Guid id, UpdateOrderRequest request, CancellationToken ct = default) =>
            call.PutAsync($"api/orders/{id}", request, typeof(OrderDetailDto), ct);

        public Task DeleteAsync(Guid id, CancellationToken ct = default) =>
            call.DeleteAsync($"api/orders/{id}", ct);

        /// <summary>Returns the raw response so the caller can stream the file download.</summary>
        public Task<HttpResponseMessage> ExportForProductionAsync(Guid id, CancellationToken ct = default) =>
            call.GetRawAsync($"api/orders/{id}/export", ct);
    }

    private sealed class AuthApi(ApiCall call) : IAuthApi
    {
        public Task RegisterAsync(string email, string password, CancellationToken ct = default) =>
            call.PostAsync("register", new { email, password }, ct);

        public Task LoginAsync(string email, string password, CancellationToken ct = default) =>
            call.PostAsync("login?useCookies=true", new { email, password }, ct);

        public Task LogoutAsync(CancellationToken ct = default) =>
            call.PostEmptyAsync("logout", ct);

        /// <summary>Returns the signed-in user's info, or null when not authenticated.</summary>
        public Task<UserInfoDto?> GetUserInfoAsync(CancellationToken ct = default) =>
            call.GetFromJsonAsync<UserInfoDto>("manage/info", ct);
    }
}
