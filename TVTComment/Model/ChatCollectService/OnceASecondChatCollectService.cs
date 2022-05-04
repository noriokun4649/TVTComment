using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace TVTComment.Model.ChatCollectService
{
    abstract class OnceASecondChatCollectService : IChatCollectService
    {
        protected readonly TimeSpan continuousCallLimit;
        protected DateTime lastTime;

        protected OnceASecondChatCollectService(TimeSpan continuousCallLimit)
        {
            this.continuousCallLimit = continuousCallLimit;
        }

        public IEnumerable<Chat> GetChats(ChannelInfo channel, EventInfo _, DateTime time)
        {
            return GetChats(channel, time);
        }

            public IEnumerable<Chat> GetChats(ChannelInfo channel, DateTime time)
        {
            List<Chat> ret = new List<Chat>();
            if (lastTime <= time && time < lastTime + continuousCallLimit)
            {
                for (DateTime t = lastTime.AddSeconds(1); t <= time; t = t.AddSeconds(1))
                {
                    ret.AddRange(GetOnceASecond(channel, t));
                    lastTime = t;
                }
            }
            else
            {
                //過去に戻るか指定時間以上間隔があいてる（シークした？）
                ret.AddRange(GetOnceASecond(channel, time));
                lastTime = time;
            }
            return ret;
        }

        public abstract string Name { get; }
        public abstract string GetInformationText();
        public abstract ChatCollectServiceEntry.IChatCollectServiceEntry ServiceEntry { get; }
        protected abstract IEnumerable<Chat> GetOnceASecond(ChannelInfo channel, DateTime time);
        public virtual bool CanPost => false;
        public virtual Task PostChat(BasicChatPostObject postObject)
        {
            throw new NotSupportedException("Posting is not supprted on this ChatCollectService");
        }
        public virtual void Dispose() { }
    }
}
