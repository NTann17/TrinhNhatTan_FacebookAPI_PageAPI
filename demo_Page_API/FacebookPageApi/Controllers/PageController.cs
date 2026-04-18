using FacebookPageApi.Models.Requests;
using FacebookPageApi.Services;
using Microsoft.AspNetCore.Mvc;
using System.Text.Json;

namespace FacebookPageApi.Controllers;

[ApiController]
[Route("api/page")]
public class PageController : ControllerBase
{
    private readonly IFacebookGraphService _facebookGraphService;

    public PageController(IFacebookGraphService facebookGraphService)
    {
        _facebookGraphService = facebookGraphService;
    }

    [HttpGet("{pageId}")]
    public async Task<IActionResult> GetPage(string pageId, CancellationToken cancellationToken)
        => await ExecuteAsync(
            () => _facebookGraphService.GetPageAsync(pageId, cancellationToken),
            "Lấy thông tin Page thành công.");

    [HttpGet("{pageId}/posts")]
    public async Task<IActionResult> GetPosts(string pageId, CancellationToken cancellationToken)
        => await ExecuteAsync(
            () => _facebookGraphService.GetPostsAsync(pageId, cancellationToken),
            "Lấy danh sách bài viết của Page thành công.");

    [HttpPost("{pageId}/posts")]
    public async Task<IActionResult> CreatePost(string pageId, [FromBody] CreatePostRequest request, CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid)
        {
            return BadRequest(new
            {
                message = "Dữ liệu gửi lên không hợp lệ.",
                errors = ModelState
            });
        }

        return await ExecuteAsync(
            () => _facebookGraphService.CreatePostAsync(pageId, request.Message, cancellationToken),
            "Tạo bài viết lên Page thành công.");
    }

    [HttpDelete("post/{postId}")]
    public async Task<IActionResult> DeletePost(string postId, CancellationToken cancellationToken)
        => await ExecuteAsync(
            () => _facebookGraphService.DeletePostAsync(postId, cancellationToken),
            "Xóa bài viết thành công.");

    [HttpGet("post/{postId}/comments")]
    public async Task<IActionResult> GetComments(string postId, CancellationToken cancellationToken)
        => await ExecuteAsync(
            () => _facebookGraphService.GetCommentsAsync(postId, cancellationToken),
            "Lấy danh sách bình luận của bài viết thành công.");

    [HttpGet("post/{postId}/likes")]
    public async Task<IActionResult> GetLikes(string postId, CancellationToken cancellationToken)
        => await ExecuteAsync(
            () => _facebookGraphService.GetLikesAsync(postId, cancellationToken),
            "Lấy danh sách lượt thích của bài viết thành công.");

    [HttpGet("{pageId}/insights")]
    public async Task<IActionResult> GetInsights(string pageId, CancellationToken cancellationToken)
        => await ExecuteAsync(
            () => _facebookGraphService.GetInsightsAsync(pageId, cancellationToken),
            "Lấy dữ liệu thống kê của Page thành công.");

    private async Task<IActionResult> ExecuteAsync(Func<Task<string>> action, string successMessage)
    {
        try
        {
            var data = await action();
            return Ok(new
            {
                message = successMessage,
                data = ParseFacebookPayload(data)
            });
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = "Cấu hình Facebook chưa hợp lệ.", error = ex.Message });
        }
        catch (HttpRequestException ex)
        {
            return StatusCode(StatusCodes.Status502BadGateway, new
            {
                message = "Facebook Graph API trả về lỗi khi xử lý yêu cầu.",
                error = ex.Message
            });
        }
    }

    private static object ParseFacebookPayload(string payload)
    {
        try
        {
            using var document = JsonDocument.Parse(payload);
            return document.RootElement.Clone();
        }
        catch (JsonException)
        {
            return payload;
        }
    }
}
