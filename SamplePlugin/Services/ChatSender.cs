using System;
using System.Text;
using FFXIVClientStructs.FFXIV.Client.System.String;
using FFXIVClientStructs.FFXIV.Client.UI;
using FFXIVClientStructs.FFXIV.Client.UI.Shell;
using FFXIVClientStructs.Interop;

namespace SamplePlugin.Services;

public static unsafe class ChatSender
{
    private const int MaxBytes = 500;

    public static bool Send(string message)
    {
        if (string.IsNullOrWhiteSpace(message))
            return false;

        // Stop the game from interpreting our text as a slash command.
        if (message.StartsWith('/'))
            message = " " + message;

        var bytes = Encoding.UTF8.GetBytes(message);
        if (bytes.Length == 0)
            return false;
        if (bytes.Length > MaxBytes)
        {
            // Truncate on a UTF-8 boundary to stay under the game's chat byte limit.
            var safe = MaxBytes;
            while (safe > 0 && (bytes[safe] & 0xC0) == 0x80) safe--;
            bytes = bytes[..safe];
        }

        Utf8String* str = null;
        try
        {
            str = Utf8String.CreateEmpty();
            fixed (byte* p = bytes)
                str->SetString(p);

            var shell = RaptureShellModule.Instance();
            var ui = UIModule.Instance();
            if (shell == null || ui == null)
                return false;

            shell->ExecuteCommandInner(str, ui);
            return true;
        }
        catch (Exception ex)
        {
            Plugin.Log.Warning($"[dudu] ChatSender failed: {ex.Message}");
            return false;
        }
        finally
        {
            if (str != null)
            {
                str->Dtor(true);
            }
        }
    }
}
