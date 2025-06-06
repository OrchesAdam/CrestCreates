using System;
using AutoMapper;
using CrestCreates.Application.Contracts.Examples.DTOs;
using CrestCreates.Domain.Examples;

namespace CrestCreates.Application.Profiles
{
    public class ProductMappingProfile : Profile
    {
        public ProductMappingProfile()
        {
            CreateMap<Product, ProductDto>()
                .ForMember(dest => dest.Price, opt => opt.MapFrom(src => src.Price.Amount))
                .ForMember(dest => dest.Currency, opt => opt.MapFrom(src => src.Price.Currency));
        }
    }
}
