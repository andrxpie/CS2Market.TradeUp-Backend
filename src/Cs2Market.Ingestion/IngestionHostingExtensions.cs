namespace Microsoft.Extensions.Hosting;

public static class IngestionHostingExtensions
{
    /// <summary>
    /// Registers the ingestion pipeline. Called by the standalone ingestion host in development
    /// and by the API host when it runs as a single process in production.
    /// </summary>
    public static TBuilder AddIngestion<TBuilder>(this TBuilder builder) where TBuilder : IHostApplicationBuilder
    {
        ArgumentNullException.ThrowIfNull(builder);

        // Catalog and price ingestion services are registered here from M2-2 onwards.

        return builder;
    }
}
