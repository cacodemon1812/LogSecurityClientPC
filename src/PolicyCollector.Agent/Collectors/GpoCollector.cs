using System.Diagnostics;
using System.Xml.Linq;
using PolicyCollector.Agent.Infrastructure;

namespace PolicyCollector.Agent.Collectors;

public sealed class GpoCollector : ICollector<GpoResult>
{
    private readonly ProcessRunner _process;
    private readonly ILogger<GpoCollector> _logger;

    public GpoCollector(ProcessRunner process, ILogger<GpoCollector> logger)
    {
        _process = process;
        _logger = logger;
    }

    public string ModuleName => "GPO";

    public async Task<CollectorResult<GpoResult>> CollectAsync(CancellationToken ct)
    {
        var sw = Stopwatch.StartNew();
        try
        {
            var tempXml = Path.Combine(Path.GetTempPath(), $"gpresult_{Guid.NewGuid():N}.xml");
            try
            {
                var result = await _process.RunAsync(
                    "gpresult.exe",
                    $"/X \"{tempXml}\" /SCOPE COMPUTER /FORCE",
                    timeout: TimeSpan.FromSeconds(20),
                    ct);

                if (result.ExitCode == 2)
                    return CollectorResult<GpoResult>.Fail("gpresult: access denied");

                var gpoResult = result.ExitCode == 0
                    ? ParseGpresultXml(tempXml)
                    : new GpoResult { RefreshStatus = "NoData" };

                return CollectorResult<GpoResult>.Ok(gpoResult, sw.Elapsed);
            }
            finally
            {
                try { if (File.Exists(tempXml)) File.Delete(tempXml); }
                catch (IOException) { /* best-effort temp file cleanup */ }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "GPO collection failed");
            return CollectorResult<GpoResult>.Fail("GPO collection failed", ex.ToString());
        }
    }

    private GpoResult ParseGpresultXml(string xmlPath)
    {
        try
        {
            var doc = XDocument.Load(xmlPath);
            var ns = XNamespace.Get("http://www.microsoft.com/GroupPolicy/Rsop");

            var computerGpos = doc.Descendants(ns + "GPO")
                .Select((elem, idx) => new GpoEntry
                {
                    Name      = elem.Element(ns + "Name")?.Value ?? string.Empty,
                    Guid      = elem.Element(ns + "GUID")?.Value,
                    LinkPath  = elem.Element(ns + "Link")?.Value,
                    LinkOrder = idx + 1,
                    Applied   = elem.Element(ns + "FilterAllowed")?.Value != "false",
                    Reason    = elem.Element(ns + "FilterAllowed")?.Value
                })
                .ToList();

            return new GpoResult
            {
                RefreshStatus = "Success",
                LastRefresh   = DateTimeOffset.UtcNow,
                ComputerGpos  = computerGpos
            };
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to parse gpresult XML");
            return new GpoResult { RefreshStatus = "ParseError" };
        }
    }
}
