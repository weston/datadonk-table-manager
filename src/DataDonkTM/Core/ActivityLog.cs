namespace DataDonkTM.Core;

/// <summary>The last few hundred things the app did, in memory only. Saved into bug reports.</summary>
public static class ActivityLog
{
    private const int MaxEntries = 500;
    private static readonly Queue<string> Entries = new();
    private static readonly object Lock = new();

    public static void Add(string text)
    {
        lock (Lock)
        {
            Entries.Enqueue($"{DateTime.Now:HH:mm:ss.fff}  {text}");
            while (Entries.Count > MaxEntries) Entries.Dequeue();
        }
    }

    public static string[] Snapshot()
    {
        lock (Lock) return Entries.ToArray();
    }
}
