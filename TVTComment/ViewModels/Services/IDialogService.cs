using System.Threading.Tasks;
using TVTComment.ViewModels.Notifications;
using TVTComment.ViewModels.SettingsWindowContents;

namespace TVTComment.ViewModels.Services
{
    interface IDialogService
    {
        void ShowNotification(Notification notification);
        Task<ChatCollectServiceCreationSettingsConfirmation> ShowAsync(ChatCollectServiceCreationSettingsConfirmation confirmation);
        Task<ChatCollectServiceCreationPresetConfirmation> ShowAsync(ChatCollectServiceCreationPresetConfirmation confirmation);
    }
}
