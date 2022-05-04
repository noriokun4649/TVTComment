using System;
using System.Collections.Generic;
using System.Threading;

namespace TVTComment.Model.NichanUtils
{
    interface INichanThreadSelector
    {
        IAsyncEnumerable<string> Get(
            ChannelInfo channel, DateTimeOffset time, CancellationToken cancellationToken
        );
    }
}
