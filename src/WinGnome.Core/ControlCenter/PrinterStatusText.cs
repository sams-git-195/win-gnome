using System.Globalization;

namespace WinGnome.Core.ControlCenter;

/// <summary>Turns a printer's <c>PRINTER_INFO_2</c> status, attributes and job count into the line the Printers panel shows.</summary>
public static class PrinterStatusText
{
    /// <summary><c>PRINTER_ATTRIBUTE_WORK_OFFLINE</c>: the user chose "Use printer offline".</summary>
    public const uint AttributeWorkOffline = 0x400;

    // PRINTER_STATUS_* bits from winspool.h.
    private const uint Paused = 0x1;
    private const uint Error = 0x2;
    private const uint PendingDeletion = 0x4;
    private const uint PaperJam = 0x8;
    private const uint PaperOut = 0x10;
    private const uint PaperProblem = 0x40;
    private const uint Offline = 0x80;
    private const uint Busy = 0x200;
    private const uint Printing = 0x400;
    private const uint OutputBinFull = 0x800;
    private const uint NotAvailable = 0x1000;
    private const uint Processing = 0x4000;
    private const uint Initializing = 0x8000;
    private const uint WarmingUp = 0x10000;
    private const uint TonerLow = 0x20000;
    private const uint NoToner = 0x40000;
    private const uint UserIntervention = 0x100000;
    private const uint OutOfMemory = 0x200000;
    private const uint DoorOpen = 0x400000;
    private const uint ServerUnknown = 0x800000;
    private const uint ServerOffline = 0x2000000;

    // Most serious first: when several bits are set only the first match is named.
    private static readonly (uint Bit, string Text)[] BySeriousness =
    [
        (PaperJam, "Paper jam"),
        (NoToner, "Out of toner"),
        (PaperOut, "Out of paper"),
        (DoorOpen, "Door open"),
        (Offline, "Offline"),
        (NotAvailable, "Not available"),
        (ServerUnknown, "Offline"),
        (ServerOffline, "Offline"),
        (Error, "Error"),
        (UserIntervention, "Needs attention"),
        (PaperProblem, "Paper problem"),
        (OutOfMemory, "Out of memory"),
        (OutputBinFull, "Output tray full"),
        (TonerLow, "Low on toner"),
        (Paused, "Paused"),
        (PendingDeletion, "Being deleted"),
        (Initializing, "Starting up"),
        (WarmingUp, "Warming up"),
        (Printing, "Printing"),
        (Processing, "Processing"),
        (Busy, "Busy"),
    ];

    /// <summary>
    /// "Ready", "Offline", "Paper jam", "2 jobs" or both ("Printing, 2 jobs"). Jobs are named on their own when the
    /// printer is otherwise ready.
    /// </summary>
    /// <param name="status">The printer's <c>Status</c> bits.</param>
    /// <param name="attributes">The printer's <c>Attributes</c> bits.</param>
    /// <param name="jobs">Jobs waiting in its queue.</param>
    public static string Describe(uint status, uint attributes, int jobs)
    {
        var state = StateOf(status, attributes);
        if (jobs <= 0)
        {
            return state ?? "Ready";
        }

        var count = string.Create(CultureInfo.InvariantCulture, $"{jobs} {(jobs == 1 ? "job" : "jobs")}");
        return state is null ? count : $"{state}, {count}";
    }

    private static string? StateOf(uint status, uint attributes)
    {
        foreach (var (bit, text) in BySeriousness)
        {
            // WORK_OFFLINE is an attribute, not a status bit; it ranks with the offline status, just before NotAvailable.
            if (bit == NotAvailable && (attributes & AttributeWorkOffline) != 0)
            {
                return "Offline";
            }

            if ((status & bit) != 0)
            {
                return text;
            }
        }

        return null;
    }
}
