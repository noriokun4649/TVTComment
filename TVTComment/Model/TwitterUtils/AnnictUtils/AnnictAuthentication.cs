﻿using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace TVTComment.Model.TwitterUtils.AnnictUtils
{
    class AnnictAuthentication
    {
        private readonly string HOST = $"https://api.annict.com/";
        private readonly string ResponseType = "code";
        private readonly string RedirectUri = "urn:ietf:wg:oauth:2.0:oob";
        private readonly string Scope = "read";
        private readonly string UA;
        private readonly string ClientId;
        private readonly string ClientSecret;


        public AnnictAuthentication(string clientId, string clientSecret)
        {
            ClientId = clientId;
            ClientSecret = clientSecret;
            var version = Assembly.GetExecutingAssembly().GetName().Version.ToString(3);
            UA = $"TvtComment/{version}";
        }

        public Uri GetAuthorizeUri()
        {
            const string endpoint = "oauth/authorize";
            return new(@$"{HOST}{endpoint}?client_id={ClientId}&response_type={ResponseType}&redirect_uri={RedirectUri}&scope={Scope}");
        }

        public async Task<string> GetTokenAsync(string token)
        {
            const string endpoint = "oauth/token";
            using var web = new HttpClient();
            var postBodyParameters = new FormUrlEncodedContent(new Dictionary<string, string>
                    {
                        {"client_id", ClientId},
                        {"client_secret", ClientSecret},
                        {"grant_type", "authorization_code"},
                        {"redirect_uri", RedirectUri},
                        {"code", token}
                    });
            web.DefaultRequestHeaders.Add("User-Agent", UA);
            try
            {
                var respons = await web.PostAsync(@$"{HOST}{endpoint}", postBodyParameters);
                var json = JsonDocument.Parse(await respons.Content.ReadAsStringAsync()).RootElement;
                return json.GetProperty("access_token").GetString();
            }
            catch (WebException e)
            {
                var message = e.Message;
                var stream = e.Response.GetResponseStream();
                if (e.Response != null && stream != null)
                {
                    using var sr = new StreamReader(stream);
                    message += $"\n{sr.ReadToEnd()}";
                    sr.Dispose();
                }
                throw new AnnictException($"Annict API /oauth/token の処理に失敗しました\n\n{message}");
            }
            catch  (Exception e) when (e is KeyNotFoundException || e is JsonException)
            {
                throw new AnnictResponseException("Annict APIからのレスポンスを正しく処理できませんでした",e);
            }
        }
    }
}
