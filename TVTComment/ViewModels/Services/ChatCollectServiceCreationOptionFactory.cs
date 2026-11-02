using System;
using System.Collections.Generic;
using TVTComment.ViewModels.ChatCollectServiceCreationOptionControl;

namespace TVTComment.ViewModels.Services
{
    sealed class ChatCollectServiceCreationOptionFactory
    {
        private readonly Model.TVTComment model;
        private readonly Dictionary<string, ChatCollectServiceCreationOptionControlViewModel> viewModels = new Dictionary<string, ChatCollectServiceCreationOptionControlViewModel>();

        public ChatCollectServiceCreationOptionFactory(Model.TVTComment model)
        {
            this.model = model;
        }

        public ChatCollectServiceCreationOptionControlViewModel GetViewModel(string viewName)
        {
            // Keep each option's input when switching views or reopening the dialog.
            if (!viewModels.TryGetValue(viewName, out var viewModel))
            {
                viewModel = viewName switch
                {
                    "NichanChatCollectServiceCreationOptionControl" => new NichanChatCollectServiceCreationOptionControlViewModel(model),
                    "FileChatCollectServiceCreationOptionControl" => new FileChatCollectServiceCreationOptionControlViewModel(),
                    "NiconicoLiveChatCollectServiceCreationOptionControl" => new NiconicoLiveChatCollectServiceCreationOptionControlViewModel(),
                    "TwitterLiveChatCollectServiceCreationOptionControl" => new TwitterLiveChatCollectServiceCreationOptionControlViewModel(),
                    _ => throw new ArgumentException($"Unknown creation option view: {viewName}", nameof(viewName)),
                };
                viewModels.Add(viewName, viewModel);
            }
            return viewModel;
        }
    }
}
