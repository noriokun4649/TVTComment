using System.Text.RegularExpressions;

namespace TVTComment.Model.TwitterUtils
{
    class AnnimeTitleGetter
    {
        const string flag = @"\[新\]|\[終\]|\[再\]|\[字\]|\[デ\]|\[解\]|\[無\]|\[二\]|\[S\]|\[SS\]|\[初\]|\[生\]|\[Ｎ\]|\[映\]|\[多\]|\[双\]";
        const string subtitleFlag = @"「.+」|【.+】|▽.+ |▼.+|◆.+|★.+|／.+";
        const string removeFlag = @"＜.+＞|〔.+〕";
        const string tvIndex = @"[#＃♯](?<index>[0-9０-９]{1,3})|[第](?<index>[0-9０-９]{1,3})[話回]";

        public static string Convert(string eventname)
        {
            var series = Regex.Replace(Regex.Replace(eventname, " ", "　"), flag + "|" + subtitleFlag + "|" + tvIndex + "|" + removeFlag, "");
            if (series.Split('　').Length > 1)
            {
                series = series.Split('　')[0];
            }
            return series;
        }
    }
}
