using System; using System.Collections.Generic; using System.Linq; using System.Threading; using System.Threading.Tasks; using AutoFixture; using AutoFixture.AutoMoq; using CrestCreates.Domain.UnitOfWork; using CrestCreates.TestBase; using Ecommerce.Application.Contracts.DTOs; using Ecommerce.Application.Contracts.Interfaces; using Ecommerce.Application.Services; using Ecommerce.Domain.Entities; using Ecommerce.Domain.Repositories; using Moq; using Xunit; using FluentAssertions;

namespace Ecommerce.Application.Tests.Services {
    public class ProductServiceTests : ApplicationTestBase {
        private readonly Mock<IProductRepository> _productRepositoryMock;
        private readonly Mock<IUnitOfWork> _unitOfWorkMock;
        private readonly Mock<AutoMapper.IMapper> _mapperMock;
        private readonly IProductService _productService;
        private readonly IFixture _fixture;

        public ProductServiceTests() {
            _fixture = new Fixture().Customize(new AutoMoqCustomization());
            _productRepositoryMock = _fixture.Freeze<Mock<IProductRepository>>();
            _unitOfWorkMock = _fixture.Freeze<Mock<IUnitOfWork>>();
            _mapperMock = _fixture.Freeze<Mock<AutoMapper.IMapper>>();
            _productService = new ProductService(
                _productRepositoryMock.Object,
                _unitOfWorkMock.Object,
                _mapperMock.Object
            );
        }

        [Fact]
        public async Task CreateAsync_ShouldCreateProduct() {
            // Arrange
            var createDto = _fixture.Create<CreateProductDto>();
            var product = _fixture.Create<Product>();
            var productDto = _fixture.Create<ProductDto>();

            _mapperMock.Setup(m => m.Map<Product>(createDto)).Returns(product);
            _productRepositoryMock.Setup(r => r.AddAsync(product)).ReturnsAsync(product);
            _unitOfWorkMock.Setup(u => u.SaveChangesAsync()).ReturnsAsync(1);
            _mapperMock.Setup(m => m.Map<ProductDto>(product)).Returns(productDto);

            // Act
            var result = await _productService.CreateAsync(createDto, CancellationToken.None);

            // Assert
            result.Should().BeEquivalentTo(productDto);
            _productRepositoryMock.Verify(r => r.AddAsync(product), Times.Once);
            _unitOfWorkMock.Verify(u => u.SaveChangesAsync(), Times.Once);
        }

        [Fact]
        public async Task GetByIdAsync_ShouldReturnProduct() {
            // Arrange
            var productId = 1;
            var product = _fixture.Create<Product>();
            var productDto = _fixture.Create<ProductDto>();

            _productRepositoryMock.Setup(r => r.GetByIdAsync(productId)).ReturnsAsync(product);
            _mapperMock.Setup(m => m.Map<ProductDto>(product)).Returns(productDto);

            // Act
            var result = await _productService.GetByIdAsync(productId, CancellationToken.None);

            // Assert
            result.Should().BeEquivalentTo(productDto);
            _productRepositoryMock.Verify(r => r.GetByIdAsync(productId), Times.Once);
        }

        [Fact]
        public async Task GetByIdAsync_ShouldThrowException_WhenProductNotFound() {
            // Arrange
            var productId = 1;

            _productRepositoryMock.Setup(r => r.GetByIdAsync(productId)).ReturnsAsync((Product)null);

            // Act & Assert
            await Assert.ThrowsAsync<InvalidOperationException>(() => 
                _productService.GetByIdAsync(productId, CancellationToken.None)
            );
        }

        [Fact]
        public async Task GetActiveProductsAsync_ShouldReturnProducts() {
            // Arrange
            var page = 1;
            var pageSize = 10;
            var products = _fixture.CreateMany<Product>(5).ToList();
            var productDtos = _fixture.CreateMany<ProductDto>(5).ToList();

            _productRepositoryMock.Setup(r => r.GetActiveProductsAsync(page, pageSize)).ReturnsAsync(products);
            _mapperMock.Setup(m => m.Map<List<ProductDto>>(products)).Returns(productDtos);

            // Act
            var result = await _productService.GetActiveProductsAsync(page, pageSize, CancellationToken.None);

            // Assert
            result.Items.Should().BeEquivalentTo(productDtos);
            result.TotalCount.Should().Be(products.Count);
            result.Page.Should().Be(page);
            result.PageSize.Should().Be(pageSize);
        }

        [Fact]
        public async Task ReduceStockAsync_ShouldReduceStock() {
            // Arrange
            var productId = 1;
            var quantity = 5;
            var product = _fixture.Create<Product>();
            product.Stock = 10;

            _productRepositoryMock.Setup(r => r.GetByIdAsync(productId)).ReturnsAsync(product);
            _productRepositoryMock.Setup(r => r.UpdateAsync(product)).ReturnsAsync(product);
            _unitOfWorkMock.Setup(u => u.SaveChangesAsync()).ReturnsAsync(1);

            // Act
            await _productService.ReduceStockAsync(productId, quantity, CancellationToken.None);

            // Assert
            product.Stock.Should().Be(5);
            _productRepositoryMock.Verify(r => r.UpdateAsync(product), Times.Once);
            _unitOfWorkMock.Verify(u => u.SaveChangesAsync(), Times.Once);
        }

        [Fact]
        public async Task ReduceStockAsync_ShouldThrowException_WhenInsufficientStock() {
            // Arrange
            var productId = 1;
            var quantity = 15;
            var product = _fixture.Create<Product>();
            product.Stock = 10;

            _productRepositoryMock.Setup(r => r.GetByIdAsync(productId)).ReturnsAsync(product);

            // Act & Assert
            await Assert.ThrowsAsync<InvalidOperationException>(() => 
                _productService.ReduceStockAsync(productId, quantity, CancellationToken.None)
            );
        }
    }
}