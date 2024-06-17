using System;
using System.Linq;
using System.Xml.Linq;
using System.Xml.XPath;

namespace TVTComment.Model
{
    class NiconicoForceValueData : IForceValueData
    {
        readonly XDocument doc;
        readonly NiconicoUtils.JkIdResolver jkIdResolver;

        public NiconicoForceValueData(XDocument doc, NiconicoUtils.JkIdResolver jkIdResolver)
        {
            this.doc = doc;
            this.jkIdResolver = jkIdResolver;
        }

        public int? GetForceValue(ChannelInfo channelInfo)
        {
            ushort nid = channelInfo.NetworkId;
            int jkId = jkIdResolver.Resolve(channelInfo.NetworkId, channelInfo.ServiceId);
            if (jkId == 0)
                return null;

            string jkIdStr = $"jk{jkId}";
            try
            {
                return (int)doc.Element("channels").XPathSelectElements("channel|bs_channel").First(item => jkIdStr == item.Element("video").Value).Element("thread").Element("force");
            }
            catch (Exception e) when (e is ArgumentNullException || e is InvalidOperationException)
            {
                //（ふつうないが）JKIDが見つからなかった場合
                return null;
            }
        }
    }
}
