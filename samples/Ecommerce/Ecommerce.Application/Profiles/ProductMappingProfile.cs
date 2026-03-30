using AutoMapper;
using Ecommerce.Application.Contracts.DTOs;
using Ecommerce.Domain.Entities;

namespace Ecommerce.Application.Profiles
{
    public class ProductMappingProfile : Profile
    {
        public ProductMappingProfile()
        {
            CreateMap<Product, ProductDto>();

            CreateMap<CreateProductDto, Product>();
            CreateMap<UpdateProductDto, Product>();
        }
    }
}