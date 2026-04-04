using System;
using System.Threading;
using System.Threading.Tasks;
using LibraryManagement.Application.Contracts.DTOs;
using LibraryManagement.Application.Contracts.Interfaces;
using Microsoft.AspNetCore.Mvc;

namespace LibraryManagement.Web.Controllers;

[ApiController]
[Route("api/[controller]")]
public class BooksController : ControllerBase
{
    private readonly IBookAppService _bookAppService;

    public BooksController(IBookAppService bookAppService)
    {
        _bookAppService = bookAppService;
    }

    [HttpGet("{id}")]
    public async Task<ActionResult<BookDto>> Get(Guid id, CancellationToken cancellationToken)
    {
        var book = await _bookAppService.GetByIdAsync(id, cancellationToken);
        if (book == null)
            return NotFound();
        return Ok(book);
    }

    [HttpPost]
    public async Task<ActionResult<BookDto>> Create(CreateBookDto input, CancellationToken cancellationToken)
    {
        var book = await _bookAppService.CreateAsync(input, cancellationToken);
        return CreatedAtAction(nameof(Get), new { id = book.Id }, book);
    }

    [HttpPut("{id}")]
    public async Task<ActionResult<BookDto>> Update(Guid id, UpdateBookDto input, CancellationToken cancellationToken)
    {
        var book = await _bookAppService.UpdateAsync(id, input, cancellationToken);
        return Ok(book);
    }

    [HttpDelete("{id}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken cancellationToken)
    {
        await _bookAppService.DeleteAsync(id, cancellationToken);
        return NoContent();
    }
}