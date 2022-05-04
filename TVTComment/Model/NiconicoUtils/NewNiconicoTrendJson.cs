using System.Runtime.Serialization;

namespace TVTComment.Model.NiconicoUtils
{
    [DataContract]
    public class NewNiconicoTrendJson
    {
        [DataMember(Name=  "data")]
        public Datum Data { get; set; }
    }


    [DataContract]
    public class Datum
    {
        [DataMember(Name = "commentCount")]
        public int CommentCount { get; set; }
    }

}
