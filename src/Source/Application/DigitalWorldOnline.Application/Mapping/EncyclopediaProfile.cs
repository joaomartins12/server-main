using AutoMapper;
using DigitalWorldOnline.Commons.DTOs.Character;
using DigitalWorldOnline.Commons.Models.Character;

namespace DigitalWorldOnline.Application.Mapping
{
    public class EncyclopediaProfile : Profile
    {
        public EncyclopediaProfile()
        {
            // DTO → Model
            CreateMap<CharacterEncyclopediaDTO, CharacterEncyclopediaModel>()
                .ForMember(d => d.Evolutions, opt => opt.MapFrom(s => s.Evolutions))
                .ForMember(d => d.EvolutionAsset, opt => opt.MapFrom(s => s.EvolutionAsset))
                .ForMember(d => d.Character, opt => opt.Ignore()); // evitar loop

            CreateMap<CharacterEncyclopediaEvolutionsDTO, CharacterEncyclopediaEvolutionsModel>()
                .ForMember(d => d.Encyclopedia, opt => opt.Ignore()) // evitar loop
                .ForMember(d => d.BaseInfo, opt => opt.MapFrom(s => s.BaseInfo));
        }
    }
}
