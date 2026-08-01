using Microsoft.Data.Sqlite;

namespace CrestCreates.Samples.DescriptorControlPlane;

public sealed class SqliteCompanyCertificationStore : ICompanyCertificationStore
{
    private readonly SqliteConnectionFactory _connectionFactory;

    public SqliteCompanyCertificationStore(SqliteConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    public async Task<CertificationRecord> CreateAsync(
        CertificationSubmitInput input,
        CancellationToken cancellationToken = default)
    {
        var record = new CertificationRecord
        {
            Id = Guid.NewGuid(),
            CompanyName = input.CompanyName,
            UnifiedSocialCreditCode = input.UnifiedSocialCreditCode,
            CertificationType = input.CertificationType,
            ApplicationDate = input.ApplicationDate,
            Notes = input.Notes,
            Status = CertificationStatus.Submitted,
        };

        using var connection = _connectionFactory.CreateConnection();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = """
            INSERT INTO company_certifications
                (id, company_name, unified_social_credit_code, certification_type,
                 application_date, notes, status, reviewer_notes, reviewer_decision, reviewed_by)
            VALUES
                (@id, @company_name, @unified_social_credit_code, @certification_type,
                 @application_date, @notes, @status, @reviewer_notes, @reviewer_decision, @reviewed_by)
            """;
        cmd.Parameters.AddWithValue("@id", record.Id.ToString());
        cmd.Parameters.AddWithValue("@company_name", record.CompanyName);
        cmd.Parameters.AddWithValue("@unified_social_credit_code", record.UnifiedSocialCreditCode);
        cmd.Parameters.AddWithValue("@certification_type", record.CertificationType);
        cmd.Parameters.AddWithValue("@application_date", (object?)record.ApplicationDate ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@notes", (object?)record.Notes ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@status", (int)record.Status);
        cmd.Parameters.AddWithValue("@reviewer_notes", (object?)record.ReviewerNotes ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@reviewer_decision", (object?)record.ReviewerDecision ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@reviewed_by", (object?)record.ReviewedBy ?? DBNull.Value);

        await cmd.ExecuteNonQueryAsync(cancellationToken);
        return record;
    }

    public async Task<CertificationRecord?> GetAsync(
        Guid id,
        CancellationToken cancellationToken = default)
    {
        using var connection = _connectionFactory.CreateConnection();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = """
            SELECT id, company_name, unified_social_credit_code, certification_type,
                   application_date, notes, status, reviewer_notes, reviewer_decision, reviewed_by
            FROM company_certifications
            WHERE id = @id
            """;
        cmd.Parameters.AddWithValue("@id", id.ToString());

        using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
            return null;

        return ReadRecord(reader);
    }

    public async Task<IReadOnlyList<CertificationRecord>> GetAllAsync(
        CancellationToken cancellationToken = default)
    {
        using var connection = _connectionFactory.CreateConnection();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = """
            SELECT id, company_name, unified_social_credit_code, certification_type,
                   application_date, notes, status, reviewer_notes, reviewer_decision, reviewed_by
            FROM company_certifications
            ORDER BY id
            """;

        var results = new List<CertificationRecord>();
        using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
            results.Add(ReadRecord(reader));

        return results.AsReadOnly();
    }

    public async Task ApproveAsync(
        Guid id,
        CertificationReviewInput review,
        string reviewerUserId,
        CancellationToken cancellationToken = default)
    {
        using var connection = _connectionFactory.CreateConnection();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = """
            UPDATE company_certifications
            SET status = @status,
                reviewer_notes = @reviewer_notes,
                reviewer_decision = @reviewer_decision,
                reviewed_by = @reviewed_by
            WHERE id = @id
            """;
        cmd.Parameters.AddWithValue("@id", id.ToString());
        cmd.Parameters.AddWithValue("@status", (int)CertificationStatus.Approved);
        cmd.Parameters.AddWithValue("@reviewer_notes", (object?)review.ReviewerNotes ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@reviewer_decision", review.Decision);
        cmd.Parameters.AddWithValue("@reviewed_by", reviewerUserId);

        var affected = await cmd.ExecuteNonQueryAsync(cancellationToken);
        if (affected == 0)
            throw new CompanyCertificationNotFoundException(id);
    }

    public async Task RejectAsync(
        Guid id,
        CertificationReviewInput review,
        string reviewerUserId,
        CancellationToken cancellationToken = default)
    {
        using var connection = _connectionFactory.CreateConnection();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = """
            UPDATE company_certifications
            SET status = @status,
                reviewer_notes = @reviewer_notes,
                reviewer_decision = @reviewer_decision,
                reviewed_by = @reviewed_by
            WHERE id = @id
            """;
        cmd.Parameters.AddWithValue("@id", id.ToString());
        cmd.Parameters.AddWithValue("@status", (int)CertificationStatus.Rejected);
        cmd.Parameters.AddWithValue("@reviewer_notes", (object?)review.ReviewerNotes ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@reviewer_decision", review.Decision);
        cmd.Parameters.AddWithValue("@reviewed_by", reviewerUserId);

        var affected = await cmd.ExecuteNonQueryAsync(cancellationToken);
        if (affected == 0)
            throw new CompanyCertificationNotFoundException(id);
    }

    public async Task<int> CountAsync(
        CancellationToken cancellationToken = default)
    {
        using var connection = _connectionFactory.CreateConnection();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM company_certifications";
        var result = await cmd.ExecuteScalarAsync(cancellationToken);
        return result is long l ? (int)l : 0;
    }

    private static CertificationRecord ReadRecord(SqliteDataReader reader)
    {
        return new CertificationRecord
        {
            Id = Guid.Parse(reader.GetString(0)),
            CompanyName = reader.GetString(1),
            UnifiedSocialCreditCode = reader.GetString(2),
            CertificationType = reader.GetString(3),
            ApplicationDate = reader.IsDBNull(4) ? null : reader.GetString(4),
            Notes = reader.IsDBNull(5) ? null : reader.GetString(5),
            Status = (CertificationStatus)reader.GetInt32(6),
            ReviewerNotes = reader.IsDBNull(7) ? null : reader.GetString(7),
            ReviewerDecision = reader.IsDBNull(8) ? null : reader.GetString(8),
            ReviewedBy = reader.IsDBNull(9) ? null : reader.GetString(9),
        };
    }
}
