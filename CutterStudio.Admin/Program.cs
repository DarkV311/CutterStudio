using System.Security.Cryptography;
using System.Diagnostics;
using System.Text;
using CutterStudio.Admin;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Http.Features;

var builder = WebApplication.CreateBuilder(args);
builder.WebHost.ConfigureKestrel(options =>
{
    options.Limits.MaxRequestBodySize = 500L * 1024 * 1024;
});

if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("ASPNETCORE_URLS"))
    && string.IsNullOrWhiteSpace(builder.Configuration["urls"]))
{
    builder.WebHost.UseUrls("http://0.0.0.0:5080");
}

builder.Services.AddSingleton<AdminRepository>();
builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(options =>
    {
        options.LoginPath = "/admin/login";
        options.AccessDeniedPath = "/admin/login";
        options.Cookie.Name = "CutterStudio.Admin";
        options.Cookie.HttpOnly = true;
        options.Cookie.SameSite = SameSiteMode.Strict;
    });
builder.Services.AddAuthorization();
builder.Services.Configure<FormOptions>(options =>
{
    options.MultipartBodyLengthLimit = 500L * 1024 * 1024;
});

var app = builder.Build();
await app.Services.GetRequiredService<AdminRepository>().InitializeAsync();

var releaseDirectory = Environment.GetEnvironmentVariable("CUTTER_UPDATES_DIR")
                       ?? app.Configuration["UpdatesDirectory"]
                       ?? @"F:\Cutter\Updates";
Directory.CreateDirectory(releaseDirectory);

app.UseAuthentication();
app.UseAuthorization();
app.UseStaticFiles(new StaticFileOptions
{
    RequestPath = "/downloads",
    FileProvider = new Microsoft.Extensions.FileProviders.PhysicalFileProvider(releaseDirectory)
});

app.MapGet("/", () => Results.Redirect("/admin"));

app.MapPost("/api/licenses/activate", async (LicenseActivationRequest request, AdminRepository repo) =>
    Results.Json(await repo.ActivateAsync(request)));

app.MapGet("/api/releases/latest", async (string? channel, AdminRepository repo) =>
    Results.Json(await repo.GetLatestReleaseAsync(channel ?? "stable")));

app.MapGet("/api/admin/licenses", async (HttpContext context, AdminRepository repo) =>
{
    if (!AdminPasswordMatches(context, app.Configuration))
        return Results.Unauthorized();
    return Results.Json(await repo.GetAdminLicensesAsync());
});

app.MapPost("/api/admin/licenses/create", async (HttpContext context, CreateLicenseRequest request, AdminRepository repo) =>
{
    if (!AdminPasswordMatches(context, app.Configuration))
        return Results.Unauthorized();
    var expiresUtc = request.DurationDays <= 0
        ? request.ExpiresUtc
        : DateTime.UtcNow.Date.AddDays(request.DurationDays + 1).AddTicks(-1);
    var license = await repo.CreateLicenseAsync(
        request.CustomerName,
        request.CustomerEmail,
        expiresUtc,
        request.MaxActivations,
        request.Notes);
    return Results.Json(license);
});

app.MapPost("/api/admin/licenses/block", async (HttpContext context, SetLicenseBlockedRequest request, AdminRepository repo) =>
{
    if (!AdminPasswordMatches(context, app.Configuration))
        return Results.Unauthorized();
    await repo.SetLicenseBlockedAsync(request.Id, request.Blocked);
    return Results.Json(new { success = true });
});

app.MapGet("/admin/login", () => Results.Content(LoginHtml(), "text/html"));
app.MapPost("/admin/login", async (HttpContext context) =>
{
    var form = await context.Request.ReadFormAsync();
    var password = form["password"].ToString();
    var configured = Environment.GetEnvironmentVariable("CUTTER_ADMIN_PASSWORD")
                     ?? app.Configuration["AdminPassword"]
                     ?? "change-me-now";
    if (!CryptographicOperations.FixedTimeEquals(
            Encoding.UTF8.GetBytes(password),
            Encoding.UTF8.GetBytes(configured)))
    {
        return Results.Content(LoginHtml("Invalid password."), "text/html");
    }

    var identity = new System.Security.Claims.ClaimsIdentity(
        [new System.Security.Claims.Claim(System.Security.Claims.ClaimTypes.Name, "admin")],
        CookieAuthenticationDefaults.AuthenticationScheme);
    await context.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme,
        new System.Security.Claims.ClaimsPrincipal(identity));
    return Results.Redirect("/admin");
});

app.MapGet("/admin/logout", async (HttpContext context) =>
{
    await context.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
    return Results.Redirect("/admin/login");
});

app.MapGet("/admin", async (AdminRepository repo) =>
{
    var licenses = await repo.GetLicensesAsync();
    var releases = await repo.GetReleasesAsync();
    return Results.Content(AdminHtml(licenses, releases), "text/html");
}).RequireAuthorization();

app.MapPost("/admin/licenses/create", async (HttpContext context, AdminRepository repo) =>
{
    var form = await context.Request.ReadFormAsync();
    DateTime? expires = null;
    if (DateTime.TryParse(form["expiresUtc"], out var parsed))
        expires = DateTime.SpecifyKind(parsed, DateTimeKind.Utc);
    await repo.CreateLicenseAsync(
        form["customerName"].ToString(),
        form["customerEmail"].ToString(),
        expires,
        int.TryParse(form["maxActivations"], out var max) ? max : 1,
        form["notes"].ToString());
    return Results.Redirect("/admin");
}).RequireAuthorization();

app.MapPost("/admin/licenses/block", async (HttpContext context, AdminRepository repo) =>
{
    var form = await context.Request.ReadFormAsync();
    await repo.SetLicenseBlockedAsync(long.Parse(form["id"]!), true);
    return Results.Redirect("/admin");
}).RequireAuthorization();

app.MapPost("/admin/licenses/unblock", async (HttpContext context, AdminRepository repo) =>
{
    var form = await context.Request.ReadFormAsync();
    await repo.SetLicenseBlockedAsync(long.Parse(form["id"]!), false);
    return Results.Redirect("/admin");
}).RequireAuthorization();

app.MapPost("/admin/releases/create", async (HttpContext context, AdminRepository repo) =>
{
    var form = await context.Request.ReadFormAsync();
    var file = form.Files["releaseFile"];
    if (file is null || file.Length == 0)
        return Results.BadRequest("releaseFile is required.");

    var safeFile = $"{DateTime.UtcNow:yyyyMMddHHmmss}_{Path.GetFileName(file.FileName)}";
    var path = Path.Combine(releaseDirectory, safeFile);
    await using (var stream = File.Create(path))
        await file.CopyToAsync(stream);

    try
    {
        using var archive = System.IO.Compression.ZipFile.OpenRead(path);
        if (!archive.Entries.Any(entry => entry.FullName.EndsWith("CutterStudio.exe", StringComparison.OrdinalIgnoreCase)))
        {
            File.Delete(path);
            return Results.BadRequest("Release ZIP must contain CutterStudio.exe.");
        }
    }
    catch
    {
        File.Delete(path);
        return Results.BadRequest("Uploaded release file is not a valid ZIP archive.");
    }

    var sha = await Sha256Async(path);
    var url = $"/downloads/{Uri.EscapeDataString(safeFile)}";
    await repo.CreateReleaseAsync(
        form["version"].ToString(),
        form["channel"].ToString(),
        safeFile,
        url,
        sha,
        form["notes"].ToString(),
        form["published"] == "on");
    return Results.Redirect("/admin");
}).RequireAuthorization();

_ = Task.Run(async () =>
{
    await Task.Delay(1200);
    try
    {
        Process.Start(new ProcessStartInfo("http://localhost:5080/admin") { UseShellExecute = true });
    }
    catch
    {
        // The admin server still runs even if the browser cannot be opened automatically.
    }
});

app.Run();

static async Task<string> Sha256Async(string path)
{
    await using var stream = File.OpenRead(path);
    var hash = await SHA256.HashDataAsync(stream);
    return Convert.ToHexString(hash).ToLowerInvariant();
}

static bool AdminPasswordMatches(HttpContext context, IConfiguration configuration)
{
    var provided = context.Request.Headers["X-Admin-Password"].ToString();
    var configured = Environment.GetEnvironmentVariable("CUTTER_ADMIN_PASSWORD")
                     ?? configuration["AdminPassword"]
                     ?? "change-me-now";
    return !string.IsNullOrWhiteSpace(provided) &&
           CryptographicOperations.FixedTimeEquals(
               Encoding.UTF8.GetBytes(provided),
               Encoding.UTF8.GetBytes(configured));
}

static string LoginHtml(string? error = null) =>
    $$"""
    <!doctype html><html><head><meta charset="utf-8"><title>Cutter Studio Admin</title>
    <style>body{font-family:Segoe UI;background:#111318;color:#f3f3f3;display:grid;place-items:center;height:100vh}
    form{background:#1c2028;padding:28px;border-radius:10px;width:360px}input,button{width:100%;padding:10px;margin:8px 0}
    button{background:#30b7a3;border:0;font-weight:700}.err{color:#ff7b88}</style></head><body>
    <form method="post"><h2>Cutter Studio Admin</h2>
    <p class="err">{{System.Net.WebUtility.HtmlEncode(error ?? "")}}</p>
    <input type="password" name="password" placeholder="Admin password" autofocus>
    <button>Login</button><p>Set CUTTER_ADMIN_PASSWORD before production use.</p></form>
    </body></html>
    """;

static string AdminHtml(IReadOnlyList<LicenseRecord> licenses, IReadOnlyList<ReleaseRecord> releases)
{
    var licenseRows = string.Join("", licenses.Select(l =>
        $"""
        <tr><td><code>{H(l.LicenseKey)}</code></td><td>{H(l.CustomerName)}</td><td>{H(l.CustomerEmail)}</td>
        <td>{l.ExpiresUtc?.ToString("yyyy-MM-dd") ?? "Never"}</td><td>{l.MaxActivations}</td>
        <td>{(l.IsBlocked ? "Blocked" : "Active")}</td><td>
        <form method="post" action="/admin/licenses/{(l.IsBlocked ? "unblock" : "block")}">
        <input type="hidden" name="id" value="{l.Id}"><button>{(l.IsBlocked ? "Unblock" : "Block")}</button></form></td></tr>
        """));
    var releaseRows = string.Join("", releases.Select(r =>
        $"""
        <tr><td>{H(r.Version)}</td><td>{H(r.Channel)}</td><td><a href="{H(r.DownloadUrl)}">{H(r.FileName)}</a></td>
        <td><code>{H(r.Sha256[..Math.Min(12, r.Sha256.Length)])}...</code></td><td>{(r.IsPublished ? "Published" : "Draft")}</td>
        <td>{r.CreatedUtc:u}</td></tr>
        """));
    return $$"""
    <!doctype html><html><head><meta charset="utf-8"><title>Cutter Studio Admin</title>
    <style>body{font-family:Segoe UI;background:#f4f6f8;margin:0;color:#151820}.top{background:#111318;color:white;padding:14px 22px}
    main{padding:20px}.grid{display:grid;grid-template-columns:1fr 1fr;gap:18px}section{background:white;border-radius:10px;padding:16px;box-shadow:0 1px 5px #0002}
    input,textarea,button{padding:8px;margin:4px;width:calc(100% - 18px)}button{background:#30b7a3;border:0;border-radius:5px;cursor:pointer}
    table{width:100%;border-collapse:collapse;background:white}td,th{border-bottom:1px solid #ddd;padding:8px;text-align:left;font-size:13px}code{font-family:Consolas}</style>
    </head><body><div class="top"><b>Cutter Studio Admin</b> <a style="color:#9be" href="/admin/logout">Logout</a></div><main>
    <div class="grid">
    <section><h2>Create license</h2><form method="post" action="/admin/licenses/create">
    <input name="customerName" placeholder="Customer name" required>
    <input name="customerEmail" placeholder="Customer email">
    <input name="expiresUtc" type="date" placeholder="Expiry date UTC">
    <input name="maxActivations" type="number" min="1" value="1">
    <textarea name="notes" placeholder="Notes"></textarea><button>Create License</button></form></section>
    <section><h2>Upload release</h2><form method="post" action="/admin/releases/create" enctype="multipart/form-data">
    <input name="version" placeholder="Version e.g. 1.2.0" required>
    <input name="channel" value="stable">
    <input name="releaseFile" type="file" required>
    <textarea name="notes" placeholder="Release notes"></textarea>
    <label><input name="published" type="checkbox" checked style="width:auto"> Published</label>
    <button>Upload Release</button></form></section></div>
    <h2>Licenses</h2><table><tr><th>Key</th><th>Name</th><th>Email</th><th>Expires</th><th>Max</th><th>Status</th><th>Action</th></tr>{{licenseRows}}</table>
    <h2>Releases</h2><table><tr><th>Version</th><th>Channel</th><th>File</th><th>SHA256</th><th>Status</th><th>Created</th></tr>{{releaseRows}}</table>
    </main></body></html>
    """;
}

static string H(string value) => System.Net.WebUtility.HtmlEncode(value);
