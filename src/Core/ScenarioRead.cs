using KTable.Core.Model;

namespace KTable.Core;

public static class ScenarioRead
{
    const int HeldTries = 4;
    const int HeldWaitMs = 250;

    public static (Scenario? Scenario, string? Refused) ToHost(string path)
    {
        for (int attempt = 1; ; attempt++)
        {
            try
            {
                if (Json.ReadFormat(path) is int format && format > Scenario.CurrentFormat)
                    return (null, $"was made by a newer version of the app (scenario format {format}; this version reads up to {Scenario.CurrentFormat}): update the app");
                var sc = Json.Load<Scenario>(path);
                return (sc is { Name: not null, Packs: not null, Sheets: not null, UmpireBoard: not null } ? sc : null, null);
            }
            catch (Exception e) when (SaveStore.IsDamage(e)) { return (null, null); }
            catch (Exception e) when (SaveStore.IsHeld(e) && attempt < HeldTries) { BestEffort.Note(e); Thread.Sleep(HeldWaitMs * attempt); }
            catch (Exception e) when (SaveStore.IsHeld(e)) { return (null, $"is held by another program ({e.Message}): close it and try again"); }
            catch (Exception e) when (e is IOException) { return (null, $"could not be read from the disk ({e.Message})"); }
        }
    }
}
