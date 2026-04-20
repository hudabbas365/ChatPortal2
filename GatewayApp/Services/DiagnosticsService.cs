using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Text.Json;
using GatewayApp.Models;

namespace GatewayApp.Services;

public sealed class DiagnosticsService
{
    private readonly ObservableCollection<TransactionLog> _logs = new();
    private readonly string _logFilePath;
    private readonly object _lock = new();

    public DiagnosticsService()
    {
        var dataDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AIInsights365Gateway");
        Directory.CreateDirectory(dataDir);
        _logFilePath = Path.Combine(dataDir, "diagnostics.log");
    }

    public void LogTransaction(TransactionLog log)
    {
        lock (_lock)
        {
            _logs.Add(log);
            var line = JsonSerializer.Serialize(log, JsonDefaults.Options);
            File.AppendAllLines(_logFilePath, new[] { line });
        }
    }

    public Task<List<TransactionLog>> GetLogsAsync()
    {
        var items = new List<TransactionLog>();
        lock (_lock)
        {
            items.AddRange(_logs);
        }

        if (File.Exists(_logFilePath))
        {
            foreach (var line in File.ReadLines(_logFilePath))
            {
                var parsed = JsonSerializer.Deserialize<TransactionLog>(line, JsonDefaults.Options);
                if (parsed is not null && items.All(x => x.Id != parsed.Id))
                {
                    items.Add(parsed);
                }
            }
        }

        return Task.FromResult(items.OrderByDescending(x => x.Timestamp).ToList());
    }
}
