using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using AutoMapper;
using Microsoft.Extensions.Logging;
using CrestCreates.Application.Contracts.Examples.DTOs;
using CrestCreates.Application.Contracts.Examples.Interfaces;
using CrestCreates.Domain.Examples;
using CrestCreates.Domain.Repositories;
using CrestCreates.Domain.Shared.Attributes;

namespace CrestCreates.Application.Examples.Services
{
    [Service(GenerateController = true, Route = "api/products")]
    public class ProductService : IProductService
    {
        private readonly IRepository<Product, Guid> _productRepository;
        private readonly IMapper _mapper;
        private readonly ILogger<ProductService> _logger;
        
        public ProductService(
            IRepository<Product, Guid> productRepository,
            IMapper mapper,
            ILogger<ProductService> logger)
        {
            _productRepository = productRepository;
            _mapper = mapper;
            _logger = logger;
        }
        
        public async Task<ProductDto> GetByIdAsync(Guid id)
        {
            var product = await _productRepository.GetByIdAsync(id);
            return _mapper.Map<ProductDto>(product);
        }
        
        public async Task<List<ProductDto>> GetAllAsync()
        {
            var products = await _productRepository.GetAllAsync();
            return _mapper.Map<List<ProductDto>>(products);
        }
        
        public async Task<ProductDto> CreateAsync(CreateProductDto input)
        {
            var money = new Money(input.Price, input.Currency);
            var product = new Product(Guid.NewGuid(), input.Name, input.Description, money, input.Type);
            
            await _productRepository.AddAsync(product);
            
            return _mapper.Map<ProductDto>(product);
        }
        
        public async Task<ProductDto> UpdateAsync(UpdateProductDto input)
        {
            var product = await _productRepository.GetByIdAsync(input.Id);
            if (product == null)
                throw new KeyNotFoundException($"产品不存在: {input.Id}");
                
            product.SetName(input.Name);
            product.SetDescription(input.Description);
            
            await _productRepository.UpdateAsync(product);
            
            return _mapper.Map<ProductDto>(product);
        }
        
        public async Task DeleteAsync(Guid id)
        {
            var product = await _productRepository.GetByIdAsync(id);
            if (product == null)
                throw new KeyNotFoundException($"产品不存在: {id}");
                
            await _productRepository.DeleteAsync(product);
        }
        
        public async Task<ProductDto> UpdatePriceAsync(UpdateProductPriceDto input)
        {
            var product = await _productRepository.GetByIdAsync(input.Id);
            if (product == null)
                throw new KeyNotFoundException($"产品不存在: {input.Id}");
                
            var newPrice = new Money(input.Price, input.Currency);
            product.UpdatePrice(newPrice);
            
            await _productRepository.UpdateAsync(product);
            
            return _mapper.Map<ProductDto>(product);
        }
        
        public async Task<ProductDto> AddStockAsync(UpdateProductStockDto input)
        {
            var product = await _productRepository.GetByIdAsync(input.Id);
            if (product == null)
                throw new KeyNotFoundException($"产品不存在: {input.Id}");
                
            product.AddStock(input.Quantity);
            
            await _productRepository.UpdateAsync(product);
            
            return _mapper.Map<ProductDto>(product);
        }
        
        public async Task<ProductDto> RemoveStockAsync(UpdateProductStockDto input)
        {
            var product = await _productRepository.GetByIdAsync(input.Id);
            if (product == null)
                throw new KeyNotFoundException($"产品不存在: {input.Id}");
                
            product.RemoveStock(input.Quantity);
            
            await _productRepository.UpdateAsync(product);
            
            return _mapper.Map<ProductDto>(product);
        }
    }
}
