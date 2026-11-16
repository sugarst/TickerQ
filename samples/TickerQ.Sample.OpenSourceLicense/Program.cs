using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using TickerQ.Dashboard.DependencyInjection;
using TickerQ.DependencyInjection;
using TickerQ.EntityFrameworkCore.DbContextFactory;
using TickerQ.EntityFrameworkCore.DependencyInjection;
using TickerQ.Utilities.DashboardDtos;
using TickerQ.Utilities.Entities;
using TickerQ.Utilities.Enums;
using TickerQ.Utilities.Interfaces;
using TickerQ.Utilities.Models;

var builder = WebApplication.CreateBuilder(args);
string RequiredPath(string key) => builder.Configuration[key]
    ?? throw new ArgumentException($"Specify --{key} <path>.");

try
{
    var expiresAt = LocalCertificate.Verify(RequiredPath("certificate"), RequiredPath("authority"), DateTimeOffset.UtcNow);
    Console.WriteLine($"Desktop certificate accepted through {expiresAt:O}.");
}
catch (Exception error)
{
    Console.Error.WriteLine($"Startup rejected. {error.Message}");
    return 1;
}

var database = new SqliteConnectionStringBuilder { DataSource = RequiredPath("database") }.ToString();
var smoke = builder.Configuration.GetValue<bool>("smoke");
var migrate = builder.Configuration.GetValue<bool>("migrate");
builder.WebHost.ConfigureKestrel(server => server.Listen(IPAddress.Loopback, smoke ? 0 : 5219));
builder.Services.AddTickerQ(options =>
{
    options.UseDefinedCronApplicationNamespace("open-source-license-sample");
    options.UseReconciliationEpoch(1);
    options.ConfigureScheduler(scheduler => scheduler.MinPollingInterval = TimeSpan.FromMilliseconds(100));
    options.AddOperationalStore(ef => ef.UseTickerQDbContext<TickerQDbContext>(db =>
        db.UseSqlite(database, sqlite =>
        {
            if (migrate)
                sqlite.MigrationsAssembly(typeof(Program).Assembly.GetName().Name);
        })));
    options.AddDashboard(dashboard => dashboard.AllowAnonymousDashboard());
});
const string function = "DesktopCertificateProbe";
builder.Services.MapTicker(function, static (_, _) => Task.CompletedTask);

await using var app = builder.Build();
await using (var scope = app.Services.CreateAsyncScope())
{
    var db = scope.ServiceProvider.GetRequiredService<TickerQDbContext>();
    if (migrate)
    {
        await db.Database.MigrateAsync();
        Console.WriteLine($"SQLite migrations applied: {string.Join(", ", await db.Database.GetAppliedMigrationsAsync())}");
    }
    else
    {
        await db.Database.EnsureCreatedAsync();
        Console.WriteLine("Standalone SQLite schema ready.");
    }
}
app.UseTickerQ();

if (!smoke)
{
    await app.RunAsync();
    return 0;
}

await app.StartAsync();
using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(25));
try
{
    var address = app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!
        .Addresses.First(address => address.StartsWith("http://127.0.0.1:", StringComparison.Ordinal));
    using var client = new HttpClient { BaseAddress = new Uri(address) };
    const string dashboard = "/tickerq/dashboard";
    using var page = await client.GetAsync($"{dashboard}/", timeout.Token);
    page.EnsureSuccessStatusCode();
    var html = await page.Content.ReadAsStringAsync(timeout.Token);
    if (!html.Contains("type=\"module\"", StringComparison.Ordinal))
        throw new InvalidOperationException("Build the snapshot Dashboard frontend before running the smoke check.");

    const string api = $"{dashboard}/api/dashboard";
    using var response = await client.PostAsJsonAsync($"{api}/time-tickers",
        new { function, executionTime = DateTime.UtcNow.AddSeconds(1) }, timeout.Token);
    response.EnsureSuccessStatusCode();
    var job = await response.Content.ReadFromJsonAsync<JsonElement>(cancellationToken: timeout.Token);
    var timeId = job.GetProperty("id").GetGuid();

    await using var scope = app.Services.CreateAsyncScope();
    var services = scope.ServiceProvider;
    var store = services.GetRequiredService<ITickerPersistenceProvider<TimeTickerEntity, CronTickerEntity>>();
    while (true)
    {
        var time = await store.GetTimeTickerById(timeId, timeout.Token);
        if (time?.Status is TickerStatus.Done or TickerStatus.DueDone)
            break;

        await Task.Delay(100, timeout.Token);
    }

    using var read = await client.GetAsync($"{api}/time-tickers/{timeId}", timeout.Token);
    read.EnsureSuccessStatusCode();
    Console.WriteLine("PASS: Dashboard HTML and job APIs responded. The time ticker completed in SQLite.");

    const string expression = "*/3 * * * * *";
    using var cronResponse = await client.PostAsJsonAsync($"{api}/cron-tickers",
        new { function, expression, description = "Recurring certificate smoke probe" }, timeout.Token);
    cronResponse.EnsureSuccessStatusCode();
    var cron = await cronResponse.Content.ReadFromJsonAsync<JsonElement>(cancellationToken: timeout.Token);
    var cronId = cron.GetProperty("id").GetGuid();
    Console.WriteLine($"Cron definition saved: {cronId} ({expression}). Waiting for scheduled occurrences.");

    var jsonOptions = new JsonSerializerOptions(JsonSerializerDefaults.Web);
    jsonOptions.Converters.Add(new JsonStringEnumConverter());
    while (true)
    {
        using var occurrencesResponse = await client.PostAsJsonAsync($"{api}/cron-occurrences/{cronId}/query",
            new { statuses = new[] { "Done", "DueDone" } }, timeout.Token);
        occurrencesResponse.EnsureSuccessStatusCode();
        var pageResult = await occurrencesResponse.Content.ReadFromJsonAsync<PaginationResult<CronOccurrenceFlatDto>>(
            jsonOptions, timeout.Token);
        var completed = pageResult?.Items.OrderBy(item => item.ScheduledFor).Take(2).ToArray() ?? [];
        if (completed.Length == 2)
        {
            if (completed[1].ScheduledFor - completed[0].ScheduledFor != TimeSpan.FromSeconds(3) ||
                completed.Any(item => item.ScheduledFor.Second % 3 != 0 ||
                                      item.ExecutedAt is null || item.ExecutedAt < item.ScheduledFor))
                throw new InvalidOperationException("The two completed occurrences did not match consecutive Cron slots.");

            foreach (var occurrence in completed)
                Console.WriteLine($"Dashboard Cron {occurrence.Id}: scheduled {occurrence.ScheduledFor:O}, " +
                                  $"executed {occurrence.ExecutedAt:O}, status {occurrence.Status}.");

            Console.WriteLine("PASS: Cron completed in two consecutive scheduled slots and Dashboard returned both records.");
            return 0;
        }

        await Task.Delay(200, timeout.Token);
    }

}
catch (OperationCanceledException) when (timeout.IsCancellationRequested)
{
    Console.Error.WriteLine("The scheduled smoke checks did not finish within 25 seconds.");
    return 1;
}
finally
{
    await app.StopAsync();
}
