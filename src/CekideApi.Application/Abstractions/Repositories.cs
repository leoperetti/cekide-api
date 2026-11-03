using CekideApi.Domain;

namespace CekideApi.Application.Abstractions;

public interface IPlanRepository
{
    Task<Plan?> GetByIdAsync(int id, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<Plan>> ListAsync(CancellationToken cancellationToken = default);

    Task<IReadOnlyList<Plan>> GetByIdsAsync(IEnumerable<int> ids, CancellationToken cancellationToken = default);

    /// <summary>Case-insensitive name check, optionally ignoring one plan (when renaming).</summary>
    Task<bool> NameExistsAsync(string name, int? exceptPlanId, CancellationToken cancellationToken = default);

    void Add(Plan plan);
}

public interface IPassTariffRepository
{
    Task<IReadOnlyList<PassTariff>> ListAsync(CancellationToken cancellationToken = default);

    Task<PassTariff?> GetByTypeAsync(PassType type, CancellationToken cancellationToken = default);

    void Add(PassTariff tariff);
}

public interface IStudentRepository
{
    Task<Student?> GetByIdAsync(int id, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<Student>> GetByIdsAsync(IEnumerable<int> ids, CancellationToken cancellationToken = default);

    Task<Student?> GetByEmailAsync(string normalizedEmail, CancellationToken cancellationToken = default);

    Task<Student?> GetByIdentityIdAsync(string identityId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<Student>> SearchAsync(string? search, bool? active, CancellationToken cancellationToken = default);

    Task<bool> EmailExistsAsync(string normalizedEmail, int? exceptStudentId, CancellationToken cancellationToken = default);

    void Add(Student student);
}
