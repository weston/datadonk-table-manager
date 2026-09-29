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

The app lives in the tray. Closing the window hides it; right-click the tray icon to exit. Add `--minimized` to start it hidden.

If your poker client runs as administrator, this app must too. Windows blocks input and window moves from lower-privileged processes.

## Setup

### 1. Add your poker site (Poker sites tab → "Set up a poker site")

Every site draws its tables differently, so each site has a profile. The guided setup walks through it one step at a time:

1. Pick the lobby.
2. Pick a table.
3. Box the pot.
4. Box the Call button.
5. Click the bet box.
6. Box the first flop card.
7. Save an empty board.
8. Name the site.

It checks each step as you go, for example by showing the pot amount it read.

**Advanced settings…** opens the full editor, described below.

1. **Detection.** Open a real table, select it in the window list, and click **Fill rules from selected window**. This fills in the process name, window class and a title rule. Then open the lobby and click **Test detection**. The lobby must *not* match.
2. **Capture** a screenshot of a table. Ideally take it mid-hand while you are facing a bet. Then mark these on the screenshot:
   - **Pot amount** (a box). Required for % pot buttons.
   - **Call button text** (a box). Used to read the amount to call; "Check" reads as 0.
   - **Hero's bet in front** (optional box). Your posted blind or bet, used for exact raise math from the blinds or when re-raising.
   - **Blinds text** (optional box). Only needed if the stakes aren't in the window title.
   - **First flop card** (a box). Capture a table with *no board dealt* and click **Board is empty now → save reference**. Buttons then switch between the preflop and postflop sets automatically.
   - **Bet amount box** (a click point).
3. Click **Test read**. It shows exactly what OCR read and what each button would type.
4. **Export** the finished profile to share it; other people can **Import** it.

Positions are stored as fractions of the table, so they survive resizing.

Only PokerStars detection is pre-filled with reasonable confidence. The other presets are unverified guesses and start disabled.

### 2. Make a layout (Table tiling tab → Edit layout…)

This opens a full-screen editor across all monitors:

- **Grid on monitor…** creates rows × columns of slots, keeping the table aspect ratio.
- **Capture open tables** adds slots where your tables are right now.
- **Double-click** a slot, or press P / O, to switch it between *playing* and *observing*.
- **Drag** a slot to move it and drag its corner to resize. Edges snap to monitors and to other slots; hold Alt to stop snapping.
- Slot numbers are the fill order for new tables.

You can have several profiles. **Activate** switches between them, and **Set as default** picks the one loaded at startup. The tray menu can switch profiles too.

Overlays (RNG and bet buttons) appear **only on tables in playing slots**.

### 3. Bet buttons (Bet buttons tab)

- **BB** buttons raise or bet to *value × big blind*. The big blind is read from the window title.
- **% pot** buttons work like this:

  `raise to = in-front + to-call + pct × (pot + to-call)`

  In words: call first, then raise that percentage of the pot after calling. For example, 100% pot from the button at 0.5/1 → 3.5.
- If your site's pot number excludes current-street bets, untick "Pot number includes this street's bets" for that site.
- The panel switches between the preflop and postflop sets by itself. Right-click it to force a street for the current hand.
- Right-click-drag moves a panel. The position is saved per site.
- The buttons only **type the amount** into the bet box. They never submit the bet; you click Bet/Raise yourself.

### RNG

- Left-click for a new number.
- Right-click-drag to move it.
- Mouse wheel (or right-click menu, or the RNG tab) to resize.

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
