using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using CrestCreates.Domain.Shared.Attributes;

namespace CrestCreates.CodeGenerator.Tests.Services
{
    [CrestService(
        GenerateController = true,
        GenerateAuthorization = true,
        ResourceName = "Product",
        GenerateCrudPermissions = true,
        DefaultRoles = new[] { "Admin", "ProductManager" },
        RequireAll = false
    )]
    public class TestProductService
    {
        public async Task<IEnumerable<ProductDto>> GetAllAsync()
        {
            return new List<ProductDto>();
        }
        
        public async Task<ProductDto> GetByIdAsync(Guid id)
        {
            return new ProductDto();
        }
        
        public async Task<ProductDto> CreateAsync(CreateProductDto dto)
        {
            return new ProductDto();
        }
        
        public async Task<ProductDto> UpdateAsync(Guid id, UpdateProductDto dto)
        {
            return new ProductDto();
        }
        
        public async Task<bool> DeleteAsync(Guid id)
        {
            return true;
        }
    }
    

}