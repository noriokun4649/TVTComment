using ObservableUtils;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Reflection;
using System.Text.Json;
using System.Threading.Tasks;
using TVTComment.Model.NiconicoUtils;

namespace TVTComment.Model.ChatTrendService
{
    class NewNiconicoChatTrendService : IChatTrendService
    {
        public string Name => "新ニコニコ実況";
        public TimeSpan UpdateInterval => new TimeSpan(0, 0, 1, 0);
        private readonly HttpClient httpClient;
        private readonly LiveIdResolver liveIdResolver;
        private readonly Dictionary<string, int[]> forces = new Dictionary<string, int[]>();

        public NewNiconicoChatTrendService(LiveIdResolver liveIdResolver, NiconicoLoginSession session)
        {
            this.liveIdResolver = liveIdResolver;
            var handler = new HttpClientHandler();
            if (session != null) handler.CookieContainer.Add(session.Cookie);
            httpClient = new HttpClient(handler);
            var assembly = Assembly.GetExecutingAssembly().GetName();
            var ua = assembly.Name + "/" + assembly.Version.ToString(3);
            httpClient.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent", ua);
        }

        public void Dispose()
        {
            httpClient.CancelPendingRequests();
        }

        public async Task<IForceValueData> GetForceValueData()
        {
            var lives = liveIdResolver.GetLiveIdList().Distinct();

            foreach (var id in lives)
            {
                try
                {
                    var liveid = id;
                    if (!liveid.StartsWith("lv")) // 代替えAPIではコミュニティ・チャンネルにおけるコメント鯖取得ができないのでlvを取得しに行く
                    {
                        var getLiveId = await httpClient.GetStreamAsync($"https://live2.nicovideo.jp/unama/tool/v1/broadcasters/social_group/{liveid}/program").ConfigureAwait(false);
                        var liveIdJson = await JsonDocument.ParseAsync(getLiveId).ConfigureAwait(false);
                        var liveIdRoot = liveIdJson.RootElement;
                        if (!liveIdRoot.GetProperty("meta").GetProperty("errorCode").GetString().Equals("OK")) throw new InvalidPlayerStatusNicoLiveCommentSenderException("コミュニティ・チャンネルが見つかりませんでした");
                        liveid = liveIdRoot.GetProperty("data").GetProperty("nicoliveProgramId").GetString(); // lvから始まるLiveIDに置き換え

                    }
                    var jk = await httpClient.GetStreamAsync(@$"https://live2.nicovideo.jp/watch/{liveid}/statistics");
                    var obj = TrendJsonUtils.ToObject<NewNiconicoTrendJson>(jk);

                    var canAdded = !forces.ContainsKey(id);
                    if (canAdded)
                    {
                        forces.Add(id, new int[] { obj.Data.CommentCount, obj.Data.CommentCount });
                    }
                    else
                    {
                        forces[id] = new int[] { forces[id][1], obj.Data.CommentCount };
                    }
                    var random = new Random();
                    await Task.Delay(1000 + random.Next(0, 101));
                }
                catch
                {
                    continue;
                }
            }
            if (forces.Count <= 0) throw new ChatTrendServiceException("勢い値データのタウンロードに失敗しました");

            return new NewNiconicoForceValueData(forces, liveIdResolver);
        }
    }
}
