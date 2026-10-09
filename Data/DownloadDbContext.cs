using Microsoft.EntityFrameworkCore;

namespace DownloadManager.Data;

public class DownloadDbContext : DbContext
{
    public DbSet<DownloadRecord> Downloads => Set<DownloadRecord>();

    public DownloadDbContext(DbContextOptions<DownloadDbContext> options)
        : base(options)
    {
    }
}