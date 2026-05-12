using System.Text;
using PolicyCollector.Backend.Services;

namespace PolicyCollector.Backend.Api.Endpoints;

public static class ReportEndpoints
{
    public static void MapReportEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/v1/reports");

        group.MapGet("/compliance", ExportComplianceReport)
             .Produces(200, contentType: "text/csv")
             .Produces<ErrorResponse>(400)
             .WithName("ExportComplianceReport");

        group.MapGet("/violations", ExportViolationsReport)
             .Produces(200, contentType: "text/csv")
             .WithName("ExportViolationsReport");
    }

    private static async Task<IResult> ExportComplianceReport(
        [FromQuery] string? domain,
        ComplianceReportService reportService,
        CancellationToken ct)
    {
        try
        {
            var csv = await reportService.GenerateComplianceCsvAsync(domain, ct);
            var bytes = Encoding.UTF8.GetBytes(csv);
            var filename = $"compliance-report-{DateTime.UtcNow:yyyy-MM-dd}.csv";
            return Results.File(bytes, "text/csv", filename);
        }
        catch (Exception ex)
        {
            return Results.BadRequest(new ErrorResponse("Failed to generate compliance report", ex.Message));
        }
    }

    private static async Task<IResult> ExportViolationsReport(
        [FromQuery] string? domain,
        [FromQuery] string? severity,
        ComplianceReportService reportService,
        CancellationToken ct)
    {
        try
        {
            var csv = await reportService.GenerateViolationsCsvAsync(domain, severity, ct);
            var bytes = Encoding.UTF8.GetBytes(csv);
            var filename = $"violations-{DateTime.UtcNow:yyyy-MM-dd}.csv";
            return Results.File(bytes, "text/csv", filename);
        }
        catch (Exception ex)
        {
            return Results.BadRequest(new ErrorResponse("Failed to generate violations report", ex.Message));
        }
    }
}
