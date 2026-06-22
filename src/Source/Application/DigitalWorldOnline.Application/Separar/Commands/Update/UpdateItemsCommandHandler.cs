using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using DigitalWorldOnline.Commons.Interfaces;
using DigitalWorldOnline.Commons.Models.Base;
using MediatR;

namespace DigitalWorldOnline.Application.Separar.Commands.Update
{
    public class UpdateItemsCommandHandler : IRequestHandler<UpdateItemsCommand>
    {
        private readonly IServiceProvider _serviceProvider;

        public UpdateItemsCommandHandler(IServiceProvider serviceProvider)
        {
            _serviceProvider = serviceProvider;
        }

        public async Task<Unit> Handle(UpdateItemsCommand request, CancellationToken cancellationToken)
        {
            var itemsToUpdate = request.Items ?? (request.Item != null ? new List<ItemModel> { request.Item } : null);

            if (itemsToUpdate == null || itemsToUpdate.Count ==0)
                return Unit.Value;

            // If caller requested force sync, persist directly (write-through)
            if (request.ForceSync)
            {
                var repositoryDirect = _serviceProvider.GetService(typeof(ICharacterCommandsRepository)) as ICharacterCommandsRepository;
                if (repositoryDirect != null)
                {
                    await repositoryDirect.UpdateItemsAsync(itemsToUpdate);
                    return Unit.Value;
                }
                // If repository not available, continue to try queue below
            }

            // Try to get the background queue; if not registered, fall back to direct repository update.
            var queue = _serviceProvider.GetService(typeof(IUpdateItemsBackgroundQueue)) as IUpdateItemsBackgroundQueue;
            if (queue != null)
            {
                queue.Enqueue(itemsToUpdate);
                return Unit.Value;
            }

            // Fallback: try to resolve repository directly from root provider (may be scoped by host)
            var repository = _serviceProvider.GetService(typeof(ICharacterCommandsRepository)) as ICharacterCommandsRepository;
            if (repository != null)
            {
                await repository.UpdateItemsAsync(itemsToUpdate);
            }

            return Unit.Value;
        }
    }
}