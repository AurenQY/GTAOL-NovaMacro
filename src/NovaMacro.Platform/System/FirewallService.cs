using System.Net;
using System.Net.Sockets;
using NovaMacro.Core.Network;

namespace NovaMacro.Platform.SystemTools;

/// <summary>Windows Defender Firewall rules through netsh advfirewall.</summary>
public static class FirewallService
{
    public static CommandResult AddBlock(string name, string program, BlockSpec spec)
    {
        Delete(name);
        var addresses = ResolveAddresses(spec);
        if (addresses is { Count: 0 })
        {
            return new CommandResult(-1, "无法解析规则地址");
        }

        CommandResult last = new(0, string.Empty);
        foreach (var dir in new[] { (spec.Outbound, "out"), (spec.Inbound, "in") }.Where(d => d.Item1).Select(d => d.Item2))
        {
            var args = $"advfirewall firewall add rule name={CommandRunner.Quote(name)} dir={dir} action=block enable=yes profile=any program={CommandRunner.Quote(program)}";
            if (spec.Protocol != RuleProtocol.Any)
            {
                args += " protocol=" + (spec.Protocol == RuleProtocol.Tcp ? "TCP" : "UDP");
                if (spec.RemotePorts.Length > 0)
                {
                    args += (dir == "out" ? " remoteport=" : " localport=") + spec.RemotePorts;
                }
            }

            if (addresses is not null)
            {
                args += " remoteip=" + string.Join(",", addresses);
            }

            last = CommandRunner.Run("netsh", args);
            if (!last.Success)
            {
                return last;
            }
        }

        return last;
    }

    public static CommandResult Delete(string name) =>
        CommandRunner.Run("netsh", $"advfirewall firewall delete rule name={CommandRunner.Quote(name)}");

    public static bool Exists(string name) =>
        CommandRunner.Run("netsh", $"advfirewall firewall show rule name={CommandRunner.Quote(name)}").Success;

    /// <summary>Firewall state per profile: 域/专用/公用 → on/off.</summary>
    public static IReadOnlyDictionary<string, bool> ProfileStates()
    {
        var result = new Dictionary<string, bool>();
        foreach (var (profile, name) in new[] { ("domainprofile", "域网络"), ("privateprofile", "专用网络"), ("publicprofile", "公用网络") })
        {
            var r = CommandRunner.Run("netsh", $"advfirewall show {profile} state");
            var on = r.Output.Contains(" ON", StringComparison.OrdinalIgnoreCase) || r.Output.Contains("启用", StringComparison.Ordinal);
            result[name] = on;
        }

        return result;
    }

    public static CommandResult SetProfileState(string profileName, bool on)
    {
        var profile = profileName switch
        {
            "域网络" => "domainprofile",
            "专用网络" => "privateprofile",
            _ => "publicprofile",
        };
        return CommandRunner.Run("netsh", $"advfirewall set {profile} state {(on ? "on" : "off")}");
    }

    /// <summary>Returns null for "all addresses", otherwise the resolved list.</summary>
    public static List<string>? ResolveAddresses(BlockSpec spec)
    {
        if (spec.RemoteHosts.Count == 0 && spec.RemoteAddresses.Count == 0)
        {
            return null;
        }

        var list = new List<string>(spec.RemoteAddresses);
        foreach (var host in spec.RemoteHosts)
        {
            try
            {
                list.AddRange(Dns.GetHostAddresses(host).Where(a => a.AddressFamily == AddressFamily.InterNetwork).Select(a => a.ToString()));
            }
            catch (SocketException)
            {
            }
        }

        return list.Distinct().ToList();
    }
}
