using DigitalWorldOnline.Application.Separar.Commands.Update;
using Microsoft.Extensions.Hosting;
using Serilog;

namespace DigitalWorldOnline.GameHost.Services
{
 public class BackgroundUpdateItemsService : IHostedService
 {
 private readonly IUpdateItemsBackgroundQueue _queue;
 private readonly ILogger _logger;
 private CancellationTokenSource _cts = new();

 public BackgroundUpdateItemsService(IUpdateItemsBackgroundQueue queue, ILogger logger)
 {
 _queue = queue;
 _logger = logger;
 }

 public async Task StartAsync(CancellationToken cancellationToken)
 {
 _logger.Information("Starting background update items queue...");
 await _queue.StartProcessing(cancellationToken);
 }

 public Task StopAsync(CancellationToken cancellationToken)
 {
 _logger.Information("Stopping background update items queue...");
 _cts.Cancel();
 return Task.CompletedTask;
 }
 }
}