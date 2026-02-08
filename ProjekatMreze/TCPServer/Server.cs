using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using Common;

namespace TCPServer
{
    internal class Server
    {
        // Helper statički pogled na probana slova (da ne menjamo potpis metode)
        // Popunjava se iz Main-a.
        private static HashSet<char> probanaSlovaStatic;

        static void Main(string[] args)
        {
            Socket serverSocket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
            IPEndPoint serverEP = new IPEndPoint(IPAddress.Any, 50001);
            serverSocket.Bind(serverEP);
            serverSocket.Blocking = false;

            int maxIgraca = 2; // unapred definisan broj igraca
            int udpPortServera = 50002;

            serverSocket.Listen(maxIgraca + 10);

            Console.WriteLine($"Server je stavljen u stanje osluškivanja i ocekuje prijave na {serverEP}");

            List<Socket> tcpKlijenti = new List<Socket>();
            List<IgracInfo> igraci = new List<IgracInfo>();
            List<PosmatracInfo> posmatraci = new List<PosmatracInfo>();

            byte[] buffer = new byte[1024];

            // Prosiren recnik (srpske reci, bez dijakritika radi lakseg unosa u konzoli)
            string[] recnik = new string[]
            {
                "mreza", "program", "konsola", "racunar", "tastatura", "mis", "monitor", "internet", "server", "klijent",
                "paket", "protokol", "port", "adresa", "socket", "udp", "tcp", "nit", "proces", "memorija",
                "datoteka", "folder", "disk", "sistem", "komanda", "izuzetak", "objekat", "klasa", "metoda", "interfejs",
                "polje", "niz", "lista", "recnik", "petlja", "uslov", "operator", "promenljiva", "konstanta", "funkcija",
                "konekcija", "poruka", "prijava", "igrac", "posmatrac", "podrska", "bodovi", "rezultat", "rang",
                "pobeda", "poraz", "greska", "pogodak", "slovo", "rec", "maskirana", "pokusaj", "tabela",
                "pitanje", "odgovor", "zadatak", "vezba", "kolokvijum", "ispit", "fakultet", "student", "predmet",
                "biblioteka", "framework", "projekat", "resenje", "debug", "release", "kompajler", "izvrsavanje",
                "komunikacija", "multiplex", "polling", "neblokirajuci", "osluskuje", "povezan", "prekid",
                "kabl", "ruter", "switch", "signal", "mrezni", "paketi", "latencija", "propusnost", "gubitak",
                "sifra", "lozinka", "korisnik", "nalog", "autentikacija", "autorizacija", "bezbednost", "enkripcija"
            };

            // Random inicijalizovan seed-om da se izbegne ponavljanje iste reci pri cesto pokretanju
            Random rnd = new Random(unchecked(Environment.TickCount * 31 + ProcessIdSeed()));

            string poslednjaRec = null;

            Igra trenutnaIgra = null;
            bool igraPocela = false;

            Socket udpSocket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
            udpSocket.Bind(new IPEndPoint(IPAddress.Any, udpPortServera));
            udpSocket.Blocking = false;

            string trazenaRec = "";
            char[] maska = null;
            HashSet<char> probanaSlova = new HashSet<char>();
            probanaSlovaStatic = probanaSlova;

            try
            {
                Console.WriteLine("Server je pokrenut! Za zavrsetak rada pritisnite Escape");

                while (true)
                {
                    List<Socket> checkRead = new List<Socket>();
                    List<Socket> checkError = new List<Socket>();

                    checkRead.Add(serverSocket);
                    checkError.Add(serverSocket);

                    foreach (Socket s in tcpKlijenti)
                    {
                        checkRead.Add(s);
                        checkError.Add(s);
                    }

                    // dodavanje UDP soketa u multiplexing listu
                    checkRead.Add(udpSocket);
                    checkError.Add(udpSocket);

                    // 1 sek timeout -> manja potrosnja CPU
                    Socket.Select(checkRead, null, checkError, 1000 * 1000);

                    foreach (Socket s in checkRead)
                    {
                        if (s == serverSocket)
                        {
                            // nova TCP konekcija
                            Socket client = serverSocket.Accept();
                            client.Blocking = false;
                            tcpKlijenti.Add(client);
                            Console.WriteLine($"Nova TCP konekcija: {client.RemoteEndPoint}");
                        }
                        else if (s == udpSocket)
                        {
                            // UDP poruka – tok igre
                            byte[] prijemniBafer = new byte[1024];
                            EndPoint igracEP = new IPEndPoint(IPAddress.Any, 0);
                            int brBajta = udpSocket.ReceiveFrom(prijemniBafer, ref igracEP);
                            string pokusaj = Encoding.UTF8.GetString(prijemniBafer, 0, brBajta).Trim();

                            if (!igraPocela) continue;

                            IgracInfo igracKojiJePoslao = igraci.Find(i => i.UDPEndPoint != null && i.UDPEndPoint.Equals(igracEP));
                            if (igracKojiJePoslao == null) continue;

                            // Ako je igrac ispao ili je vec pobedio: posalji mu obavestenje da ne moze vise da igra
                            if (igracKojiJePoslao.PreostaleGreske <= 0)
                            {
                                string msgOut = "[GAME] Nemate vise dozvoljenih gresaka. Ne mozete vise pogadjati.";
                                byte[] outBytes = Encoding.UTF8.GetBytes(msgOut);
                                try { udpSocket.SendTo(outBytes, igracKojiJePoslao.UDPEndPoint); } catch { }
                                continue;
                            }
                            if (igracKojiJePoslao.PogodioCeluRec)
                            {
                                string msgWin = "[GAME] Vec ste pogodili celu rec. Sacekajte kraj igre.";
                                byte[] winBytes = Encoding.UTF8.GetBytes(msgWin);
                                try { udpSocket.SendTo(winBytes, igracKojiJePoslao.UDPEndPoint); } catch { }
                                continue;
                            }

                            string infoLine = null;

                            Console.WriteLine($"Pokusaj igraca {igracKojiJePoslao.Igrac.KorisnickoIme} = {pokusaj}");

                            if (string.IsNullOrWhiteSpace(pokusaj))
                            {
                                infoLine = "[ERROR] Prazan unos je ignorisan.";
                            }
                            else if (pokusaj.Length == 1)
                            {
                                // slovo
                                char slovoRaw = pokusaj[0];
                                if (!char.IsLetter(slovoRaw))
                                {
                                    infoLine = "[ERROR] Unesite jedno slovo (A-Z).";
                                }
                                else
                                {
                                    char slovo = char.ToUpperInvariant(slovoRaw);
                                    if (probanaSlova.Contains(slovo))
                                    {
                                        infoLine = $"[ERROR] Slovo '{slovo}' je vec pokusano.";
                                    }
                                    else
                                    {
                                        probanaSlova.Add(slovo);
                                        bool pogodjeno = false;

                                        for (int i = 0; i < trazenaRec.Length; i++)
                                        {
                                            if (char.ToUpperInvariant(trazenaRec[i]) == slovo)
                                            {
                                                if (maska[i] == '_')
                                                {
                                                    maska[i] = trazenaRec[i];
                                                    igracKojiJePoslao.BrojPogodjenihSlova++;
                                                }
                                                pogodjeno = true;
                                            }
                                        }

                                        if (!pogodjeno)
                                        {
                                            igracKojiJePoslao.PreostaleGreske--;
                                            infoLine = $"[ERROR] Slovo '{slovo}' nije u reci. -1 greska.";
                                        }
                                        else
                                        {
                                            infoLine = $"[INFO] Slovo '{slovo}' je pogodjeno.";
                                        }
                                    }
                                }
                            }
                            else
                            {
                                // pokusaj cele reci
                                if (pokusaj.Length != trazenaRec.Length)
                                {
                                    igracKojiJePoslao.PreostaleGreske--; // strogo: pogresna duzina -> pogresan pokusaj
                                    infoLine = $"[ERROR] Neispravna duzina reci (ocekivano {trazenaRec.Length}). -1 greska.";
                                }
                                else
                                {
                                    if (string.Equals(pokusaj, trazenaRec, StringComparison.OrdinalIgnoreCase))
                                    {
                                        maska = trazenaRec.ToCharArray();
                                        igracKojiJePoslao.PogodioCeluRec = true;
                                        infoLine = $"[GAME] Igrac {igracKojiJePoslao.Igrac.KorisnickoIme} je pogodio celu rec!";
                                    }
                                    else
                                    {
                                        igracKojiJePoslao.PreostaleGreske--;
                                        infoLine = "[ERROR] Pogresna rec. -1 greska.";
                                    }
                                }
                            }

                            // slanje broadcast stanja svim igracima preko UDP-a
                            string maskiranaRec = new string(maska);
                            string stanje = FormirajStanjeIgre(maskiranaRec, igraci, infoLine);

                            // server-side prikaz liste povezanih (aktivni/pasivni)
                            // PrintConnectedLists(igraci, posmatraci); // uklonjeno sa svakog poteza da ne spammuje konzolu

                            byte[] stanjeBytes = Encoding.UTF8.GetBytes(stanje);
                            foreach (var ig in igraci)
                            {
                                if (ig.UDPEndPoint != null)
                                {
                                    udpSocket.SendTo(stanjeBytes, ig.UDPEndPoint);
                                }
                            }

                            // slanje stanja posmatracima preko TCP-a (kao objekat)
                            GameState gs = new GameState { StanjeTekst = stanje };
                            byte[] stanjeObj = SerializeObject(gs);
                            foreach (var pos in posmatraci)
                            {
                                try
                                {
                                    pos.TcpSocket.Send(stanjeObj);
                                }
                                catch { }
                            }

                            // proveri kraj igre
                            if (GameFinished(trazenaRec, maska, igraci, out IgracInfo pobednik))
                            {
                                PosaljiKrajIgre(igraci, posmatraci, trazenaRec, pobednik);
                                igraPocela = false;
                            }
                        }
                        else
                        {
                            // neki TCP klijent je poslao nesto
                            int brBajta = 0;
                            try
                            {
                                brBajta = s.Receive(buffer);
                            }
                            catch (SocketException)
                            {
                                brBajta = 0;
                            }

                            if (brBajta == 0)
                            {
                                Console.WriteLine("Klijent je prekinuo komunikaciju");

                                // ukloni iz liste igraca/posmatraca ako postoji
                                IgracInfo igracZaUklanjanje = igraci.Find(i => i.TcpSocket == s);
                                if (igracZaUklanjanje != null)
                                {
                                    igraci.Remove(igracZaUklanjanje);
                                    Console.WriteLine($"Igrac {igracZaUklanjanje.Igrac.KorisnickoIme} uklonjen iz igre.");
                                }

                                PosmatracInfo posmatracZaUklanjanje = posmatraci.Find(p => p.TcpSocket == s);
                                if (posmatracZaUklanjanje != null)
                                {
                                    posmatraci.Remove(posmatracZaUklanjanje);
                                    Console.WriteLine($"Posmatrac {posmatracZaUklanjanje.Igrac.KorisnickoIme} uklonjen.");
                                }

                                s.Close();
                                tcpKlijenti.Remove(s);
                                continue;
                            }

                            try
                            {
                                object obj = null;
                                if (!NetCodec.TryDeserialize(buffer, brBajta, out obj))
                                {
                                    // ako stigne tekst (da server ne pukne)
                                    string msg = Encoding.UTF8.GetString(buffer, 0, brBajta);
                                    Console.WriteLine(msg);
                                    continue;
                                }

                                if (obj is Igrac igracPrijava)
                                {
                                    // validacija korisnickog imena: jedinstveno za igrace+posmatrace
                                    string uname = (igracPrijava.KorisnickoIme ?? "").Trim();
                                    bool unamePostoji = false;
                                    if (uname.Length > 0)
                                    {
                                        unamePostoji = igraci.Exists(i => string.Equals(i.Igrac.KorisnickoIme, uname, StringComparison.OrdinalIgnoreCase))
                                            || posmatraci.Exists(p => string.Equals(p.Igrac.KorisnickoIme, uname, StringComparison.OrdinalIgnoreCase));
                                    }

                                    if (unamePostoji)
                                    {
                                        SendTcpResponse(s, false, "[ERROR] Korisnicko ime je vec zauzeto. Izaberite drugo.");
                                        continue;
                                    }

                                    if (igracPrijava.Tip == TipPrijave.Igrac)
                                    {
                                        EndPoint proposedUdp = null;
                                        try
                                        {
                                            proposedUdp = new IPEndPoint(IPAddress.Parse(igracPrijava.IPAdresa), igracPrijava.UDPPort);
                                        }
                                        catch
                                        {
                                            SendTcpResponse(s, false, "[ERROR] Neispravna IP adresa/UDP port u prijavi.");
                                            continue;
                                        }

                                        // odbij duplikat UDP endpoint-a
                                        bool postoji = igraci.Exists(i => i.UDPEndPoint != null && i.UDPEndPoint.Equals(proposedUdp));
                                        if (postoji)
                                        {
                                            SendTcpResponse(s, false, "[ERROR] UDP port je vec zauzet (duplikat). Pokrenite klijenta ponovo.");
                                            Console.WriteLine($"Odbijena prijava igraca {igracPrijava.KorisnickoIme}: duplikat UDP {proposedUdp}");
                                            continue;
                                        }

                                        IgracInfo info = new IgracInfo
                                        {
                                            Igrac = igracPrijava,
                                            TcpSocket = s,
                                            UDPEndPoint = proposedUdp,
                                            PreostaleGreske = 5,
                                            BrojPogodjenihSlova = 0,
                                            PogodioCeluRec = false,
                                            SupportPoeni = 0
                                        };
                                        igraci.Add(info);

                                        // potvrda da je igrac registrovan (ali igra mozda jos nije pocela)
                                        SendTcpResponse(s, true, "[INFO] Prijava uspesna. Cekamo ostale igrace...");

                                        Console.WriteLine($"Prijavljen igrac {igracPrijava.KorisnickoIme} ({igracPrijava.IPAdresa}:{igracPrijava.UDPPort})");

                                        // server-side prikaz liste povezanih (aktivni/pasivni)
                                        PrintConnectedLists(igraci, posmatraci);

                                        // pocetak igre kada se prijavi maxIgraca
                                        if (!igraPocela && igraci.Count == maxIgraca)
                                        {
                                            // reset state za novu partiju
                                            foreach (var ig in igraci)
                                            {
                                                ig.PreostaleGreske = 5;
                                                ig.BrojPogodjenihSlova = 0;
                                                ig.PogodioCeluRec = false;
                                                ig.SupportPoeni = 0;
                                            }

                                            trazenaRec = IzaberiNovuRec(recnik, rnd, poslednjaRec);
                                            poslednjaRec = trazenaRec;

                                            maska = new string('_', trazenaRec.Length).ToCharArray();
                                            probanaSlova.Clear();

                                            trenutnaIgra = new Igra
                                            {
                                                ImePrvog = igraci[0].Igrac.Ime,
                                                ImeDrugog = igraci.Count > 1 ? igraci[1].Igrac.Ime : "",
                                                DuzinaReci = trazenaRec.Length,
                                                MaxGresaka = 5,
                                                TrajanjeSekunde = 120,
                                                UdpPortServera = udpPortServera
                                            };

                                            igraPocela = true;

                                            foreach (var ig in igraci)
                                            {
                                                // po specifikaciji: igrac dobija start parametre direktno iz klase Igra
                                                byte[] data = SerializeObject(trenutnaIgra);
                                                ig.TcpSocket.Send(data);
                                            }

                                            // posalji inicijalno stanje preko UDP-a (da igraci vide masku odmah)
                                            string initState = FormirajStanjeIgre(new string(maska), igraci, "[GAME] Igra je pocela.");
                                            byte[] initBytes = Encoding.UTF8.GetBytes(initState);
                                            foreach (var ig in igraci)
                                            {
                                                try { udpSocket.SendTo(initBytes, ig.UDPEndPoint); } catch { }
                                            }

                                            Console.WriteLine("Igra je pocela, rec je zadana i poslati su parametri svim igracima.");
                                        }
                                        // (nema vise else grane sa SendTcpResponse(..., null))
                                    }
                                    else
                                    {
                                        // posmatrac
                                        PosmatracInfo pos = new PosmatracInfo
                                        {
                                            Igrac = igracPrijava,
                                            TcpSocket = s,
                                            PreostaliSupportPoeni = 5
                                        };
                                        posmatraci.Add(pos);
                                        Console.WriteLine($"Prijavljen posmatrac {igracPrijava.KorisnickoIme}");

                                        // server-side prikaz liste povezanih (aktivni/pasivni)
                                        PrintConnectedLists(igraci, posmatraci);

                                        SendTcpResponse(s, true, "[INFO] Posmatrac uspesno prijavljen.");
                                    }
                                }
                                else if (obj is SupportCommand cmd)
                                {
                                    ObradiSupportKomandu(cmd, igraci, posmatraci, s);

                                    // server-side prikaz liste povezanih ( aktivni/pasivni)
                                    PrintConnectedLists(igraci, posmatraci);
                                }
                            }
                            catch
                            {
                                string tekst = Encoding.UTF8.GetString(buffer, 0, brBajta);
                                Console.WriteLine($"Primljena tekstualna poruka preko TCP-a: {tekst}");
                            }
                        }

                        if (Console.KeyAvailable)
                        {
                            if (Console.ReadKey().Key == ConsoleKey.Escape)
                            {
                                goto kraj;
                            }
                        }
                    }

                    // mali delay da ne vrti bespotrebno kad nema dogadjaja
                    Thread.Sleep(10);
                }

            kraj:
                ;
            }
            catch (SocketException ex)
            {
                Console.WriteLine($"Doslo je do greske {ex}");
            }

            foreach (Socket s in tcpKlijenti)
            {
                try
                {
                    s.Send(Encoding.UTF8.GetBytes("Server je zavrsio sa radom"));
                }
                catch { }
                s.Close();
            }

            Console.WriteLine("Server zavrsava sa radom");
            Console.ReadKey();
            serverSocket.Close();
        }

        static byte[] SerializeObject(object obj)
        {
            return NetCodec.Serialize(obj);
        }

        static void SendTcpResponse(Socket s, bool ok, string poruka)
        {
            TcpResponse resp = new TcpResponse
            {
                Ok = ok,
                Poruka = poruka
            };

            byte[] data = SerializeObject(resp);
            try { s.Send(data); } catch { }
        }

        static string FormirajStanjeIgre(string maska, List<IgracInfo> igraci, string infoLine)
        {
            StringBuilder sb = new StringBuilder();
            sb.AppendLine("[GAME]");
            sb.AppendLine($"REC: {maska}");

            // probana slova (globalno) - sortirano radi lepseg prikaza
            if (probanaSlovaStatic != null && probanaSlovaStatic.Count > 0)
            {
                List<char> list = new List<char>(probanaSlovaStatic);
                list.Sort();

                sb.Append("PROBANA SLOVA: ");
                for (int i = 0; i < list.Count; i++)
                {
                    if (i > 0) sb.Append(", ");
                    sb.Append(list[i]);
                }
                sb.AppendLine();
            }
            else
            {
                sb.AppendLine("PROBANA SLOVA: (nema)");
            }

            if (!string.IsNullOrWhiteSpace(infoLine))
            {
                sb.AppendLine(infoLine);
            }

            sb.AppendLine("[PLAYERS]");
            for (int i = 0; i < igraci.Count; i++)
            {
                var ig = igraci[i];
                sb.AppendLine($"{i + 1}. {ig.Igrac.KorisnickoIme} | preostale_greske={ig.PreostaleGreske} | tacna_slova={ig.BrojPogodjenihSlova} | support={ig.SupportPoeni}");
            }
            return sb.ToString();
        }

        static bool GameFinished(string rec, char[] maska, List<IgracInfo> igraci, out IgracInfo pobednik)
        {
            pobednik = null;
            bool sviPali = true;

            foreach (var ig in igraci)
            {
                if (ig.PreostaleGreske > 0)
                {
                    sviPali = false;
                }
                if (ig.PogodioCeluRec)
                {
                    pobednik = ig;
                    return true;
                }
            }

            if (new string(maska).Equals(rec, StringComparison.OrdinalIgnoreCase))
            {
                pobednik = NadjiNajboljeg(igraci);
                return true;
            }

            if (sviPali)
            {
                pobednik = NadjiNajboljeg(igraci);
                return true;
            }

            return false;
        }

        static IgracInfo NadjiNajboljeg(List<IgracInfo> igraci)
        {
            IgracInfo best = null;
            int maxScore = int.MinValue;

            foreach (var ig in igraci)
            {
                // strogo po tekstu: pobednik po vecem broju pogodjenih slova
                int score = ig.BrojPogodjenihSlova;
                if (score > maxScore)
                {
                    maxScore = score;
                    best = ig;
                }
            }

            return best;
        }

        static void PosaljiKrajIgre(List<IgracInfo> igraci, List<PosmatracInfo> posmatraci, string rec, IgracInfo pobednik)
        {
            StringBuilder sb = new StringBuilder();
            sb.AppendLine("===== KRAJ IGRE =====");
            sb.AppendLine($"Originalna rec: {rec}");

            if (pobednik != null)
            {
                sb.AppendLine($"Pobednik: {pobednik.Igrac.KorisnickoIme}");
            }
            else
            {
                sb.AppendLine("Nema pobednika.");
            }

            sb.AppendLine("Tabela bodova: (tacna slova | pogodjena rec | greske | bodovi podrske)");
            foreach (var ig in igraci)
            {
                sb.AppendLine($"{ig.Igrac.KorisnickoIme}: {ig.BrojPogodjenihSlova} | {ig.PogodioCeluRec} | {5 - ig.PreostaleGreske} | {ig.SupportPoeni}");
            }

            byte[] msg = Encoding.UTF8.GetBytes(sb.ToString());

            foreach (var ig in igraci)
            {
                try { ig.TcpSocket.Send(msg); } catch { }
            }

            foreach (var p in posmatraci)
            {
                try { p.TcpSocket.Send(msg); } catch { }
            }
        }

        static void ObradiSupportKomandu(SupportCommand cmd, List<IgracInfo> igraci, List<PosmatracInfo> posmatraci, Socket posmatracSocket)
        {
            PosmatracInfo pos = posmatraci.Find(p => p.TcpSocket == posmatracSocket);
            if (pos == null) return;

            if (pos.PreostaliSupportPoeni <= 0) return;
            if (cmd.RedniBrojIgraca < 1 || cmd.RedniBrojIgraca > igraci.Count) return;

            IgracInfo target = igraci[cmd.RedniBrojIgraca - 1];

            target.SupportPoeni++;
            pos.PreostaliSupportPoeni--;

            string info = $"[INFO] Posmatrac {pos.Igrac.KorisnickoIme} je dao bod podrske igracu {target.Igrac.KorisnickoIme}. Preostalo posmatracu: {pos.PreostaliSupportPoeni}";
            byte[] msg = Encoding.UTF8.GetBytes(info);

            foreach (var ig in igraci)
            {
                try { ig.TcpSocket.Send(msg); } catch { }
            }

            foreach (var p in posmatraci)
            {
                try { p.TcpSocket.Send(msg); } catch { }
            }
        }

        static void PrintConnectedLists(List<IgracInfo> igraci, List<PosmatracInfo> posmatraci)
        {
            Console.WriteLine("\n=== LISTA POVEZANIH (SERVER) ===");
            Console.WriteLine("AKTIVNI IGRACI:");
            if (igraci.Count == 0)
            {
                Console.WriteLine("  (nema)");
            }
            else
            {
                for (int i = 0; i < igraci.Count; i++)
                {
                    var ig = igraci[i];
                    string udp = ig.UDPEndPoint != null ? ig.UDPEndPoint.ToString() : "(UDP nije postavljen)";
                    Console.WriteLine($"  {i + 1}. {ig.Igrac.KorisnickoIme} | UDP={udp} | greske={ig.PreostaleGreske} | tacna_slova={ig.BrojPogodjenihSlova} | support={ig.SupportPoeni}");
                }
            }

            Console.WriteLine("PASIVNI POSMATRACI:");
            if (posmatraci.Count == 0)
            {
                Console.WriteLine("  (nema)");
            }
            else
            {
                for (int i = 0; i < posmatraci.Count; i++)
                {
                    var p = posmatraci[i];
                    Console.WriteLine($"  {i + 1}. {p.Igrac.KorisnickoIme} | preostali_support={p.PreostaliSupportPoeni}");
                }
            }
            Console.WriteLine("===============================\n");
        }

        private static int ProcessIdSeed()
        {
            try
            {
                return System.Diagnostics.Process.GetCurrentProcess().Id;
            }
            catch
            {
                return 0;
            }
        }

        private static string IzaberiNovuRec(string[] recnik, Random rnd, string poslednjaRec)
        {
            if (recnik == null || recnik.Length == 0)
                return "mreza";

            // ako ima samo jedna rec, nema smisla izbegavati ponavljanje
            if (recnik.Length == 1)
                return recnik[0];

            string izabrana;
            int tries = 0;
            do
            {
                izabrana = recnik[rnd.Next(recnik.Length)];
                tries++;
            } while (string.Equals(izabrana, poslednjaRec, StringComparison.OrdinalIgnoreCase) && tries < 10);

            return izabrana;
        }
    }

    public class IgracInfo
    {
        public Igrac Igrac { get; set; }
        public Socket TcpSocket { get; set; }
        public EndPoint UDPEndPoint { get; set; }

        public int PreostaleGreske { get; set; }
        public int BrojPogodjenihSlova { get; set; }
        public bool PogodioCeluRec { get; set; }
        public int SupportPoeni { get; set; }
    }

    public class PosmatracInfo
    {
        public Igrac Igrac { get; set; }
        public Socket TcpSocket { get; set; }
        public int PreostaliSupportPoeni { get; set; }
    }
}

