using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;

namespace TVTComment.Model.NichanUtils
{
    class FixedNichanThreadSelector : INichanThreadSelector
    {
        public IEnumerable<string> Uris { get; }

        public FixedNichanThreadSelector(IEnumerable<string> uris)
        {
            Uris = uris;
        }

#pragma warning disable CS1998 // この非同期メソッドには 'await' 演算子がないため、同期的に実行されます。'await' 演算子を使用して非ブロッキング API 呼び出しを待機するか、'await Task.Run(...)' を使用してバックグラウンドのスレッドに対して CPU 主体の処理を実行することを検討してください。
        public async IAsyncEnumerable<string> Get(
#pragma warning restore CS1998 // この非同期メソッドには 'await' 演算子がないため、同期的に実行されます。'await' 演算子を使用して非ブロッキング API 呼び出しを待機するか、'await Task.Run(...)' を使用してバックグラウンドのスレッドに対して CPU 主体の処理を実行することを検討してください。
            ChannelInfo channel, DateTimeOffset time, [EnumeratorCancellation] CancellationToken cancellationToken
        )
        {
            foreach (var uri in Uris)
            {
                yield return uri;
            }
        }
    }
}
