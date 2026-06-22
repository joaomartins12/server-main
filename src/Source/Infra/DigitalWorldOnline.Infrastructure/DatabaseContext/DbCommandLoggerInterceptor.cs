using System;
using System.Data.Common;
using System.Diagnostics;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Configuration;

namespace DigitalWorldOnline.Infrastructure
{
 public class DbCommandLoggerInterceptor : DbCommandInterceptor
 {
 private readonly string _logFilePath;
 private readonly TimeSpan _threshold;
 private static readonly object _fileLock = new();

 public DbCommandLoggerInterceptor(IConfiguration? configuration)
 {
 var logDir = configuration?["Logging:DbLogDirectory"] ?? "logs";
 var fileName = configuration?["Logging:DbLogFileName"] ?? "db-commands.txt";

 // default threshold:1000 ms (1 second)
 TimeSpan threshold = TimeSpan.FromMilliseconds(1000);

 if (configuration is not null)
 {
 // support milliseconds config first
 var msText = configuration["Logging:DbLogThresholdMs"];
 if (!string.IsNullOrEmpty(msText) && int.TryParse(msText, out var ms) && ms >=0)
 {
 threshold = TimeSpan.FromMilliseconds(ms);
 }
 else
 {
 // support seconds as alternative
 var secText = configuration["Logging:DbLogThresholdSeconds"];
 if (!string.IsNullOrEmpty(secText) && double.TryParse(secText, out var seconds) && seconds >=0)
 {
 threshold = TimeSpan.FromSeconds(seconds);
 }
 }
 }

 _threshold = threshold;

 try
 {
 Directory.CreateDirectory(logDir);
 }
 catch
 {
 // ignore directory creation errors
 }

 _logFilePath = Path.Combine(logDir, fileName);
 }

 private static string ComputeQueryHash(string text)
 {
 if (string.IsNullOrEmpty(text)) return string.Empty;
 using var sha = SHA256.Create();
 var bytes = Encoding.UTF8.GetBytes(text);
 var hash = sha.ComputeHash(bytes);
 var sb = new StringBuilder(hash.Length *2);
 foreach (var b in hash)
 sb.Append(b.ToString("x2"));
 return sb.ToString();
 }

 private void LogIfSlow(DbCommand command, TimeSpan elapsed, string? additional = null, string? dbContextName = null)
 {
 if (elapsed <= _threshold)
 return; // skip logging if below or equal to threshold

 try
 {
 // compute hash to help correlate same query text
 var queryHash = ComputeQueryHash(command.CommandText ?? string.Empty);

 // capture stack trace to identify call site; skip2 frames to avoid interceptor internals
 var stack = new StackTrace(2, true);
 var stackText = stack.ToString();

 var sb = new StringBuilder();
 sb.Append($"[{DateTime.UtcNow:O}] ElapsedMs={elapsed.TotalMilliseconds:0.###}; ");
 if (!string.IsNullOrEmpty(dbContextName))
 {
 sb.Append($"DbContext={dbContextName}; ");
 }
 sb.Append($"QueryHash={queryHash}; ");
 sb.Append($"CommandType={command.CommandType}; ");
 sb.Append($"CommandText={command.CommandText}; ");

 if (command.Parameters?.Count >0)
 {
 sb.Append("Parameters=[");
 var first = true;
 foreach (DbParameter p in command.Parameters)
 {
 if (!first) sb.Append(", ");
 first = false;
 sb.Append($"{p.ParameterName}={(p.Value is DBNull ? "NULL" : p.Value)}");
 }
 sb.Append("]; ");
 }

 if (!string.IsNullOrEmpty(additional))
 {
 sb.Append(additional);
 }

 // append stack trace on its own line for readability
 sb.AppendLine();
 sb.AppendLine("--- StackTrace ---");
 sb.AppendLine(stackText);

 var line = sb.ToString();
 lock (_fileLock)
 {
 File.AppendAllText(_logFilePath, line + Environment.NewLine);
 }
 }
 catch
 {
 // swallow any logging errors to avoid affecting DB operations
 }
 }

 public override DbDataReader ReaderExecuted(DbCommand command, CommandExecutedEventData eventData, DbDataReader result)
 {
 var elapsed = eventData?.Duration ?? TimeSpan.Zero;
 var ctxName = eventData?.Context?.GetType()?.FullName;
 LogIfSlow(command, elapsed, null, ctxName);
 return base.ReaderExecuted(command, eventData, result);
 }

 public override ValueTask<DbDataReader> ReaderExecutedAsync(DbCommand command, CommandExecutedEventData eventData, DbDataReader result, CancellationToken cancellationToken = default)
 {
 var elapsed = eventData?.Duration ?? TimeSpan.Zero;
 var ctxName = eventData?.Context?.GetType()?.FullName;
 LogIfSlow(command, elapsed, null, ctxName);
 return base.ReaderExecutedAsync(command, eventData, result, cancellationToken);
 }

 public override int NonQueryExecuted(DbCommand command, CommandExecutedEventData eventData, int result)
 {
 var elapsed = eventData?.Duration ?? TimeSpan.Zero;
 var ctxName = eventData?.Context?.GetType()?.FullName;
 LogIfSlow(command, elapsed, $"ResultRows={result}; ", ctxName);
 return base.NonQueryExecuted(command, eventData, result);
 }

 public override ValueTask<int> NonQueryExecutedAsync(DbCommand command, CommandExecutedEventData eventData, int result, CancellationToken cancellationToken = default)
 {
 var elapsed = eventData?.Duration ?? TimeSpan.Zero;
 var ctxName = eventData?.Context?.GetType()?.FullName;
 LogIfSlow(command, elapsed, $"ResultRows={result}; ", ctxName);
 return base.NonQueryExecutedAsync(command, eventData, result, cancellationToken);
 }

 public override object? ScalarExecuted(DbCommand command, CommandExecutedEventData eventData, object? result)
 {
 var elapsed = eventData?.Duration ?? TimeSpan.Zero;
 var ctxName = eventData?.Context?.GetType()?.FullName;
 LogIfSlow(command, elapsed, $"ScalarResult={result ?? "NULL"}; ", ctxName);
 return base.ScalarExecuted(command, eventData, result);
 }

 public override ValueTask<object?> ScalarExecutedAsync(DbCommand command, CommandExecutedEventData eventData, object? result, CancellationToken cancellationToken = default)
 {
 var elapsed = eventData?.Duration ?? TimeSpan.Zero;
 var ctxName = eventData?.Context?.GetType()?.FullName;
 LogIfSlow(command, elapsed, $"ScalarResult={result ?? "NULL"}; ", ctxName);
 return base.ScalarExecutedAsync(command, eventData, result, cancellationToken);
 }
 }
}
