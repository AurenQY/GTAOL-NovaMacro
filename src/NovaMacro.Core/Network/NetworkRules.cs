namespace NovaMacro.Core.Network;

public enum RuleProtocol
{
    Any,
    Tcp,
    Udp,
}

/// <summary>One outbound/inbound block specification for the game executable.</summary>
/// <param name="RemoteHosts">Host names resolved to addresses at apply time.</param>
/// <param name="RemoteAddresses">Literal addresses (empty + no hosts = all addresses).</param>
public sealed record BlockSpec(RuleProtocol Protocol, string RemotePorts, IReadOnlyList<string> RemoteHosts, IReadOnlyList<string> RemoteAddresses, bool Inbound, bool Outbound);

/// <summary>Network cut rule sets (QuellGTA-style) and the session lock rule.</summary>
public static class NetworkRules
{
    public const string CutRuleName = "NovaMacro_GTA5断网";
    public const string LockRuleName = "NovaMacro_GTA5战局锁单";
    public const string HoverRuleName = "NovaMacro_GTA5断网悬停";
    public const string QosGameName = "NovaMacro_QOS限速_游戏";
    public const string QosAcceleratorName = "NovaMacro_QOS限速_加速器";

    /// <summary>GTA Online peer-to-peer port used for player connections.</summary>
    public const int PlayerPort = 6672;

    public static readonly string[] CloudSaveHosts = { "cs-gta5-prod.ros.rockstargames.com", "ps-gta5-prod.ros.rockstargames.com" };

    public static readonly string[] TransactionHosts = { "prod.p01sjc.pod.rockstargames.com", "prod.p02sjc.pod.rockstargames.com" };

    public static readonly string[] TransactionAddresses = { "185.56.65.200", "185.56.65.201" };

    public static IReadOnlyList<string> RuleNames { get; } = new[] { "全部断网", "仅断开存档(80,443)", "仅断开云存档", "仅断开交易" };

    public static BlockSpec ForCut(string rule) => rule switch
    {
        "仅断开存档(80,443)" => new BlockSpec(RuleProtocol.Tcp, "80,443", Array.Empty<string>(), Array.Empty<string>(), Inbound: false, Outbound: true),
        "仅断开云存档" => new BlockSpec(RuleProtocol.Any, string.Empty, CloudSaveHosts, Array.Empty<string>(), Inbound: true, Outbound: true),
        "仅断开交易" => new BlockSpec(RuleProtocol.Any, string.Empty, TransactionHosts, TransactionAddresses, Inbound: true, Outbound: true),
        _ => new BlockSpec(RuleProtocol.Any, string.Empty, Array.Empty<string>(), Array.Empty<string>(), Inbound: true, Outbound: true),
    };

    /// <summary>Blocks only player (P2P) traffic: UDP 6672 both directions.</summary>
    public static BlockSpec SessionLock { get; } = new(RuleProtocol.Udp, PlayerPort.ToString(), Array.Empty<string>(), Array.Empty<string>(), Inbound: true, Outbound: true);

    public static string Describe(string rule) => rule switch
    {
        "仅断开存档(80,443)" => "无法上传数据但仍可以留在当前战局！(借鉴 QuellGTA)",
        "仅断开云存档" => "无法上传云存档但仍可以交易！(借鉴 QuellGTA)",
        "仅断开交易" => "无法进行交易但仍可以上传数据！(借鉴 QuellGTA)",
        _ => "游戏全部网络已断开",
    };

    /// <summary>Converts a KB/s throttle rate into bits per second for a QoS policy.</summary>
    public static ulong ThrottleBitsPerSecond(int kilobytesPerSecond) => (ulong)Math.Max(1, kilobytesPerSecond) * 8UL * 1000UL;
}
