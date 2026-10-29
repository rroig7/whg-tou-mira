using BepInEx;
using BepInEx.Unity.IL2CPP;
using TownOfUs;

namespace TouMiraBclBridge;

/// <summary>
/// Client-only companion to Town of Us: Mira. Reads TOU-Mira's per-player effects (jailed,
/// blackmailed, controlled, ...) and serves them on localhost for BetterCrewLink. It sends no game
/// RPCs, so it doesn't need to be installed by every player in the lobby.
/// </summary>
[BepInPlugin(Id, "TOU-Mira BetterCrewLink Bridge", "0.1.0")]
[BepInProcess("Among Us.exe")]
[BepInDependency(TownOfUsPlugin.Id)]
public sealed class BridgePlugin : BasePlugin
{
    public const string Id = "whg.tou.bcl-bridge";

    /// <summary>Must match TOU_MIRA_BRIDGE_PORT in BetterCrewLink's src/common/TouMira.ts.</summary>
    public const int Port = 47652;

    internal static BridgeServer? Server { get; private set; }

    public override void Load()
    {
        Server = new BridgeServer(Port, Log);
        Server.Start();
        AddComponent<BridgeBehaviour>();
    }

    public override bool Unload()
    {
        Server?.Dispose();
        Server = null;
        return true;
    }
}
