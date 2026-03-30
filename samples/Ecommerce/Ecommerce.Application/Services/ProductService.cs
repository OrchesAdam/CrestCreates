using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using AutoMapper;
using CrestCreates.Domain.UnitOfWork;
using Ecommerce.Application.Contracts.DTOs;
using Ecommerce.Application.Contracts.Interfaces;
using Ecommerce.Domain.Entities;
using Ecommerce.Domain.Repositories;

namespace Ecommerce.Application.Services
{
    public class ProductService : IProductService
    {
        private readonly IProductRepository _productRepository;
        private readonly IUnitOfWork _unitOfWork;
        private readonly IMapper _mapper;

        public ProductService(IProductRepository productRepository, IUnitOfWork unitOfWork, IMapper mapper)
        {
            _productRepository = productRepository;
            _unitOfWork = unitOfWork;
            _mapper = mapper;
        }

        public async Task<ProductDto> CreateAsync(CreateProductDto dto, CancellationToken cancellationToken = default)
        {
            var product = _mapper.Map<Product>(dto);
            await _productRepository.AddAsync(product);
            await _unitOfWork.SaveChangesAsync();
            return _mapper.Map<ProductDto>(product);
        }

        public async Task<ProductDto> UpdateAsync(int id, UpdateProductDto dto, CancellationToken cancellationToken = default)
        {
            var product = await _productRepository.GetByIdAsync(id);
            if (product == null)
            {
                throw new InvalidOperationException("Product not found");
            }

            _mapper.Map(dto, product);
            await _productRepository.UpdateAsync(product);
            await _unitOfWork.SaveChangesAsync();
            return _mapper.Map<ProductDto>(product);
        }

        public async Task<ProductDto> GetByIdAsync(int id, CancellationToken cancellationToken = default)
        {
            var product = await _productRepository.GetByIdAsync(id);
            if (product == null)
            {
                throw new InvalidOperationException("Product not found");
            }
            return _mapper.Map<ProductDto>(product);
        }

        public async Task<ProductDto> GetByNameAsync(string name, CancellationToken cancellationToken = default)
        {
            var product = await _productRepository.GetByNameAsync(name);
            if (product == null)
            {
                throw new InvalidOperationException("Product not found");
            }
            return _mapper.Map<ProductDto>(product);
        }

        public async Task<ProductListDto> GetActiveProductsAsync(int page = 1, int pageSize = 10, CancellationToken cancellationToken = default)
        {
            var products = await _productRepository.GetActiveProductsAsync(page, pageSize);
            var totalCount = products.Count;
            return new ProductListDto
            {
                Items = _mapper.Map<List<ProductDto>>(products),
                TotalCount = totalCount,
                Page = page,
                PageSize = pageSize
            };
        }

        public async Task<List<ProductDto>> GetOutOfStockProductsAsync(CancellationToken cancellationToken = default)
        {
            var products = await _productRepository.GetOutOfStockProductsAsync();
            return _mapper.Map<List<ProductDto>>(products);
        }

        public async Task<decimal> GetAveragePriceAsync(CancellationToken cancellationToken = default)
        {
            return await _productRepository.GetAveragePriceAsync();
        }

        public async Task DeleteAsync(int id, CancellationToken cancellationToken = default)
        {
            var product = await _productRepository.GetByIdAsync(id);
            if (product == null)
            {
                throw new InvalidOperationException("Product not found");
            }

            await _productRepository.DeleteAsync(product);
            await _unitOfWork.SaveChangesAsync();
        }

        public async Task ReduceStockAsync(int id, int quantity, CancellationToken cancellationToken = default)
        {
            var product = await _productRepository.GetByIdAsync(id);
            if (product == null)
            {
                throw new InvalidOperationException("Product not found");
            }

            product.ReduceStock(quantity);
            await _productRepository.UpdateAsync(product);
            await _unitOfWork.SaveChangesAsync();
        }

        public async Task IncreaseStockAsync(int id, int quantity, CancellationToken cancellationToken = default)
        {
            var product = await _productRepository.GetByIdAsync(id);
            if (product == null)
            {
                throw new InvalidOperationException("Product not found");
            }

            product.IncreaseStock(quantity);
            await _productRepository.UpdateAsync(product);
            await _unitOfWork.SaveChangesAsync();
        }
    }
}