using DataDonkTM.Core;
using DataDonkTM.Native;

namespace DataDonkTM.Betting;

/// <summary>
/// Types a bet amount into the site's bet box using ordinary synthetic mouse/keyboard input
/// (SendInput) - the same thing you would do by hand - or, for clients that ignore that, by posting the
/// same click and key messages straight to the table window, or by setting the text of a native text box.
/// Never reads or writes the client's memory.
/// </summary>
public static class InputSender
{
    /// <summary>Types the amount with the given method. Returns notes for bug reports (e.g. whether the table became active).</summary>
    public static async Task<string> EnterAmountAsync(IntPtr hwnd, SiteProfile site, string amountText, TypeMethod method)
    {
        if (site.BetBoxPoint == null) throw new InvalidOperationException("Bet box position is not calibrated for this site.");
        if (method == TypeMethod.WindowMessages) return await PostAmountAsync(hwnd, site, amountText);
        if (method == TypeMethod.TextBox) return SetTextBox(hwnd, site, amountText);
        var notes = await PrepareBoxAsync(hwnd, site);
        return notes + "; " + await TypeAsync(site, amountText, method);
    }

    /// <summary>
    /// For simulated typing: activates the table, clicks the bet box and selects what's in it. None of this
    /// depends on the amount, so it can run while the table is still being read. Returns notes for bug reports.
    /// </summary>
    public static async Task<string> PrepareBoxAsync(IntPtr hwnd, SiteProfile site)
    {
        if (site.BetBoxPoint == null) throw new InvalidOperationException("Bet box position is not calibrated for this site.");
        var client = Win32.GetClientBounds(hwnd);
        var box = site.BetBoxPoint.ToPixels(client.Size);
        box.Offset(client.Location);
        int delay = Math.Clamp(site.InputDelayMs, 0, 500);

        Win32.GetCursorPos(out var original);
        try
        {
            // The bet buttons never take focus, so the table may not be the active window yet. Keys sent
            // before it is active (and its bet box focused) are lost, so activate it and wait until it is.
            bool wasActive = Win32.GetForegroundWindow() == hwnd;
            if (!wasActive)
            {
                Win32.SetForegroundWindow(hwnd);
                for (int waited = 0; waited < 250 && Win32.GetForegroundWindow() != hwnd; waited += 10)
                    await Task.Delay(10);
                await Task.Delay(delay);
            }
            var notes = $"table was {(wasActive ? "" : "not ")}active, " +
                        $"after activating it is {(Win32.GetForegroundWindow() == hwnd ? "" : "NOT ")}active; clicked at {box.X},{box.Y}";
            switch (site.SelectAll)
            {
                case SelectAllMethod.DoubleClick:
                    Click(box); Click(box);
                    break;
                case SelectAllMethod.TripleClick:
                    Click(box); Click(box); Click(box);
                    break;
                default:
                    Click(box);
                    break;
            }
            await Task.Delay(Math.Max(delay, 60)); // let the box take focus before selecting/typing

            if (site.SelectAll == SelectAllMethod.CtrlA)
            {
                KeyChord(0x11 /*VK_CONTROL*/, 0x41 /*A*/);
            }
            else if (site.SelectAll == SelectAllMethod.Backspaces)
            {
                KeyPress(0x23 /*VK_END*/);
                for (int i = 0; i < 15; i++) KeyPress(0x08 /*VK_BACK*/);
            }
            await Task.Delay(delay);
            return notes;
        }
        finally
        {
            Win32.SetCursorPos(original.X, original.Y);
        }
    }

    /// <summary>For simulated typing, after <see cref="PrepareBoxAsync"/>: types the amount into the focused bet box.</summary>
    public static async Task<string> TypeAsync(SiteProfile site, string amountText, TypeMethod method)
    {
        // Only the amount is typed. The bet is never submitted - the player clicks Bet/Raise.
        TypeText(amountText, method);
        await Task.Delay(Math.Clamp(site.InputDelayMs, 0, 500));
        return $"{method}: typed";
    }

    /// <summary>
    /// The native Windows text box (a child window whose class is some kind of "Edit") at, or within 80 px of,
    /// the marked bet box point; zero if the client draws its own instead.
    /// </summary>
    public static IntPtr FindTextBox(IntPtr hwnd, SiteProfile site)
    {
        if (site.BetBoxPoint == null) return IntPtr.Zero;
        var client = Win32.GetClientBounds(hwnd);
        var p = site.BetBoxPoint.ToPixels(client.Size);
        p.Offset(client.Location);

        IntPtr best = IntPtr.Zero;
        double bestDist = 80;
        Win32.EnumChildWindows(hwnd, (child, _) =>
        {
            if (!Win32.IsWindowVisible(child) || !Win32.GetClass(child).Contains("edit", StringComparison.OrdinalIgnoreCase)) return true;
            var r = Win32.GetWindowBounds(child);
            if (r.Width <= 0) return true;
            double dx = Math.Max(0, Math.Max(r.Left - p.X, p.X - r.Right));
            double dy = Math.Max(0, Math.Max(r.Top - p.Y, p.Y - r.Bottom));
            double d = Math.Sqrt(dx * dx + dy * dy); // 0 when the point is inside the box
            if (d <= bestDist) { bestDist = d; best = child; }
            return true;
        }, IntPtr.Zero);
        return best;
    }

    /// <summary>Sets the native text box's text directly: no mouse, keyboard or focus needed.</summary>
    private static string SetTextBox(IntPtr hwnd, SiteProfile site, string amountText)
    {
        var box = FindTextBox(hwnd, site);
        if (box == IntPtr.Zero) throw new InvalidOperationException("no native text box at the bet box");
        if (Win32.SendMessageTimeout(box, Win32.WM_SETTEXT, IntPtr.Zero, amountText, Win32.SMTO_ABORTIFHUNG, 500, out _) == IntPtr.Zero)
            throw new InvalidOperationException("the text box didn't answer");
        return $"TextBox: set the text of 0x{box.ToInt64():X} ({Win32.GetClass(box)})";
    }

    /// <summary>
    /// Posts the click, clear and keys straight to the table window. Works whether or not the table is the
    /// active window, and for clients that ignore simulated input (seen with SwC Poker).
    /// </summary>
    private static async Task<string> PostAmountAsync(IntPtr hwnd, SiteProfile site, string amountText)
    {
        var client = Win32.GetClientBounds(hwnd);
        var p = site.BetBoxPoint!.ToPixels(client.Size);
        int delay = Math.Max(Math.Clamp(site.InputDelayMs, 0, 500), 60);

        // The deepest child window under the point gets the messages (often the table window itself).
        var target = hwnd;
        var pt = new Win32.POINT { X = p.X, Y = p.Y };
        while (true)
        {
            var child = Win32.ChildWindowFromPointEx(target, pt, Win32.CWP_SKIPINVISIBLE | Win32.CWP_SKIPTRANSPARENT);
            if (child == IntPtr.Zero || child == target) break;
            Win32.MapWindowPoints(target, child, ref pt, 1);
            target = child;
        }

        var xy = (IntPtr)((pt.Y << 16) | (pt.X & 0xFFFF));
        Win32.PostMessage(target, Win32.WM_MOUSEMOVE, IntPtr.Zero, xy);
        Win32.PostMessage(target, Win32.WM_LBUTTONDOWN, (IntPtr)Win32.MK_LBUTTON, xy);
        Win32.PostMessage(target, Win32.WM_LBUTTONUP, IntPtr.Zero, xy);
        await Task.Delay(delay);

        // Clear the box: End, then Backspace (no modifier keys needed, unlike Ctrl+A).
        PostKey(target, 0x23 /*VK_END*/, null);
        for (int i = 0; i < 15; i++) PostKey(target, 0x08 /*VK_BACK*/, (char)8);
        await Task.Delay(delay / 2);

        foreach (char ch in amountText)
        {
            ushort vk = ch switch
            {
                >= '0' and <= '9' => ch,
                '.' => 0xBE,
                ',' => 0xBC,
                _ => (ushort)(Win32.VkKeyScan(ch) & 0xFF),
            };
            PostKey(target, vk, ch);
        }
        return $"WindowMessages: posted to 0x{target.ToInt64():X} ({Win32.GetClass(target)}) at {pt.X},{pt.Y}";
    }

    /// <summary>Key down (+ the character it types) + key up, as a real keyboard would produce them.</summary>
    private static void PostKey(IntPtr target, ushort vk, char? ch)
    {
        uint scan = Win32.MapVirtualKey(vk, 0);
        long extended = vk is >= 0x21 and <= 0x2E ? 1L << 24 : 0;
        long down = 1 | (scan << 16) | extended;
        long up = down | (1L << 30) | (1L << 31);
        Win32.PostMessage(target, Win32.WM_KEYDOWN, (IntPtr)vk, (IntPtr)down);
        if (ch != null) Win32.PostMessage(target, Win32.WM_CHAR, (IntPtr)ch.Value, (IntPtr)down);
        Win32.PostMessage(target, Win32.WM_KEYUP, (IntPtr)vk, (IntPtr)up);
    }

    private static void Click(Point p)
    {
        Win32.SetCursorPos(p.X, p.Y);
        Send(Mouse(Win32.MOUSEEVENTF_LEFTDOWN), Mouse(Win32.MOUSEEVENTF_LEFTUP));
    }

    private static void TypeText(string text, TypeMethod method)
    {
        var inputs = new List<Win32.INPUT>();
        foreach (char ch in text)
        {
            if (method == TypeMethod.Unicode)
            {
                inputs.Add(Key(0, ch, Win32.KEYEVENTF_UNICODE));
                inputs.Add(Key(0, ch, Win32.KEYEVENTF_UNICODE | Win32.KEYEVENTF_KEYUP));
            }
            else
            {
                ushort vk = ch switch
                {
                    >= '0' and <= '9' => ch,
                    '.' => 0xBE, // VK_OEM_PERIOD
                    ',' => 0xBC, // VK_OEM_COMMA
                    _ => (ushort)(Win32.VkKeyScan(ch) & 0xFF),
                };
                inputs.Add(Key(vk, 0, 0));
                inputs.Add(Key(vk, 0, Win32.KEYEVENTF_KEYUP));
            }
        }
        Send(inputs.ToArray());
    }

    private static void KeyPress(ushort vk) => Send(Key(vk, 0, 0), Key(vk, 0, Win32.KEYEVENTF_KEYUP));

    private static void KeyChord(ushort modifier, ushort vk) => Send(
        Key(modifier, 0, 0), Key(vk, 0, 0), Key(vk, 0, Win32.KEYEVENTF_KEYUP), Key(modifier, 0, Win32.KEYEVENTF_KEYUP));

    private static Win32.INPUT Mouse(uint flags) => new()
    {
        type = Win32.INPUT_MOUSE,
        U = new Win32.InputUnion { mi = new Win32.MOUSEINPUT { dwFlags = flags } },
    };

    private static Win32.INPUT Key(ushort vk, ushort scan, uint flags)
    {
        // Include the hardware scan code like a real keyboard does: Qt-based clients (e.g. SwC Poker)
        // accept shortcuts like Ctrl+A without it but silently drop typed digits.
        if (vk != 0 && (flags & Win32.KEYEVENTF_UNICODE) == 0)
        {
            scan = (ushort)Win32.MapVirtualKey(vk, 0);
            if (vk is >= 0x21 and <= 0x2E) flags |= Win32.KEYEVENTF_EXTENDEDKEY; // End, Home, arrows, Delete...
        }
        return new()
        {
            type = Win32.INPUT_KEYBOARD,
            U = new Win32.InputUnion { ki = new Win32.KEYBDINPUT { wVk = vk, wScan = scan, dwFlags = flags } },
        };
    }

    private static void Send(params Win32.INPUT[] inputs) =>
        Win32.SendInput((uint)inputs.Length, inputs, Win32.INPUT.Size);
}
