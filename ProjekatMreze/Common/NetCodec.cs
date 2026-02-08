using System;
using System.IO;
using System.Text;

namespace Common
{
    public enum MsgKind : byte
    {
        TcpResponse = 1,
        Igrac = 2,
        Igra = 3,
        GameState = 4,
        SupportCommand = 5
    }

    public static class NetCodec
    {
        public static byte[] Serialize(object obj)
        {
            using (MemoryStream ms = new MemoryStream())
            using (BinaryWriter bw = new BinaryWriter(ms, Encoding.UTF8, leaveOpen: true))
            {
                if (obj is TcpResponse resp)
                {
                    bw.Write((byte)MsgKind.TcpResponse);
                    bw.Write(resp.Ok);
                    bw.Write(resp.Poruka ?? "");
                }
                else if (obj is Igrac igr)
                {
                    bw.Write((byte)MsgKind.Igrac);
                    bw.Write(igr.Ime ?? "");
                    bw.Write(igr.KorisnickoIme ?? "");
                    bw.Write(igr.IPAdresa ?? "");
                    bw.Write(igr.UDPPort);
                    bw.Write((int)igr.Tip);
                }
                else if (obj is Igra igra)
                {
                    bw.Write((byte)MsgKind.Igra);
                    bw.Write(igra.ImePrvog ?? "");
                    bw.Write(igra.ImeDrugog ?? "");
                    bw.Write(igra.TrajanjeSekunde);
                    bw.Write(igra.DuzinaReci);
                    bw.Write(igra.MaxGresaka);
                    bw.Write(igra.UdpPortServera);
                }
                else if (obj is GameState gs)
                {
                    bw.Write((byte)MsgKind.GameState);
                    bw.Write(gs.StanjeTekst ?? "");
                }
                else if (obj is SupportCommand sc)
                {
                    bw.Write((byte)MsgKind.SupportCommand);
                    bw.Write(sc.RedniBrojIgraca);
                }
                else
                {
                    throw new InvalidOperationException("NetCodec: Nepoznat tip poruke.");
                }

                bw.Flush();
                return ms.ToArray();
            }
        }

        public static object Deserialize(byte[] buffer, int len)
        {
            using (MemoryStream ms = new MemoryStream(buffer, 0, len))
            using (BinaryReader br = new BinaryReader(ms, Encoding.UTF8, leaveOpen: true))
            {
                MsgKind kind = (MsgKind)br.ReadByte();

                switch (kind)
                {
                    case MsgKind.TcpResponse:
                        return new TcpResponse
                        {
                            Ok = br.ReadBoolean(),
                            Poruka = br.ReadString()
                        };

                    case MsgKind.Igrac:
                        return new Igrac
                        {
                            Ime = br.ReadString(),
                            KorisnickoIme = br.ReadString(),
                            IPAdresa = br.ReadString(),
                            UDPPort = br.ReadInt32(),
                            Tip = (TipPrijave)br.ReadInt32()
                        };

                    case MsgKind.Igra:
                        return new Igra
                        {
                            ImePrvog = br.ReadString(),
                            ImeDrugog = br.ReadString(),
                            TrajanjeSekunde = br.ReadInt32(),
                            DuzinaReci = br.ReadInt32(),
                            MaxGresaka = br.ReadInt32(),
                            UdpPortServera = br.ReadInt32()
                        };

                    case MsgKind.GameState:
                        return new GameState
                        {
                            StanjeTekst = br.ReadString()
                        };

                    case MsgKind.SupportCommand:
                        return new SupportCommand
                        {
                            RedniBrojIgraca = br.ReadInt32()
                        };

                    default:
                        throw new InvalidDataException("NetCodec: Nepoznat MsgKind.");
                }
            }
        }

        public static T Deserialize<T>(byte[] buffer, int len)
        {
            return (T)Deserialize(buffer, len);
        }

        public static bool TryDeserialize(byte[] buffer, int len, out object obj)
        {
            obj = null;
            try
            {
                obj = Deserialize(buffer, len);
                return true;
            }
            catch
            {
                return false;
            }
        }
    }
}