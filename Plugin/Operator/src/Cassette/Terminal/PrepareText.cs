namespace Operator.Cassette;

internal static class PrepareText
{
    internal static string For(bool hasFile, int waiting)
    {
        if (!hasFile) return "RECEIVING RECORDING...\nPLEASE WAIT";
        if (waiting == 1) return "WAITING FOR 1 PLAYER\nTO RECEIVE RECORDING...";
        if (waiting > 1) return $"WAITING FOR {waiting} PLAYERS\nTO RECEIVE RECORDING...";
        return "WAITING FOR PLAYERS\nTO RECEIVE RECORDING...";
    }
}
