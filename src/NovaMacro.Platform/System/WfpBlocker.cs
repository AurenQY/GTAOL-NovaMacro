using System.Net;
using System.Runtime.InteropServices;
using NovaMacro.Core.Network;

namespace NovaMacro.Platform.SystemTools;

/// <summary>
/// WFP规则断网 — adds block filters for the game executable in a dynamic WFP session.
/// Works regardless of the Windows Firewall profile state; filters disappear when the
/// session is closed (or NovaMacro exits), so the game can never stay disconnected by accident.
/// </summary>
public sealed class WfpBlocker : IDisposable
{
    private static readonly Guid LayerConnectV4 = new("c38d57d1-05a7-4c33-904f-7fbceee60e82");
    private static readonly Guid LayerRecvAcceptV4 = new("e1cd9fe7-f4b5-4273-96c0-592e487b8650");
    private static readonly Guid LayerConnectV6 = new("4a72393b-319f-44bc-84c3-ba54dcb3b6b4");
    private static readonly Guid LayerRecvAcceptV6 = new("a3b42c97-9f04-4672-b87e-cee9c483257f");
    private static readonly Guid ConditionAppId = new("d78e1e87-8644-4ea5-9437-d809ecefc971");
    private static readonly Guid ConditionRemotePort = new("c35a604d-d22b-4e1a-91b4-68f674ee674b");
    private static readonly Guid ConditionProtocol = new("3971ef2b-623e-4f9a-8cb1-6e79b806b9a7");
    private static readonly Guid ConditionRemoteAddress = new("b235ae9a-1d64-49b8-a44c-5ff3d9095045");

    private const uint FwpActionBlock = 0x00001001;
    private const uint FwpmSessionFlagDynamic = 0x00000001;
    private const uint RpcAuthnDefault = 0xFFFFFFFF;
    private const int FwpUint8 = 1;
    private const int FwpUint16 = 2;
    private const int FwpUint32 = 3;
    private const int FwpByteBlobType = 12;

    private readonly Dictionary<string, List<ulong>> _filters = new();
    private nint _engine;

    public bool IsActive(string name) => _filters.ContainsKey(name);

    public string? Block(string name, string program, BlockSpec spec)
    {
        Unblock(name);
        if (_engine == 0)
        {
            var session = new FWPM_SESSION0 { flags = FwpmSessionFlagDynamic, displayData = new FWPM_DISPLAY_DATA0 { name = "NovaMacro" } };
            var rc = FwpmEngineOpen0(null, RpcAuthnDefault, 0, ref session, out _engine);
            if (rc != 0)
            {
                _engine = 0;
                return $"FwpmEngineOpen0 失败：0x{rc:X8}（需要管理员权限）";
            }
        }

        var rcApp = FwpmGetAppIdFromFileName0(program, out var appIdBlob);
        if (rcApp != 0)
        {
            return $"FwpmGetAppIdFromFileName0 失败：0x{rcApp:X8}";
        }

        var ids = new List<ulong>();
        try
        {
            var addresses = FirewallService.ResolveAddresses(spec);
            var ports = spec.RemotePorts.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(p => ushort.TryParse(p, out var v) ? v : (ushort)0).Where(p => p != 0).ToArray();
            var layers = new List<Guid>();
            if (spec.Outbound)
            {
                layers.Add(LayerConnectV4);
                if (addresses is null)
                {
                    layers.Add(LayerConnectV6);
                }
            }

            if (spec.Inbound)
            {
                layers.Add(LayerRecvAcceptV4);
                if (addresses is null)
                {
                    layers.Add(LayerRecvAcceptV6);
                }
            }

            var variants = new List<(ushort? Port, uint? Address)>();
            if (addresses is not null)
            {
                foreach (var a in addresses)
                {
                    if (IPAddress.TryParse(a, out var ip) && ip.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork)
                    {
                        var b = ip.GetAddressBytes();
                        variants.Add((null, (uint)((b[0] << 24) | (b[1] << 16) | (b[2] << 8) | b[3])));
                    }
                }
            }
            else if (ports.Length > 0)
            {
                variants.AddRange(ports.Select(p => ((ushort?)p, (uint?)null)));
            }
            else
            {
                variants.Add((null, null));
            }

            foreach (var layer in layers)
            {
                foreach (var (port, address) in variants)
                {
                    var id = AddFilter(name, layer, appIdBlob, spec.Protocol, port, address);
                    if (id is null)
                    {
                        foreach (var added in ids)
                        {
                            FwpmFilterDeleteById0(_engine, added);
                        }

                        return "FwpmFilterAdd0 失败（需要管理员权限）";
                    }

                    ids.Add(id.Value);
                }
            }
        }
        finally
        {
            FwpmFreeMemory0(ref appIdBlob);
        }

        _filters[name] = ids;
        return null;
    }

    public void Unblock(string name)
    {
        if (_engine == 0 || !_filters.Remove(name, out var ids))
        {
            return;
        }

        foreach (var id in ids)
        {
            FwpmFilterDeleteById0(_engine, id);
        }
    }

    public void Dispose()
    {
        if (_engine != 0)
        {
            FwpmEngineClose0(_engine);
            _engine = 0;
        }

        _filters.Clear();
    }

    private ulong? AddFilter(string name, Guid layer, nint appIdBlob, RuleProtocol protocol, ushort? port, uint? address)
    {
        var conditions = new List<FWPM_FILTER_CONDITION0>
        {
            new() { fieldKey = ConditionAppId, matchType = 0, conditionValue = new FWP_CONDITION_VALUE0 { type = FwpByteBlobType, value = (ulong)appIdBlob } },
        };
        if (protocol != RuleProtocol.Any)
        {
            conditions.Add(new FWPM_FILTER_CONDITION0 { fieldKey = ConditionProtocol, conditionValue = new FWP_CONDITION_VALUE0 { type = FwpUint8, value = protocol == RuleProtocol.Tcp ? 6UL : 17UL } });
        }

        if (port is not null)
        {
            conditions.Add(new FWPM_FILTER_CONDITION0 { fieldKey = ConditionRemotePort, conditionValue = new FWP_CONDITION_VALUE0 { type = FwpUint16, value = port.Value } });
        }

        if (address is not null)
        {
            conditions.Add(new FWPM_FILTER_CONDITION0 { fieldKey = ConditionRemoteAddress, conditionValue = new FWP_CONDITION_VALUE0 { type = FwpUint32, value = address.Value } });
        }

        var size = Marshal.SizeOf<FWPM_FILTER_CONDITION0>();
        var array = Marshal.AllocHGlobal(size * conditions.Count);
        var namePtr = Marshal.StringToHGlobalUni(name);
        try
        {
            for (var i = 0; i < conditions.Count; i++)
            {
                Marshal.StructureToPtr(conditions[i], array + (i * size), false);
            }

            var filter = new FWPM_FILTER0
            {
                filterKey = Guid.NewGuid(),
                displayData = new FWPM_DISPLAY_DATA0 { name = name },
                layerKey = layer,
                numFilterConditions = (uint)conditions.Count,
                filterCondition = array,
                action = new FWPM_ACTION0 { type = FwpActionBlock },
                weight = new FWP_VALUE0 { type = 0 },
            };
            var rc = FwpmFilterAdd0(_engine, ref filter, 0, out var id);
            return rc == 0 ? id : null;
        }
        finally
        {
            Marshal.FreeHGlobal(array);
            Marshal.FreeHGlobal(namePtr);
        }
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct FWPM_DISPLAY_DATA0
    {
        public string? name;
        public string? description;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct FWPM_SESSION0
    {
        public Guid sessionKey;
        public FWPM_DISPLAY_DATA0 displayData;
        public uint flags;
        public uint txnWaitTimeoutInMSec;
        public uint processId;
        public nint sid;
        public nint username;
        public int kernelMode;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct FWP_BYTE_BLOB
    {
        public uint size;
        public nint data;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct FWP_VALUE0
    {
        public int type;
        public ulong value;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct FWP_CONDITION_VALUE0
    {
        public int type;
        public ulong value;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct FWPM_FILTER_CONDITION0
    {
        public Guid fieldKey;
        public int matchType;
        public FWP_CONDITION_VALUE0 conditionValue;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct FWPM_ACTION0
    {
        public uint type;
        public Guid filterType;
    }

    [StructLayout(LayoutKind.Explicit, Size = 16)]
    private struct ContextUnion
    {
        [FieldOffset(0)]
        public ulong rawContext;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct FWPM_FILTER0
    {
        public Guid filterKey;
        public FWPM_DISPLAY_DATA0 displayData;
        public uint flags;
        public nint providerKey;
        public FWP_BYTE_BLOB providerData;
        public Guid layerKey;
        public Guid subLayerKey;
        public FWP_VALUE0 weight;
        public uint numFilterConditions;
        public nint filterCondition;
        public FWPM_ACTION0 action;
        public ContextUnion context;
        public nint reserved;
        public ulong filterId;
        public FWP_VALUE0 effectiveWeight;
    }

    [DllImport("fwpuclnt.dll", CharSet = CharSet.Unicode)]
    private static extern uint FwpmEngineOpen0(string? serverName, uint authnService, nint authIdentity, ref FWPM_SESSION0 session, out nint engineHandle);

    [DllImport("fwpuclnt.dll")]
    private static extern uint FwpmEngineClose0(nint engineHandle);

    [DllImport("fwpuclnt.dll", CharSet = CharSet.Unicode)]
    private static extern uint FwpmGetAppIdFromFileName0(string fileName, out nint appId);

    [DllImport("fwpuclnt.dll")]
    private static extern void FwpmFreeMemory0(ref nint p);

    [DllImport("fwpuclnt.dll")]
    private static extern uint FwpmFilterAdd0(nint engineHandle, ref FWPM_FILTER0 filter, nint sd, out ulong id);

    [DllImport("fwpuclnt.dll")]
    private static extern uint FwpmFilterDeleteById0(nint engineHandle, ulong id);
}
