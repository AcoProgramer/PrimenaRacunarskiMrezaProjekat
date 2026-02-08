using System;

namespace Common
{
    [Serializable]
    public class TcpResponse
    {
        public bool Ok { get; set; }
        public string Poruka { get; set; } = "";
    }
}

