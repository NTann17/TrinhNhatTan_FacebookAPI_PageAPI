using System.ComponentModel.DataAnnotations;

namespace FacebookPageApi.Models.Requests;

public class CreatePostRequest
{
    [Required]
    [MinLength(1)]
    public string Message { get; set; } = string.Empty;
}
