using LabOps.Core.Sync;

namespace LabOps.Core.Quotes;

/// <summary>
/// Resolves generated-file conflicts during a sync by rebuilding with quote.py: the quote's text,
/// and its statement of work when it has one (from the merged quote and its saved sample counts).
/// </summary>
public sealed class EngineRebuilder(QuoteEngine engine) : IGeneratedFileRebuilder
{
    public async Task RebuildAsync(string folder, CancellationToken cancellationToken)
    {
        await engine.BuildAsync(folder, cancellationToken).ConfigureAwait(false);
        var path = engine.RepositoryPath is { } root ? Path.Combine(root, folder.Replace('/', Path.DirectorySeparatorChar)) : null;
        if (path is not null && Directory.Exists(path) && Directory.EnumerateFiles(path, "*-SOW.md").Any())
        {
            await engine.StatementOfWorkAsync(folder, null, cancellationToken).ConfigureAwait(false);
        }
    }
}
