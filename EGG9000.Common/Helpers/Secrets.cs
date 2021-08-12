using System;
using System.Collections.Generic;
using System.Text;

namespace EGG9000.Common.Helpers
{
    public class Secrets
    {
        public string Token { get; set; }
        public ConnectionStrings ConnectionStrings { get; set; }
    }

    public class ConnectionStrings
    {
        public string DefaultConnection { get; set; }
    }
}
