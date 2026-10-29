using System.Diagnostics;
using System.IO.Pipes;
using System.Security.Principal;
using System.Text;
using ISLe8.Core.Agents;
using ISLe8.Core.Diagnostics;
using Microsoft.UI.Dispatching;

namespace ISLe8.Agents;

/// <summary>
/// The pipe <c>isle8</c> talks to (LocalServer.swift): one JSON line in, one out, per connection.
/// Only this user can open it. Requests go to <see cref="AgentHub"/> on the UI thread; an approval's
/// answer may come minutes later, and a hook that gives up first closes its end, which is noticed.
/// Waiting costs nothing: every wait is an overlapped I/O, no thread spins.
/// </summary>
internal sealed class AgentServer : IDisposable
{
    private readonly AgentHub _hub;
    private readonly DispatcherQueue _queue;
    private readonly CancellationTokenSource _stop = new();
    private int _connections;

    public AgentServer(AgentHub hub, DispatcherQueue queue)
    {
        _hub = hub;
        _queue = queue;
    }

    public static string PipeName { get; } = AgentPipe.Name(WindowsIdentity.GetCurrent().User?.Value ?? "", Process.GetCurrentProcess().SessionId);

    public void Start() => _ = Task.Run(AcceptAsync);

    public void Dispose()
    {
        _stop.Cancel();
        _stop.Dispose();
    }

    private async Task AcceptAsync()
    {
        while (!_stop.IsCancellationRequested)
        {
            NamedPipeServerStream pipe;
            try
            {
                pipe = new NamedPipeServerStream(PipeName, PipeDirection.InOut, NamedPipeServerStream.MaxAllowedServerInstances,
                    PipeTransmissionMode.Byte, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                Log.Error(e, "agent pipe");
                return;
            }
            try
            {
                await pipe.WaitForConnectionAsync(_stop.Token);
            }
            catch (Exception e) when (e is OperationCanceledException or ObjectDisposedException or IOException)
            {
                await pipe.DisposeAsync();
                if (e is IOException) continue;
                return;
            }
            // The next end listens at once; this one is served meanwhile.
            _ = ServeAsync(pipe, Interlocked.Increment(ref _connections));
        }
    }

    private async Task ServeAsync(NamedPipeServerStream pipe, int connection)
    {
        await using var _ = pipe;
        try
        {
            if (await ReadLineAsync(pipe) is not { } line) return;
            var answer = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
            _queue.TryEnqueue(() => _hub.Handle(line, connection, reply => answer.TrySetResult(reply)));
            // The client sends nothing more: a read that ends means it went away.
            var gone = WaitUntilClosedAsync(pipe);
            if (await Task.WhenAny(answer.Task, gone) == gone)
            {
                _queue.TryEnqueue(() => _hub.Abandoned(connection));
                return;
            }
            await pipe.WriteAsync(Encoding.UTF8.GetBytes(answer.Task.Result + "\n"));
            await pipe.FlushAsync();
            // Closing first could throw the answer away before the client read it.
            await Task.WhenAny(gone, Task.Delay(TimeSpan.FromSeconds(5)));
        }
        catch (Exception e) when (e is IOException or ObjectDisposedException or OperationCanceledException)
        {
        }
    }

    private static async Task<string?> ReadLineAsync(Stream pipe)
    {
        var buffer = new MemoryStream();
        var chunk = new byte[8192];
        while (true)
        {
            var count = await pipe.ReadAsync(chunk);
            if (count == 0) return null;
            var end = Array.IndexOf(chunk, (byte)'\n', 0, count);
            buffer.Write(chunk, 0, end < 0 ? count : end);
            if (end >= 0) return Encoding.UTF8.GetString(buffer.ToArray());
            if (buffer.Length > AgentPipe.MaxRequestBytes) return null;
        }
    }

    private static async Task WaitUntilClosedAsync(Stream pipe)
    {
        var chunk = new byte[256];
        try
        {
            while (await pipe.ReadAsync(chunk) > 0)
            {
            }
        }
        catch (Exception e) when (e is IOException or ObjectDisposedException)
        {
        }
    }
}
