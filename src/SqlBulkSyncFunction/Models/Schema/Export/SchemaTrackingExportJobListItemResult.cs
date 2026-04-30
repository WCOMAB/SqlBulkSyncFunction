using System;

namespace SqlBulkSyncFunction.Models.Schema.Export;

#nullable enable

/// <summary>
/// SAS download details exposed on <see cref="SchemaTrackingExportJobListItem.Result"/> for status responses.
/// Subset of <see cref="SchemaTrackingExportJobResult"/> (blob <c>result.json</c> still stores the full record).
/// </summary>
/// <param name="SasExpires">UTC time when the returned SAS URLs expire (aligned with SAS token).</param>
/// <param name="UpdatedZipSasUri">Read SAS URI for <c>updated.zip</c>.</param>
/// <param name="InsertedZipSasUri">Read SAS URI for <c>inserted.zip</c>.</param>
/// <param name="DeletedZipSasUri">Read SAS URI for <c>deleted.zip</c>.</param>
public record SchemaTrackingExportJobListItemResult(
    DateTimeOffset SasExpires,
    Uri UpdatedZipSasUri,
    Uri InsertedZipSasUri,
    Uri DeletedZipSasUri
)
{
    /// <summary>
    /// Maps a deserialized <see cref="SchemaTrackingExportJobResult"/> to the API subset, or returns <see langword="null"/> when <paramref name="jobResult"/> is <see langword="null"/>.
    /// </summary>
    /// <param name="jobResult">Full result from <c>result.json</c>, or <see langword="null"/>.</param>
    /// <returns>List-item result with SAS URIs only, or <see langword="null"/>.</returns>
    public static SchemaTrackingExportJobListItemResult? FromJobResult(SchemaTrackingExportJobResult? jobResult)
        => jobResult == null
            ? null
            : new SchemaTrackingExportJobListItemResult(
                jobResult.SasExpires,
                jobResult.UpdatedZipSasUri,
                jobResult.InsertedZipSasUri,
                jobResult.DeletedZipSasUri
            );
}
