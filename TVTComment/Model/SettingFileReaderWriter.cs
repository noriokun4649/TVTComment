using System;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

namespace TVTComment.Model
{
    class SettingFileReaderWriter<SettingT> where SettingT : new()
    {
        class TimeSpanConverter : JsonConverter<TimeSpan>
        {
            public override TimeSpan Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
            {
                var value = reader.GetString();
                return TimeSpan.Parse(value);
            }

            public override void Write(Utf8JsonWriter writer, TimeSpan value, JsonSerializerOptions options)
            {
                writer.WriteStringValue(value.ToString());
            }
        }

        private readonly JsonSerializerOptions jsonSerializerOptions;

        public string FilePath { get; }
        public SettingFileReaderWriter(string filepath, bool appendFileExtension)
        {
            FilePath = filepath;
            if (appendFileExtension)
                FilePath += ".json";

            jsonSerializerOptions = new JsonSerializerOptions()
            {
                NumberHandling = JsonNumberHandling.AllowNamedFloatingPointLiterals |
                                 JsonNumberHandling.AllowReadingFromString
            };
            jsonSerializerOptions.Converters.Add(new TimeSpanConverter());
            jsonSerializerOptions.Converters.Add(new JsonStringEnumConverter());
        }

        public async Task<SettingT> Read()
        {
            StreamReader reader;
            try
            {
                reader = new StreamReader(new FileStream(FilePath, FileMode.Open, FileAccess.Read,
                    FileShare.Read | FileShare.Delete), Encoding.UTF8);
            }
            catch (Exception e) when (e is DirectoryNotFoundException || e is FileNotFoundException)
            {
                return new SettingT();
            }
            try
            {
                return JsonSerializer.Deserialize<SettingT>(await reader.ReadToEndAsync(), jsonSerializerOptions);
            }
            catch (JsonException e)
            {
                throw new FormatException(null, e);
            }
            finally
            {
                reader.Dispose();
            }
        }

        public Task Write(SettingT setting)
        {
            // UIスレッドでの保存内容の確定と、別スレッドでのファイル書き込み
            string json = JsonSerializer.Serialize(setting, jsonSerializerOptions);
            return Task.Run(() =>
            {
                Directory.CreateDirectory(Path.GetDirectoryName(FilePath));
                string temp = FilePath + "." + Guid.NewGuid().ToString("N") + ".tmp";
                try
                {
                    File.WriteAllText(temp, json, Encoding.UTF8);
                    try { File.Replace(temp, FilePath, null); }
                    catch (FileNotFoundException) { File.Move(temp, FilePath, true); }
                }
                finally { File.Delete(temp); }
            });
        }
    }
}
