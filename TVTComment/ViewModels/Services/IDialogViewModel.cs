using System;

namespace TVTComment.ViewModels.Services
{
    interface IDialogViewModel
    {
        event Action CloseRequested;
    }
}
