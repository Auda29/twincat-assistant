using System.IO.Pipes;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Engineering.Safety;

namespace Engineering.Bridge;

public sealed class AuthenticationException(string message) : InvalidOperationException(message);
public sealed class BridgeProtocolException(string message) : InvalidOperationException(message);
file sealed class FrameReadTimeoutException : IOException;

public interface IBridgeReadService
{
    Task<object> GetContextAsync(JsonElement parameters, CancellationToken cancellationToken);
    Task<object> GetStatusAsync(JsonElement parameters, CancellationToken cancellationToken);
    Task<object> ReadTargetAsync(JsonElement parameters, CancellationToken cancellationToken);
}

public sealed class UnavailableBridgeReadService : IBridgeReadService
{
    public Task<object> GetContextAsync(JsonElement _, CancellationToken __) => Task.FromResult<object>(new { available = false, status = "unavailable", reason = "no native engineering adapter is installed", readOnly = true });
    public Task<object> GetStatusAsync(JsonElement _, CancellationToken __) => Task.FromResult<object>(new { available = false, status = "unavailable", reason = "no native engineering adapter is installed", readOnly = true });
    public Task<object> ReadTargetAsync(JsonElement _, CancellationToken __) => Task.FromResult<object>(new { available = false, status = "unavailable", reason = "no native engineering adapter is installed", readOnly = true });
}

public sealed class PipeBridgeServer : IDisposable
{
    public const int ProtocolVersion = 1;
    public const int DefaultMaxFrameBytes = 16 * 1024;
    private readonly IBridgeReadService _service;
    private readonly int _maxFrameBytes;
    private readonly TimeSpan _readTimeout;
    private CancellationTokenSource? _disposeCancellation;

    public PipeBridgeServer(IBridgeReadService service, int maxFrameBytes = DefaultMaxFrameBytes, TimeSpan? readTimeout = null)
    {
        _service = service ?? throw new ArgumentNullException(nameof(service));
        if (maxFrameBytes is < 256 or > 1024 * 1024) throw new ArgumentOutOfRangeException(nameof(maxFrameBytes));
        _maxFrameBytes = maxFrameBytes;
        _readTimeout = readTimeout ?? TimeSpan.FromSeconds(5);
        if (_readTimeout <= TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(readTimeout));
    }

    public async Task RunAsync(string pipeName, string authenticationToken, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(pipeName) || string.IsNullOrWhiteSpace(authenticationToken)) throw new ArgumentException("pipe name and authentication token are required");
        _disposeCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        while (!_disposeCancellation.IsCancellationRequested)
        {
            // Serve one peer at a time, but permit a replacement OS instance after
            // an expired peer: on Windows its still-open client handle can keep
            // the disconnected old instance alive after the server handle closes.
            await using var server = new NamedPipeServerStream(pipeName, PipeDirection.InOut, NamedPipeServerStream.MaxAllowedServerInstances, PipeTransmissionMode.Byte,
                PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly, _maxFrameBytes, _maxFrameBytes);
            try
            {
                await server.WaitForConnectionAsync(_disposeCancellation.Token).ConfigureAwait(false);
                await ServeConnectionAsync(server, authenticationToken, _disposeCancellation.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (_disposeCancellation.IsCancellationRequested) { break; }
            catch (IOException) when (_disposeCancellation.IsCancellationRequested) { break; }
            catch (IOException) when (!_disposeCancellation.IsCancellationRequested) { }
            catch (BridgeProtocolException) when (!_disposeCancellation.IsCancellationRequested) { }
        }
    }

    private async Task ServeConnectionAsync(Stream stream, string authenticationToken, CancellationToken cancellationToken)
    {
        string? helloLine;
        try { helloLine = await ReadWithDeadlineAsync(stream, cancellationToken).ConfigureAwait(false); }
        catch (FrameReadTimeoutException) { return; }
        if (helloLine is null) return;
        JsonDocument hello;
        try { hello = JsonDocument.Parse(helloLine); }
        catch (JsonException) { await WriteErrorAsync(stream, null, "invalid_json", "handshake is not JSON", cancellationToken).ConfigureAwait(false); return; }
        using (hello)
        {
            var root = hello.RootElement;
            if (root.ValueKind != JsonValueKind.Object || !TryGetString(root, "type", out var type) || type != "hello" || !TryGetInt(root, "protocol", out var protocol) || protocol != ProtocolVersion ||
                !TryGetString(root, "token", out var token) || !SecureEquals(token, authenticationToken))
            {
                await WriteErrorAsync(stream, null, "unauthorized", "authentication failed", cancellationToken).ConfigureAwait(false);
                return;
            }
        }
        await WriteJsonAsync(stream, new { type = "hello-ack", protocol = ProtocolVersion }, cancellationToken).ConfigureAwait(false);

        while (!cancellationToken.IsCancellationRequested)
        {
            string? line;
            try { line = await ReadWithDeadlineAsync(stream, cancellationToken).ConfigureAwait(false); }
            catch (FrameReadTimeoutException) { return; }
            catch (BridgeProtocolException error)
            {
                await WriteErrorAsync(stream, null, "invalid_request", error.Message, cancellationToken).ConfigureAwait(false);
                return;
            }
            if (line is null) return;
            JsonDocument request;
            try { request = JsonDocument.Parse(line); }
            catch (JsonException) { await WriteErrorAsync(stream, null, "invalid_json", "request is not JSON", cancellationToken).ConfigureAwait(false); continue; }
            using (request)
            {
                var root = request.RootElement;
                var id = root.ValueKind == JsonValueKind.Object && TryGetString(root, "id", out var requestId) ? requestId : null;
                if (root.ValueKind != JsonValueKind.Object || id is null || !TryGetString(root, "method", out var method) || !root.TryGetProperty("params", out var parameters) || parameters.ValueKind != JsonValueKind.Object)
                {
                    await WriteErrorAsync(stream, id, "invalid_request", "request requires string id, method, and object params", cancellationToken).ConfigureAwait(false);
                    continue;
                }
                try
                {
                    object result = method switch
                    {
                        "get-context" => await _service.GetContextAsync(parameters, cancellationToken).ConfigureAwait(false),
                        "get-status" => await _service.GetStatusAsync(parameters, cancellationToken).ConfigureAwait(false),
                        "read-target" => await _service.ReadTargetAsync(parameters, cancellationToken).ConfigureAwait(false),
                        _ => throw new BridgeProtocolException("method is not allowed")
                    };
                    await WriteJsonAsync(stream, new { id, result }, cancellationToken).ConfigureAwait(false);
                }
                catch (BridgeProtocolException error)
                {
                    await WriteErrorAsync(stream, id, "method_not_allowed", error.Message, cancellationToken).ConfigureAwait(false);
                }
                catch (EngineeringUnavailableException error)
                {
                    await WriteErrorAsync(stream, id, "unavailable", error.Message, cancellationToken).ConfigureAwait(false);
                }
                catch (Exception error) when (error is not OperationCanceledException)
                {
                    await WriteErrorAsync(stream, id, "internal_error", error.Message, cancellationToken).ConfigureAwait(false);
                }
            }
        }
    }

    internal async Task WriteErrorAsync(Stream stream, string? id, string code, string message, CancellationToken cancellationToken) =>
        await WriteJsonAsync(stream, new { id, error = new { code, message } }, cancellationToken).ConfigureAwait(false);

    internal async Task WriteJsonAsync(Stream stream, object value, CancellationToken cancellationToken)
    {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(value);
        if (bytes.Length > _maxFrameBytes) throw new BridgeProtocolException("response exceeds frame limit");
        await stream.WriteAsync(bytes, cancellationToken).ConfigureAwait(false);
        await stream.WriteAsync("\n"u8.ToArray(), cancellationToken).ConfigureAwait(false);
        // Named pipes are unbuffered here. Windows FlushFileBuffers waits for
        // the peer to drain and can block cancellation on a stalled peer.
    }

    private static bool SecureEquals(string left, string right) => CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(left), Encoding.UTF8.GetBytes(right));
    private static bool TryGetString(JsonElement root, string name, out string value)
    {
        if (root.ValueKind == JsonValueKind.Object && root.TryGetProperty(name, out var property) && property.ValueKind == JsonValueKind.String && property.GetString() is { } text)
        { value = text; return true; }
        value = string.Empty; return false;
    }
    private static bool TryGetInt(JsonElement root, string name, out int value)
    {
        if (root.ValueKind == JsonValueKind.Object && root.TryGetProperty(name, out var property) && property.ValueKind == JsonValueKind.Number && property.TryGetInt32(out var parsed))
        { value = parsed; return true; }
        value = 0; return false;
    }

    private async Task<string?> ReadWithDeadlineAsync(Stream stream, CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(_readTimeout);
        try { return await ReadBoundedLineAsync(stream, _maxFrameBytes, timeout.Token).ConfigureAwait(false); }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested) { throw new FrameReadTimeoutException(); }
    }

    public static async Task<string?> ReadBoundedLineAsync(Stream stream, int maxBytes, CancellationToken cancellationToken)
    {
        var bytes = new List<byte>(Math.Min(maxBytes, 1024));
        var one = new byte[1];
        while (true)
        {
            var read = await stream.ReadAsync(one, cancellationToken).ConfigureAwait(false);
            if (read == 0) return bytes.Count == 0 ? null : throw new BridgeProtocolException("truncated frame");
            if (one[0] == (byte)'\n') break;
            if (bytes.Count >= maxBytes) throw new BridgeProtocolException("frame exceeds limit");
            bytes.Add(one[0]);
        }
        if (bytes.Count > 0 && bytes[^1] == (byte)'\r') bytes.RemoveAt(bytes.Count - 1);
        return Encoding.UTF8.GetString(bytes.ToArray());
    }

    public void Dispose()
    {
        _disposeCancellation?.Cancel();
        _disposeCancellation?.Dispose();
        _disposeCancellation = null;
    }
}

public sealed class AuthenticatedPipeClient : IAsyncDisposable
{
    private readonly NamedPipeClientStream _pipe;
    private readonly int _maxFrameBytes;
    private int _sequence;

    internal AuthenticatedPipeClient(NamedPipeClientStream pipe, int maxFrameBytes)
    {
        _pipe = pipe;
        _maxFrameBytes = maxFrameBytes;
    }

    public static async Task<AuthenticatedPipeClient> ConnectAsync(string pipeName, string authenticationToken, TimeSpan? timeout = null, int maxFrameBytes = PipeBridgeServer.DefaultMaxFrameBytes, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(pipeName) || string.IsNullOrWhiteSpace(authenticationToken)) throw new ArgumentException("pipe name and authentication token are required");
        var pipe = new NamedPipeClientStream(".", pipeName, PipeDirection.InOut, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
        try
        {
            using var timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutSource.CancelAfter(timeout ?? TimeSpan.FromSeconds(5));
            await pipe.ConnectAsync(timeoutSource.Token).ConfigureAwait(false);
            var client = new AuthenticatedPipeClient(pipe, maxFrameBytes);
            await client.WriteJsonAsync(new { type = "hello", protocol = PipeBridgeServer.ProtocolVersion, token = authenticationToken }, cancellationToken).ConfigureAwait(false);
            var response = await client.ReadJsonAsync(cancellationToken).ConfigureAwait(false);
            if (response.TryGetProperty("error", out _)) throw new AuthenticationException("named pipe authentication failed");
            if (!response.TryGetProperty("type", out var type) || type.GetString() != "hello-ack") throw new AuthenticationException("invalid named pipe handshake");
            return client;
        }
        catch { await pipe.DisposeAsync().ConfigureAwait(false); throw; }
    }

    public async Task<JsonElement> RequestAsync(string method, object parameters, CancellationToken cancellationToken = default)
    {
        var id = $"ok-{Interlocked.Increment(ref _sequence)}";
        var payload = new { id, method, @params = parameters };
        await WriteJsonAsync(payload, cancellationToken).ConfigureAwait(false);
        var response = await ReadJsonAsync(cancellationToken).ConfigureAwait(false);
        if (!response.TryGetProperty("id", out var responseId) || responseId.ValueKind != JsonValueKind.String || responseId.GetString() != id)
            throw new BridgeProtocolException("response id did not match request");
        if (response.TryGetProperty("error", out _)) return response.Clone();
        if (!response.TryGetProperty("result", out _))
            throw new BridgeProtocolException("response did not contain a result");
        return response.Clone();
    }

    private async Task WriteJsonAsync(object value, CancellationToken cancellationToken)
    {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(value);
        if (bytes.Length > _maxFrameBytes) throw new BridgeProtocolException("request exceeds frame limit");
        await _pipe.WriteAsync(bytes, cancellationToken).ConfigureAwait(false);
        await _pipe.WriteAsync("\n"u8.ToArray(), cancellationToken).ConfigureAwait(false);
    }
    private async Task<JsonElement> ReadJsonAsync(CancellationToken cancellationToken)
    {
        var line = await PipeBridgeServer.ReadBoundedLineAsync(_pipe, _maxFrameBytes, cancellationToken).ConfigureAwait(false) ?? throw new BridgeProtocolException("pipe closed");
        try { using var document = JsonDocument.Parse(line); return document.RootElement.Clone(); }
        catch (JsonException error) { throw new BridgeProtocolException($"invalid JSON response: {error.Message}"); }
    }
    public ValueTask DisposeAsync() => _pipe.DisposeAsync();
}

public static class BridgeProgram
{
    public static async Task<int> Main(string[] args)
    {
        string? pipeName = null;
        string? tokenEnvironmentVariable = null;
        for (var index = 0; index < args.Length; index++)
        {
            if (args[index] == "--pipe" && index + 1 < args.Length) pipeName = args[++index];
            else if (args[index] == "--token-env" && index + 1 < args.Length) tokenEnvironmentVariable = args[++index];
            else return Usage();
        }
        if (string.IsNullOrWhiteSpace(pipeName) || string.IsNullOrWhiteSpace(tokenEnvironmentVariable)) return Usage();
        var token = Environment.GetEnvironmentVariable(tokenEnvironmentVariable);
        if (string.IsNullOrWhiteSpace(token)) return 2;
        using var stop = new CancellationTokenSource();
        Console.CancelKeyPress += (_, eventArgs) => { eventArgs.Cancel = true; stop.Cancel(); };
        await using var serverLifetime = new ServerLifetime(new PipeBridgeServer(new UnavailableBridgeReadService()));
        await serverLifetime.Server.RunAsync(pipeName, token, stop.Token).ConfigureAwait(false);
        return 0;
    }
    private static int Usage()
    {
        Console.Error.WriteLine("usage: Engineering.Bridge --pipe <name> --token-env <environment-variable>");
        return 2;
    }
    private sealed class ServerLifetime(PipeBridgeServer server) : IAsyncDisposable
    {
        public PipeBridgeServer Server { get; } = server;
        public ValueTask DisposeAsync() { Server.Dispose(); return ValueTask.CompletedTask; }
    }
}
