// Copyright (c) DGP Studio. All rights reserved.
// Licensed under the MIT license.

using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

namespace Snap.Hutao.GachaExport;

/// <summary>
/// Console application to export wish history from Snap Hutao database to UIGF format.
/// This app works completely offline without network access.
/// </summary>
internal static class Program
{
    private const string UIGFVersion = "v4.1";
    private const string ExportAppName = "Snap Hutao GachaExport";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        WriteIndented = true,
    };

    public static async Task<int> Main(string[] args)
    {
        try
        {
            string? databasePath = null;
            string? outputPath = null;
            bool showHelp = false;

            for (int i = 0; i < args.Length; i++)
            {
                switch (args[i])
                {
                    case "-d":
                    case "--database":
                        if (i + 1 < args.Length)
                        {
                            databasePath = args[++i];
                        }

                        break;
                    case "-o":
                    case "--output":
                        if (i + 1 < args.Length)
                        {
                            outputPath = args[++i];
                        }

                        break;
                    case "-h":
                    case "--help":
                        showHelp = true;
                        break;
                }
            }

            if (showHelp || databasePath is null)
            {
                ShowUsage();
                return showHelp ? 0 : 1;
            }

            if (!File.Exists(databasePath))
            {
                Console.Error.WriteLine($"Error: Database file not found: {databasePath}");
                ShowDatabaseLocationHelp();
                return 1;
            }

            outputPath ??= Path.Combine(
                Path.GetDirectoryName(databasePath) ?? Environment.CurrentDirectory,
                $"UIGF_Export_{DateTime.Now:yyyyMMdd_HHmmss}.json");

            Console.WriteLine($"Reading database from: {databasePath}");
            Console.WriteLine($"Output will be saved to: {outputPath}");
            Console.WriteLine();

            UIGF uigf = await ExportToUIGFAsync(databasePath);

            if (uigf.Hk4e.Length == 0)
            {
                Console.WriteLine("No gacha records found in the database.");
                return 0;
            }

            await using FileStream stream = File.Create(outputPath);
            await JsonSerializer.SerializeAsync(stream, uigf, JsonOptions);

            Console.WriteLine();
            Console.WriteLine($"Successfully exported {uigf.Hk4e.Length} archive(s) to {outputPath}");

            foreach (UIGFEntry entry in uigf.Hk4e)
            {
                Console.WriteLine($"  UID {entry.Uid}: {entry.List.Length} records");
            }

            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Error: {ex.Message}");
#if DEBUG
            Console.Error.WriteLine(ex.StackTrace);
#endif
            return 1;
        }
    }

    private static void ShowUsage()
    {
        Console.WriteLine("Snap Hutao Gacha Export Tool");
        Console.WriteLine("Exports wish history from Snap Hutao database to UIGF format.");
        Console.WriteLine();
        Console.WriteLine("Usage: Snap.Hutao.GachaExport -d <database-path> [-o <output-path>]");
        Console.WriteLine();
        Console.WriteLine("Options:");
        Console.WriteLine("  -d, --database <path>  Path to the Userdata.db database file (required)");
        Console.WriteLine("  -o, --output <path>    Output file path (default: UIGF_Export_<timestamp>.json)");
        Console.WriteLine("  -h, --help             Show this help message");
        Console.WriteLine();
        ShowDatabaseLocationHelp();
    }

    private static void ShowDatabaseLocationHelp()
    {
        Console.WriteLine("The Snap Hutao database is typically located at:");
        Console.WriteLine("  - %USERPROFILE%\\Documents\\Hutao\\Userdata.db");
        Console.WriteLine("  - %LOCALAPPDATA%\\Packages\\7f0db578-026f-4e0b-a75b-d5d06bb0a74d_<suffix>\\LocalState\\Hutao\\Userdata.db");
        Console.WriteLine();
        Console.WriteLine("For Alpha builds, look for HutaoAlpha folder instead of Hutao.");
    }

    private static async Task<UIGF> ExportToUIGFAsync(string databasePath)
    {
        string connectionString = $"Data Source={databasePath}";

        DbContextOptions<GachaDbContext> options = new DbContextOptionsBuilder<GachaDbContext>()
            .UseSqlite(connectionString)
            .Options;

        await using GachaDbContext dbContext = new(options);

        List<GachaArchive> archives = await dbContext.GachaArchives.ToListAsync();

        Console.WriteLine($"Found {archives.Count} archive(s)");

        // Load all items once (more efficient for in-memory filtering)
        List<GachaItem> allItems = await dbContext.GachaItems.ToListAsync();
        Console.WriteLine($"Total gacha records: {allItems.Count}");

        ImmutableArray<UIGFEntry>.Builder entries = ImmutableArray.CreateBuilder<UIGFEntry>(archives.Count);

        foreach (GachaArchive archive in archives)
        {
            Console.WriteLine($"Processing UID: {archive.Uid}");

            // Filter in memory because SQLite GUID comparison doesn't work reliably with EF Core
            List<GachaItem> items = allItems
                .Where(i => i.ArchiveId == archive.InnerId)
                .OrderBy(i => i.Id)
                .ToList();

            Console.WriteLine($"  Found {items.Count} gacha record(s)");

            ImmutableArray<Hk4eItem> hk4eItems = items
                .Select(item => new Hk4eItem
                {
                    UIGFGachaType = item.QueryType,
                    GachaType = item.GachaType,
                    ItemId = item.ItemId,
                    Time = item.Time.UtcDateTime,
                    Id = item.Id,
                })
                .ToImmutableArray();

            if (uint.TryParse(archive.Uid, out uint uid))
            {
                entries.Add(new UIGFEntry
                {
                    Uid = uid,
                    TimeZone = 0,
                    List = hk4eItems,
                });
            }
            else
            {
                Console.WriteLine($"  Warning: Invalid UID format: {archive.Uid}");
            }
        }

        return new UIGF
        {
            Info = new UIGFInfo
            {
                ExportApp = ExportAppName,
                ExportAppVersion = "1.0.0",
                ExportTimestamp = DateTimeOffset.Now.ToUnixTimeSeconds(),
                Version = UIGFVersion,
            },
            Hk4e = entries.ToImmutable(),
        };
    }
}

#region Database Models

internal sealed class GachaDbContext : DbContext
{
    public GachaDbContext(DbContextOptions<GachaDbContext> options)
        : base(options)
    {
    }

    public DbSet<GachaArchive> GachaArchives { get; set; } = null!;

    public DbSet<GachaItem> GachaItems { get; set; } = null!;

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<GachaArchive>(entity =>
        {
            entity.ToTable("gacha_archives");
            entity.HasKey(e => e.InnerId);
        });

        modelBuilder.Entity<GachaItem>(entity =>
        {
            entity.ToTable("gacha_items");
            entity.HasKey(e => e.InnerId);
        });
    }
}

internal sealed class GachaArchive
{
    public Guid InnerId { get; set; }

    public string Uid { get; set; } = string.Empty;

    public bool IsSelected { get; set; }
}

internal sealed class GachaItem
{
    public Guid InnerId { get; set; }

    public Guid ArchiveId { get; set; }

    public int GachaType { get; set; }

    public int QueryType { get; set; }

    public uint ItemId { get; set; }

    public DateTimeOffset Time { get; set; }

    public long Id { get; set; }
}

#endregion

#region UIGF Models

internal sealed class UIGF
{
    [JsonPropertyName("info")]
    public required UIGFInfo Info { get; init; }

    [JsonPropertyName("hk4e")]
    public ImmutableArray<UIGFEntry> Hk4e { get; set; }
}

internal sealed class UIGFInfo
{
    [JsonPropertyName("export_timestamp")]
    public required long ExportTimestamp { get; init; }

    [JsonPropertyName("export_app")]
    public required string ExportApp { get; init; }

    [JsonPropertyName("export_app_version")]
    public required string ExportAppVersion { get; init; }

    [JsonPropertyName("version")]
    public required string Version { get; init; }
}

internal sealed class UIGFEntry
{
    [JsonPropertyName("uid")]
    [JsonNumberHandling(JsonNumberHandling.WriteAsString)]
    public required uint Uid { get; init; }

    [JsonPropertyName("timezone")]
    public required int TimeZone { get; init; }

    [JsonPropertyName("lang")]
    public string? Language { get; init; }

    [JsonPropertyName("list")]
    public required ImmutableArray<Hk4eItem> List { get; init; }
}

internal sealed class Hk4eItem
{
    [JsonPropertyName("uigf_gacha_type")]
    [JsonConverter(typeof(GachaTypeConverter))]
    public required int UIGFGachaType { get; init; }

    [JsonPropertyName("gacha_type")]
    [JsonConverter(typeof(GachaTypeConverter))]
    public required int GachaType { get; init; }

    [JsonPropertyName("item_id")]
    [JsonNumberHandling(JsonNumberHandling.WriteAsString)]
    public required uint ItemId { get; init; }

    [JsonPropertyName("time")]
    [JsonConverter(typeof(SimpleDateTimeConverter))]
    public required DateTime Time { get; init; }

    [JsonPropertyName("id")]
    [JsonNumberHandling(JsonNumberHandling.WriteAsString)]
    public required long Id { get; init; }
}

#endregion

#region JSON Converters

internal sealed class SimpleDateTimeConverter : JsonConverter<DateTime>
{
    private const string Format = "yyyy-MM-dd HH:mm:ss";

    public override DateTime Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.GetString() is { } dateTimeString)
        {
            return DateTime.ParseExact(dateTimeString, Format, CultureInfo.InvariantCulture);
        }

        return default;
    }

    public override void Write(Utf8JsonWriter writer, DateTime value, JsonSerializerOptions options)
    {
        writer.WriteStringValue(value.ToString(Format, CultureInfo.InvariantCulture));
    }
}

internal sealed class GachaTypeConverter : JsonConverter<int>
{
    public override int Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.String)
        {
            if (int.TryParse(reader.GetString(), out int result))
            {
                return result;
            }
        }
        else if (reader.TokenType == JsonTokenType.Number)
        {
            return reader.GetInt32();
        }

        return 0;
    }

    public override void Write(Utf8JsonWriter writer, int value, JsonSerializerOptions options)
    {
        writer.WriteStringValue(value.ToString(CultureInfo.InvariantCulture));
    }
}

#endregion
