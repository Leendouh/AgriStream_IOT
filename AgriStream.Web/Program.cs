using AgriStream.Web.Options;
using AgriStream.Web.Services;
using Microsoft.AspNetCore.Authentication.Cookies;

var builder = WebApplication.CreateBuilder(args);

// Azure resource configuration (bound from "Azure" section / App Service settings).
builder.Services.Configure<AzureOptions>(builder.Configuration.GetSection(AzureOptions.SectionName));

builder.Services.AddControllersWithViews();

// Application Insights (reads ApplicationInsights:ConnectionString /
// APPLICATIONINSIGHTS_CONNECTION_STRING set by the App Service).
builder.Services.AddApplicationInsightsTelemetry();

// Azure data services.
builder.Services.AddSingleton<SqlService>();
builder.Services.AddSingleton<StorageService>();
builder.Services.AddHttpClient<FunctionsClient>();

// Cookie authentication against the Azure SQL Users table.
builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(options =>
    {
        options.LoginPath = "/Account/Login";
        options.AccessDeniedPath = "/Account/AccessDenied";
        options.ExpireTimeSpan = TimeSpan.FromHours(8);
    });
builder.Services.AddAuthorization();

var app = builder.Build();

// Create relational tables + seed data on first run (idempotent).
try
{
    await app.Services.GetRequiredService<SqlService>().EnsureSchemaAsync();
}
catch (Exception ex)
{
    app.Logger.LogWarning(ex, "Azure SQL schema check failed - app will run but DB features may be unavailable.");
}

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseStaticFiles();

app.UseRouting();

app.UseAuthentication();
app.UseAuthorization();

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}");

app.Run();
