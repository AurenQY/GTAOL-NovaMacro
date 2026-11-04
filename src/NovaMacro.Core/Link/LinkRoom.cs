using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace NovaMacro.Core.Link;

public static class LinkKinds
{
    public const string NetworkCut = "断网";
    public const string Solo = "卡单";
    public const string KillProcess = "断进程";

    public static IReadOnlyList<string> All { get; } = new[] { NetworkCut, Solo, KillProcess };
}

public sealed class LinkMessage
{
    [JsonPropertyName("t")]
    public string Type { get; set; } = string.Empty;

    [JsonPropertyName("昵称")]
    public string? Nickname { get; set; }

    [JsonPropertyName("消息")]
    public string? Message { get; set; }

    [JsonPropertyName("联动类型")]
    public string? Kind { get; set; }

    [JsonPropertyName("成员")]
    public List<string>? Members { get; set; }

    public static LinkMessage? Parse(string line)
    {
        try
        {
            return JsonSerializer.Deserialize<LinkMessage>(line);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    public string Serialize() => JsonSerializer.Serialize(this, new JsonSerializerOptions
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    });
}

/// <summary>
/// 联动功能 — one player hosts a room (TCP, default port 19730), others join with a nickname.
/// Members can chat and trigger a "联动" (断网/卡单/断进程) that every member executes locally.
/// </summary>
public sealed class LinkRoom : IDisposable
{
    private readonly ConcurrentDictionary<Guid, Peer> _peers = new();
    private readonly HashSet<string> _blacklist = new(StringComparer.Ordinal);
    private TcpListener? _listener;
    private Peer? _client;
    private CancellationTokenSource? _cts;

    public event EventHandler<string>? Status;

    public event EventHandler<LinkMessage>? Chat;

    /// <summary>A link action to execute locally (raised for every member, including the initiator).</summary>
    public event EventHandler<LinkMessage>? LinkTriggered;

    public event EventHandler<IReadOnlyList<string>>? MembersChanged;

    public bool IsHost => _listener is not null;

    public bool IsConnected => IsHost || _client is { IsConnected: true };

    public string Nickname { get; private set; } = string.Empty;

    public IReadOnlyCollection<string> Blacklist
    {
        get
        {
            lock (_blacklist)
            {
                return _blacklist.ToArray();
            }
        }
    }

    public void SetBlacklist(IEnumerable<string> names)
    {
        lock (_blacklist)
        {
            _blacklist.Clear();
            foreach (var n in names)
            {
                _blacklist.Add(n);
            }
        }
    }

    public int Host(int port, string nickname)
    {
        if (string.IsNullOrWhiteSpace(nickname))
        {
            throw new ArgumentException("[联动功能] 联动昵称不能为空！");
        }

        Leave();
        Nickname = nickname;
        _cts = new CancellationTokenSource();
        _listener = new TcpListener(IPAddress.Any, port);
        _listener.Start();
        var actualPort = ((IPEndPoint)_listener.LocalEndpoint).Port;
        _ = AcceptLoop(_listener, _cts.Token);
        Status?.Invoke(this, $"[联动功能] [你创建了一个服务端，连接端口为：{actualPort}]");
        RaiseMembers();
        return actualPort;
    }

    public async Task JoinAsync(string address, string nickname, CancellationToken token = default)
    {
        if (string.IsNullOrWhiteSpace(nickname))
        {
            throw new ArgumentException("[联动功能] 联动昵称不能为空！");
        }

        Leave();
        Nickname = nickname;
        var (host, port) = ParseAddress(address);
        var tcp = new TcpClient();
        try
        {
            await tcp.ConnectAsync(host, port, token).ConfigureAwait(false);
        }
        catch (SocketException)
        {
            tcp.Dispose();
            Status?.Invoke(this, "[联动功能] [无法连接到服务器，对方可能没有创建房间]");
            throw;
        }

        _cts = new CancellationTokenSource();
        _client = new Peer(tcp);
        await _client.SendAsync(new LinkMessage { Type = "hello", Nickname = nickname }).ConfigureAwait(false);
        _ = ClientLoop(_client, _cts.Token);
        Status?.Invoke(this, "连接状态：已连接");
    }

    public static (string Host, int Port) ParseAddress(string address, int defaultPort = 19730)
    {
        var text = address.Trim();
        var colon = text.LastIndexOf(':');
        if (colon > 0 && int.TryParse(text[(colon + 1)..], out var port))
        {
            return (text[..colon], port);
        }

        return (text.Length == 0 ? "127.0.0.1" : text, defaultPort);
    }

    public Task SendChatAsync(string message) => Broadcast(new LinkMessage { Type = "chat", Nickname = Nickname, Message = message });

    public Task TriggerLinkAsync(string kind) => Broadcast(new LinkMessage { Type = "link", Nickname = Nickname, Kind = kind });

    public async Task KickAsync(string nickname, bool blacklist)
    {
        if (!IsHost)
        {
            return;
        }

        if (blacklist)
        {
            lock (_blacklist)
            {
                _blacklist.Add(nickname);
            }
        }

        foreach (var peer in _peers.Values.Where(p => p.Nickname == nickname).ToArray())
        {
            await peer.SendAsync(new LinkMessage { Type = blacklist ? "banned" : "kick" }).ConfigureAwait(false);
            Drop(peer);
            Status?.Invoke(this, blacklist ? $"[{nickname}] 被拉入了黑名单" : $"[{nickname}] 被踢出了房间");
        }
    }

    public IReadOnlyList<string> Members =>
        IsHost ? new[] { Nickname }.Concat(_peers.Values.Where(p => p.Nickname is not null).Select(p => p.Nickname!)).ToArray() : _members;

    private IReadOnlyList<string> _members = Array.Empty<string>();

    public void Leave()
    {
        var wasConnected = IsConnected;
        _cts?.Cancel();
        if (_listener is not null)
        {
            foreach (var peer in _peers.Values)
            {
                _ = peer.SendAsync(new LinkMessage { Type = "closed" });
                peer.Dispose();
            }

            _peers.Clear();
            _listener.Stop();
            _listener = null;
        }

        _client?.Dispose();
        _client = null;
        _members = Array.Empty<string>();
        if (wasConnected)
        {
            Status?.Invoke(this, "[联动功能] [你退出了房间]");
        }
    }

    public void Dispose() => Leave();

    private async Task Broadcast(LinkMessage message)
    {
        if (IsHost)
        {
            await HandleFromMember(null, message).ConfigureAwait(false);
        }
        else if (_client is not null)
        {
            await _client.SendAsync(message).ConfigureAwait(false);
        }
    }

    private async Task AcceptLoop(TcpListener listener, CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            TcpClient tcp;
            try
            {
                tcp = await listener.AcceptTcpClientAsync(token).ConfigureAwait(false);
            }
            catch (Exception) when (token.IsCancellationRequested)
            {
                return;
            }
            catch (SocketException)
            {
                continue;
            }
            catch (ObjectDisposedException)
            {
                return;
            }

            var peer = new Peer(tcp);
            _peers[peer.Id] = peer;
            _ = ServerPeerLoop(peer, token);
        }
    }

    private async Task ServerPeerLoop(Peer peer, CancellationToken token)
    {
        try
        {
            await foreach (var message in peer.ReadAsync(token).ConfigureAwait(false))
            {
                if (message.Type == "hello")
                {
                    var nick = message.Nickname ?? string.Empty;
                    bool banned;
                    lock (_blacklist)
                    {
                        banned = _blacklist.Contains(nick);
                    }

                    if (banned || nick.Length == 0)
                    {
                        await peer.SendAsync(new LinkMessage { Type = "banned" }).ConfigureAwait(false);
                        break;
                    }

                    peer.Nickname = nick;
                    Status?.Invoke(this, $"[{nick}] 加入了房间");
                    await BroadcastToPeers(new LinkMessage { Type = "chat", Nickname = "房间", Message = $"[{nick}] 加入了房间" }).ConfigureAwait(false);
                    await SendMembers().ConfigureAwait(false);
                    continue;
                }

                if (peer.Nickname is null)
                {
                    continue;
                }

                message.Nickname = peer.Nickname;
                await HandleFromMember(peer, message).ConfigureAwait(false);
            }
        }
        finally
        {
            Drop(peer);
        }
    }

    private async Task HandleFromMember(Peer? from, LinkMessage message)
    {
        switch (message.Type)
        {
            case "chat":
                Chat?.Invoke(this, message);
                await BroadcastToPeers(message).ConfigureAwait(false);
                break;
            case "link":
                Status?.Invoke(this, $"[联动功能] {message.Nickname} 发起了一个联动：{message.Kind}");
                LinkTriggered?.Invoke(this, message);
                await BroadcastToPeers(message).ConfigureAwait(false);
                break;
        }

        _ = from;
    }

    private async Task BroadcastToPeers(LinkMessage message)
    {
        foreach (var peer in _peers.Values.Where(p => p.Nickname is not null).ToArray())
        {
            await peer.SendAsync(message).ConfigureAwait(false);
        }
    }

    private async Task SendMembers()
    {
        var members = Members.ToList();
        await BroadcastToPeers(new LinkMessage { Type = "members", Members = members }).ConfigureAwait(false);
        RaiseMembers();
    }

    private void Drop(Peer peer)
    {
        if (_peers.TryRemove(peer.Id, out _))
        {
            peer.Dispose();
            if (peer.Nickname is not null)
            {
                Status?.Invoke(this, $"[{peer.Nickname}] 离开了房间");
                _ = SendMembers();
            }
        }
    }

    private void RaiseMembers() => MembersChanged?.Invoke(this, Members);

    private async Task ClientLoop(Peer peer, CancellationToken token)
    {
        var reason = "[联动功能] [你退出了房间]";
        try
        {
            await foreach (var message in peer.ReadAsync(token).ConfigureAwait(false))
            {
                switch (message.Type)
                {
                    case "chat":
                        Chat?.Invoke(this, message);
                        break;
                    case "link":
                        Status?.Invoke(this, $"[联动功能] {message.Nickname} 发起了一个联动：{message.Kind}");
                        LinkTriggered?.Invoke(this, message);
                        break;
                    case "members":
                        _members = message.Members ?? new List<string>();
                        RaiseMembers();
                        break;
                    case "kick":
                        reason = "[你被房主踢出了房间]";
                        return;
                    case "banned":
                        reason = "[你已被拉黑]";
                        return;
                    case "closed":
                        reason = "[房间已被关闭]";
                        return;
                }
            }
        }
        finally
        {
            if (ReferenceEquals(_client, peer))
            {
                _client = null;
                _members = Array.Empty<string>();
                peer.Dispose();
                Status?.Invoke(this, reason);
                Status?.Invoke(this, "连接状态：未连接");
                RaiseMembers();
            }
        }
    }

    private sealed class Peer : IDisposable
    {
        private readonly TcpClient _tcp;
        private readonly NetworkStream _stream;
        private readonly SemaphoreSlim _writeLock = new(1, 1);

        public Peer(TcpClient tcp)
        {
            _tcp = tcp;
            _stream = tcp.GetStream();
        }

        public Guid Id { get; } = Guid.NewGuid();

        public string? Nickname { get; set; }

        public bool IsConnected => _tcp.Connected;

        public async Task SendAsync(LinkMessage message)
        {
            var bytes = Encoding.UTF8.GetBytes(message.Serialize() + "\n");
            await _writeLock.WaitAsync().ConfigureAwait(false);
            try
            {
                await _stream.WriteAsync(bytes).ConfigureAwait(false);
                await _stream.FlushAsync().ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is IOException or ObjectDisposedException or SocketException)
            {
            }
            finally
            {
                _writeLock.Release();
            }
        }

        public async IAsyncEnumerable<LinkMessage> ReadAsync([System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken token)
        {
            using var reader = new StreamReader(_stream, Encoding.UTF8, leaveOpen: true);
            while (!token.IsCancellationRequested)
            {
                string? line;
                try
                {
                    line = await reader.ReadLineAsync(token).ConfigureAwait(false);
                }
                catch (Exception ex) when (ex is IOException or ObjectDisposedException or OperationCanceledException or SocketException)
                {
                    yield break;
                }

                if (line is null)
                {
                    yield break;
                }

                if (line.Length > 65536)
                {
                    continue;
                }

                var message = LinkMessage.Parse(line);
                if (message is not null)
                {
                    yield return message;
                }
            }
        }

        public void Dispose()
        {
            try
            {
                _tcp.Dispose();
            }
            catch (SocketException)
            {
            }
        }
    }
}
