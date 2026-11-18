using System.Data.Common;
using Messenger.Application.Interfaces;
using Messenger.Infrastructure.Db;
using Messenger.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace Messenger.Tests.Services;

public sealed class FileDeletionProcessorTests
{
    [Fact]
    public async Task DatabaseQueryFailure_IsDeferredWithoutTouchingFiles()
    {
        // Перехватываем открытие соединения до сети: реальный PostgreSQL для этого отказа не нужен.
        var failure = new FailingConnection();
        await using var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql("Host=127.0.0.1;Database=unused;Username=unused;Password=unused")
            .AddInterceptors(failure).Options);
        var content = new UnusedContentStore();
        var logger = new RecordingLogger<FileDeletionProcessor>();
        var cleanup = new FileDeletionProcessor(db, content, logger);

        await cleanup.ProcessAsync();

        Assert.Equal(1, failure.OpenCalls);
        Assert.Equal(0, content.DeleteCalls);
        Assert.Contains(logger.Warnings, x => x.Contains("NpgsqlException"));
        Assert.DoesNotContain(logger.Warnings, x => x.Contains("database-secret"));
    }

    private sealed class FailingConnection : DbConnectionInterceptor
    {
        public int OpenCalls { get; private set; }
        public override ValueTask<InterceptionResult> ConnectionOpeningAsync(
            DbConnection connection, ConnectionEventData eventData, InterceptionResult result,
            CancellationToken cancellationToken = default)
        {
            OpenCalls++;
            throw new NpgsqlException("database-secret");
        }
    }

    private sealed class UnusedContentStore : IAttachmentContentStore
    {
        public int DeleteCalls { get; private set; }
        public Task<StoredContent> SaveAsync(Stream content, long maxBytes, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
        public Task DeleteAsync(string storageKey, CancellationToken cancellationToken = default)
        { DeleteCalls++; return Task.CompletedTask; }
        public Task<Stream?> OpenAsync(string storageKey, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }
}

internal sealed class RecordingLogger<T> : ILogger<T>
{
    public List<string> Warnings { get; } = [];
    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
    public bool IsEnabled(LogLevel logLevel) => true;
    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
    {
        if (logLevel == LogLevel.Warning) Warnings.Add(formatter(state, exception));
    }
}
