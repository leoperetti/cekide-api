using CekideApi.Application.Abstractions;
using CekideApi.Application.Students;
using CekideApi.Domain;

namespace CekideApi.Application.Routines;

public sealed class RoutineService(IStudentRepository students, IRoutineStorage storage, IUnitOfWork unitOfWork, IClock clock)
{
    public const int MaxBytes = 5 * 1024 * 1024;
    public const string PdfContentType = "application/pdf";

    private static readonly byte[] PdfSignature = "%PDF"u8.ToArray();

    /// <summary>Replaces the student's current routine sheet; only the current one is kept.</summary>
    public async Task SaveAsync(
        int studentId, Stream content, string? contentType, string? fileName, string uploadedBy,
        CancellationToken cancellationToken = default)
    {
        var student = await students.GetByIdAsync(studentId, cancellationToken)
                      ?? throw new NotFoundException($"Student {studentId} was not found.");

        var mediaType = contentType?.Split(';')[0].Trim();
        if (!string.Equals(mediaType, PdfContentType, StringComparison.OrdinalIgnoreCase))
        {
            throw new DomainValidationException("The routine must be a PDF file (application/pdf).");
        }

        var buffer = await ReadBoundedAsync(content, cancellationToken);
        if (buffer.Length == 0)
        {
            throw new DomainValidationException("The routine file is empty.");
        }

        if (!buffer.AsSpan().StartsWith(PdfSignature))
        {
            throw new DomainValidationException("The file is not a valid PDF.");
        }

        var path = $"students/{student.Id}/routine.pdf";
        await storage.SaveAsync(path, new MemoryStream(buffer), PdfContentType, cancellationToken);

        student.SetRoutine(path, SanitizeFileName(fileName), uploadedBy, clock.UtcNow.UtcDateTime);
        await unitOfWork.SaveChangesAsync(cancellationToken);
    }

    public async Task<RoutineDownload> GetAsync(int studentId, CancellationToken cancellationToken = default)
    {
        var student = await students.GetByIdAsync(studentId, cancellationToken)
                      ?? throw new NotFoundException($"Student {studentId} was not found.");

        if (student.RoutineBlobPath is null)
        {
            throw new NotFoundException("The student has no routine sheet.");
        }

        var stream = await storage.OpenReadAsync(student.RoutineBlobPath, cancellationToken)
                     ?? throw new NotFoundException("The routine sheet could not be found.");
        return new RoutineDownload(stream, student.RoutineFileName ?? "rutina.pdf");
    }

    private static async Task<byte[]> ReadBoundedAsync(Stream content, CancellationToken cancellationToken)
    {
        using var buffer = new MemoryStream();
        var chunk = new byte[81920];
        int read;
        while ((read = await content.ReadAsync(chunk, cancellationToken)) > 0)
        {
            if (buffer.Length + read > MaxBytes)
            {
                throw new PayloadTooLargeException("The routine file cannot exceed 5 MB.");
            }

            buffer.Write(chunk, 0, read);
        }

        return buffer.ToArray();
    }

    private static string SanitizeFileName(string? fileName)
    {
        var name = Path.GetFileName(fileName ?? string.Empty).Trim();
        if (string.IsNullOrEmpty(name))
        {
            return "rutina.pdf";
        }

        return name.Length > 255 ? name[..255] : name;
    }
}
