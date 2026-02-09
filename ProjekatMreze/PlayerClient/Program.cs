using System;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using Common;

namespace PlayerClient
{
    internal class Program
    {
        static void Main(string[] args)
        {
            Socket tcpSocket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
            IPEndPoint serverEP = new IPEndPoint(IPAddress.Loopback, 50001);

            try
            {
                tcpSocket.Connect(serverEP);
                Console.WriteLine("Klijent (igrac) je uspesno povezan sa serverom!");
            }
            catch (SocketException ex)
            {
                Console.WriteLine($"Greska pri povezivanju sa serverom: {ex.SocketErrorCode}");
                return;
            }

            // Uvek automatski dodeljen UDP port (nema manuelnog unosa -> nema kolizije)
            Socket udpSocket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
            int mojUdpPort;
            try
            {
                udpSocket.Bind(new IPEndPoint(IPAddress.Any, 0));
                udpSocket.Blocking = false;
                mojUdpPort = ((IPEndPoint)udpSocket.LocalEndPoint).Port;
                Console.WriteLine($"[INFO] Dodeljen UDP port: {mojUdpPort}");
            }
            catch (SocketException ex)
            {
                Console.WriteLine($"[ERROR] Ne mogu da pokrenem UDP socket ({ex.SocketErrorCode}).");
                return;
            }

            //Console.Write("Unesi svoju (lokalnu) IP adresu klijenta: ");
            //igrac.IPAdresa = Console.ReadLine();

            Igrac igrac = new Igrac();
            igrac.IPAdresa = "127.0.0.1";
            igrac.UDPPort = mojUdpPort;
            igrac.Tip = TipPrijave.Igrac;


            // ponavljaj prijavu dok server ne prihvati, a zatim cekaj start parametre direktno iz klase Igra
            Igra igra = null;
            bool prijavljen = false;
            while (igra == null)
            {
                if (!prijavljen)
                {
                    igrac.Ime = ReadRequired("Unesite ime:");
                    igrac.KorisnickoIme = ReadRequired("Unesite korisnicko ime:");

                    try
                    {
                        byte[] buffer = Serialize(igrac);
                        tcpSocket.Send(buffer);
                    }
                    catch (SocketException)
                    {
                        PrintWithColors("[ERROR] Neuspesno slanje prijave.");
                        return;
                    }

                    Console.WriteLine("[INFO] Prijava poslata. Cekanje odgovora servera...");
                }

                try
                {
                    if (tcpSocket.Poll(200 * 1000, SelectMode.SelectRead))
                    {
                        byte[] recvBuf = new byte[4096];
                        int brBajta = tcpSocket.Receive(recvBuf);

                        if (brBajta == 0)
                        {
                            PrintWithColors("[ERROR] Server je zatvorio vezu.");
                            return;
                        }

                        object obj;
                        if (!NetCodec.TryDeserialize(recvBuf, brBajta, out obj))
                        {
                            PrintWithColors("[ERROR] Ne mogu da procitam poruku od servera.");
                            return;
                        }

                        if (!prijavljen)
                        {
                            // ocekujemo TcpResponse kao potvrdu prijave
                            TcpResponse resp = obj as TcpResponse;
                            if (resp == null)
                            {
                                PrintWithColors("[ERROR] Neispravan odgovor servera.");
                                return;
                            }

                            if (!string.IsNullOrWhiteSpace(resp.Poruka))
                                PrintWithColors(resp.Poruka);

                            if (!resp.Ok)
                            {
                                prijavljen = false;
                                PrintWithColors("[INFO] Pokusajte ponovo sa drugim korisnickim imenom.\n");
                                continue;
                            }

                            prijavljen = true;
                            tcpSocket.Blocking = false;
                        }
                        else
                        {
                            // nakon prijave: server šalje direktno Igra kada igra krene
                            Igra ig = obj as Igra;
                            if (ig != null)
                            {
                                igra = ig;
                                break;
                            }


                        }
                    }
                }
                catch (SocketException)
                {
                    PrintWithColors("[ERROR] Doslo je do greske u komunikaciji sa serverom.");
                    return;
                }


                Thread.Sleep(10);
            }

            Console.WriteLine($"Igra pocinje! Duzina reci: {igra.DuzinaReci}, Max gresaka: {igra.MaxGresaka}, UDP port servera: {igra.UdpPortServera}");

            IPEndPoint udpServerEP = new IPEndPoint(IPAddress.Loopback, igra.UdpPortServera);

            Console.WriteLine("Unosite slovo ili celu rec. ENTER za slanje. 'exit' za izlaz.");
            Console.Write("Unesite potez: ");
            StringBuilder unos = new StringBuilder();

            bool gameOver = false;
            bool eliminated = false;

            while (true)
            {
                if (udpSocket.Poll(200 * 1000, SelectMode.SelectRead))
                {
                    byte[] prijemni = new byte[2048];
                    EndPoint from = new IPEndPoint(IPAddress.Any, 0);
                    int len = udpSocket.ReceiveFrom(prijemni, ref from);
                    string msg = Encoding.UTF8.GetString(prijemni, 0, len);

                    Console.Clear();
                    PrintWithColors(msg);

                    if (msg.IndexOf("ne mozete vise pogadjati", StringComparison.OrdinalIgnoreCase) >= 0 ||
                        msg.IndexOf("ispao iz igre", StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        eliminated = true;
                        Console.WriteLine();
                        PrintWithColors("[ERROR] Ispali ste iz igre. Sacekajte zavrsetak.");
                    }

                    if (!gameOver && !eliminated)
                    {
                        Console.WriteLine();
                        Console.Write("Unesite potez: ");
                    }
                }

                // TCP poruke od servera (kraj igre / support info / obavestenja)
                try
                {
                    if (tcpSocket.Poll(200 * 1000, SelectMode.SelectRead))
                    {
                        byte[] tcpRecv = new byte[4096];
                        int tlen = tcpSocket.Receive(tcpRecv);

                        if (tlen == 0)
                        {
                            Console.WriteLine("\n[SERVER TCP]: Veza je zatvorena.");
                            break;
                        }

                        string tmsg = Encoding.UTF8.GetString(tcpRecv, 0, tlen);

                        Console.WriteLine();
                        PrintWithColors("[SERVER TCP]:");
                        PrintWithColors(tmsg);

                        if (tmsg.IndexOf("KRAJ IGRE", StringComparison.OrdinalIgnoreCase) >= 0)
                        {
                            gameOver = true;
                            Console.WriteLine();
                            PrintWithColors("[GAME] Kraj igre. Pritisnite bilo koji taster za izlaz.");
                            break;
                        }

                        if (!gameOver && !eliminated)
                            Console.Write("Unesite potez: ");
                    }
                }
                catch (SocketException)
                {
                }
                catch (ObjectDisposedException)
                {
                    break;
                }

                if (gameOver)
                    break;

                if (Console.KeyAvailable)
                {
                    ConsoleKeyInfo k = Console.ReadKey(true);
                    if (k.Key == ConsoleKey.Enter)
                    {
                        string pokusaj = unos.ToString();
                        if (pokusaj.ToLower() == "exit")
                            break;

                        if (eliminated)
                        {
                            Console.WriteLine();
                            PrintWithColors("[ERROR] Ne mozete vise slati poteze (ostali ste bez gresaka).");
                            unos.Clear();
                            continue;
                        }

                        if (!string.IsNullOrWhiteSpace(pokusaj))
                        {
                            byte[] data = Encoding.UTF8.GetBytes(pokusaj);
                            udpSocket.SendTo(data, udpServerEP);
                        }
                        unos.Clear();
                        Console.WriteLine();
                        Console.Write("Unesite potez: ");
                    }
                    else if (k.Key == ConsoleKey.Backspace)
                    {
                        if (unos.Length > 0)
                        {
                            unos.Length--;
                            Console.Write("\b \b");
                        }
                    }
                    else
                    {
                        if (!eliminated)
                        {
                            unos.Append(k.KeyChar);
                            Console.Write(k.KeyChar);
                        }
                    }
                }
            }

            Console.WriteLine("Igrac zavrsava sa radom");
            udpSocket.Close();
            tcpSocket.Close();
            Console.ReadKey();
        }

        static string ReadRequired(string prompt)
        {
            while (true)
            {
                Console.WriteLine(prompt);
                string s = Console.ReadLine();
                if (string.IsNullOrWhiteSpace(s))
                {
                    PrintWithColors("[ERROR] Polje ne sme biti prazno. Pokusajte ponovo.");
                    continue;
                }

                s = s.Trim();

                if (s.Contains(" "))
                {
                    PrintWithColors("[ERROR] Polje ne sme sadrzati razmake.");
                    continue;
                }

                if (s.Length > 15)
                {
                    PrintWithColors("[ERROR] Maksimalna duzina je 15 karaktera.");
                    continue;
                }

                return s;
            }
        }

        static void PrintWithColors(string text)
        {
            if (text == null)
                return;

            string[] lines = text.Replace("\r", "").Split('\n');
            foreach (string line in lines)
            {
                if (line.StartsWith("[ERROR]", StringComparison.OrdinalIgnoreCase))
                    Console.ForegroundColor = ConsoleColor.Red;
                else if (line.StartsWith("[INFO]", StringComparison.OrdinalIgnoreCase))
                    Console.ForegroundColor = ConsoleColor.Green;
                else if (line.StartsWith("[GAME]", StringComparison.OrdinalIgnoreCase))
                    Console.ForegroundColor = ConsoleColor.Cyan;
                else
                    Console.ResetColor();

                Console.WriteLine(line);
                Console.ResetColor();
            }
        }

        static byte[] Serialize(object obj)
        {
            return NetCodec.Serialize(obj);
        }

        static T Deserialize<T>(byte[] data, int len)
        {
            return NetCodec.Deserialize<T>(data, len);
        }
    }
}
