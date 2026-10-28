using System;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace AiBots.Bots;

/// <summary>One bounded off-turn worker, so conversations never overlap and room listeners never wait on a provider.</summary>
public sealed class AiBotWork(ILogger<AiBotWork> logger) : BackgroundService
{
    private readonly Channel<Func<CancellationToken, Task>> _work = Channel.CreateBounded<Func<CancellationToken, Task>>(
        new BoundedChannelOptions(128) { SingleReader = true, FullMode = BoundedChannelFullMode.Wait });

    public bool TryEnqueue(Func<CancellationToken, Task> work) => _work.Writer.TryWrite(work);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            await foreach (var work in _work.Reader.ReadAllAsync(stoppingToken).ConfigureAwait(false))
            {
                try { await work(stoppingToken).ConfigureAwait(false); }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
                catch (Exception ex) { logger.LogError(ex, "AI bot work failed"); }
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { }
        finally { _work.Writer.TryComplete(); }
    }

    public override Task StopAsync(CancellationToken ct)
    {
        _work.Writer.TryComplete();
        return base.StopAsync(ct);
    }
}
