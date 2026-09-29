using DataDonkTM.Core;
using DataDonkTM.Native;

namespace DataDonkTM.Betting;

/// <summary>
/// Types a bet amount into the site's bet box using ordinary synthetic mouse/keyboard input
/// (SendInput) - the same thing you would do by hand. Never reads or writes the client's memory.
/// </summary>
public static class InputSender
{
    public static async Task EnterAmountAsync(IntPtr hwnd, SiteProfile site, string amountText)
    {
        if (site.BetBoxPoint == null) throw new InvalidOperationException("Bet box position is not calibrated for this site.");
        var client = Win32.GetClientBounds(hwnd);
        var box = site.BetBoxPoint.ToPixels(client.Size);
        box.Offset(client.Location);
        int delay = Math.Clamp(site.InputDelayMs, 0, 500);

        Win32.GetCursorPos(out var original);
        try
        {
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
            await Task.Delay(delay);

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

            // Only the amount is typed. The bet is never submitted - the player clicks Bet/Raise.
            TypeText(amountText, site.Typing);
        }
        finally
        {
            await Task.Delay(delay);
            Win32.SetCursorPos(original.X, original.Y);
        }
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

    private static Win32.INPUT Key(ushort vk, ushort scan, uint flags) => new()
    {
        type = Win32.INPUT_KEYBOARD,
        U = new Win32.InputUnion { ki = new Win32.KEYBDINPUT { wVk = vk, wScan = scan, dwFlags = flags } },
    };

    private static void Send(params Win32.INPUT[] inputs) =>
        Win32.SendInput((uint)inputs.Length, inputs, Win32.INPUT.Size);
}
