using DigitalWorldOnline.Commons.DTOs.Config;

namespace DigitalWorldOnline.Application.Admin.Queries
{
    public class GetGlobalDropsConfigsQueryDto
    {
        public int TotalRegisters { get; set; }
        public List<GlobalDropsConfigDTO> Registers { get; set; }
    }
}