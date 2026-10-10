using ScadaSpace.Core;

namespace ScadaSpace.Persistence;

public interface IProjectRepository
{
    Task<ProjectDefinition?> LoadAsync(CancellationToken cancellationToken = default);
    Task<long> SaveAsync(ProjectDefinition project, long expectedRevision, CancellationToken cancellationToken = default);
}
public sealed class ProjectConflictException(long expected, long actual) : InvalidOperationException($"Project revision conflict: expected {expected}, actual {actual}.")
{
    public long ActualRevision { get; } = actual;
}

/// <summary>Single-process repository with atomic replacement. Use one instance per file; multi-process locking is not provided.</summary>
public sealed class FileProjectRepository(string path) : IProjectRepository, IDisposable
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    public async Task<ProjectDefinition?> LoadAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try { return await Read(cancellationToken); }
        finally { _gate.Release(); }
    }
    private async Task<ProjectDefinition?> Read(CancellationToken token)
    {
        if (!File.Exists(path)) return null;
        if (new FileInfo(path).Length > ProjectCodec.MaximumBytes) throw new FormatException("Stored project exceeds size limit.");
        return ProjectCodec.Deserialize(await File.ReadAllTextAsync(path, token));
    }
    public async Task<long> SaveAsync(ProjectDefinition project, long expectedRevision, CancellationToken cancellationToken = default)
    {
        var copy = ProjectCodec.Clone(project);
        await _gate.WaitAsync(cancellationToken);
        string? temp = null;
        try
        {
            var old = await Read(cancellationToken); var actual = old?.Revision ?? 0;
            if (actual != expectedRevision) throw new ProjectConflictException(expectedRevision, actual);
            copy.Revision = checked(actual + 1);
            var directory = Path.GetDirectoryName(Path.GetFullPath(path))!;
            Directory.CreateDirectory(directory);
            temp = Path.Combine(directory, ".scadaspace-" + Guid.NewGuid().ToString("N") + ".tmp");
            await File.WriteAllTextAsync(temp, ProjectCodec.Serialize(copy), cancellationToken);
            File.Move(temp, path, true); temp = null;
            return copy.Revision;
        }
        finally { if (temp is not null && File.Exists(temp)) File.Delete(temp); _gate.Release(); }
    }
    public void Dispose() => _gate.Dispose();
}
