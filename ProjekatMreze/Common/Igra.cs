using System;

namespace Common
{
    [Serializable]
    public class Igra
    {
        public string ImePrvog { get; set; } = "";
        public string ImeDrugog { get; set; } = "";
        public int TrajanjeSekunde { get; set; }
        public int DuzinaReci { get; set; }
        public int MaxGresaka { get; set; }
        public int UdpPortServera { get; set; }
    }

    [Serializable]
    public enum TipPrijave
    {
        Igrac,
        Posmatrac
    }

    [Serializable]
    public class Igrac
    {
        public string Ime { get; set; } = "";
        public string KorisnickoIme { get; set; } = "";
        public string IPAdresa { get; set; } = "";
        public int UDPPort { get; set; }
        public TipPrijave Tip { get; set; }
    }
}

