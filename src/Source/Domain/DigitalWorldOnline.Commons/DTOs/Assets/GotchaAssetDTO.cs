using DigitalWorldOnline.Commons.DTOs.Assets;

public sealed class GotchaAssetDTO
{
    public int Id { get; set; }
    public int GotchaId { get; set; }
    public int NpcId { get; set; }
    public int UseItem { get; set; }
    public bool Active { get; set; } // Alterado para bool
    public int UseCount { get; set; }
    public short Limit { get; set; }
    public short MinLv { get; set; }
    public short MaxLv { get; set; }
    public short RareItemCnt { get; set; }
    public short Chance { get; set; }
    public List<GotchaItemsAssetDTO> Items { get; set; }
    public List<GotchaRareItemsAssetDTO> RareItems { get; set; }
}