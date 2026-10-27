using System;
using System.IO;
using System.Linq;
using System.Net;
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

        private sealed class StoredCookie
        {
            public string Name { get; set; }
            public string Value { get; set; }
            public string Domain { get; set; }
            public string Path { get; set; }
            public bool Secure { get; set; }
            public bool HttpOnly { get; set; }
            public DateTime Expires { get; set; }
        }

        public void Save(NiconicoLoginSession session)
        {
            var values = session.Cookie.Cast<Cookie>().Where(c => NiconicoLoginSession.IsServiceDomain(c.Domain) && !c.Expired)
                .Select(c => new StoredCookie { Name = c.Name, Value = c.Value, Domain = c.Domain, Path = c.Path, Secure = c.Secure, HttpOnly = c.HttpOnly, Expires = c.Expires });
            var data = JsonSerializer.SerializeToUtf8Bytes(values);
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

        public CookieCollection Load()
        {
            if (!File.Exists(path)) return null;
            var data = ProtectedData.Unprotect(File.ReadAllBytes(path), null, DataProtectionScope.CurrentUser);
            try
            {
                var values = JsonSerializer.Deserialize<StoredCookie[]>(data) ?? throw new FormatException("セッション保存データが空です。");
                var cookies = new CookieCollection();
                foreach (var value in values)
                {
                    if (!NiconicoLoginSession.IsServiceDomain(value.Domain)) continue;
                    var cookie = new Cookie(value.Name, value.Value, value.Path, value.Domain)
                    { Secure = value.Secure, HttpOnly = value.HttpOnly, Expires = value.Expires };
                    if (!cookie.Expired) cookies.Add(cookie);
                }
                return cookies;
            }
            finally { CryptographicOperations.ZeroMemory(data); }
        }

        public void Delete() { if (File.Exists(path)) File.Delete(path); }
    }
}
