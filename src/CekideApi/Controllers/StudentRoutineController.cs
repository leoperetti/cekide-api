using CekideApi.Application.Routines;
using CekideApi.Authorization;
using CekideApi.Domain;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Mvc;

namespace CekideApi.Controllers;

[ApiController]
[Route("api/gym/students/{studentId:int}/routine")]
[Authorize(Policy = Policies.StaffOnly)]
public class StudentRoutineController(RoutineService routines) : ControllerBase
{
    // Transport limit is above the 5 MB business limit so oversized files get a clean 413 from the service.
    private const long TransportLimit = 10 * 1024 * 1024;

    [HttpPut]
    [RequestSizeLimit(TransportLimit)]
    [RequestFormLimits(MultipartBodyLengthLimit = TransportLimit)]
    public async Task<IActionResult> Upload(int studentId, IFormFile? file, CancellationToken cancellationToken)
    {
        if (file is null)
        {
            throw new DomainValidationException("A 'file' part with the PDF is required.");
        }

        await using var stream = file.OpenReadStream();
        await routines.SaveAsync(studentId, stream, file.ContentType, file.FileName, User.GetActor(), cancellationToken);
        return NoContent();
    }

    [HttpGet]
    public async Task<IActionResult> Download(int studentId, CancellationToken cancellationToken)
    {
        var routine = await routines.GetAsync(studentId, cancellationToken);
        return File(routine.Content, RoutineService.PdfContentType, routine.FileName);
    }
}
