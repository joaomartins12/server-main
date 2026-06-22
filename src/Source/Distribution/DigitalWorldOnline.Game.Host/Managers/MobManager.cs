using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AutoMapper;
using DigitalWorldOnline.Application.Separar.Queries;
using DigitalWorldOnline.Commons.DTOs.Config;
using DigitalWorldOnline.Commons.Models.Config;
using Serilog;
using MediatR;

namespace DigitalWorldOnline.Game.Managers
{
 /// <summary>
 /// Manager responsible for loading, caching and coalescing mob loads per map.
 /// </summary>
 public class MobManager
 {
 private readonly ISender _sender;
 private readonly IMapper _mapper;
 private readonly ILogger _logger;

 // cache: mapId -> (list, fetched)
 private readonly ConcurrentDictionary<long, (List<MobConfigModel> Items, DateTime Fetched)> _cache = new();
 private readonly TimeSpan _ttl;

 // coalescing: mapId -> in-flight task
 private readonly ConcurrentDictionary<long, Task<List<MobConfigModel>>> _ongoingLoads = new();

 public MobManager(ISender sender, IMapper mapper, ILogger logger, TimeSpan? ttl = null)
 {
 _sender = sender ?? throw new ArgumentNullException(nameof(sender));
 _mapper = mapper ?? throw new ArgumentNullException(nameof(mapper));
 _logger = logger ?? throw new ArgumentNullException(nameof(logger));
 _ttl = ttl ?? TimeSpan.FromSeconds(5);
 }

 public void InvalidateMapCache(long mapId)
 {
 _cache.TryRemove(mapId, out _);
 }

 public async Task<List<MobConfigModel>> GetMobsForMapAsync(long mapId, CancellationToken cancellationToken = default)
 {
 if (_cache.TryGetValue(mapId, out var entry) && (DateTime.UtcNow - entry.Fetched) < _ttl)
 {
 _logger.Debug("[MOB MANAGER] Cache hit for map {MapId}", mapId);
 return entry.Items;
 }

 // coalesce concurrent loads
 var loadTask = _ongoingLoads.GetOrAdd(mapId, _ => LoadAndCacheMobsAsync(mapId, cancellationToken));

 try
 {
 var result = await loadTask.ConfigureAwait(false);
 return result;
 }
 catch (Exception ex)
 {
 _logger.Error(ex, "[MOB MANAGER] Failed loading mobs for map {MapId}", mapId);
 throw;
 }
 }

 private async Task<List<MobConfigModel>> LoadAndCacheMobsAsync(long mapId, CancellationToken cancellationToken)
 {
 try
 {
 var sw = Stopwatch.StartNew();
 // call application query to get DTOs
 var dtos = await _sender.Send(new MapMobConfigsQuery(mapId));
 sw.Stop();

 _logger.Information("[MOB MANAGER] MapMobConfigsQuery mapId={MapId} took {Elapsed}ms", mapId, sw.ElapsedMilliseconds);

 var list = (dtos == null) ? new List<MobConfigModel>() : _mapper.Map<List<MobConfigModel>>(dtos);

 _cache[mapId] = (list, DateTime.UtcNow);

 return list;
 }
 finally
 {
 // ensure the in-flight entry is removed so retry/refresh can occur
 _ongoingLoads.TryRemove(mapId, out _);
 }
 }
 }
}
