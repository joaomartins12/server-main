using DigitalWorldOnline.Application.Separar.Commands.Update;
using DigitalWorldOnline.Commons.Interfaces;
using DigitalWorldOnline.Commons.Models.Base;
using Serilog;
using System.Threading.Channels;
using Microsoft.Extensions.DependencyInjection;
using System.Threading;
using System.Threading.Tasks;
using System.Text.Json;
using System.IO;
using System;
using System.Collections.Generic;

namespace DigitalWorldOnline.GameHost.Services
{
 public class UpdateItemsBackgroundQueue : IUpdateItemsBackgroundQueue, IDisposable
 {
 private sealed class WalEntry
 {
 public List<ItemModel> Items { get; set; } = new();
 public string WalFilePath { get; set; } = string.Empty;
 }

 private readonly Channel<WalEntry> _channel = Channel.CreateBounded<WalEntry>(1000);
 private readonly IServiceScopeFactory _scopeFactory;
 private readonly ILogger _logger;
 private readonly CancellationTokenSource _internalCts = new();
 private Task? _processingTask;

 private readonly string _walDir;

 public UpdateItemsBackgroundQueue(IServiceScopeFactory scopeFactory, ILogger logger)
 {
 _scopeFactory = scopeFactory;
 _logger = logger;

 _walDir = Path.Combine(AppContext.BaseDirectory, "wal", "items");
 try
 {
 Directory.CreateDirectory(_walDir);
 }
 catch (Exception ex)
 {
 _logger.Error(ex, "Failed to create WAL directory '{WalDir}'", _walDir);
 }
 }

 public void Enqueue(List<ItemModel> items)
 {
 if (items == null || items.Count ==0)
 return;

 // Write WAL before enqueueing
 var walFile = Path.Combine(_walDir, $"{Guid.NewGuid():N}.json");
 try
 {
 var json = JsonSerializer.Serialize(items);
 File.WriteAllText(walFile, json);
 }
 catch (Exception ex)
 {
 _logger.Error(ex, "Failed to write WAL for items batch");
 // Fall back to enqueue without WAL (best-effort) to avoid losing the update path
 walFile = string.Empty;
 }

 var entry = new WalEntry { Items = items, WalFilePath = walFile };

 if (!_channel.Writer.TryWrite(entry))
 {
 _logger.Warning("UpdateItemsBackgroundQueue full - dropping update batch (WAL={WalFile})", walFile);
 }
 }

 public async Task StartProcessing(CancellationToken cancellationToken)
 {
 var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _internalCts.Token);

 // On startup, enqueue any existing WAL files for processing
 try
 {
 foreach (var file in Directory.EnumerateFiles(_walDir, "*.json"))
 {
 try
 {
 var content = await File.ReadAllTextAsync(file, linked.Token).ConfigureAwait(false);
 var items = JsonSerializer.Deserialize<List<ItemModel>>(content) ?? new List<ItemModel>();
 var entry = new WalEntry { Items = items, WalFilePath = file };
 // best-effort enqueue
 if (!_channel.Writer.TryWrite(entry))
 {
 _logger.Warning("Failed to enqueue WAL file on startup: {File}", file);
 break; // channel full, stop trying
 }
 }
 catch (OperationCanceledException) { break; }
 catch (Exception ex)
 {
 _logger.Error(ex, "Failed to read WAL file '{File}' on startup", file);
 }
 }
 }
 catch (DirectoryNotFoundException) { }
 catch (Exception ex)
 {
 _logger.Error(ex, "Error while scanning WAL directory");
 }

 _processingTask ??= Task.Run(() => ProcessLoop(linked.Token), linked.Token);
 await Task.CompletedTask;
 }

 private async Task ProcessLoop(CancellationToken cancellationToken)
 {
 try
 {
 while (!cancellationToken.IsCancellationRequested)
 {
 var read = await _channel.Reader.ReadAsync(cancellationToken);
 try
 {
 using var scope = _scopeFactory.CreateScope();
 var repository = scope.ServiceProvider.GetRequiredService<ICharacterCommandsRepository>();
 await repository.UpdateItemsAsync(read.Items);

 // On success, remove WAL file if present
 if (!string.IsNullOrWhiteSpace(read.WalFilePath) && File.Exists(read.WalFilePath))
 {
 try { File.Delete(read.WalFilePath); } catch (Exception ex)
 {
 _logger.Warning(ex, "Failed to delete WAL file '{WalFilePath}' after successful flush", read.WalFilePath);
 }
 }
 }
 catch (Exception ex)
 {
 _logger.Error(ex, "Failed to persist items batch in background; requeueing after delay");

 // Requeue after small delay to avoid tight failure loops
 _ = Task.Run(async () =>
 {
 try
 {
 await Task.Delay(1000, cancellationToken);
 var requeued = _channel.Writer.TryWrite(read);
 if (!requeued)
 {
 _logger.Warning("Failed to requeue items batch after failure; leaving WAL for later retry (WAL={WalFile})", read.WalFilePath);
 }
 }
 catch (Exception e)
 {
 _logger.Error(e, "Error while attempting to requeue failed items batch");
 }
 });
 }
 }
 }
 catch (OperationCanceledException) { }
 catch (Exception ex)
 {
 _logger.Error(ex, "Processing loop terminated unexpectedly");
 }
 }

 public void Dispose()
 {
 _internalCts.Cancel();
 _channel.Writer.Complete();
 try { _processingTask?.Wait(1000); } catch { }
 _internalCts.Dispose();
 }
 }
}