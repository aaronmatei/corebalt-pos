using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Pos.Api.Auth;
using Pos.Infrastructure.Persistence;
using Pos.SharedKernel.Ids;
using Xunit;

namespace Pos.Api.Tests;

/// <summary>
/// Boots the API against a dedicated `pos_test` Postgres database so the dev `pos` database
/// is never touched by the test run. The connection string is derived from POS_DB_TEST,
/// or — if that isn't set — POS_DB with the database swapped to pos_test. Migrations run
/// once on fixture init; tests isolate by minting a fresh TenantId per case.
/// </summary>
public sealed class PosApiFixture : IAsyncLifetime
{
    private const string DefaultPassword = "pos"; // matches CLAUDE.md's docker default
    private const string TestDatabase    = "pos_test";

    public WebApplicationFactory<Program> Factory { get; private set; } = default!;
    public string ConnectionString { get; private set; } = string.Empty;

    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() }
    };

    public async Task InitializeAsync()
    {
        ConnectionString = ResolveTestConnectionString();

        // Program reads POS_DB at startup; point it at pos_test for the WebApplicationFactory.
        Environment.SetEnvironmentVariable("POS_DB", ConnectionString);

        Factory = new WebApplicationFactory<Program>()
            .WithWebHostBuilder(b => b.UseEnvironment("Testing"));

        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<PosDbContext>();
        await db.Database.MigrateAsync();
    }

    public async Task DisposeAsync()
    {
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

    private static string ResolveTestConnectionString()
    {
        var explicitOverride = Environment.GetEnvironmentVariable("POS_DB_TEST");
        if (!string.IsNullOrWhiteSpace(explicitOverride)) return explicitOverride;

        var devConn = Environment.GetEnvironmentVariable("POS_DB");
        if (!string.IsNullOrWhiteSpace(devConn))
            return SwapDatabase(devConn, TestDatabase);

        return $"Host=localhost;Port=5432;Database={TestDatabase};Username=postgres;Password={DefaultPassword}";
    }

    private static string SwapDatabase(string conn, string newDb)
    {
        var parts = conn.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        for (int i = 0; i < parts.Length; i++)
        {
            if (parts[i].StartsWith("Database=", StringComparison.OrdinalIgnoreCase))
                parts[i] = $"Database={newDb}";
        }
        return string.Join(';', parts);
    }
}

[CollectionDefinition(Name)]
public sealed class PosApiCollection : ICollectionFixture<PosApiFixture>
{
    public const string Name = "Pos.Api";
}
