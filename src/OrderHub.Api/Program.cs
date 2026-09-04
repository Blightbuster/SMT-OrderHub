using Microsoft.EntityFrameworkCore;
using OrderHub.Application.Interfaces;
using OrderHub.Application.ProductionExport;
using OrderHub.Infrastructure.Persistence;
using Serilog;

var builder = WebApplication.CreateBuilder(args);

// Serilog: single logging pipeline for the whole API. The static Log.* calls in
// Application services (e.g. OrderProductionService audit trails) now flow here.
// Config lives in Serilog section of appsettings; console sink = Azure Log Stream.
builder.Host.UseSerilog((context, loggerConfiguration) => loggerConfiguration
    .ReadFrom.Configuration(context.Configuration)
    .Enrich.FromLogContext()
    .WriteTo.Console(
        outputTemplate: "[{Timestamp:HH:mm:ss} {Level:u3}] {SourceContext}: {Message:lj}{NewLine}{Exception}"));

// Category logger for the CORS diagnostics below (flows through Serilog).
var corsLog = Log.ForContext("SourceContext", "CORS");

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

// Startup diagnostic: shows exactly which origins the policy resolved to.
// If this list is empty or missing the client origin, every preflight is denied.
corsLog.Information("CORS policy '{Policy}' resolved origins: [{Origins}]",
    ClientCorsPolicy, string.Join(", ", allowedOrigins));

corsLog.Information("Config sources — Cors:AllowedOrigins section type: {SectionValueKind}, raw value: {Raw}",
    builder.Configuration.GetSection("Cors:AllowedOrigins").Value is null ? "array-or-null" : "string",
    builder.Configuration["Cors:AllowedOrigins"] ?? "<null>");

builder.Services.AddCors(options =>
    options.AddPolicy(ClientCorsPolicy, policy => policy
        .WithOrigins(allowedOrigins)
        .AllowAnyHeader()
        .AllowAnyMethod()
        .AllowCredentials()));

// Real-time: SignalR for concurrency notifications.
builder.Services.AddSignalR();

var app = builder.Build();

// Serilog request logging: one line per HTTP request with method, path, status
// and duration. RequestLoggingPhase.Start/End keeps ordering deterministic.
app.UseSerilogRequestLogging();

// CORS diagnostics middleware: logs every request that carries an Origin header
// with the decision the CORS middleware will make. Place BEFORE UseCors so we
// see the origin even when the request is later short-circuited.
app.Use(async (context, next) =>
{
    var origin = context.Request.Headers.Origin.ToString();
    if (!string.IsNullOrEmpty(origin))
    {
        var allowed = allowedOrigins.Contains(origin, StringComparer.OrdinalIgnoreCase);
        corsLog.Information("{Method} {Path} Origin={Origin} -> {Result}",
            context.Request.Method, context.Request.Path, origin,
            allowed ? "ALLOWED" : "DENIED (not in configured origins)");
    }
    await next();
});

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
    var migrationLog = Log.ForContext("SourceContext", "Startup");
    migrationLog.Information("Applying database migrations...");
    db.Database.Migrate();
    migrationLog.Information("Database migrations applied.");
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

// Flush buffered log entries on shutdown so nothing is lost in the console sink.
Log.CloseAndFlush();

