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
        Assert.True(factory.LastConnection.ScalarExecuted);
        Assert.Equal("SELECT 1", factory.LastConnection.LastCommandText);
        Assert.True(factory.LastConnection.WasDisposed);
    }

    [Fact]
    public async Task Execute_CopiesOutputParameterValueBack()
    {
        var database = new AdoNetGameDatabase(new ProbeFactory(), "Data Source=fake", healthCheckCommand: string.Empty);
        var output = new DatabaseParameter("@result", null, DbType.Int32, ParameterDirection.Output);

        var affected = await database.ExecuteAsync(new DatabaseCommand(
            "CALL demo",
            [output],
            CommandType.StoredProcedure));

        Assert.Equal(1, affected);
        Assert.Equal(42, output.Value);
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
        public bool ScalarExecuted { get; set; }
        public string? LastCommandText { get; set; }
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

        protected override DbCommand CreateDbCommand() => new ProbeCommand(this);

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

    private sealed class ProbeCommand : DbCommand
    {
        private readonly ProbeConnection _connection;
        private readonly ProbeParameterCollection _parameters = new();

        public ProbeCommand(ProbeConnection connection) => _connection = connection;

        [System.Diagnostics.CodeAnalysis.AllowNull]
        public override string CommandText { get; set; } = string.Empty;
        public override int CommandTimeout { get; set; }
        public override CommandType CommandType { get; set; }
        public override bool DesignTimeVisible { get; set; }
        public override UpdateRowSource UpdatedRowSource { get; set; }
        protected override DbConnection? DbConnection { get; set; }
        protected override DbParameterCollection DbParameterCollection => _parameters;
        protected override DbTransaction? DbTransaction { get; set; }

        public override void Cancel() { }
        public override void Prepare() { }
        public override int ExecuteNonQuery()
        {
            foreach (DbParameter parameter in _parameters)
            {
                if (parameter.Direction is ParameterDirection.Output
                    or ParameterDirection.InputOutput
                    or ParameterDirection.ReturnValue)
                    parameter.Value = 42;
            }

            return 1;
        }

        public override object? ExecuteScalar()
        {
            _connection.ScalarExecuted = true;
            _connection.LastCommandText = CommandText;
            return 1;
        }

        protected override DbParameter CreateDbParameter() => new ProbeParameter();
        protected override DbDataReader ExecuteDbDataReader(CommandBehavior behavior) =>
            throw new NotSupportedException();
    }

    private sealed class ProbeParameter : DbParameter
    {
        public override DbType DbType { get; set; }
        public override ParameterDirection Direction { get; set; }
        public override bool IsNullable { get; set; }
        [System.Diagnostics.CodeAnalysis.AllowNull]
        public override string ParameterName { get; set; } = string.Empty;
        [System.Diagnostics.CodeAnalysis.AllowNull]
        public override string SourceColumn { get; set; } = string.Empty;
        public override object? Value { get; set; }
        public override bool SourceColumnNullMapping { get; set; }
        public override int Size { get; set; }
        public override void ResetDbType() => DbType = DbType.String;
    }

    private sealed class ProbeParameterCollection : DbParameterCollection
    {
        private readonly List<DbParameter> _items = [];

        public override int Count => _items.Count;
        public override object SyncRoot => _items;

        public override int Add(object value)
        {
            _items.Add((DbParameter)value);
            return _items.Count - 1;
        }

        public override void AddRange(Array values)
        {
            foreach (var value in values) Add(value!);
        }

        public override void Clear() => _items.Clear();
        public override bool Contains(object value) => _items.Contains((DbParameter)value);
        public override bool Contains(string value) => IndexOf(value) >= 0;
        public override void CopyTo(Array array, int index) => _items.CopyTo((DbParameter[])array, index);
        public override System.Collections.IEnumerator GetEnumerator() => _items.GetEnumerator();
        public override int IndexOf(object value) => _items.IndexOf((DbParameter)value);
        public override int IndexOf(string parameterName) =>
            _items.FindIndex(item => string.Equals(item.ParameterName, parameterName, StringComparison.Ordinal));
        public override void Insert(int index, object value) => _items.Insert(index, (DbParameter)value);
        public override void Remove(object value) => _items.Remove((DbParameter)value);
        public override void RemoveAt(int index) => _items.RemoveAt(index);
        public override void RemoveAt(string parameterName) => _items.RemoveAt(IndexOf(parameterName));

        protected override DbParameter GetParameter(int index) => _items[index];
        protected override DbParameter GetParameter(string parameterName) => _items[IndexOf(parameterName)];
        protected override void SetParameter(int index, DbParameter value) => _items[index] = value;
        protected override void SetParameter(string parameterName, DbParameter value) =>
            _items[IndexOf(parameterName)] = value;
    }
}
