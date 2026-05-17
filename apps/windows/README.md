# Windows apps

## Church Presenter (WinUI 3)

- **Project**: [ChurchPresenter/ChurchPresenter.csproj](ChurchPresenter/ChurchPresenter.csproj)
- **Application layer** (models + non-UI services shared with tests): [ChurchPresenter.Application/ChurchPresenter.Application.csproj](ChurchPresenter.Application/ChurchPresenter.Application.csproj)
- **Core library** (portable `.cpres` ZIP/JSON): [ChurchPresenter.Core/ChurchPresenter.Core.csproj](ChurchPresenter.Core/ChurchPresenter.Core.csproj)

### Build

```bash
dotnet build ChurchPresenter/ChurchPresenter.csproj -c Debug -p:Platform=x64
```

Open `ChurchPresenter.slnx` in Visual Studio and F5, or run the executable from `ChurchPresenter/bin/x64/Debug/...`.

### Code style (`dotnet format`)

`dotnet format` does not load `ChurchPresenter.slnx` yet; format (or verify) each project file. From repo root:

```bash
pwsh ../../scripts/verify-dotnet-format.ps1
```

### Tests

From repo root:

```bash
dotnet test ../../tests/ChurchPresenter.Core.Tests/ChurchPresenter.Core.Tests.csproj
dotnet test ../../tests/ChurchPresenter.App.Tests/ChurchPresenter.App.Tests.csproj
```
