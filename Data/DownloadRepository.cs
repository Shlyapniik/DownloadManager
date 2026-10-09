using Microsoft.EntityFrameworkCore;

namespace DownloadManager.Data;

public class DownloadRepository
{
    private readonly IDbContextFactory<DownloadDbContext> _contextFactory;

    public DownloadRepository(
        IDbContextFactory<DownloadDbContext> contextFactory)
    {
        _contextFactory = contextFactory;
    }

    public async Task<List<DownloadRecord>> GetAllAsync()
    {
        await using var dbContext =
            await _contextFactory.CreateDbContextAsync();

        return await dbContext.Downloads
            .AsNoTracking()
            .OrderByDescending(x => x.CreatedAt)
            .ToListAsync();
    }

    public async Task<DownloadRecord> AddAsync(
        DownloadRecord record)
    {
        await using var dbContext =
            await _contextFactory.CreateDbContextAsync();

        dbContext.Downloads.Add(record);

        await dbContext.SaveChangesAsync();

        return record;
    }

    public async Task UpdateAsync(
        DownloadRecord record)
    {
        await using var dbContext =
            await _contextFactory.CreateDbContextAsync();

        var existingRecord =
            await dbContext.Downloads
                .FirstOrDefaultAsync(x => x.Id == record.Id);

        if (existingRecord is null)
            return;

        existingRecord.FileName = record.FileName;
        existingRecord.Url = record.Url;
        existingRecord.FilePath = record.FilePath;
        existingRecord.Status = record.Status;
        existingRecord.TotalBytes = record.TotalBytes;
        existingRecord.DownloadedBytes = record.DownloadedBytes;
        existingRecord.ErrorMessage = record.ErrorMessage;

        await dbContext.SaveChangesAsync();
    }

    public async Task DeleteAsync(int id)
    {
        await using var dbContext =
            await _contextFactory.CreateDbContextAsync();

        var record =
            await dbContext.Downloads
                .FirstOrDefaultAsync(x => x.Id == id);

        if (record is null)
            return;

        dbContext.Downloads.Remove(record);

        await dbContext.SaveChangesAsync();
    }
}