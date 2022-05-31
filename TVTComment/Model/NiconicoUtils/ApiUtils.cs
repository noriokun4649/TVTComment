using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace TVTComment.Model.NiconicoUtils
{
    public static class OAuthApiUtils
    {
        public class Room
        {
            public readonly Uri webSocketUri;
            public readonly string threadId;
            public Room(Uri uri, string id)
            {
                webSocketUri = uri;
                threadId = id;
            }
        }

        private static async Task<JsonElement> GetProgramInfo(HttpClient client, CancellationToken cancellationToken, string programId, string userId,string fields)
        {
            var stream = await client.GetStreamAsync($"https://api.live2.nicovideo.jp/api/v1/watch/programs?nicoliveProgramId={programId}&userId={userId}&fields={fields}", cancellationToken).ConfigureAwait(false);
            var respJson = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken).ConfigureAwait(false);
            var watchData = respJson.RootElement.GetProperty("data");
            return watchData;
        }

        public static async Task<string> GetProgramInfoFromCommunityId(HttpClient client, CancellationToken cancellationToken, string programId, string userId)
        {
            var field = "socialGroup";
            var watchData = await GetProgramInfo(client, cancellationToken, programId, userId, field);
            var social = watchData.GetProperty(field);
            var id = social.GetProperty("socialGroupId").GetString();
            return id;
        }

        public static async Task<long> GetProgramInfoFromVposBaseTime(HttpClient client, CancellationToken cancellationToken, string programId, string userId)
        {
            var field = "program";
            var watchData = await GetProgramInfo(client, cancellationToken, programId, userId, field);
            var program = watchData.GetProperty(field);
            var vposBaseTime = program.GetProperty("schedule").GetProperty("vposBaseTime").GetString();
            var vposBaseTimeUnix = new DateTimeOffset(DateTime.Parse(vposBaseTime)).ToUnixTimeSeconds();
            return vposBaseTimeUnix;
        }

        public static async Task<string> GetProgramIdFromChAsync(HttpClient client, CancellationToken cancellationToken , string chId)
        {
            var resp = await client.GetStreamAsync($"https://api.live2.nicovideo.jp/api/v1/watch/channels/programs/onair?id={chId}", cancellationToken).ConfigureAwait(false);
            var respJson = await JsonDocument.ParseAsync(resp, cancellationToken: cancellationToken).ConfigureAwait(false);
            var programId = respJson.RootElement.GetProperty("data").GetProperty("programId").GetString();
            return programId;
        }

        public static async Task<Room> GetRoomFromProgramId(HttpClient client, CancellationToken cancellationToken, string programId, string userId)
        {
            var stream = await client.GetStreamAsync($"https://api.live2.nicovideo.jp/api/v1/unama/programs/rooms?nicoliveProgramId={programId}&userId={userId}", cancellationToken).ConfigureAwait(false);
            var respJson = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken).ConfigureAwait(false);
            
            var roomsData = respJson.RootElement.GetProperty("data")[0]; // 0 なのでアリーナ固定
            var webSocketUrl = roomsData.GetProperty("webSocketUri").GetString();
            var threadId = roomsData.GetProperty("threadId").GetString();
            var webSocketUri = new Uri(webSocketUrl);
            var room = new Room(webSocketUri, threadId);
            return room;
        }

        public static async Task<Uri> GetWatchWebSocketUri(HttpClient client, CancellationToken cancellationToken, string programId, string userId)
        {
            var stream = await client.GetStreamAsync($"https://api.live2.nicovideo.jp/api/v1/wsendpoint?nicoliveProgramId={programId}&userId={userId}", cancellationToken).ConfigureAwait(false);
            var respJson = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken).ConfigureAwait(false);

            var watchData = respJson.RootElement.GetProperty("data");
            var url = watchData.GetProperty("url").GetString();
            var uri = new Uri(url);
            return uri;
        }
    }
}
