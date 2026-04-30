using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.SqlClient;

namespace SqlBulkSyncFunction.Services;

#nullable enable

/// <summary>
/// Streams SQL rows into a JSON array inside a single ZIP entry written to <paramref name="zipDestination"/> (e.g. Azure Blob <c>OpenWriteAsync</c>), avoiding a temp file and full in-memory buffering of the export.
/// </summary>
public static class SchemaTrackingExportStreamingZip
{
    private static readonly JsonSerializerOptions RowSerializerOptions = new()
    {
        WriteIndented = false,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    /// <summary>
    /// Reads forward-only from <paramref name="reader"/> and writes a JSON array into one ZIP entry named <paramref name="zipEntryFileName"/> on <paramref name="zipDestination"/>.
    /// </summary>
    /// <param name="reader">Open data reader; disposed by caller.</param>
    /// <param name="zipDestination">Writable stream for the ZIP payload (e.g. block blob staged write). Must remain open until this method returns; caller disposes it.</param>
    /// <param name="zipEntryFileName">Entry name inside the archive (e.g. <c>inserted.json</c>).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public static async Task WriteReaderToZipAsync(
        SqlDataReader reader,
        Stream zipDestination,
        string zipEntryFileName,
        CancellationToken cancellationToken
        )
    {
        ArgumentNullException.ThrowIfNull(reader);
        ArgumentNullException.ThrowIfNull(zipDestination);
        ArgumentException.ThrowIfNullOrWhiteSpace(zipEntryFileName);
        if (!zipDestination.CanWrite)
        {
            throw new ArgumentException("Stream must be writable.", nameof(zipDestination));
        }

        using var bufferedStream = new BufferedStream(zipDestination, 8192);
        using var zipArchive = new ZipArchive(bufferedStream, ZipArchiveMode.Create, leaveOpen: true);
        await WriteReaderAsIndentedJsonArrayAsync(reader, zipArchive, zipEntryFileName, cancellationToken).ConfigureAwait(false);
        await bufferedStream.FlushAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Creates a single ZIP entry and streams a UTF-8 JSON array: opening bracket, one compact object per SQL row (commas between rows), closing bracket.
    /// </summary>
    /// <param name="reader">Forward-only SQL reader; disposed by caller.</param>
    /// <param name="zipArchive">ZIP archive already opened in create mode.</param>
    /// <param name="zipEntryFileName">Name of the entry inside the archive (e.g. <c>inserted.json</c>).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    private static async Task WriteReaderAsIndentedJsonArrayAsync(
        SqlDataReader reader,
        ZipArchive zipArchive,
        string zipEntryFileName,
        CancellationToken cancellationToken
        )
    {
        // One deflated entry; JSON is built incrementally so the full export is not held in memory.
        var entry = zipArchive.CreateEntry(zipEntryFileName, CompressionLevel.Optimal);
        await using var entryStream = entry.Open();

        // Outer JSON array: '[' then rows; each row is one JsonSerializer object (no extra array indent options).
        entryStream.WriteByte(0x5b); // '['
        var rowBuffer = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
        var firstRow = true;
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            // Human-friendly separation between top-level array elements (objects stay compact via RowSerializerOptions).
            if (firstRow)
            {
                firstRow = false;
                entryStream.Write("\n "u8);
            }
            else
            {
                entryStream.Write(",\n "u8);
            }

            // Reuse one dictionary per row to avoid allocating a new map for every record.
            rowBuffer.Clear();
            for (var i = 0; i < reader.FieldCount; i++)
            {
                // Skip SQL NULLs: omit properties entirely (smaller JSON than null fields).
                if (reader.IsDBNull(i))
                {
                    continue;
                }

                rowBuffer.Add(reader.GetName(i), reader.GetValue(i));
            }

            await JsonSerializer
                .SerializeAsync(entryStream, rowBuffer, RowSerializerOptions, cancellationToken)
                .ConfigureAwait(false);
        }

        // Closing bracket: ']' with trailing newline for human readability.
        entryStream.Write("\n]"u8);
    }
}
