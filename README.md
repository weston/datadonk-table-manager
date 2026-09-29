# DataDonk Table Manager

A small, free, open-source poker table manager for Windows. It does three things:

1. **Table tiling**: profiles of table slots across all your monitors. New tables snap into free slots. Drag a table onto another slot to move it there, or swap it with the table already in that slot.
2. **RNG**: a small box on each playing table. Click it for a number from 1 to 100.
3. **Bet-size buttons**: up to 8 buttons (2 rows × 4) with separate preflop and postflop sets, sized in **big blinds** or **% of pot**.

## Download

**[Download DataDonkTM.exe](https://github.com/weston/datadonk-table-manager/releases/latest)**. It's one file with nothing else to install.

New here? Read the **[installation & update guide](INSTALL.md)**. It's written for non-technical users.

## Why you can trust it

All of the source code is here, so you can read it and check for yourself that it isn't doing anything sketchy. You don't have to take anyone's word for it.

- **Where to look:**
  - Everything it does to Windows is in one file: `src/DataDonkTM/Native/Win32.cs`.
  - Everything it does on the internet (checking GitHub for updates) is in one file: `src/DataDonkTM/Core/UpdateChecker.cs`.
- **Build it yourself** if you'd rather not trust the downloaded exe (see below). Then what you run is exactly the code you read.
- **It never reads or changes your poker client's memory.**

## Build and run

You need the .NET 8 SDK on Windows 10 (19041) or later.

```
dotnet build -c Release
dotnet test
src\DataDonkTM\bin\Release\net8.0-windows10.0.19041.0\DataDonkTM.exe
```

If your poker client runs as administrator, this app must too. Windows blocks input and window moves from lower-privileged processes.

## Notes

- Check your site's rules on third-party software before using bet-sizing tools. Some sites, notably GGPoker, only allow approved tools.
- Windows OCR needs an installed language with OCR support. English, which is standard, works.

## Layout of the code

| Path | What |
|---|---|
| `Core/TableManager.cs` | Window detection (WinEvent hooks + periodic scan), tiling, and overlay lifecycle |
| `Core/SlotLogic.cs` | Pure slot rules (tested) |
| `Betting/BetCalculator.cs` | Bet math (tested) |
| `Betting/AmountParser.cs` | Reads amounts from OCR text and titles (tested) |
| `Betting/OcrService.cs`, `TableCapture.cs`, `TableReader.cs` | Screenshots, OCR, and street detection |
| `Betting/InputSender.cs` | Clicks the bet box and types the amount |
| `Overlays/*` | The RNG box and bet panel |
| `UI/*` | Settings window, layout editor, and site calibration |

## License

MIT (see `LICENSE`).

## Releasing a new version (maintainers)

```
.\release.ps1 0.2.0
```

This bumps the version, runs the tests, and builds `DataDonkTM.exe`. It then tags, pushes, and creates a GitHub release with the exe attached. Everyone's app shows the yellow "update available" bar within 12 hours, or right away if they click **Check for updates**.
