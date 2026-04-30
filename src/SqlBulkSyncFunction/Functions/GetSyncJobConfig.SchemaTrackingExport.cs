using System;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;
using SqlBulkSyncFunction.Models.Schema.Export;

namespace SqlBulkSyncFunction.Functions;

#nullable enable

public partial class GetSyncJobConfig
{
    private static readonly JsonSerializerOptions ExportJsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    /// <summary>
    /// Accepts a schema tracking data export request, persists metadata to blob and table storage, and enqueues processing.
    /// </summary>
    [Function(nameof(GetSyncJobConfig) + nameof(PostSchemaTrackingExport))]
    public async Task<IActionResult> PostSchemaTrackingExport(
        [HttpTrigger(
            AuthorizationLevel.Function,
            "post",
            Route = "config/{area}/{id}/schema/tracking/{tableId}/export"
        )]
        HttpRequest req,
        string area,
        string id,
        string tableId,
        CancellationToken cancellationToken
        )
    {
        ArgumentNullException.ThrowIfNull(req);

        SchemaTrackingExportRequestBody? body;
        try
        {
            body = await JsonSerializer
                .DeserializeAsync<SchemaTrackingExportRequestBody>(req.Body, ExportJsonOptions, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (JsonException)
        {
            return new BadRequestObjectResult("Invalid JSON body.");
        }

        if (body == null)
        {
            return new BadRequestObjectResult("Body is required.");
        }

        var result = await schemaTrackingExportService
            .TryCreateExportJobAsync(area, id, tableId, body, cancellationToken)
            .ConfigureAwait(false);

        if (result.Code == ExportJobCreateResultCode.ValidationFailed)
        {
            return new BadRequestObjectResult("author, referenceId, and purpose must be non-empty strings.");
        }

        if (result.Code == ExportJobCreateResultCode.NotFound || result.Job == null)
        {
            return new NotFoundResult();
        }

        var location = BuildExportStatusLocation(req, area, id, tableId, result.Job.CorrelationId);
        return new AcceptedResult(location: location, value: result.Job);
    }

    /// <summary>
    /// Returns detailed status for a single export job when <paramref name="correlationId"/> is present in the path,
    /// or lists export jobs for the table when the URL ends at <c>.../export/status</c> (catch-all binds empty).
    /// </summary>
    /// <remarks>
    /// A separate route without <c>{*correlationId}</c> would never win in the host: the catch-all matches the same URL with an empty remainder,
    /// and <see cref="SchemaTrackingExportService.TryGetExportStatusAsync"/> returns null for an empty id, producing 404. List behavior is therefore handled here.
    /// </remarks>
    [Function(nameof(GetSyncJobConfig) + nameof(GetSchemaTrackingExportStatus))]
    public async Task<IActionResult> GetSchemaTrackingExportStatus(
        [HttpTrigger(
            AuthorizationLevel.Function,
            "get",
            Route = "config/{area}/{id}/schema/tracking/{tableId}/export/status/{*correlationId}"
        )]
        HttpRequest req,
        string area,
        string id,
        string tableId,
        string correlationId,
        CancellationToken cancellationToken
        )
    {
        ArgumentNullException.ThrowIfNull(req);

        if (string.IsNullOrWhiteSpace(correlationId))
        {
            if (string.IsNullOrWhiteSpace(area) ||
                string.IsNullOrWhiteSpace(id) ||
                string.IsNullOrWhiteSpace(tableId) ||
                syncJobsConfig?.Value?.Jobs?.TryGetValue(id, out var jobConfig) != true ||
                jobConfig == null ||
                !StringComparer.OrdinalIgnoreCase.Equals(area, jobConfig.Area) ||
                jobConfig.Tables == null ||
                !jobConfig.Tables.TryGetValue(tableId, out _))
            {
                return new NotFoundResult();
            }

            var items = await schemaTrackingExportService
                .ListExportJobsAsync(area, id, tableId, cancellationToken)
                .ConfigureAwait(false);

            return new OkObjectResult(items);
        }

        var status = await schemaTrackingExportService
            .TryGetExportStatusAsync(area, id, tableId, correlationId, cancellationToken)
            .ConfigureAwait(false);

        if (status == null)
        {
            return new NotFoundResult();
        }

        return new OkObjectResult(status);
    }

    private static string BuildExportStatusLocation(
        HttpRequest req,
        string area,
        string jobId,
        string tableId,
        string correlationId
        )
    {
        var encodedCorrelation = string.Join(
            "/",
            correlationId.Split('/', StringSplitOptions.RemoveEmptyEntries).Select(Uri.EscapeDataString)
        );
        var pathBase = req.PathBase.Value?.TrimEnd('/') ?? string.Empty;
        var apiRoot = string.IsNullOrEmpty(pathBase) ? "/api" : pathBase;
        var path = string.Format(
            CultureInfo.InvariantCulture,
            "{0}/config/{1}/{2}/schema/tracking/{3}/export/status/{4}",
            apiRoot,
            Uri.EscapeDataString(area),
            Uri.EscapeDataString(jobId),
            Uri.EscapeDataString(tableId),
            encodedCorrelation
        );
        return string.Format(CultureInfo.InvariantCulture, "{0}://{1}{2}", req.Scheme, req.Host.Value, path);
    }
}
