using Microsoft.AspNetCore.Mvc;
using Ecommerce.Application.Contracts.DTOs;
using Ecommerce.Application.Contracts.Interfaces;

namespace Ecommerce.Web.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class ProductController : ControllerBase
    {
        private readonly IProductService _productService;

        public ProductController(IProductService productService)
        {
            _productService = productService;
        }

        [HttpGet("{id}")]
        public async Task<ActionResult<ProductDto>> GetById(int id, CancellationToken cancellationToken = default)
        {
            var product = await _productService.GetByIdAsync(id, cancellationToken);
            return Ok(product);
        }

        [HttpGet("name/{name}")]
        public async Task<ActionResult<ProductDto>> GetByName(string name, CancellationToken cancellationToken = default)
        {
            var product = await _productService.GetByNameAsync(name, cancellationToken);
            return Ok(product);
        }

        [HttpGet("active")]
        public async Task<ActionResult<ProductListDto>> GetActiveProducts(int page = 1, int pageSize = 10, CancellationToken cancellationToken = default)
        {
            var products = await _productService.GetActiveProductsAsync(page, pageSize, cancellationToken);
            return Ok(products);
        }

        [HttpGet("out-of-stock")]
        public async Task<ActionResult<List<ProductDto>>> GetOutOfStockProducts(CancellationToken cancellationToken = default)
        {
            var products = await _productService.GetOutOfStockProductsAsync(cancellationToken);
            return Ok(products);
        }

        [HttpGet("average-price")]
        public async Task<ActionResult<decimal>> GetAveragePrice(CancellationToken cancellationToken = default)
        {
            var averagePrice = await _productService.GetAveragePriceAsync(cancellationToken);
            return Ok(averagePrice);
        }

        [HttpPost]
        public async Task<ActionResult<ProductDto>> Create([FromBody] CreateProductDto dto, CancellationToken cancellationToken = default)
        {
            var product = await _productService.CreateAsync(dto, cancellationToken);
            return CreatedAtAction(nameof(GetById), new { id = product.Id }, product);
        }

        [HttpPut("{id}")]
        public async Task<ActionResult<ProductDto>> Update(int id, [FromBody] UpdateProductDto dto, CancellationToken cancellationToken = default)
        {
            var product = await _productService.UpdateAsync(id, dto, cancellationToken);
            return Ok(product);
        }

        [HttpDelete("{id}")]
        public async Task<ActionResult> Delete(int id, CancellationToken cancellationToken = default)
        {
            await _productService.DeleteAsync(id, cancellationToken);
            return NoContent();
        }

        [HttpPost("{id}/reduce-stock")]
        public async Task<ActionResult> ReduceStock(int id, [FromBody] int quantity, CancellationToken cancellationToken = default)
        {
            await _productService.ReduceStockAsync(id, quantity, cancellationToken);
            return Ok();
        }

        [HttpPost("{id}/increase-stock")]
        public async Task<ActionResult> IncreaseStock(int id, [FromBody] int quantity, CancellationToken cancellationToken = default)
        {
            await _productService.IncreaseStockAsync(id, quantity, cancellationToken);
            return Ok();
        }
    }
}