using Microsoft.AspNetCore.Diagnostics;
using OracleSqlPortal.Models;
using OracleSqlPortal.Services;

var builder = WebApplication.CreateBuilder(args);

// --- Services Configuration ---
// PortalDbService now handles ALL data (users, permissions, access requests,
// reset tokens, audit logs). appsettings.json stores only Environments + PortalDb.
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<PortalDbService>();
builder.Services.AddScoped<PortalDataService>();
builder.Services.AddScoped<QueryHistoryService>();
builder.Services.AddScoped<PermissionService>();
builder.Services.AddScoped<OracleService>();
builder.Services.AddScoped<MigrationService>();

// Configure Session (Required for Auth logic)
builder.Services.AddSession(options =>
{
    options.IdleTimeout = TimeSpan.FromMinutes(60);
    options.Cookie.HttpOnly = true;
    options.Cookie.IsEssential = true;
});

builder.Services.AddControllersWithViews();

var app = builder.Build();
app.UseExceptionHandler(errorApp =>
{
    errorApp.Run(async context =>
    {
        var ex = context.Features.Get<IExceptionHandlerFeature>()?.Error;
        if (ex != null)
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

        context.Response.Redirect("/Home/Error");
    });
});
// --- Seed Default Admin from Oracle DB ---
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<PortalDbService>();
    var config = scope.ServiceProvider.GetRequiredService<IConfiguration>();

    if (!db.UsernameExists("admin"))
    {
        db.AddUser(new OracleSqlPortal.Models.AppUser
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

    // Auto-grant all admins full permissions on every configured environment
    foreach (var envChild in config.GetSection("Environments").GetChildren())
        db.EnsureAdminPermissionsForEnv(envChild.Key);
}

// --- Middleware Pipeline ---
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
    app.UseHsts();
}

app.UseStaticFiles();
app.UseRouting();
app.UseSession();

// --- Unified QueryPad Routing ---
app.MapControllerRoute(
    name: "querypad",
    pattern: "QueryPad/{controller=Auth}/{action=Login}/{id?}");

// Root Redirect
app.MapGet("/", context => {
    context.Response.Redirect("/QueryPad/Auth/Login");
    return Task.CompletedTask;
});

app.Run();