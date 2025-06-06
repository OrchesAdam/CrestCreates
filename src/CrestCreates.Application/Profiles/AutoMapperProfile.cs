using AutoMapper;
using CrestCreates.Application.Contracts.Examples.DTOs;
using CrestCreates.Domain.Examples;

namespace CrestCreates.Application.Profiles
{
    public class AutoMapperProfile : Profile
    {
        public AutoMapperProfile()
        {
            // 直接定义映射，包含ProductMappingProfile中的配置
            CreateMap<Product, ProductDto>()
                .ForMember(dest => dest.Price, opt => opt.MapFrom(src => src.Price.Amount))
                .ForMember(dest => dest.Currency, opt => opt.MapFrom(src => src.Price.Currency));
            
            // 其他映射配置可以在这里添加
        }
    }
}