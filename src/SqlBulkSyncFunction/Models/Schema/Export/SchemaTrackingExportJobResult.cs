using System;

namespace SqlBulkSyncFunction.Models.Schema.Export;

#nullable enable

/// <summary>
/// Final export outcome written to <c>response/result.json</c>, including time-limited read URLs for ZIP responses.
/// </summary>
/// <param name="Area">Configured sync job area.</param>
/// <param name="Id">Configured sync job identifier.</param>
/// <param name="TableId">Configured table mapping identifier.</param>
/// <param name="CorrelationId">Blob path prefix for this job.</param>
/// <param name="ExportJobId">Export job id (<c>n</c> format); same as Azure Table row key.</param>
/// <param name="ReferenceId">Reference from the export request.</param>
/// <param name="Author">Author from the export request.</param>
/// <param name="Purpose">Purpose from the export request.</param>
/// <param name="Created">UTC time when the job was accepted.</param>
/// <param name="Completed">UTC time when all segments and result metadata finished.</param>
/// <param name="SasExpires">UTC time when the returned SAS URLs expire (aligned with SAS token).</param>
/// <param name="UpdatedZipSasUri">Read SAS URI for <c>updated.zip</c>.</param>
/// <param name="InsertedZipSasUri">Read SAS URI for <c>inserted.zip</c>.</param>
/// <param name="DeletedZipSasUri">Read SAS URI for <c>deleted.zip</c>.</param>
/// <param name="ExistingZipSasUri">Read SAS URI for <c>existing.zip</c> (changes whose PK exists on target, including target column values); null for results written before this feature.</param>
/// <param name="MissingZipSasUri">Read SAS URI for <c>missing.zip</c> (changes whose PK does not exist on target); null for results written before this feature.</param>
public record SchemaTrackingExportJobResult(
    string Area,
    string Id,
    string TableId,
    string CorrelationId,
    string ExportJobId,
    string ReferenceId,
    string Author,
    string Purpose,
    DateTimeOffset Created,
    DateTimeOffset Completed,
    DateTimeOffset SasExpires,
    Uri UpdatedZipSasUri,
    Uri InsertedZipSasUri,
    Uri DeletedZipSasUri,
    Uri? ExistingZipSasUri = null,
    Uri? MissingZipSasUri = null
);
