using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using CrestCreates.FileManagement.Models;

namespace CrestCreates.FileManagement.Repositories;

public interface IFileRepository
{
    Task<FileEntity?> GetByKeyAsync(FileKey key, CancellationToken ct = default);

    Task<IEnumerable<FileEntity>> ListAsync(Guid tenantId, int? year = null, CancellationToken ct = default);

    Task CreateAsync(FileEntity entity, CancellationToken ct = default);

    Task UpdateAsync(FileEntity entity, CancellationToken ct = default);

    Task DeleteAsync(FileKey key, CancellationToken ct = default);
}
