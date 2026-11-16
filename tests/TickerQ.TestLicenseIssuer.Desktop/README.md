# TickerQ Test License Issuer

Run the AtomUI desktop project with the .NET 10 SDK from the repository root.

```sh
dotnet run --project tests/TickerQ.TestLicenseIssuer.Desktop
```

Choose a local folder. The application creates a new subfolder containing `tickerq.tqlicense` and `fixture-authority.json`. Each export uses a fresh Ed25519 key and writes only the public key to the authority file. The certificate is a 30-day Evaluation fixture.

The integration test uses the same exporter and passes the authority file's public key to the internal test resolver before the scheduler starts. The standard TickerQ startup path rejects these files.

```sh
dotnet test tests/TickerQ.Tests --filter FullyQualifiedName~DesktopTestLicenseTests
```
