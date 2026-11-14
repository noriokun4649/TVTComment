using System;
using System.IO;
using System.Net.Http;
using System.Threading.Tasks;
using System.Xml;
using System.Xml.Linq;
using TVTComment.Model.ChatService;

namespace TVTComment.Model
{
    class NiconicoChatTrendService : IChatTrendService
    {
        public string Name => "ニコニコ実況互換";
        public TimeSpan UpdateInterval => new TimeSpan(0, 0, 0, 50);

        private readonly NiconicoUtils.JkIdResolver jkIdResolver;
        private readonly NiconicoChatServiceSettings chatServiceSettings;
        private readonly HttpClient httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };

        public NiconicoChatTrendService(NiconicoUtils.JkIdResolver jkIdResolver, NiconicoChatServiceSettings chatServiceSettings)
        {
            this.jkIdResolver = jkIdResolver;
            this.chatServiceSettings = chatServiceSettings;
        }

        public async Task<IForceValueData> GetForceValueData()
        {
            XDocument doc;
            try
            {
                var apiUri = chatServiceSettings.EnableThirdForce ? chatServiceSettings.ThirdForceApiUri : @"https://force.norikun.jp/api/v2_app/getchannels";
                // HTTP本文全体への取得期限の適用
                byte[] body = await httpClient.GetByteArrayAsync(apiUri).ConfigureAwait(false);
                using Stream stream = new MemoryStream(body);
                doc = XDocument.Load(stream);
                if (doc.Root?.Name != "channels")
                    throw new XmlException("channels要素がありません");
            }
            catch (Exception e) when (e is HttpRequestException || e is TaskCanceledException || e is XmlException)
            {
                throw new ChatTrendServiceException("勢い値データの取得に失敗しました", e);
            }
            return new NiconicoForceValueData(doc, jkIdResolver);
        }

        public void Dispose()
        {
            httpClient.Dispose();
        }
    }
}
