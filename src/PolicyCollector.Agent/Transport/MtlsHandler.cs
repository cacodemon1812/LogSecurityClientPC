using System.Security.Cryptography.X509Certificates;
using System.Net.Security;

namespace PolicyCollector.Agent.Transport;

public sealed class MtlsHandler : HttpClientHandler
{
    public MtlsHandler(TransportOptions options, ILogger<MtlsHandler> logger)
    {
        SslProtocols = SslProtocols.Tls12 | SslProtocols.Tls13;
        CheckCertificateRevocationList = true;

        if (options.UseMtls && !string.IsNullOrEmpty(options.ClientCertThumbprint))
        {
            var cert = LoadCertificate(
                options.ClientCertStore,
                options.ClientCertThumbprint,
                logger);

            if (cert is not null)
            {
                ClientCertificates.Add(cert);
                logger.LogInformation("mTLS client certificate loaded: {Thumbprint}",
                    options.ClientCertThumbprint);
            }
            else
            {
                logger.LogWarning("mTLS enabled but certificate not found: {Thumbprint}",
                    options.ClientCertThumbprint);
            }
        }
        else if (!options.UseMtls)
        {
            logger.LogDebug("mTLS disabled");
        }
    }

    private static X509Certificate2? LoadCertificate(
        string storeName, string thumbprint, ILogger logger)
    {
        try
        {
            var location = storeName.Equals("CurrentUser", StringComparison.OrdinalIgnoreCase)
                ? StoreLocation.CurrentUser
                : StoreLocation.LocalMachine;

            using var store = new X509Store(StoreName.My, location);
            store.Open(OpenFlags.ReadOnly);

            var thumb = thumbprint.Replace(" ", "").ToUpperInvariant();
            var certs = store.Certificates.Find(
                X509FindType.FindByThumbprint, thumb, validOnly: true);

            if (certs.Count == 0)
            {
                logger.LogWarning("Certificate not found in {Location}\\My: {Thumbprint}",
                    location, thumb);
                return null;
            }

            var cert = certs[0];
            if (!cert.HasPrivateKey)
            {
                logger.LogWarning("Certificate {Thumbprint} has no private key", thumb);
                return null;
            }

            return cert;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to load certificate: {Thumbprint}", thumbprint);
            return null;
        }
    }
}
