using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;

namespace TVTComment.Model.Utils
{
    class MiscUtils()
    {
        public static string GetUserAgent()
        {
            var assembly = Assembly.GetExecutingAssembly().GetName();
            var ua = assembly.Name + "/" + assembly.Version.ToString(3);
            return ua;
        }
    }
}
