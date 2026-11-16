# TickerQ Certificates

Run the AtomUI desktop project with the .NET 10 SDK from the repository root.

```sh
dotnet run --project tests/TickerQ.TestLicenseIssuer.Desktop
```

The desktop tool opens in Simplified Chinese. Use **语言** to switch to English.

## Service certificate

Open the licensing portal from the app and download the certificate for your entitlement. The portal determines its expiration. Choose **Export a portal certificate**, select the downloaded `.tqlicense` file, and choose a local output folder. The app copies the file unchanged as `tickerq.tqlicense`. The service verifies the signature at startup.

Place the exported file in the content root of each TickerQ host or set its path inside `AddTickerQ`:

```csharp
options.UseLicense("licenses/tickerq.tqlicense");
```

Relative paths resolve from the content root. Give the service identity read access to the file, restart the hosts, and check the startup logs or the Dashboard **License** page.

## Local integration test certificate

Open the expiration picker, select a future date and time in Beijing time (UTC+8), and confirm the selection. Choose **生成并导出测试证书** (**Generate and export test certificate**). On Windows, the app saves `tickerq.tqlicense` and `fixture-authority.json` in a new subfolder under `%LOCALAPPDATA%\TickerQ\TestCertificates`. Choose **打开文件夹** (**Open folder**) to view the exported files. The app shows both time zones and writes the expiration in UTC. Each export uses a fresh Ed25519 key and saves only its public key in the authority file. The custom-date payload uses the subscription schema because the Evaluation schema requires a fixed 30-day term.

Point the integration test at an exported folder to exercise the scheduler and Dashboard with those exact files. The test host verifies the certificate using the exported public key before the scheduler starts. Standard TickerQ hosts reject this test signature.

```powershell
$env:TICKERQ_TEST_LICENSE_DIRECTORY = 'C:\path\to\tickerq-test-...'
dotnet test tests/TickerQ.Tests --filter FullyQualifiedName~Exported_fixture_runs_time_and_cron_jobs
Remove-Item Env:TICKERQ_TEST_LICENSE_DIRECTORY
```

Run all desktop issuer checks with:

```sh
dotnet test tests/TickerQ.Tests --filter FullyQualifiedName~DesktopTestLicenseTests
```
