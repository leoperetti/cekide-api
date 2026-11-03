using CekideApi.Application.Students;
using CekideApi.Authorization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CekideApi.Controllers;

[ApiController]
[Route("api/gym/students")]
[Authorize(Policy = Policies.StaffOnly)]
public class StudentsController(StudentService students) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<StudentDto>>> List(
        [FromQuery] string? search, [FromQuery] bool? active, CancellationToken cancellationToken) =>
        Ok(await students.ListAsync(search, active, cancellationToken));

    [HttpGet("{studentId:int}")]
    public async Task<ActionResult<StudentDto>> Get(int studentId, CancellationToken cancellationToken) =>
        Ok(await students.GetAsync(studentId, cancellationToken));

    [HttpPost]
    public async Task<ActionResult<StudentDto>> Create(CreateStudentRequest request, CancellationToken cancellationToken)
    {
        var student = await students.CreateAsync(request, cancellationToken);
        return Created($"/api/gym/students/{student.Id}", student);
    }

    [HttpPut("{studentId:int}")]
    public async Task<ActionResult<StudentDto>> Update(
        int studentId, UpdateStudentRequest request, CancellationToken cancellationToken) =>
        Ok(await students.UpdateAsync(studentId, request, cancellationToken));

    [HttpPost("{studentId:int}/deactivate")]
    public async Task<IActionResult> Deactivate(int studentId, CancellationToken cancellationToken)
    {
        await students.DeactivateAsync(studentId, cancellationToken);
        return NoContent();
    }
}
