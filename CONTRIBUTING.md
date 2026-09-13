# Contributing

Contributions are welcome.

## Development environment

- .NET SDK 10.x
- Windows or Linux

Build the solution:

```bash
dotnet restore
dotnet build
```

Run the UI:

```bash
dotnet run --project src/AmneziaDashboard.App
```

## Architecture

- `AmneziaDashboard.Core` — domain models and service interfaces.
- `AmneziaDashboard.Infrastructure` — SSH, Docker, storage and OS security integrations.
- `AmneziaDashboard.App` — Avalonia UI, view models and application services.

Keep platform-specific code behind small abstractions when possible. Avoid storing credentials in application configuration files.

## Pull requests

Before opening a pull request:

1. Build the solution in Release configuration.
2. Test both light and dark themes for UI changes.
3. For server-side mutations, verify failure and rollback paths.
4. Do not commit real server IPs, passwords, private keys or user configuration files.
