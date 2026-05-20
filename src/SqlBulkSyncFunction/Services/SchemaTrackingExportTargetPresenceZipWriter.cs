using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace SqlBulkSyncFunction.Services;

#nullable enable

/// <summary>
/// Opens two ZIP archives and streams UTF-8 JSON arrays for export rows that exist vs do not exist on the sync target.
/// Disposing finalizes ZIP entries and archives but leaves outer streams open for the caller to flush and dispose.
/// </summary>
internal sealed class SchemaTrackingExportTargetPresenceZipWriter : IAsyncDisposable
{
    private readonly Stream _existingOuter;
    private readonly Stream _missingOuter;
    private readonly ZipArchive _existingZip;
    private readonly ZipArchive _missingZip;
    private readonly Stream _existingEntryStream;
    private readonly Stream _missingEntryStream;
    private readonly JsonSerializerOptions _rowJsonOptions;
    private bool _existingFirst = true;
    private bool _missingFirst = true;

    /// <summary>
    /// Initializes a writer over two blob (or file) streams, each containing a single JSON-array entry (<c>existing.json</c> / <c>missing.json</c>).
    /// </summary>
    /// <param name="existingZipDestination">Writable stream for <c>existing.zip</c>.</param>
    /// <param name="missingZipDestination">Writable stream for <c>missing.zip</c>.</param>
    /// <param name="rowJsonOptions">Serializer options per row object (compact JSON).</param>
    public SchemaTrackingExportTargetPresenceZipWriter(
        Stream existingZipDestination,
        Stream missingZipDestination,
        JsonSerializerOptions rowJsonOptions
        )
    {
        ArgumentNullException.ThrowIfNull(existingZipDestination);
        ArgumentNullException.ThrowIfNull(missingZipDestination);
        ArgumentNullException.ThrowIfNull(rowJsonOptions);

        _rowJsonOptions = rowJsonOptions;
        _existingOuter = existingZipDestination;
        _missingOuter = missingZipDestination;
        _existingZip = new ZipArchive(_existingOuter, ZipArchiveMode.Create, leaveOpen: true);
        _missingZip = new ZipArchive(_missingOuter, ZipArchiveMode.Create, leaveOpen: true);
        _existingEntryStream = _existingZip.CreateEntry("existing.json", CompressionLevel.Optimal).Open();
        _missingEntryStream = _missingZip.CreateEntry("missing.json", CompressionLevel.Optimal).Open();
        _existingEntryStream.WriteByte((byte)'[');
        _missingEntryStream.WriteByte((byte)'[');
    }

    /// <summary>
    /// Appends one object to the existing-target array: change operation, primary key map, and full target column map.
    /// </summary>
    public async Task WriteExistingRowAsync(
        string changeOperation,
        IReadOnlyDictionary<string, object?> primaryKey,
        IReadOnlyDictionary<string, object?> targetColumns,
        CancellationToken cancellationToken
        )
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(changeOperation);
        ArgumentNullException.ThrowIfNull(primaryKey);
        ArgumentNullException.ThrowIfNull(targetColumns);

        if (_existingFirst)
        {
            _existingFirst = false;
            _existingEntryStream.Write("\n "u8);
        }
        else
        {
            _existingEntryStream.Write(",\n "u8);
        }

        var row = new TargetPresenceExistingRow(changeOperation, primaryKey, targetColumns);
        await JsonSerializer
            .SerializeAsync(_existingEntryStream, row, _rowJsonOptions, cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>
    /// Appends one object to the missing-on-target array: change operation and primary key map only.
    /// </summary>
    public async Task WriteMissingRowAsync(
        string changeOperation,
        IReadOnlyDictionary<string, object?> primaryKey,
        CancellationToken cancellationToken
        )
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(changeOperation);
        ArgumentNullException.ThrowIfNull(primaryKey);

        if (_missingFirst)
        {
            _missingFirst = false;
            _missingEntryStream.Write("\n "u8);
        }
        else
        {
            _missingEntryStream.Write(",\n "u8);
        }

        var row = new TargetPresenceMissingRow(changeOperation, primaryKey);
        await JsonSerializer
            .SerializeAsync(_missingEntryStream, row, _rowJsonOptions, cancellationToken)
            .ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        _existingEntryStream.Write("\n]"u8);
        _missingEntryStream.Write("\n]"u8);
        await _existingEntryStream.DisposeAsync().ConfigureAwait(false);
        await _missingEntryStream.DisposeAsync().ConfigureAwait(false);
        _existingZip.Dispose();
        _missingZip.Dispose();
        await _existingOuter.FlushAsync(CancellationToken.None).ConfigureAwait(false);
        await _missingOuter.FlushAsync(CancellationToken.None).ConfigureAwait(false);
    }

    private sealed record TargetPresenceExistingRow(
        string ChangeOperation,
        IReadOnlyDictionary<string, object?> PrimaryKey,
        IReadOnlyDictionary<string, object?> Target
        );

    private sealed record TargetPresenceMissingRow(
        string ChangeOperation,
        IReadOnlyDictionary<string, object?> PrimaryKey
        );
}
