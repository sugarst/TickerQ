using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TickerQ.BackgroundServices;
using TickerQ.Dashboard.DependencyInjection;
using TickerQ.DependencyInjection;
using TickerQ.Licensing;
using TickerQ.Utilities.Entities;
using TickerQ.Utilities.Enums;
using TickerQ.Utilities.Interfaces;
using TickerQ.Utilities.Interfaces.Managers;
using TickerQ.Utilities.Licensing;

namespace TickerQ.Tests.Licensing;

[Collection("TickerCancellationTokenState")]
public sealed class DesktopTestLicenseTests : IDisposable
{
    private const string Api = "/tickerq/dashboard/api/dashboard";
    private const string AuthScheme = "TestDashboardUser";
    private readonly DirectoryInfo _output = Directory.CreateTempSubdirectory("tickerq-test-issuer-");

    public void Dispose() => _output.Delete(recursive: true);

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Exported_fixture_is_rejected_by_the_default_trust_store(bool customTerm)
    {
        var now = DateTimeOffset.UtcNow;
        var directory = await TestLicenseIssuer.ExportAsync(_output.FullName, now, customTerm ? now.AddDays(45) : null);
        var state = LicenseCertificateReader.Read(TestLicenseIssuer.CertificateFileName, directory, now);

        Assert.Equal(TickerQLicenseStatus.Invalid, state.Status);
        Assert.False(state.ExecutionAllowed);
        Assert.Contains("untrusted signing key", state.Message);
    }

    [Fact]
    public async Task Exported_fixture_honors_the_selected_expiration()
    {
        var issuedAt = new DateTimeOffset(2026, 1, 2, 3, 4, 0, TimeSpan.Zero);
        var expiresAt = issuedAt.AddDays(45).AddMinutes(17);
        var directory = await TestLicenseIssuer.ExportAsync(_output.FullName, issuedAt, expiresAt);
        var authority = TestLicenseIssuer.ReadAuthority(directory);

        var before = LicenseCertificateReader.Read(TestLicenseIssuer.CertificateFileName, directory,
            expiresAt.AddSeconds(-1), authority.Resolve);
        var after = LicenseCertificateReader.Read(TestLicenseIssuer.CertificateFileName, directory,
            expiresAt, authority.Resolve);

        Assert.Equal(expiresAt, before.ExpiresAt);
        Assert.Equal(TickerQLicenseStatus.Expiring, before.Status);
        Assert.Equal(TickerQLicenseStatus.Expired, after.Status);
        Assert.True(after.ExecutionAllowed);
    }

    [Fact]
    public async Task Exported_fixture_runs_time_and_cron_jobs_and_keeps_viewer_writes_forbidden()
    {
        var now = DateTimeOffset.UtcNow;
        var configuredDirectory = Environment.GetEnvironmentVariable("TICKERQ_TEST_LICENSE_DIRECTORY");
        var directory = string.IsNullOrWhiteSpace(configuredDirectory)
            ? await TestLicenseIssuer.ExportAsync(_output.FullName, now, now.AddDays(45))
            : Path.GetFullPath(configuredDirectory);
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            Args = [],
            ContentRootPath = directory,
        });
        builder.WebHost.UseTestServer();
        builder.Services.AddAuthentication(AuthScheme)
            .AddScheme<AuthenticationSchemeOptions, DashboardUserHandler>(AuthScheme, _ => { });
        builder.Services.AddTickerQ(options =>
        {
            options.UseApplicationNamespace("desktop-license-fixture").UseReconciliationEpoch(1);
            options.ConfigureScheduler(scheduler => scheduler.MinPollingInterval = TimeSpan.FromMilliseconds(20));
            options.AddDashboard(dashboard => dashboard.WithHostAuthentication()
                .SetReadOnly(context => !context.User.IsInRole("ops")));
        });
        UseExportedFixture(builder.Services, directory);

        const string function = "DesktopTestLicenseProbe";
        builder.Services.MapTicker(function, static (_, _) => Task.CompletedTask);
        await using var app = builder.Build();
        app.UseAuthentication();
        app.UseTickerQ();
        await app.StartAsync();
        try
        {
            var license = app.Services.GetRequiredService<TickerQLicenseStateProvider>().Current;
            Assert.True(license.ExecutionAllowed, license.Message);
            Assert.Contains(license.Status, new[] { TickerQLicenseStatus.Active, TickerQLicenseStatus.Expiring });
            Assert.Equal(TestLicenseIssuer.KeyId, license.KeyId);

            using var viewer = app.GetTestClient();
            viewer.DefaultRequestHeaders.Add("X-Test-Role", "viewer");
            using var ops = app.GetTestClient();
            ops.DefaultRequestHeaders.Add("X-Test-Role", "ops");

            using var viewerRead = await viewer.GetAsync("/tickerq/dashboard/api/license");
            Assert.Equal(HttpStatusCode.OK, viewerRead.StatusCode);

            var request = new { function, executionTime = DateTime.UtcNow.AddSeconds(1) };
            using var forbidden = await viewer.PostAsJsonAsync($"{Api}/time-tickers", request);
            Assert.Equal(HttpStatusCode.Forbidden, forbidden.StatusCode);
            Assert.Equal("Dashboard is running in read-only mode.", await forbidden.Content.ReadAsStringAsync());

            using var created = await ops.PostAsJsonAsync($"{Api}/time-tickers", request);
            created.EnsureSuccessStatusCode();
            var timeId = (await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();

            var cronManager = app.Services.GetRequiredService<ICronTickerManager<CronTickerEntity>>();
            var cron = await cronManager.AddAsync(new CronTickerEntity { Function = function, Expression = "* * * * * *" });
            Assert.True(cron.IsSucceeded, cron.Exception?.ToString());
            var cronId = cron.Result.Id;

            var store = app.Services.GetRequiredService<ITickerPersistenceProvider<TimeTickerEntity, CronTickerEntity>>();
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            while (!timeout.IsCancellationRequested)
            {
                var time = await store.GetTimeTickerById(timeId, timeout.Token);
                var occurrences = await store.GetAllCronTickerOccurrences(row => row.CronTickerId == cronId, timeout.Token);
                if (time?.Status is TickerStatus.Done or TickerStatus.DueDone &&
                    occurrences.Any(row => row.Status is TickerStatus.Done or TickerStatus.DueDone))
                    return;

                await Task.Delay(25);
            }

            Assert.Fail("The scheduler did not persist completion for both the TimeTicker and a scheduled Cron occurrence.");
        }
        finally
        {
            await app.StopAsync();
        }
    }

    private static void UseExportedFixture(IServiceCollection services, string directory)
    {
        var registration = services.Single(service => service.ServiceType == typeof(IHostedService) &&
            service.ImplementationType == typeof(TickerQLicenseHostedService));
        // Publish the reader's verdict in the original startup slot, before scheduler initialization.
        services[services.IndexOf(registration)] = ServiceDescriptor.Singleton<IHostedService>(provider =>
            new FixtureLicenseService(directory, provider.GetRequiredService<TickerQLicenseStateProvider>(),
                provider.GetRequiredService<ITickerClock>()));
    }

    private sealed class FixtureLicenseService(string directory, TickerQLicenseStateProvider state, ITickerClock clock)
        : IHostedService
    {
        public Task StartAsync(CancellationToken cancellationToken)
        {
            var authority = TestLicenseIssuer.ReadAuthority(directory);
            state.Publish(LicenseCertificateReader.Read(TestLicenseIssuer.CertificateFileName, directory,
                new DateTimeOffset(clock.UtcNow, TimeSpan.Zero), authority.Resolve));
            return Task.CompletedTask;
        }

        public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }

    private sealed class DashboardUserHandler(IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger, System.Text.Encodings.Web.UrlEncoder encoder)
        : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            var role = Request.Headers["X-Test-Role"].ToString();
            if (role is not ("viewer" or "ops"))
                return Task.FromResult(AuthenticateResult.NoResult());

            var identity = new ClaimsIdentity([new Claim(ClaimTypes.Name, role), new Claim(ClaimTypes.Role, role)], Scheme.Name);
            return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(identity), Scheme.Name)));
        }
    }
}
