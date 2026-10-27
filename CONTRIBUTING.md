# Contributing

Thanks for looking. This is a personal tool shared as is, so the bar is: keep
it working for its real use, keep it read-only, and keep it honest about its
numbers.

- **Bugs and questions:** open an issue. Please leave out your drives' serial
  numbers and your own figures; describe the shape of the problem instead.
  The output of `nandwatch probe` masks serials; its raw `.bin` pages do not,
  so never attach those.
- **Small fixes:** a pull request is welcome.
- **Anything larger:** open an issue first, so we can agree it fits before you
  spend the time.
- **Security issues:** not in a public issue; see [SECURITY.md](SECURITY.md).

## Before sending a change

From the repo root:

```powershell
dotnet build NandWatch.slnx -c Release
dotnet test --project tests\NandWatch.Core.Tests\NandWatch.Core.Tests.csproj
```

The tests need no admin rights and no real drive. If you touch a parser,
compare `nandwatch read` before and after, and with a tool such as HWiNFO or
CrystalDiskInfo, field by field.

## Conventions

- **Conventional commit messages** (`feat:`, `fix:`, `docs:`), one concern per
  commit.
- **Read-only, always.** A test reads the source and fails if any I/O control
  code other than the three read-only queries appears, or if the RST reader's
  one request changes. Adding one is a design change: say why in the issue
  first.
- **The history's schema only grows**, by numbered migrations.
- **Keep every `.ps1` pure ASCII**, in Windows PowerShell 5.1 syntax. Windows
  PowerShell misreads a UTF-8 dash in a BOM-less script.
- **Never commit collected data.** `.gitignore` blocks databases, raw pages,
  logs and the `screenshots/` folder.
- **Screenshots come from the invented history only**: regenerate them with
  `tools\Capture-Readme.ps1`, never from your own drives.
- **No new dependencies without a reason.**
