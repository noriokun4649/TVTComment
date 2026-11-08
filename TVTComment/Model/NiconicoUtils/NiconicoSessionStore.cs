using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace TVTComment.Model.NiconicoUtils
{
    sealed class NiconicoSessionStore
    {
        private readonly string path;
        public NiconicoSessionStore(string applicationDirectory)
        {
            path = Path.Combine(GetDataDirectory(applicationDirectory), "niconico-session.bin");
        }

        internal static string GetDataDirectory(string applicationDirectory)
        {
            var key = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(Path.TrimEndingDirectorySeparator(Path.GetFullPath(applicationDirectory)).ToUpperInvariant()))).Substring(0, 24);
            return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "TVTComment", key);
        }

        public void Save(NiconicoOAuthSession session)
        {
            var data = JsonSerializer.SerializeToUtf8Bytes(session.Credentials);
            try
            {
                var encrypted = ProtectedData.Protect(data, null, DataProtectionScope.CurrentUser);
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                var temp = path + ".tmp";
                try { File.WriteAllBytes(temp, encrypted); File.Move(temp, path, true); }
                finally { if (File.Exists(temp)) File.Delete(temp); }
            }
            finally { CryptographicOperations.ZeroMemory(data); }
        }

        public NiconicoOAuthSession Load()
        {
            if (!File.Exists(path)) return null;
            var data = ProtectedData.Unprotect(File.ReadAllBytes(path), null, DataProtectionScope.CurrentUser);
            try
            {
                using var document = JsonDocument.Parse(data);
                if (document.RootElement.ValueKind == JsonValueKind.Array)
                {
                    Delete();
                    throw new FormatException("通常ログインは廃止されました。設定の「ログイン」からOAuth認証を行ってください。");
                }
                if (document.RootElement.ValueKind != JsonValueKind.Object)
                    throw new FormatException("OAuthのセッション保存データが不正です。");
                return new NiconicoOAuthSession(document.RootElement.Deserialize<NiconicoOAuthCredentials>());
            }
            finally { CryptographicOperations.ZeroMemory(data); }
        }

        public void Delete() { if (File.Exists(path)) File.Delete(path); }
    }
}
