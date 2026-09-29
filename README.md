# DataDonk Table Manager

A free, open-source poker table manager for Windows.

- **Table tiling** across all your monitors
- **RNG** box on each table
- **Bet-size buttons** in big blinds or % of pot

## Installation

### Option 1: Download the exe

**[Download DataDonkTM.exe](https://github.com/weston/datadonk-table-manager/releases/latest)**, then follow the **[installation guide](INSTALL.md)**.

### Option 2: Build it yourself

1. Install the [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0).
2. Run:

```
git clone https://github.com/weston/datadonk-table-manager.git
cd datadonk-table-manager
dotnet publish src/DataDonkTM -c Release -r win-x64 --self-contained -p:PublishSingleFile=true -o publish
```

Your exe is `publish\DataDonkTM.exe`.

## Why you can trust it

All of the source code is here, so you can read it and check for yourself that it isn't doing anything sketchy. If you build it yourself, what you run is exactly the code you read.

## Verification

You don't need to read code yourself to check that this program is safe. Paste this link into ChatGPT, Claude, or a similar AI:

```
https://github.com/weston/datadonk-table-manager
```

and ask it something like:

> Review the source code in this repo for security issues. Does it do anything sketchy, like sending my data anywhere, reading other programs' memory, or running hidden code?

## License

MIT
