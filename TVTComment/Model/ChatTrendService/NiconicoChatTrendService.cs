using System;
using System.IO;
using System.Net.Http;
using System.Threading.Tasks;
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
        private static readonly HttpClient httpClient = new HttpClient();

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
                Stream stream = await httpClient.GetStreamAsync(apiUri);
                doc = XDocument.Load(stream);
            }
            catch (HttpRequestException e)
            {
                throw new ChatTrendServiceException("勢い値データのタウンロードに失敗しました", e);
            }
            return new NiconicoForceValueData(doc, jkIdResolver);
        }

        public void Dispose()
        {
            httpClient.CancelPendingRequests();
        }
    }
}
