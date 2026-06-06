using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Npgsql;
using Pos.Api.Auth;
using Pos.Application.Payments;
using Pos.Infrastructure.Persistence;
using Pos.SharedKernel.Ids;
using Xunit;

namespace Pos.Api.Tests;

/// <summary>
/// Boots the API against a dedicated `pos_test` Postgres database so the dev `pos` database
/// is never touched by the test run. The connection string is read from POS_TEST_DB; if the
/// variable isn't set we fall back to the same credentials the README's docker container
/// (postgres / pos / localhost:5544) so a fresh checkout runs green without ceremony.
/// The pos_test database is created on first run if it doesn't exist; migrations run on
/// fixture init; tests isolate by minting a fresh TenantId per case.
/// </summary>
public sealed class PosApiFixture : IAsyncLifetime
{
    /// <summary>The connection string used when POS_TEST_DB isn't set. Matches the README docker setup.</summary>
    public const string DefaultConnectionString =
        "Host=localhost;Port=5544;Database=pos_test;Username=postgres;Password=pos";

    public WebApplicationFactory<Program> Factory { get; private set; } = default!;
    public string ConnectionString { get; private set; } = string.Empty;

    /// <summary>The in-memory M-Pesa provider backing the test host. Reset it at the start of each test.</summary>
    public FakeMpesaClient Mpesa => Factory.Services.GetRequiredService<FakeMpesaClient>();

    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() }
    };

    public async Task InitializeAsync()
    {
        ConnectionString = Environment.GetEnvironmentVariable("POS_TEST_DB") ?? DefaultConnectionString;

        // EF's MigrateAsync does NOT create the database itself — it just runs the migration
        // scripts against an existing one. We create pos_test up front by connecting to the
        // maintenance "postgres" database and issuing CREATE DATABASE if it isn't there.
        await EnsureDatabaseExistsAsync(ConnectionString);

        // Program reads POS_DB at startup; point it at pos_test for the WebApplicationFactory.
        Environment.SetEnvironmentVariable("POS_DB", ConnectionString);

        Factory = new WebApplicationFactory<Program>()
            .WithWebHostBuilder(b =>
            {
                b.UseEnvironment("Testing");
                // Swap the real Daraja client for an in-memory fake so no test hits the network.
                b.ConfigureServices(services =>
                {
                    services.RemoveAll<IMpesaClient>();
                    services.AddSingleton<FakeMpesaClient>();
                    services.AddSingleton<IMpesaClient>(sp => sp.GetRequiredService<FakeMpesaClient>());
                });
            });

        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<PosDbContext>();
        await db.Database.MigrateAsync();
    }

    public async Task DisposeAsync()
    {
        // Null-guard: if InitializeAsync threw before Factory was assigned (e.g. the DB was
        // unreachable), don't mask that real error with a NullReferenceException here.
        if (Factory is not null)
            await Factory.DisposeAsync();
    }

    /// <summary>Builds an HttpClient pre-populated with the identity headers for a fresh scope.</summary>
    public (HttpClient Client, Guid TenantId, Guid StoreId, Guid UserId) NewClient()
    {
        var tenant = Uuid7.NewGuid();
        var store  = Uuid7.NewGuid();
        var user   = Uuid7.NewGuid();
        var client = Factory.CreateClient();
        client.DefaultRequestHeaders.Add(HeaderCurrentContext.TenantHeader, tenant.ToString());
        client.DefaultRequestHeaders.Add(HeaderCurrentContext.StoreHeader,  store.ToString());
        client.DefaultRequestHeaders.Add(HeaderCurrentContext.UserHeader,   user.ToString());
        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        return (client, tenant, store, user);
    }

    private static async Task EnsureDatabaseExistsAsync(string conn)
    {
        var builder = new NpgsqlConnectionStringBuilder(conn);
        var dbName = builder.Database
            ?? throw new InvalidOperationException("Connection string is missing Database=.");

        // Reconnect to the maintenance database to query pg_database + create if missing.
        builder.Database = "postgres";
        await using var c = new NpgsqlConnection(builder.ConnectionString);
        await c.OpenAsync();

        await using (var check = new NpgsqlCommand("SELECT 1 FROM pg_database WHERE datname = @name", c))
        {
            check.Parameters.AddWithValue("@name", dbName);
            if (await check.ExecuteScalarAsync() is not null) return;
        }

        // CREATE DATABASE doesn't accept query parameters; quote the identifier safely.
        await using var create = new NpgsqlCommand($"CREATE DATABASE {QuoteIdentifier(dbName)}", c);
        await create.ExecuteNonQueryAsync();
    }

    private static string QuoteIdentifier(string ident) => "\"" + ident.Replace("\"", "\"\"") + "\"";
}

[CollectionDefinition(Name)]
public sealed class PosApiCollection : ICollectionFixture<PosApiFixture>
{
    public const string Name = "Pos.Api";
}
