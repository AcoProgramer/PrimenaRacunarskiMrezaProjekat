using Common;
using System;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Runtime.Serialization.Formatters.Binary;
using System.Text;
using System.Threading;

namespace ObserverClient
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
                Console.WriteLine("Posmatrac je uspesno povezan sa serverom!");
            }
            catch (SocketException ex)
            {
                Console.WriteLine($"Greska pri povezivanju sa serverom: {ex.SocketErrorCode}");
                return;
            }

            tcpSocket.Blocking = false;

            Igrac posmatrac = new Igrac();
            posmatrac.IPAdresa = "127.0.0.1";
            posmatrac.UDPPort = 0;
            posmatrac.Tip = TipPrijave.Posmatrac;

            bool prijavljen = false;
            while (!prijavljen)
            {
                posmatrac.Ime = ReadRequired("Unesite ime posmatraca:");
                posmatrac.KorisnickoIme = ReadRequired("Unesite korisnicko ime posmatraca:");

                byte[] buffer = Serialize(posmatrac);
                try { tcpSocket.Send(buffer); } catch { Console.WriteLine("[ERROR] Neuspesno slanje prijave."); return; }

                Console.WriteLine("[INFO] Prijava poslata. Cekanje potvrde...");

                // cekaj TcpResponse
                while (true)
                {
                    try
                    {
                        if (tcpSocket.Poll(200 * 1000, SelectMode.SelectRead))
                        {
                            byte[] recv = new byte[2048];
                            int len = tcpSocket.Receive(recv);
                            if (len == 0) return;

                            TcpResponse resp = Deserialize<TcpResponse>(recv, len);
                            if (resp == null)
                            {
                                Console.WriteLine("[GAME] Neispravan odgovor servera.");
                                return;
                            }

                            if (!string.IsNullOrWhiteSpace(resp.Poruka))
                                Console.WriteLine(resp.Poruka);

                            if (!resp.Ok)
                            {
                                Console.WriteLine("[ERROR] Pokusajte ponovo sa drugim korisnickim imenom.\n");
                                break;
                            }

                            prijavljen = true;
                            break;
                        }
                    }
                    catch (SocketException)
                    {
                    }
                    Thread.Sleep(10);
                }
            }

            Console.WriteLine("Unesite broj igraca kome zelite da posaljete bod podrske (ENTER za slanje), 'exit' za izlaz.");
            Console.Write("Igrac za podrsku: ");
            StringBuilder unos = new StringBuilder();

            while (true)
            {
                byte[] recv = new byte[4096];
                int len = 0;
                try
                {
                    if (tcpSocket.Poll(200 * 1000, SelectMode.SelectRead))
                    {
                        len = tcpSocket.Receive(recv);
                        if (len == 0)
                            break;

                        object obj;
                        if (!NetCodec.TryDeserialize(recv, len, out obj))
                        {
                            obj = null;
                        }

                        if (obj is GameState gs)
                        {
                            Console.WriteLine();
                            Console.WriteLine("--- STANJE IGRE (TCP) ---");
                            Console.WriteLine(gs.StanjeTekst);
                            Console.Write("Igrac za podrsku: ");
                        }
                        else
                        {
                            // fallback: ako stigne obican tekst
                            string msg = Encoding.UTF8.GetString(recv, 0, len);
                            Console.WriteLine();
                            Console.WriteLine("[SERVER]:");
                            Console.WriteLine(msg);
                            Console.Write("Igrac za podrsku: ");
                        }
                    }
                }
                catch (SocketException)
                {
                }


                if (Console.KeyAvailable)
                {
                    ConsoleKeyInfo k = Console.ReadKey(true);
                    if (k.Key == ConsoleKey.Enter)
                    {
                        string komanda = unos.ToString();
                        if (komanda.ToLower() == "exit")
                            break;

                        if (int.TryParse(komanda, out int redni))
                        {
                            SupportCommand cmd = new SupportCommand { RedniBrojIgraca = redni };
                            byte[] data = Serialize(cmd);
                            tcpSocket.Send(data);
                        }
                        else
                        {
                            Console.WriteLine();
                            Console.WriteLine("[ERROR] Unesite redni broj igraca.");
                        }

                        unos.Clear();
                        Console.WriteLine();
                        Console.Write("Igrac za podrsku: ");
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
                        unos.Append(k.KeyChar);
                        Console.Write(k.KeyChar);
                    }
                }
            }

            Console.WriteLine("Posmatrac zavrsava sa radom");
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
                    Console.WriteLine("[ERROR] Polje ne sme biti prazno. Pokusajte ponovo.");
                    continue;
                }

                s = s.Trim();

                if (s.Contains(" "))
                {
                    Console.WriteLine("[ERROR] Polje ne sme sadrzati razmake.");
                    continue;
                }

                if (s.Length > 15)
                {
                    Console.WriteLine("[ERROR] Maksimalna duzina je 15 karaktera.");
                    continue;
                }

                return s;
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

