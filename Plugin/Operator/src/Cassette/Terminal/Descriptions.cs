using System.Linq;
using System.Text;

namespace Operator.Cassette;

internal static partial class TerminalCassettePlayback
{
    internal static void OnLoadNewNode(Terminal terminal, TerminalNode? node)
    {
        if (terminal == null || node == null) return;
        string evt = node.terminalEvent ?? string.Empty;
        if (!_ourNodes.ContainsKey(evt)) return;

        if (evt == EvtPlay)
        {
            node.displayText = DescribePlay(terminal);
        }
        else if (evt == EvtStop)
        {
            node.displayText = "\n>PLAYBACK STOPPED.\n\n";
        }
        else if (evt == EvtSave)
        {
            node.displayText = DescribeSave();
        }
        else if (evt == EvtDelete)
        {
            node.displayText = DescribeDelete();
        }
        else if (evt == EvtList)
        {
            node.displayText = DescribeList();
        }
        else if (evt == EvtHelp)
        {
            node.displayText = DescribeHelp();
        }
        else if (evt == EvtSlow)
        {
            node.displayText = FormatStatus("PLAYBACK SPEED - 0.5x.");
        }
        else if (evt == EvtNormal)
        {
            node.displayText = FormatStatus("PLAYBACK SPEED - 1x.");
        }
        else if (evt == EvtFast)
        {
            node.displayText = FormatStatus("PLAYBACK SPEED - 2x.");
        }
        else if (evt == EvtTurbo)
        {
            node.displayText = FormatStatus("PLAYBACK SPEED - 4x.");
        }
        else if (evt == EvtPause)
        {
            node.displayText = FormatStatus(
                "PLAYBACK PAUSED. Type '" + Constants.Terminal.KeywordResume + "' to resume the playback.");
        }
        else if (evt == EvtResume)
        {
            node.displayText = FormatStatus("PLAYBACK RESUMED.");
        }
        else if (evt == EvtVolume)
        {
            node.displayText = DescribeVolume();
        }
        else if (evt == EvtTransfer)
        {
            node.displayText = DescribeTransfer();
        }
    }

    private static string DescribeTransfer()
    {
        string transfer = Constants.Terminal.KeywordTransfer;

        if (!IsPlaying || string.IsNullOrEmpty(PlayingFileName))
        {
            return $"\nNOTHING PLAYING TO TRANSFER.\n" +
                   $"Type '{Constants.Terminal.KeywordPlay} <ID>' first, then '{transfer}'.\n\n";
        }

        return FormatStatus("TRANSFERRED TO SHIP MONITOR.");
    }

    private static string DescribePlay(Terminal terminal)
    {
        string? id = ArgAfterVerb();
        if (string.IsNullOrEmpty(id))
        {
            return $"\nUSAGE: {Constants.Terminal.KeywordPlay} <CASSETTE-ID>\n\n" +
                   $"Scan or hold cassette to get its ID.\n\n";
        }
        if (!TryFindCassette(id, out string fileName, out string? filePath, out string source))
        {
            return $"\nNO CASSETTE WITH ID '{id.ToUpperInvariant()}' FOUND.\n\n";
        }
        if (string.IsNullOrEmpty(filePath))
        {
            if (CassetteLibrary.IsMergePending(fileName))
            {
                return $"\nMERGING FILM FOR [{id.ToUpperInvariant()}]...\n\n";
            }
            if (CassetteItem.IsEncodingFile(fileName))
            {
                return $"\nWRITING TAPE [{id.ToUpperInvariant()}]...\n\n";
            }
            return $"\nFILE FOR '{id.ToUpperInvariant()}' NOT ON DISK.\n  Expected: {fileName}\n\n";
        }

        string playbackLine = $"PLAYING [{id.ToUpperInvariant()}] - '{fileName}'.\n";
        return FormatStatus(playbackLine);
    }

    private static string DescribeSave()
    {
        string? id = ArgAfterVerb();
        if (string.IsNullOrEmpty(id))
        {
            return $"\nUSAGE: {Constants.Terminal.KeywordSave} <CASSETTE-ID>\n.\n\n";
        }
        string typedKey = CassetteIdGenerator.ToMatchKey(id);
        var cassette = FindWorldCassette(typedKey);
        if (cassette == null)
        {
            return $"\nNO CASSETTE WITH ID [{id.ToUpperInvariant()}] FOUND.\n\n";
        }
        if (cassette.IsEncoding)
        {
            return $"\nTAPE [{id.ToUpperInvariant()}] IS STILL BEING WRITTEN.\n\n";
        }

        return $"\nSAVED [{cassette.CassetteId.ToUpperInvariant()}] TO LIBRARY.\n\n";
    }

    private static string DescribeDelete()
    {
        string? id = ArgAfterVerb();
        if (string.IsNullOrEmpty(id))
        {
            return $"\nUSAGE: {Constants.Terminal.KeywordDelete} <CASSETTE-ID>\n  Removes saved cassette from the library.\n\n";
        }

        if (!TryGetLibraryEntry(id, out CassetteLibrary.Entry? entry))
        {
            return $"\nNO SAVED CASSETTE WITH ID '{id.ToUpperInvariant()}' FOUND.\n\n";
        }

        if (VideoCameraConfig.KeepUnsavedPerLobby.Value)
        {
            return $"\nDELETED [{entry!.Id.ToUpperInvariant()}] FROM LIBRARY.\n Associated recording file will also be deleted.\n\n";
        }

        return $"\nDELETED [{entry!.Id.ToUpperInvariant()}] FROM LIBRARY.\n\n";
    }

    private static string DescribeList()
    {
        var all = CassetteLibrary.All;
        if (all.Count == 0)
        {
            return "\nNO SAVED CASSETTES.\n  type '" +
                   Constants.Terminal.KeywordSave +
                   " <ID>' to add it.\n\n";
        }
        var sb = new System.Text.StringBuilder();
        sb.Append("\nSAVED CASSETTES ------------------------------\n");
        foreach (var kv in all.OrderByDescending(e => e.Value.SavedAt))
        {
            var e = kv.Value;
            sb.Append(' ').Append(e.Id.ToUpperInvariant().PadRight(16))
              .Append('$').Append(e.ScrapValue.ToString().PadRight(5))
              .Append(' ').Append(e.Timestamp.PadRight(18))
              .Append(' ').Append(e.Duration.ToString("F0")).Append('s')
              .Append('\n');
        }
        sb.Append(" ----------------------------------------------\n");
        sb.Append(" (").Append(all.Count).Append(" saved; '")
          .Append(Constants.Terminal.KeywordPlay)
          .Append(" <id>' to play)\n\n");
        return sb.ToString();
    }

    private static string DescribeHelp()
    {
        string play = Constants.Terminal.KeywordPlay;
        string stop = Constants.Terminal.KeywordStop;
        string save = Constants.Terminal.KeywordSave;
        string delete = Constants.Terminal.KeywordDelete;
        string list = Constants.Terminal.KeywordList;
        string help = GetHelpKeyword();
        string slow = Constants.Terminal.KeywordSlow;
        string normal = Constants.Terminal.KeywordNormal;
        string fast = Constants.Terminal.KeywordFast;
        string turbo = Constants.Terminal.KeywordTurbo;
        string pause = Constants.Terminal.KeywordPause;
        string resume = Constants.Terminal.KeywordResume;
        string volume = Constants.Terminal.KeywordVolume;

        var sb = new StringBuilder();
        sb.Append('>').Append(play.ToUpperInvariant()).Append(" <ID>\n");
        sb.Append("To play cassette.\n\n");

        sb.Append('>').Append(stop.ToUpperInvariant()).Append('\n');
        sb.Append("To stop playback.\n\n");

        sb.Append('>').Append(save.ToUpperInvariant()).Append(" <ID>\n");
        sb.Append("To save cassette to the library.\n\n");

        sb.Append('>').Append(delete.ToUpperInvariant()).Append(" <ID>\n");
        sb.Append("To delete cassette from the saved library.\n\n");

        sb.Append('>').Append(list.ToUpperInvariant()).Append('\n');
        sb.Append("To list saved cassettes.\n\n");

        sb.Append('>').Append(help.ToUpperInvariant()).Append('\n');
        sb.Append("To show this command list.\n\n");

        sb.Append('>').Append(slow.ToUpperInvariant()).Append(" / ")
            .Append(normal.ToUpperInvariant()).Append(" / ")
            .Append(fast.ToUpperInvariant()).Append(" / ")
            .Append(turbo.ToUpperInvariant()).Append('\n');
        sb.Append("To change playback speed.\n\n");

        sb.Append('>').Append(pause.ToUpperInvariant()).Append(" / ")
            .Append(resume.ToUpperInvariant()).Append('\n');
        sb.Append("To pause or resume playback.\n\n");

        sb.Append('>').Append(volume.ToUpperInvariant()).Append(" <0-100>\n");
        sb.Append("To set playback volume.\n\n");

        if (ShipMonitors.Available)
        {
            sb.Append('>').Append(Constants.Terminal.KeywordTransfer.ToUpperInvariant()).Append('\n');
            sb.Append("To move playing recording onto the ship monitor.\n\n");
        }
        return sb.ToString();
    }

    private static string DescribeVolume()
    {
        string volumeKeyword = Constants.Terminal.KeywordVolume;
        string? rawArg = ArgAfterVerb();
        int currentPercent = GetVolumePercent();

        if (string.IsNullOrEmpty(rawArg))
        {
            return $"\nTERMINAL PLAYBACK VOLUME: {currentPercent}%\n" +
                   $"USAGE: {volumeKeyword} <0-100>\n\n";
        }

        if (!TryParseVolumePercent(rawArg, out int parsedPercent))
        {
            return $"\nINVALID VOLUME VALUE '{rawArg}'.\n" +
                   $"USAGE: {volumeKeyword} <0-100>\n" +
                   $"CURRENT: {currentPercent}%\n\n";
        }

        return FormatStatus($"PLAYBACK VOLUME - {parsedPercent}%.");
    }

    private static string FormatStatus(string message)
    {
        string body = message?.TrimEnd() ?? string.Empty;
        return "\n" + body + "\n\n";
    }
}
