using CSharp.Japanese.Kanaxs;
using Microsoft.VisualBasic;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Text.Json;
using System.Threading.Tasks;

namespace TVTComment.Model.TwitterUtils.AnnictUtils
{
    class AnnictApis
    {
        private readonly string HOST = $"https://api.annict.com/";
        private readonly string UA;
        private readonly string ACCESSTOKEN;
        
        public AnnictApis(string accsestoken)
        {
            ACCESSTOKEN = accsestoken;
            var version = Assembly.GetExecutingAssembly().GetName().Version.ToString(3);
            UA = $"TvtComment/{version}";
        }

        public async Task<string> GetTwitterHashtagAsync(string title)
        {
            const string endpoint = "v1/works";
            using var client = new HttpClient();
            client.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent", UA);

            title = KanaEx.ToHankaku(title);

            Stream stream;
            try
            {
                stream = await client.GetStreamAsync($"{HOST}{endpoint}?access_token={ACCESSTOKEN}&filter_title={title}&fields=title,media_text,twitter_hashtag&sort_season=desc").ConfigureAwait(false);
                var jsonDocument = await JsonDocument.ParseAsync(stream).ConfigureAwait(false);
                var result = jsonDocument.RootElement.GetProperty("works").EnumerateArray().Where(x => x.GetProperty("media_text").GetString().Equals("TV")).First();
                Debug.WriteLine(result);
                return result.GetProperty("twitter_hashtag").GetString();
            }
            catch (InvalidOperationException) {
                throw new AnnictNotFoundResponseException("Annictでアニメが見つかりませんでした");
            }
            catch (HttpRequestException e)
            {
                var message = e.Message;
                switch (e.StatusCode)
                {
                    case HttpStatusCode.Unauthorized:
                        message += "\nアクセストークンが無効か失効しています";
                        break;
                }
                throw new AnnictException($"Annict API {endpoint} の処理に失敗しました\n\n{message}");
            }
            catch (Exception e) when (e is KeyNotFoundException || e is JsonException)
            {
                throw new AnnictResponseException("Annict APIからのレスポンスを正しく処理できませんでした", e);
            }
        }
    }
}
