using CutterStudio.Models;

namespace CutterStudio.Services;

public interface IProjectRepository
{
    Task InitializeAsync(CancellationToken cancellationToken = default);
    Task<long> SaveAsync(ProjectRecord project, CancellationToken cancellationToken = default);
    Task<ProjectRecord?> GetAsync(long id, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<RecentProject>> GetRecentAsync(int limit = 20, CancellationToken cancellationToken = default);
}
