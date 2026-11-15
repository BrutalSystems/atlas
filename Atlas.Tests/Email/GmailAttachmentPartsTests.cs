using System.Text.Json;
using Atlas.Email.Providers;
using Xunit;

namespace Atlas.Tests.Email;

/// <summary>
/// How GoogleMailProvider finds a message's attachments in Gmail's users.messages.get (format=full) JSON. The HTTP calls
/// themselves have no seam for a fake transport (see FetchMessagesRequestTests), so this covers the parsing only.
/// </summary>
public class GmailAttachmentPartsTests
{
    private static List<Atlas.Email.Models.MailAttachment> Parse(string json) =>
        GoogleMailProvider.AttachmentParts(JsonDocument.Parse(json).RootElement).Select(p => p.Attachment).ToList();

    [Fact]
    public void Finds_nested_files_by_part_id_and_skips_the_body()
    {
        var found = Parse("""
            {"payload":{"partId":"","mimeType":"multipart/mixed","filename":"","body":{"size":0},"parts":[
              {"partId":"0","mimeType":"multipart/alternative","filename":"","body":{"size":0},"parts":[
                {"partId":"0.0","mimeType":"text/plain","filename":"","body":{"size":12,"data":"aGVsbG8"}},
                {"partId":"0.1","mimeType":"text/html","filename":"","body":{"size":30,"data":"PGI-aGVsbG88L2I-"}}]},
              {"partId":"1","mimeType":"application/vnd.openxmlformats-officedocument.spreadsheetml.sheet","filename":"Inventory.xlsx",
               "headers":[{"name":"Content-Disposition","value":"attachment; filename=\"Inventory.xlsx\""}],
               "body":{"attachmentId":"ANGjdJ8-changes-every-fetch","size":48211}}]}}
            """);

        var file = Assert.Single(found);
        Assert.Equal("1", file.Id);
        Assert.Equal("Inventory.xlsx", file.FileName);
        Assert.Equal("application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", file.ContentType);
        Assert.Equal(48211, file.Size);
        Assert.False(file.IsInline);
    }

    [Fact]
    public void Marks_signature_images_inline()
    {
        var found = Parse("""
            {"payload":{"partId":"","mimeType":"multipart/related","filename":"","body":{"size":0},"parts":[
              {"partId":"0","mimeType":"text/html","filename":"","body":{"size":10,"data":"aGk"}},
              {"partId":"1","mimeType":"image/png","filename":"image001.png",
               "headers":[{"name":"Content-ID","value":"<image001.png@01DB>"}],"body":{"attachmentId":"x","size":900}},
              {"partId":"2","mimeType":"image/jpeg","filename":"logo.jpg",
               "headers":[{"name":"Content-Disposition","value":"inline; filename=\"logo.jpg\""}],"body":{"attachmentId":"y","size":800}}]}}
            """);

        Assert.Equal(["1", "2"], found.Select(a => a.Id));
        Assert.All(found, a => Assert.True(a.IsInline));
    }

    [Fact]
    public void A_message_that_is_just_a_file_is_its_own_attachment()
    {
        var found = Parse("""
            {"payload":{"partId":"","mimeType":"application/pdf","filename":"scan.pdf","body":{"attachmentId":"z","size":5}}}
            """);

        var file = Assert.Single(found);
        Assert.Equal("root", file.Id);
        Assert.Equal("scan.pdf", file.FileName);
    }
}
