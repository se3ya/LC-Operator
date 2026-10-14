using System.Collections.Generic;

namespace Operator.Cassette;

internal sealed class PrepareOutcome
{
    internal PrepareOutcome(string fileName, List<ulong> ready, List<ulong> gaveUp)
    {
        FileName = fileName;
        Ready = ready;
        GaveUp = gaveUp;
    }

    internal string FileName { get; }

    internal List<ulong> Ready { get; }

    internal List<ulong> GaveUp { get; }
}

internal sealed class PrepareRound
{
    private readonly HashSet<ulong> _clients = new();
    private readonly HashSet<ulong> _ready = new();
    private readonly HashSet<ulong> _gaveUp = new();
    private int _lastWaiting = -1;

    internal string FileName { get; private set; } = string.Empty;

    internal bool IsActive => FileName.Length > 0;

    internal void Begin(string fileName, IEnumerable<ulong> clients)
    {
        Clear();
        FileName = fileName;
        foreach (ulong id in clients) _clients.Add(id);
    }

    internal void Clear()
    {
        FileName = string.Empty;
        _clients.Clear();
        _ready.Clear();
        _gaveUp.Clear();
        _lastWaiting = -1;
    }

    internal bool IsReady(ulong clientId) => _ready.Contains(clientId);

    internal void MarkReady(ulong clientId, string fileName)
    {
        if (!IsActive || fileName != FileName) return;
        _ready.Add(clientId);
    }

    internal void MarkGaveUp(ulong clientId, string fileName)
    {
        if (!IsActive || fileName != FileName) return;
        _gaveUp.Add(clientId);
    }

    internal void GiveUpPending(IEnumerable<ulong> connected)
    {
        foreach (ulong id in connected)
        {
            if (!_clients.Contains(id)) continue;
            if (_ready.Contains(id)) continue;
            _gaveUp.Add(id);
        }
    }

    internal int CountWaiting(IEnumerable<ulong> connected)
    {
        int waiting = 0;
        foreach (ulong id in connected)
        {
            if (!_clients.Contains(id)) continue;
            if (_ready.Contains(id) || _gaveUp.Contains(id)) continue;
            waiting++;
        }
        return waiting;
    }

    internal bool IsNewWaiting(int waiting)
    {
        if (waiting == _lastWaiting) return false;
        _lastWaiting = waiting;
        return true;
    }

    internal PrepareOutcome Finish()
    {
        var outcome = new PrepareOutcome(FileName, new List<ulong>(_ready), new List<ulong>(_gaveUp));
        Clear();
        return outcome;
    }
}
