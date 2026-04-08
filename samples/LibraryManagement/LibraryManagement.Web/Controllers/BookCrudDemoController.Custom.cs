using System.Threading;
using System.Threading.Tasks;
using LibraryManagement.Application.Contracts.DTOs;
using Microsoft.AspNetCore.Mvc;

namespace LibraryManagement.Application.Contracts.Interfaces.Controllers;

/// <summary>
/// 自动生成的 BookCrudDemoController 的 Web 层扩展示例。
/// </summary>
public sealed partial class BookCrudDemoController
{
    [HttpGet("by-isbn/{isbn}")]
    public async Task<ActionResult<BookDto>> GetByIsbnAsync([FromRoute] string isbn, CancellationToken cancellationToken = default)
    {
        var book = await Service.GetByIsbnAsync(isbn, cancellationToken);
        return book == null ? NotFound() : Ok(book);
    }
}
