using Dawn;

namespace Operator.Cassette;

internal static class CassettePersistence
{
    private const string Ns = Constants.PluginMetadata.ContentNamespace;
    private static readonly NamespacedKey NextHandleKey = NamespacedKey.From(Ns, "cassette_next_handle");

    public static bool IsAvailable => TryGetSave(out _);

    private static bool TryGetSave(out PersistentDataContainer save)
    {
        save = null!;
        try
        {
            PersistentDataContainer? current = DawnLib.GetCurrentSave();
            if (current == null) return false;
            save = current;
            return true;
        }
        catch
        {
            return false;
        }
    }

    public static int AllocateHandle()
    {
        if (!TryGetSave(out PersistentDataContainer save)) return 0;
        int next = 1;
        if (save.TryGet(NextHandleKey, out int stored) && stored >= 1)
        {
            next = stored;
        }
        save.Set(NextHandleKey, next + 1);
        return next;
    }

    public static void Store(int handle, CassetteSaveData data)
    {
        if (handle <= 0 || data == null) return;
        if (!TryGetSave(out PersistentDataContainer save)) return;
        save.Set(EntryKey(handle), data);
    }

    public static bool TryLoad(int handle, out CassetteSaveData data)
    {
        data = null!;
        if (handle <= 0) return false;
        if (!TryGetSave(out PersistentDataContainer save)) return false;
        if (save.TryGet(EntryKey(handle), out CassetteSaveData? loaded) && loaded != null)
        {
            data = loaded;
            return true;
        }
        return false;
    }

    private static NamespacedKey EntryKey(int handle) => NamespacedKey.From(Ns, $"cassette.{handle}");
}
