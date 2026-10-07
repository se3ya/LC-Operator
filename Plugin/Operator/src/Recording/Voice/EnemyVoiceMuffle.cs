using System.Collections.Generic;
using GameNetcodeStuff;

namespace Operator.Recording.Voice;

internal static class EnemyVoiceMuffle
{
    private static CentipedeAI? _localFlea;

    public static void SetFlea(CentipedeAI flea) => _localFlea = flea;

    public static void ClearFlea(CentipedeAI flea)
    {
        if (_localFlea == flea)
        {
            _localFlea = null;
        }
    }

    public static bool IsLocalFleaMuffled(PlayerControllerB? local)
    {
        CentipedeAI? flea = _localFlea;
        if (local != null && flea != null && flea.clingingToPlayer == local && !flea.isEnemyDead)
        {
            return true;
        }

        return IsFleaMuffled(local);
    }

    public static bool IsFleaMuffled(PlayerControllerB? player)
    {
        if (player == null) return false;

        List<CentipedeAI>? all = CentipedeAI.allCentipedes;
        if (all == null) return false;

        for (int i = 0; i < all.Count; i++)
        {
            CentipedeAI flea = all[i];
            if (flea == null || flea.isEnemyDead) continue;
            if (flea.clingingToPlayer == player) return true;
        }

        return false;
    }
}
