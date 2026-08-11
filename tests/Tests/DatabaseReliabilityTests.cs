using System.Data;
using System.Data.Common;
using System.Diagnostics;

using Core.Configuration;
using Core.Persistence;

using Hosting;
using Hosting.HealthChecks;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;

using Xunit;

namespace Tests;

public sealed class DatabaseReliabilityTests
{
    [Fact]
    public async Task HealthProbe_OpensAndDisposesProviderConnection()
    {
        var factory = new ProbeFactory();
        var database = new AdoNetGameDatabase(factory, "Data Source=fake");

        await database.CheckHealthAsync();

        Assert.NotNull(factory.LastConnection);
        Assert.True(factory.LastConnection!.Opened);
        Assert.True(factory.LastConnection.WasDisposed);
    }

    [Fact]
    public async Task TransactionRollback_HasIndependentDeadlineAndPreservesOriginalException()
    {
        var factory = new ProbeFactory(hangingRollback: true);
        var database = new AdoNetGameDatabase(factory, "Data Source=fake", rollbackTimeoutSeconds: 1);
        var stopwatch = Stopwatch.StartNew();

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            database.InTransactionAsync<int>((_, _) => throw new InvalidOperationException("business failure")));

        stopwatch.Stop();
        Assert.Equal("business failure", exception.Message);
        Assert.True(factory.LastTransaction!.CancellationObserved);
        Assert.InRange(stopwatch.Elapsed, TimeSpan.FromMilliseconds(800), TimeSpan.FromSeconds(3));
    }

    [Fact]
    public void HostingRegistration_UsesSameDatabaseForApiAndHealthProbe()
    {
        var values = new Dictionary<string, string?>
        {
            ["database:enabled"] = "true",
            ["database:connectionString"] = "Data Source=fake",
            ["database:providerInvariantName"] = "fake"
        };
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(values).Build();
        var services = new ServiceCollection();
        services.AddGameDatabase(configuration, (_, _) => new ProbeFactory());
        using var provider = services.BuildServiceProvider();

        var database = provider.GetRequiredService<IGameDatabase>();
        var healthProbe = provider.GetRequiredService<IGameDatabaseHealthProbe>();

        Assert.Same(database, healthProbe);
    }

    [Fact]
    public async Task DatabaseHealthCheck_IsHealthyWhenDatabaseIsDisabled()
    {
        using var provider = new ServiceCollection().BuildServiceProvider();
        var check = new DatabaseHealthCheck(provider, Options.Create(new DatabaseOptions { Enabled = false }));

        var result = await check.CheckHealthAsync(new HealthCheckContext());

        Assert.Equal(HealthStatus.Healthy, result.Status);
        Assert.Equal("disabled", result.Description);
    }

    private sealed class ProbeFactory(bool hangingRollback = false) : DbProviderFactory
    {
        public ProbeConnection? LastConnection { get; private set; }
        public ProbeTransaction? LastTransaction => LastConnection?.Transaction;

        public override DbConnection CreateConnection() =>
            LastConnection = new ProbeConnection(hangingRollback);
    }

    private sealed class ProbeConnection : DbConnection
    {
        private readonly bool _hangingRollback;
        private ConnectionState _state;
        private string _connectionString;
        public bool Opened { get; private set; }
        public bool WasDisposed { get; private set; }
        public ProbeTransaction? Transaction { get; private set; }

        public ProbeConnection(bool hangingRollback)
        {
            _hangingRollback = hangingRollback;
            _connectionString = string.Empty;
        }

        [System.Diagnostics.CodeAnalysis.AllowNull]
        public override string ConnectionString
        {
            get => _connectionString;
            set => _connectionString = value ?? string.Empty;
        }
        public override string Database => "fake";
        public override string DataSource => "fake";
        public override string ServerVersion => "1";
        public override ConnectionState State => _state;

        public override void ChangeDatabase(string databaseName) { }
        public override void Close() => _state = ConnectionState.Closed;

        public override void Open()
        {
            Opened = true;
            _state = ConnectionState.Open;
        }

        public override Task OpenAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Open();
            return Task.CompletedTask;
        }

        protected override DbTransaction BeginDbTransaction(IsolationLevel isolationLevel) =>
            Transaction = new ProbeTransaction(this, isolationLevel, _hangingRollback);

        protected override DbCommand CreateDbCommand() => throw new NotSupportedException();

        protected override void Dispose(bool disposing)
        {
            WasDisposed = true;
            Close();
            base.Dispose(disposing);
        }
    }

    private sealed class ProbeTransaction(
        DbConnection connection,
        IsolationLevel isolationLevel,
        bool hangingRollback) : DbTransaction
    {
        public bool CancellationObserved { get; private set; }
        public override IsolationLevel IsolationLevel => isolationLevel;
        protected override DbConnection DbConnection => connection;
        public override void Commit() { }
        public override void Rollback() { }

        public override Task RollbackAsync(CancellationToken cancellationToken = default)
        {
            if (!hangingRollback) return Task.CompletedTask;
            cancellationToken.Register(() => CancellationObserved = true);
            return Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
        }
    }
}
