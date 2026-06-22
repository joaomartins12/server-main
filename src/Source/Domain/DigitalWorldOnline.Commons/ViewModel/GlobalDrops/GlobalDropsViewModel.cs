
namespace DigitalWorldOnline.Commons.ViewModel.GlobalDrops
{
    public class GlobalDropsViewModel
    {
        public int Id { get; set; }
        public int ItemId { get; set; }
        public byte MinDrop { get; set; }
        public byte MaxDrop { get; set; }
        public double Chance { get; set; }
        public int Map { get; set; }
        public DateTime? StartTime { get; set; }
        public DateTime? EndTime { get; set; }
    }
}
