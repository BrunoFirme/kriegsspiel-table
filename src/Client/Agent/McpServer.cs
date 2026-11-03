using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;

namespace KTable.Client.Agent;

public sealed class McpServer : IDisposable
{
    public const int HeadLimit = 32 * 1024;
    public const int BodyLimit = 8 * 1024 * 1024;
    public const int MaxClients = 8;
    public static readonly TimeSpan ReadTimeout = TimeSpan.FromSeconds(20);
    public static readonly TimeSpan AnswerTimeout = TimeSpan.FromSeconds(120);

    readonly int _wantedPort;
    readonly byte[] _token;
    readonly Func<string, Task<string?>> _handle;
    readonly CancellationTokenSource _stop = new();
    readonly SemaphoreSlim _slots = new(MaxClients, MaxClients);
    TcpListener? _listener;
    int _calls;

    public McpServer(int port, string token, Func<string, Task<string?>> handle)
    {
        _wantedPort = port; _token = Encoding.UTF8.GetBytes(token); _handle = handle;
    }

    public int Port { get; private set; }
    public int Calls => Volatile.Read(ref _calls);
    public string Address => $"http://127.0.0.1:{Port}/mcp";

    public void Start()
    {
        _listener = new TcpListener(IPAddress.Loopback, _wantedPort);
        _listener.Start();
        Port = ((IPEndPoint)_listener.LocalEndpoint).Port;
        _ = Task.Run(AcceptLoop);
    }

    public void Dispose()
    {
        _stop.Cancel();
        try { _listener?.Stop(); } catch (SocketException) { }
    }

    async Task AcceptLoop()
    {
        while (!_stop.IsCancellationRequested)
        {
            TcpClient client;
            try { client = await _listener!.AcceptTcpClientAsync(_stop.Token); }
            catch (OperationCanceledException) { return; }
            catch (ObjectDisposedException) { return; }
            catch (SocketException) { if (_stop.IsCancellationRequested) return; continue; }
            _ = Task.Run(() => Serve(client));
        }
    }

    async Task Serve(TcpClient client)
    {
        using (client)
        {
            var stream = client.GetStream();
            if (!await _slots.WaitAsync(0)) { await Send(stream, 503, "busy: too many connections", "text/plain"); return; }
            try
            {
                using var read = CancellationTokenSource.CreateLinkedTokenSource(_stop.Token);
                read.CancelAfter(ReadTimeout);
                var request = await Request.Read(stream, read.Token);
                if (request == null) { await Send(stream, 400, "bad request", "text/plain"); return; }
                var (code, body, type) = await Answer(request);
                await Send(stream, code, body, type);
            }
            catch (OperationCanceledException) { }
            catch (IOException) { }
            catch (SocketException) { }
            finally { _slots.Release(); }
        }
    }

    async Task<(int code, string body, string type)> Answer(Request r)
    {
        if (!HostAllowed(r.Header("host"))) return (403, "only 127.0.0.1 or localhost", "text/plain");
        if (r.Header("origin") is { } origin && !OriginAllowed(origin)) return (403, "not from a web page", "text/plain");
        var path = r.Path.Split('?')[0];
        if (path != "/mcp") return (404, "the endpoint is /mcp", "text/plain");
        if (!Authorized(r.Header("authorization"))) return (401, "a Bearer token is needed: Settings → AI agent shows it", "text/plain");
        if (r.Method != "POST") return (405, "POST JSON-RPC to /mcp", "text/plain");
        Interlocked.Increment(ref _calls);
        var work = _handle(r.Body);
        var done = await Task.WhenAny(work, Task.Delay(AnswerTimeout, _stop.Token));
        if (done != work) return (504, "the app did not answer in time", "text/plain");
        var answer = await work;
        return answer == null ? (202, "", "text/plain") : (200, answer, "application/json");
    }

    bool HostAllowed(string? host) =>
        host != null && (host.Equals($"127.0.0.1:{Port}", StringComparison.OrdinalIgnoreCase) || host.Equals($"localhost:{Port}", StringComparison.OrdinalIgnoreCase) || host.Equals($"[::1]:{Port}", StringComparison.OrdinalIgnoreCase));

    bool OriginAllowed(string origin) =>
        origin.Equals($"http://127.0.0.1:{Port}", StringComparison.OrdinalIgnoreCase) || origin.Equals($"http://localhost:{Port}", StringComparison.OrdinalIgnoreCase);

    bool Authorized(string? header)
    {
        if (header == null || !header.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase) || _token.Length == 0) return false;
        var given = Encoding.UTF8.GetBytes(header[7..].Trim());
        return given.Length == _token.Length && CryptographicOperations.FixedTimeEquals(given, _token);
    }

    static async Task Send(NetworkStream stream, int code, string body, string type)
    {
        var bytes = Encoding.UTF8.GetBytes(body);
        var head = new StringBuilder();
        head.Append($"HTTP/1.1 {code} {Reason(code)}\r\n");
        if (bytes.Length > 0 || code != 202) head.Append($"Content-Type: {type}; charset=utf-8\r\n");
        head.Append($"Content-Length: {bytes.Length}\r\n");
        if (code == 401) head.Append("WWW-Authenticate: Bearer\r\n");
        if (code == 405) head.Append("Allow: POST\r\n");
        head.Append("Cache-Control: no-store\r\nConnection: close\r\n\r\n");
        try
        {
            await stream.WriteAsync(Encoding.ASCII.GetBytes(head.ToString()));
            if (bytes.Length > 0) await stream.WriteAsync(bytes);
            await stream.FlushAsync();
        }
        catch (IOException) { }
    }

    static string Reason(int code) => code switch
    {
        200 => "OK", 202 => "Accepted", 400 => "Bad Request", 401 => "Unauthorized", 403 => "Forbidden", 404 => "Not Found",
        405 => "Method Not Allowed", 411 => "Length Required", 413 => "Payload Too Large", 503 => "Service Unavailable", 504 => "Gateway Timeout",
        _ => "Error",
    };

    sealed class Request
    {
        public string Method = "", Path = "", Body = "";
        readonly Dictionary<string, string> _headers = new(StringComparer.OrdinalIgnoreCase);

        public string? Header(string name) => _headers.GetValueOrDefault(name);

        public static async Task<Request?> Read(NetworkStream stream, CancellationToken stop)
        {
            var buf = new byte[HeadLimit];
            int got = 0, end = -1;
            while (end < 0)
            {
                if (got == buf.Length) return null;
                int n = await stream.ReadAsync(buf.AsMemory(got), stop);
                if (n == 0) return null;
                got += n;
                end = IndexOf(buf, got, "\r\n\r\n"u8);
            }
            var lines = Encoding.ASCII.GetString(buf, 0, end).Split("\r\n");
            var first = lines[0].Split(' ');
            if (first.Length < 3) return null;
            var r = new Request { Method = first[0].ToUpperInvariant(), Path = first[1] };
            foreach (var line in lines.Skip(1))
            {
                int colon = line.IndexOf(':');
                if (colon <= 0) return null;
                r._headers[line[..colon].Trim()] = line[(colon + 1)..].Trim();
            }
            if (r.Method != "POST") return r;
            if (r.Header("transfer-encoding") != null) return null;
            if (!int.TryParse(r.Header("content-length"), out var length) || length < 0 || length > BodyLimit) return null;
            var body = new byte[length];
            int have = Math.Min(length, got - (end + 4));
            Array.Copy(buf, end + 4, body, 0, have);
            while (have < length)
            {
                int n = await stream.ReadAsync(body.AsMemory(have), stop);
                if (n == 0) return null;
                have += n;
            }
            r.Body = Encoding.UTF8.GetString(body);
            return r;
        }

        static int IndexOf(byte[] buf, int count, ReadOnlySpan<byte> what) => buf.AsSpan(0, count).IndexOf(what);
    }
}
