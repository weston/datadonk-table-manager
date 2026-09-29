# How to install and update DataDonk Table Manager

This guide needs no technical knowledge. It takes about 5 minutes.

You need a Windows 10 or Windows 11 PC. You don't need to install anything else.

---

## Installing

### 1. Download it

1. Go to the **[download page](https://github.com/weston/datadonk-table-manager/releases/latest)**.
2. Under **Assets**, click **DataDonkTM.exe**. It's about 180 MB, so it may take a minute.

### 2. Put it in a folder you'll remember

1. Open your **Documents** folder.
2. Make a new folder called **DataDonk**. Right-click an empty area, choose **New → Folder**, and type `DataDonk`.
3. Move **DataDonkTM.exe** from your **Downloads** folder into the new **DataDonk** folder.

> Don't put it in "Program Files". The in-app **Update** button can't replace the app there.

### 3. Start it

1. Double-click **DataDonkTM.exe**.
2. You may see a blue box saying **"Windows protected your PC"**. This appears because the app is new and isn't paid-for "signed" software; it doesn't mean anything is wrong. The code is public on this page for anyone to check. To continue:
   1. Click **More info**.
   2. Click **Run anyway**.
3. You only need to do this once.

The settings window opens. After that, the app lives in the **system tray**: the small icons at the bottom-right of your screen, next to the clock. Look for the **DataDonk donkey** icon. If you don't see it, click the small **^** arrow there.

- **Double-click** the donkey icon to open the settings.
- **Right-click** the donkey icon to switch layouts or to **Exit**.
- Closing the settings window does **not** close the app. It keeps running in the tray.

### 4. (Optional) Pin it so it's easy to find

Right-click **DataDonkTM.exe** and choose **Pin to Start**, or **Show more options → Pin to taskbar**.

---

## First-time setup

### A. Tell it about your poker site

1. Open your poker site and open a table.
2. In DataDonk, go to the **Poker sites** tab and click the green **Set up a poker site** button.
3. Follow the steps on screen. Each step says exactly what to click.
4. **Only want table tiling and the random number box?** Click **Skip bet buttons** when you reach step 3.

### B. Choose where your tables go

1. Go to the **Table tiling** tab and click **Edit layout**.
2. The screen turns dark and shows your monitors. Click **Grid on monitor**, choose how many rows and columns you want, and click **Add grid**.
3. Click **Save**.

Now every new table jumps into its place. To move a table, drag it onto another spot and it snaps there. Drop it on a spot that already has a table and the two swap.

**Playing** spots (green) get the bet buttons and the random number box. **Observing** spots (blue) don't. Double-click a spot in the layout editor to switch it between the two.

---

## Updating

### The easy way (recommended)

When a new version is out, you'll see:

- a **yellow bar** at the top of the settings window: *"A new version is available"*, and
- an **"⬆ Update to …"** option at the top of the tray menu (right-click the donkey icon).

Click **Update now**. The app downloads the new version, closes, and starts again by itself. **Your settings and layouts are kept.**

To check yourself, go to the **About** tab and click **Check for updates**.

### The manual way (if the easy way doesn't work)

1. Right-click the **donkey** icon in the tray and click **Exit**.
2. Download the new **DataDonkTM.exe** from the **[download page](https://github.com/weston/datadonk-table-manager/releases/latest)**.
3. Move it into your **DataDonk** folder. When Windows asks, choose **Replace the file in the destination**.
4. Double-click it to start. Your settings are kept.

---

## Common questions

**Where are my settings saved?**
In one file, `%APPDATA%\DataDonkTM\config.json`. To open it, paste that path into the address bar of a File Explorer window. The **About** tab also has an **Open settings folder** button. Copy this file to back up your setup or move it to another PC.

**Does it go on the internet?**
Only to check GitHub for a newer version, and to download it when you click **Update now**. Nothing about you or your games is sent. You can turn off the check in the **About** tab by unticking **Check for updates automatically**.

**Does it read my poker client's memory, or play for me?**
No. It moves windows, looks at the table picture (the way your eyes do), and types a bet amount into the bet box when *you* click a bet button. It never clicks Bet or Raise for you.

**Is it allowed on my poker site?**
Check your site's rules on third-party tools first. Some sites, notably GGPoker, only allow tools from an approved list.

**How do I uninstall it?**
1. Right-click the **donkey** icon and click **Exit**.
2. Delete your **DataDonk** folder.
3. (Optional) Delete the settings folder `%APPDATA%\DataDonkTM`.

**The bet buttons type the wrong amount.**
1. In **Poker sites**, select your site and click **Redo setup**.
2. Click the step you want to fix in the bar at the top.
3. If your site shows amounts in big blinds (e.g. "Pot 12.5 BB"), tick **This site shows amounts in big blinds** on the last page.
