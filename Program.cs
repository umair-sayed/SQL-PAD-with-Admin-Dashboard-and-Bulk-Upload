using Microsoft.AspNetCore.Diagnostics;
using OracleSqlPortal.Models;
using OracleSqlPortal.Services;

var builder = WebApplication.CreateBuilder(args);

// Fetch routing values early for middleware/route setup
var loginPath = builder.Configuration["RoutingConfig:LoginPath"] ?? "/Auth/Login";
var errorPath = builder.Configuration["RoutingConfig:ErrorPath"] ?? "/Home/Error";

// ----------------------------------------------------
// Services Configuration
// ----------------------------------------------------
builder.Services.AddHttpContextAccessor();

builder.Services.AddScoped<PortalDbService>();
builder.Services.AddScoped<PortalDataService>();
builder.Services.AddScoped<QueryHistoryService>();
builder.Services.AddScoped<PermissionService>();
builder.Services.AddScoped<OracleService>();
builder.Services.AddScoped<MigrationService>();

// Session Configuration
builder.Services.AddSession(options =>
{
    options.IdleTimeout = TimeSpan.FromMinutes(60);
    options.Cookie.HttpOnly = true;
    options.Cookie.IsEssential = true;
});

builder.Services.AddControllersWithViews();

var app = builder.Build();

// ----------------------------------------------------
// Middleware Pipeline
// ----------------------------------------------------
if (!app.Environment.IsDevelopment())
{
    app.UseHsts();
}

app.UseStaticFiles();
app.UseRouting();
app.UseSession();

// ----------------------------------------------------
// Global Exception Handling
// ----------------------------------------------------
app.UseExceptionHandler(errorApp =>
{
    errorApp.Run(async context =>
    {
        var ex = context.Features.Get<IExceptionHandlerFeature>()?.Error;

        if (ex != null)
        {
            try
            {
                var db = context.RequestServices.GetRequiredService<PortalDbService>();

                db.LogErrorToDb(new ErrorLog
                {
                    Username = context.Session?.GetString("username"),
                    Method = "GLOBAL",
                    Message = ex.Message,
                    StackTrace = ex.ToString()
                });
            }
            catch
            {
                // Prevent secondary exception failure
            }
        }

        // Dynamic redirect from appsettings.json
        context.Response.Redirect(errorPath);
    });
});

// ----------------------------------------------------
// Seed Default Admin
// ----------------------------------------------------
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<PortalDbService>();
    var config = scope.ServiceProvider.GetRequiredService<IConfiguration>();

    if (!db.UsernameExists("admin"))
    {
        db.AddUser(new AppUser
        {
            Username = "admin",
            Password = "Admin@1234",
            DisplayName = "Administrator",
            Email = "admin@company.com",
            IsApproved = true,
            IsAdmin = true,
            IsMigration = true
        });
    }

    foreach (var envChild in config.GetSection("Environments").GetChildren())
    {
        db.EnsureAdminPermissionsForEnv(envChild.Key);
    }
}

// ----------------------------------------------------
// MVC Route Configuration
// ----------------------------------------------------
// Parsing the string path to dynamically construct the fallback route matching your appsettings
var routeSegments = loginPath.Trim('/').Split('/');
var defaultController = routeSegments.Length > 0 ? routeSegments[0] : "Auth";
var defaultAction = routeSegments.Length > 1 ? routeSegments[1] : "Login";

app.MapControllerRoute(
    name: "default",
    pattern: $"{{controller={defaultController}}}/{{action={defaultAction}}}/{{id?}}");

// ── Version API ────────────────────────────────────────────
app.MapGet("/api/version", (PortalDbService db) =>
{
    var v = db.GetAppVersion();
    if (v == null) return Results.Ok(new { versionNumber = "1.0.0", versionDate = "", expired = false });
    return Results.Ok(new
    {
        versionNumber = v.VersionNumber,
        versionDate = v.VersionDate.ToString("dd-MMM-yyyy"),
        expiryDate = v.ExpiryDate.ToString("dd-MMM-yyyy"),
        expired = v.IsExpired
    });
});

// ── External Apps API (from appsettings.json) ─────────────
app.MapGet("/api/external-apps", (IConfiguration config) =>
{
    var apps = config.GetSection("ExternalApps").GetChildren()
        .Select(c => new { name = c["Name"] ?? c.Key, url = c["Url"] ?? "#" })
        .Where(a => !string.IsNullOrWhiteSpace(a.url) && a.url != "#")
        .ToList();
    return Results.Ok(apps);
});

// ----------------------------------------------------
// Root Redirect
// ----------------------------------------------------
app.MapGet("/", context =>
{
    // Dynamic root redirect using the config value
    context.Response.Redirect(loginPath);
    return Task.CompletedTask;
});

app.Run();