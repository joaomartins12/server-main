using DigitalWorldOnline.Commons.Models;
using DigitalWorldOnline.Commons.Models.Base;
using DigitalWorldOnline.Commons.Models.Map;
using DigitalWorldOnline.Commons.Utils;
using Serilog;

namespace DigitalWorldOnline.Game.Managers
{
    public class DropManager
    {
        private long _dropId;
        private readonly ILogger _logger;

        public DropManager(ILogger logger)
        {
            _logger = logger;
            _dropId = UtilitiesFunctions.RandomInt();
        }

        public Drop CreateBitDrop(
            long ownerId,
            ushort ownerHandler,
            int minAmount,
            int maxAmount,
            short mapId,
            int x,
            int y
        )
        {
            var drop = new Drop(
                _dropId,
                ownerId,
                ownerHandler,
                new ItemModel(
                    90600,
                    UtilitiesFunctions.RandomInt(
                        minAmount,
                        maxAmount
                    )
                ),
                new Location(
                    mapId,
                    x + UtilitiesFunctions.RandomInt(-100, 100),//TODO: externalizar
                    y + UtilitiesFunctions.RandomInt(-25, 25)
                )
            );

            _dropId++;


            return drop;
        }

        public Drop CreateItemDrop(
            long ownerId,
            ushort ownerHandler,
            int itemId,
            int minAmount,
            int maxAmount,
            short mapId,
            int x,
            int y,
            bool thrown = false
        )
        {
            var drop = new Drop(
                _dropId,
                ownerId,
                ownerHandler,
                new ItemModel (
                    itemId,
                    UtilitiesFunctions.RandomInt(
                        minAmount,
                        maxAmount
                    )
                ),
                new Location(
                    mapId,
                    x + UtilitiesFunctions.RandomInt(-100, 100),
                    y + UtilitiesFunctions.RandomInt(-25, 25)
                ),
                thrown
            );

            _dropId++;


            return drop;
        }
    }
}
