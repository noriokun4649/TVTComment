using System.Net;
using System.Threading;
using System.Threading.Tasks;

namespace TVTComment.Model.NiconicoUtils
{
    interface INiconicoLoginService
    {
        Task<CookieCollection> LoginAsync(CancellationToken cancellationToken);
    }
}
