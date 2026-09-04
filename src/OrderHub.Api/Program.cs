using Microsoft.EntityFrameworkCore;
using OrderHub.Application.Interfaces;
using OrderHub.Application.ProductionExport;
using OrderHub.Infrastructure.Persistence;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
// Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
builder.Services.AddOpenApi();

// Persistence: SQLite database (file path from configuration).
builder.Services.AddDbContext<SmtDbContext>(options =>
    options.UseSqlite(builder.Configuration.GetConnectionString("SmtDatabase")));

// Authentication: ASP.NET Core Identity with cookie auth (browser-friendly).
builder.Services
    .AddIdentityApiEndpoints<Microsoft.AspNetCore.Identity.IdentityUser>()
    .AddEntityFrameworkStores<SmtDbContext>();

builder.Services.ConfigureApplicationCookie(options =>
{
    options.Cookie.Name = "OrderHub.Auth";
    options.Cookie.HttpOnly = true;
    // SameSite=Lax: client and API live on the same site (localhost, different
    // ports — SameSite ignores ports), so Lax cookies are sent on XHR/fetch.
    // Real cross-site deployments (step 17, HTTPS domains) would need
    // SameSite=None + Secure behind TLS termination instead.
    options.Cookie.SameSite = Microsoft.AspNetCore.Http.SameSiteMode.Lax;
    options.Cookie.SecurePolicy = Microsoft.AspNetCore.Http.CookieSecurePolicy.SameAsRequest;
    options.ExpireTimeSpan = TimeSpan.FromHours(8);
});

builder.Services.AddAuthorization();

// Rate limiting: protects the Identity auth endpoints (/register, /login, /logout)
// from online brute-force. Applied per-IP via the "auth" policy only — no global limiter.
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

    // Strict partition for the auth endpoints (per IP).
    options.AddPolicy("auth", httpContext =>
        System.Threading.RateLimiting.RateLimitPartition.GetFixedWindowLimiter(
            httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            _ => new System.Threading.RateLimiting.FixedWindowRateLimiterOptions
            {
                PermitLimit = 10,
                Window = TimeSpan.FromMinutes(1),
                QueueLimit = 0
            }));
});

// Repositories & application services.
builder.Services.AddScoped<IOrderRepository, OrderRepository>();
builder.Services.AddScoped<IBoardRepository, BoardRepository>();
builder.Services.AddScoped<IComponentRepository, ComponentRepository>();
builder.Services.AddScoped<IOrderProductionService, OrderProductionService>();

// API controllers with JSON options for the DTO contracts.
builder.Services.AddControllers(options =>
    {
        // CSRF defense-in-depth: mutating requests must carry a custom header.
        options.Filters.Add<OrderHub.Api.Security.RequireCsrfHeaderAttribute>();
    })
    .AddJsonOptions(options =>
    {
        options.JsonSerializerOptions.PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase;
        options.JsonSerializerOptions.ReferenceHandler = System.Text.Json.Serialization.ReferenceHandler.IgnoreCycles;
    });

// CORS for the Blazor WebAssembly client (separate host, cookie auth needs credentials).
const string ClientCorsPolicy = "OrderHubClient";
var allowedOrigins = builder.Configuration
    .GetSection("Cors:AllowedOrigins").Get<string[]>()
    ?? (builder.Configuration["Cors:AllowedOrigins"]?
        .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
        ?? []);

builder.Services.AddCors(options =>
    options.AddPolicy(ClientCorsPolicy, policy => policy
        .WithOrigins(allowedOrigins)
        .AllowAnyHeader()
        .AllowAnyMethod()
        .AllowCredentials()));

// Real-time: SignalR for concurrency notifications.
builder.Services.AddSignalR();

var app = builder.Build();

// Configure the HTTP request pipeline.
app.UseCors(ClientCorsPolicy);

app.MapOpenApi();

// Apply pending migrations at startup — except when Testing: integration tests
// bootstrap the schema themselves via EnsureCreated on a shared in-memory DB,
// so Migrate() would collide with the existing tables.
if (!app.Environment.IsEnvironment("Testing"))
{
    using var scope = app.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<SmtDbContext>();
    db.Database.Migrate();
}

app.UseHttpsRedirection();
app.UseRateLimiter();

app.UseAuthentication();
app.UseAuthorization();

// Built-in Identity endpoints: /register, /login (cookie-based), rate-limited.
// Note: MapIdentityApi's /logout targets the bearer-token flow; with cookie auth
// we map sign-out explicitly via SignInManager below.
app.MapIdentityApi<Microsoft.AspNetCore.Identity.IdentityUser>().RequireRateLimiting("auth");

// Cookie-based logout (requires the auth cookie; invalidates it server-side).
app.MapPost("/api/auth/logout", async (
    Microsoft.AspNetCore.Identity.SignInManager<Microsoft.AspNetCore.Identity.IdentityUser> signInManager) =>
{
    await signInManager.SignOutAsync();
    return Results.NoContent();
}).RequireAuthorization();

// Real-time hub: same cookie auth, CORS-enabled for the WASM client.
app.MapHub<OrderHub.Api.RealTime.OrderHub>("/hubs/orders");

app.MapControllers();

app.Run();

