using System.Collections.Generic;
using System.Linq;

namespace TVTComment.Model.IPC.IPCMessage
{
    class PanelIPCMessage : IIPCMessage
    {
        public string MessageName => "Panel";
        public string Operation { get; set; }
        public string[] Arguments { get; set; } = System.Array.Empty<string>();

        public IEnumerable<string> Encode()
        {
            // IPCの区切り文字は空白に置き換える
            return new[] { Operation }.Concat(Arguments)
                .Select(x => (x ?? "").Replace('\u001e', ' ').Replace('\u001f', ' ').Replace('\0', ' '));
        }

        public void Decode(IEnumerable<string> content)
        {
            var fields = content.ToArray();
            if (fields.Length == 0 || string.IsNullOrEmpty(fields[0]))
                throw new IPCMessageDecodeException("Panelの操作名がありません");
            Operation = fields[0];
            Arguments = fields.Skip(1).ToArray();
        }
    }
}
