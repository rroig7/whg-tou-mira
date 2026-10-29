using System.Buffers;
using System.Text.Json;
using InnerNet;
using MiraAPI.Modifiers;
using TownOfUs.Modifiers.Crewmate;
using TownOfUs.Modifiers.Impostor;
using TownOfUs.Modifiers.Neutral;
using TownOfUs.Roles.Crewmate;
using TownOfUs.Utilities;

namespace TouMiraBclBridge;

/// <summary>
/// Reads TOU-Mira's per-player state on the Unity thread and serializes it in the format
/// BetterCrewLink's src/common/TouMira.ts expects. Must only be called from the Unity thread.
/// </summary>
internal sealed class SnapshotBuilder
{
    /// <summary>Must match TOU_MIRA_PROTOCOL_VERSION in BetterCrewLink's src/common/TouMira.ts.</summary>
    private const int ProtocolVersion = 1;

    private readonly ArrayBufferWriter<byte> _buffer = new(4096);
    private readonly HashSet<byte> _meetingBlackmailed = [];
    private readonly HashSet<byte> _nextRoundBlackmailed = [];
    private string _previousPhase = "menu";

    /// <summary>Returns one JSON line (with trailing newline).</summary>
    public byte[] Build()
    {
        var phase = ResolvePhase();
        TrackBlackmailAcrossMeeting(phase);

        _buffer.Clear();
        using (var json = new Utf8JsonWriter(_buffer))
        {
            json.WriteStartObject();
            json.WriteNumber("v", ProtocolVersion);
            json.WriteString("phase", phase);
            json.WriteNumber("localId", PlayerControl.LocalPlayer != null ? PlayerControl.LocalPlayer.PlayerId : -1);
            json.WriteStartArray("players");
            if (phase is not "menu")
            {
                foreach (var player in PlayerControl.AllPlayerControls)
                {
                    if (player == null || player.Data == null || player.Data.Disconnected)
                    {
                        continue;
                    }

                    WritePlayer(json, player, phase);
                }
            }

            json.WriteEndArray();
            json.WriteEndObject();
        }

        var line = new byte[_buffer.WrittenCount + 1];
        _buffer.WrittenSpan.CopyTo(line);
        line[^1] = (byte)'\n';
        return line;
    }

    private void WritePlayer(Utf8JsonWriter json, PlayerControl player, string phase)
    {
        var id = player.PlayerId;
        var jail = player.GetModifier<JailedModifier>();

        json.WriteStartObject();
        json.WriteNumber("id", id);
        json.WriteBoolean("dead", player.HasDied());
        // TOU removes the modifier as the meeting ends, so keep reporting it through the exile cutscene.
        json.WriteBoolean("blackmailed",
            player.HasModifier<BlackmailedModifier>() || (phase is "exile" && _meetingBlackmailed.Contains(id)));
        json.WriteBoolean("blackmailedNextRound", _nextRoundBlackmailed.Contains(id));
        json.WriteBoolean("jailed", jail != null);
        json.WriteBoolean("jailorAlive", jail != null && IsLivingJailor(jail.JailorId));
        json.WriteNumber("puppeteerControllerId", ControllerId(player.GetModifier<PuppeteerControlModifier>()?.Controller));
        json.WriteNumber("parasiteControllerId", ControllerId(player.GetModifier<ParasiteInfectedModifier>()?.Controller));
        json.WriteBoolean("swooped", player.HasModifier<SwoopModifier>());
        json.WriteBoolean("hacked", player.GetModifier<GlitchHackedModifier>() is { ShouldHideHacked: false });
        json.WriteBoolean("blinded",
            player.HasModifier<EclipsalBlindModifier>() || player.HasModifier<GrenadierFlashModifier>());
        json.WriteBoolean("hysteria", player.GetModifier<HypnotisedModifier>()?.HysteriaActive == true);
        WriteMediumSpirit(json, player);
        json.WriteNumber("mediatedBy", player.GetModifier<MediatedModifier>()?.MediumId ?? -1);
        json.WriteEndObject();
    }

    /// <summary>The Medium's spirit (ghost form) position as [x, y], or null when not in spirit form.</summary>
    private static void WriteMediumSpirit(Utf8JsonWriter json, PlayerControl player)
    {
        if (player.Data?.Role is MediumRole { Spirit: { } spirit } && spirit != null)
        {
            var position = spirit.transform.position;
            json.WriteStartArray("mediumSpirit");
            json.WriteNumberValue(position.x);
            json.WriteNumberValue(position.y);
            json.WriteEndArray();
            return;
        }

        json.WriteNull("mediumSpirit");
    }

    private static int ControllerId(PlayerControl? controller) => controller != null ? controller.PlayerId : -1;

    private static bool IsLivingJailor(byte playerId)
    {
        var jailor = MiscUtils.PlayerById(playerId);
        return jailor != null && !jailor.HasDied() && jailor.Data.Role is JailorRole;
    }

    private static string ResolvePhase()
    {
        var client = AmongUsClient.Instance;
        if (client == null || client.GameState == InnerNetClient.GameStates.NotJoined)
        {
            return "menu";
        }

        if (client.GameState != InnerNetClient.GameStates.Started)
        {
            return "lobby";
        }

        if (ExileController.Instance != null)
        {
            return "exile";
        }

        return MeetingHud.Instance != null ? "meeting" : "tasks";
    }

    /// <summary>
    /// Mirrors TOU-Mira's Perfect Comms integration: whoever was blackmailed during a meeting is
    /// remembered for the following task round, and forgotten when the next meeting starts.
    /// </summary>
    private void TrackBlackmailAcrossMeeting(string phase)
    {
        if (phase != _previousPhase)
        {
            if (phase is "meeting" or "lobby" or "menu")
            {
                _meetingBlackmailed.Clear();
                _nextRoundBlackmailed.Clear();
            }
            else if (phase is "tasks" && _previousPhase is "meeting" or "exile")
            {
                _nextRoundBlackmailed.Clear();
                _nextRoundBlackmailed.UnionWith(_meetingBlackmailed);
                _meetingBlackmailed.Clear();
            }

            _previousPhase = phase;
        }

        if (phase is not "meeting")
        {
            return;
        }

        foreach (var player in PlayerControl.AllPlayerControls)
        {
            if (player != null && !player.HasDied() && player.HasModifier<BlackmailedModifier>())
            {
                _meetingBlackmailed.Add(player.PlayerId);
            }
        }
    }
}
