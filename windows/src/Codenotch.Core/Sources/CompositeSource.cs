using Codenotch.Core.Model;

namespace Codenotch.Core.Sources;

/// <summary>
/// Fans out to every available source at once and concatenates the results in the
/// *declared* order, not completion order — otherwise the rows in the flyout and
/// the dials in the notch would reshuffle every time one vendor answered faster
/// than the other.
/// </summary>
public sealed class CompositeSource : IUsageSource
{
    public CompositeSource(IReadOnlyList<IUsageSource> sources)
    {
        Sources = sources ?? throw new ArgumentNullException(nameof(sources));
    }

    public IReadOnlyList<IUsageSource> Sources { get; }

    public string Id => "composite";

    public bool IsAvailable()
    {
        foreach (IUsageSource source in Sources)
        {
            if (source.IsAvailable())
                return true;
        }
        return false;
    }

    public async Task<IReadOnlyList<ProviderEntry>> FetchAsync(CancellationToken ct = default)
    {
        var queried = new List<IUsageSource>(Sources.Count);
        var tasks = new List<Task<IReadOnlyList<ProviderEntry>>>(Sources.Count);
        foreach (IUsageSource source in Sources)
        {
            if (!source.IsAvailable())
                continue;
            queried.Add(source);
            tasks.Add(RunAsync(source, ct));
        }

        if (tasks.Count == 0)
            return [];

        IReadOnlyList<ProviderEntry>[] results = await Task.WhenAll(tasks).ConfigureAwait(false);

        var out_ = new List<ProviderEntry>();
        for (int i = 0; i < results.Length; i++)
        {
            IReadOnlyList<ProviderEntry> entries = results[i];
            if (entries.Count == 0)
            {
                // One row per queried source, always: a silent source would just
                // vanish from the notch, which reads as "everything is fine".
                out_.Add(ErrorEntry.For(queried[i].Id, "no data"));
                continue;
            }
            out_.AddRange(entries);
        }
        return out_;
    }

    private static async Task<IReadOnlyList<ProviderEntry>> RunAsync(IUsageSource source, CancellationToken ct)
    {
        try
        {
            return await source.FetchAsync(ct).ConfigureAwait(false) ?? [];
        }
        catch (OperationCanceledException)
        {
            throw;                     // a cancelled poll is not a provider error
        }
        catch (Exception ex)
        {
            return [ErrorEntry.For(source.Id, ex.Message)];
        }
    }
}
