using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using CekideApi.IntegrationTests.Infrastructure;

namespace CekideApi.IntegrationTests;

public class RoutineTests : ApiTestBase
{
    private const int MaxBytes = 5 * 1024 * 1024;

    private static byte[] Pdf(int length = 1024)
    {
        var bytes = new byte[length];
        Encoding.ASCII.GetBytes("%PDF-1.7\n").CopyTo(bytes, 0);
        return bytes;
    }

    private static MultipartFormDataContent FileContent(byte[] bytes, string contentType = "application/pdf", string fileName = "rutina.pdf", string name = "file")
    {
        var file = new ByteArrayContent(bytes);
        file.Headers.ContentType = new MediaTypeHeaderValue(contentType);
        return new MultipartFormDataContent { { file, name, fileName } };
    }

    private async Task<int> NewStudentIdAsync() => (await CreateStudentAsync()).GetProperty("id").GetInt32();

    [Fact]
    public async Task Staff_uploads_a_valid_pdf_and_downloads_it()
    {
        var id = await NewStudentIdAsync();
        var bytes = Pdf(2048);

        var put = await Teacher.PutAsync($"/api/gym/students/{id}/routine", FileContent(bytes));

        Assert.Equal(HttpStatusCode.NoContent, put.StatusCode);
        var get = await Owner.GetAsync($"/api/gym/students/{id}/routine");
        Assert.Equal(HttpStatusCode.OK, get.StatusCode);
        Assert.Equal("application/pdf", get.Content.Headers.ContentType?.MediaType);
        Assert.Equal(bytes, await get.Content.ReadAsByteArrayAsync());
    }

    [Fact]
    public async Task Detail_records_who_uploaded_the_routine_and_when()
    {
        var id = await NewStudentIdAsync();
        await Teacher.PutAsync($"/api/gym/students/{id}/routine", FileContent(Pdf(), fileName: "rutina-ana.pdf"));

        var detail = await ReadAsync(await Teacher.GetAsync($"/api/gym/students/{id}"));

        Assert.True(detail.GetProperty("has_routine").GetBoolean());
        Assert.Equal("rutina-ana.pdf", detail.GetProperty("routine_file_name").GetString());
        Assert.Equal("teacher-1", detail.GetProperty("routine_uploaded_by").GetString());
        Assert.Equal(Factory.Clock.UtcNow.UtcDateTime, detail.GetProperty("routine_uploaded_at").GetDateTime().ToUniversalTime());
    }

    [Fact]
    public async Task A_new_upload_replaces_the_current_routine()
    {
        var id = await NewStudentIdAsync();
        await Teacher.PutAsync($"/api/gym/students/{id}/routine", FileContent(Pdf(100), fileName: "v1.pdf"));
        var second = Pdf(200);

        var put = await Owner.PutAsync($"/api/gym/students/{id}/routine", FileContent(second, fileName: "v2.pdf"));

        Assert.Equal(HttpStatusCode.NoContent, put.StatusCode);
        Assert.Equal(second, await (await Teacher.GetAsync($"/api/gym/students/{id}/routine")).Content.ReadAsByteArrayAsync());
        Assert.Equal(1, Factory.Routines.Count);
        var detail = await ReadAsync(await Teacher.GetAsync($"/api/gym/students/{id}"));
        Assert.Equal("v2.pdf", detail.GetProperty("routine_file_name").GetString());
        Assert.Equal("owner-1", detail.GetProperty("routine_uploaded_by").GetString());
    }

    [Fact]
    public async Task A_file_that_is_not_a_pdf_content_type_is_rejected()
    {
        var id = await NewStudentIdAsync();

        var response = await Teacher.PutAsync($"/api/gym/students/{id}/routine", FileContent(Pdf(), "image/png", "rutina.png"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(0, Factory.Routines.Count);
    }

    [Fact]
    public async Task A_pdf_content_type_without_the_pdf_signature_is_rejected()
    {
        var id = await NewStudentIdAsync();
        var fake = Encoding.ASCII.GetBytes("MZ this is an executable pretending to be a pdf");

        var response = await Teacher.PutAsync($"/api/gym/students/{id}/routine", FileContent(fake));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(0, Factory.Routines.Count);
    }

    [Fact]
    public async Task An_empty_file_or_a_missing_file_part_is_rejected()
    {
        var id = await NewStudentIdAsync();

        var empty = await Teacher.PutAsync($"/api/gym/students/{id}/routine", FileContent([]));
        var wrongPart = await Teacher.PutAsync($"/api/gym/students/{id}/routine", FileContent(Pdf(), name: "other"));

        Assert.Equal(HttpStatusCode.BadRequest, empty.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, wrongPart.StatusCode);
    }

    [Fact]
    public async Task A_file_over_5_mb_is_rejected_with_413()
    {
        var id = await NewStudentIdAsync();

        var response = await Teacher.PutAsync($"/api/gym/students/{id}/routine", FileContent(Pdf(MaxBytes + 1)));

        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, response.StatusCode);
        Assert.Equal(0, Factory.Routines.Count);
    }

    [Fact]
    public async Task A_file_of_exactly_5_mb_is_accepted()
    {
        var id = await NewStudentIdAsync();

        var response = await Teacher.PutAsync($"/api/gym/students/{id}/routine", FileContent(Pdf(MaxBytes)));

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
    }

    [Fact]
    public async Task A_rejected_upload_keeps_the_current_routine()
    {
        var id = await NewStudentIdAsync();
        var original = Pdf(300);
        await Teacher.PutAsync($"/api/gym/students/{id}/routine", FileContent(original));

        await Teacher.PutAsync($"/api/gym/students/{id}/routine", FileContent(Pdf(), "text/plain", "x.txt"));

        Assert.Equal(original, await (await Teacher.GetAsync($"/api/gym/students/{id}/routine")).Content.ReadAsByteArrayAsync());
    }

    [Fact]
    public async Task Download_without_routine_or_for_unknown_student_is_not_found()
    {
        var id = await NewStudentIdAsync();

        Assert.Equal(HttpStatusCode.NotFound, (await Teacher.GetAsync($"/api/gym/students/{id}/routine")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await Teacher.GetAsync("/api/gym/students/999/routine")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await Teacher.PutAsync("/api/gym/students/999/routine", FileContent(Pdf()))).StatusCode);
    }

    [Fact]
    public async Task Students_role_and_anonymous_cannot_touch_routines()
    {
        var id = await NewStudentIdAsync();
        await Teacher.PutAsync($"/api/gym/students/{id}/routine", FileContent(Pdf()));

        Assert.Equal(HttpStatusCode.Forbidden, (await Student.GetAsync($"/api/gym/students/{id}/routine")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await Student.PutAsync($"/api/gym/students/{id}/routine", FileContent(Pdf()))).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await Anonymous.GetAsync($"/api/gym/students/{id}/routine")).StatusCode);
    }
}
