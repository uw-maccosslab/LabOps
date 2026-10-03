using ServicesQuotes.Core.Sync;

namespace ServicesQuotes.Core.Quotes;

/// <summary>Resolves generated-file conflicts during a sync by rebuilding with quote.py.</summary>
public sealed class EngineRebuilder(QuoteEngine engine) : IGeneratedFileRebuilder
{
    public async Task RebuildAsync(string folder, CancellationToken cancellationToken) =>
        await engine.BuildAsync(folder, cancellationToken).ConfigureAwait(false);
}
