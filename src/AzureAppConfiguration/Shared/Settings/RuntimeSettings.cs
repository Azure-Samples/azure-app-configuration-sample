using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Shared.Settings
{
    public class RuntimeSettings
    {
        public static RuntimeSettings Create(string env) {
            Enum.TryParse(env, out Environment environment);
            var v = new RuntimeSettings();
            v.CurrentEnvironment = environment;
            return v;
        }

        public enum Environment
        {
            Development,
            Production,
            Test,
            PPE
        }
        public Environment CurrentEnvironment { get; set; }
        public string? UAMI { get; set; }
    }
}
