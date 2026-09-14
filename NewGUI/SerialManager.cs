using System;
using System.IO.Ports;
using System.Text;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace NewGUI
{
    // Třída SerialManager je „správce“ fyzické komunikace po sériovém portu (COM port).
    // Postará se o otevření, čtení, zápis a rozdělování dat na celé řádky.
    public sealed class SerialManager
    {
        private static readonly SerialManager _instance = new SerialManager();  //Singleton pattern – jen jedna jediná instance v celé aplikaci.
        public static SerialManager Instance => _instance; // Přístup k té jediné instanci

        private readonly SerialPort _port = new SerialPort();  // Objekt SerialPort z .NET knihovny – skutečná komunikace s COM portem.
        private readonly object _ioLock = new object(); // Zámek pro bezpečný přístup při čtení/zápisu z více vláken.
        private SerialDataReceivedEventHandler _attachedHandler; // Pokud je připojen externí handler (uživatelem), uloží se sem.

        // internal line buffering
        private readonly StringBuilder _lineBuffer = new StringBuilder(); //Buffer pro neúplné textové řádky, které ještě nemají '\n'
        private readonly object _bufLock = new object(); // zámek pro přístup k bufferu
        private readonly SerialDataReceivedEventHandler _internalDataReceivedHandler;  // Interní handler, který zpracovává data (používáme vlastní, ne přímo port.DataReceived)
        private bool _internalHandlerAttached = false; // hlídá, že se nepřipojí víckrát

        public event EventHandler<LinesEventArgs> LinesReceived; // Událost, kterou vyšleme, když máme k dispozici celé řádky textu

        // Simulator mode support
        private bool _isSimulated = false;
        private bool _simulatedOpen = false;
        private string _simulatedPortName = "COM22";

        // Konstruktor
        public SerialManager()
        {
            _internalDataReceivedHandler = InternalDataReceived; // Uložíme si odkaz na metodu, která bude zpracovávat příchozí data
            _port.ReadTimeout = 500;  // Nastaví se základní časové limity čtení/zápisu (v milisekundách)
            _port.WriteTimeout = 500;
            _port.NewLine = "\r\n"; // Výchozí znak pro konec řádku
        }

        public bool IsOpen => _isSimulated ? _simulatedOpen : _port.IsOpen; //Jen pro čtení: zda je port otevřený
        public string PortName => _isSimulated ? (_simulatedPortName ?? "COM22") : _port.PortName; //Aktuální jméno portu
        public int BaudRate => _isSimulated ? 115200 : _port.BaudRate; // Aktuální rychlost
        public bool IsSimulated => _isSimulated;

        public void ConfigurePort( // Nastaví parametry portu (název, rychlost, paritu, stop bity, handshake…)
            string portName,
            int baudRate = 115200,
            Parity parity = Parity.None,
            int dataBits = 8,
            StopBits stopBits = StopBits.One,
            Handshake handshake = Handshake.None,
            string newLine = "\n")
        {
            // Změnu nelze provést, pokud je port otevřený
            if (IsOpen) throw new InvalidOperationException("Nejdřív zavři port (Close), pak měň konfiguraci.");

            _isSimulated = string.IsNullOrWhiteSpace(portName) ||
                           string.Equals(portName, "COM22", StringComparison.OrdinalIgnoreCase) ||
                           string.Equals(portName, "SIMULATOR", StringComparison.OrdinalIgnoreCase) ||
                           portName.IndexOf("SIMULAT", StringComparison.OrdinalIgnoreCase) >= 0 ||
                           string.Equals(portName, "SIMULACE", StringComparison.OrdinalIgnoreCase) ||
                           string.Equals(portName, "VIRTUAL", StringComparison.OrdinalIgnoreCase);

            if (!_isSimulated)
            {
                // Uložení všech nastavení do objektu SerialPort
                _port.PortName = portName;
                _port.BaudRate = baudRate;
                _port.Parity = parity;
                _port.DataBits = dataBits;
                _port.StopBits = stopBits;
                _port.Handshake = handshake;
                _port.NewLine = newLine;
            }
            else
            {
                _simulatedPortName = string.IsNullOrWhiteSpace(portName) ? "COM22" : portName;
                _port.NewLine = newLine;
            }
        }

        // Otevře port a připojí náš interní handler (pokud ještě není připojen)
        public void Open()
        {
            if (_isSimulated)
            {
                _simulatedOpen = true;
                VirtualDeviceSimulator.Instance.ResetState();
                return;
            }

            if (!IsOpen) _port.Open(); // Fyzicky otevře COM port

            // ensure our internal handler is attached once
            if (!_internalHandlerAttached) // Připojíme interní handler jen jednou
            {
                _port.DataReceived += _internalDataReceivedHandler; // Když přijdou data, volá se naše metoda
                _internalHandlerAttached = true;
            }
        }

        public void Close() //Zavře port, odpojí všechny handlery a uklidí
        {
            if (_isSimulated)
            {
                _simulatedOpen = false;
                return;
            }

            try
            {
                DetachReceiver(); // Odpojíme případného externího „receivera“
                if (_internalHandlerAttached) // Odpojíme i náš interní handler
                {
                    try { _port.DataReceived -= _internalDataReceivedHandler; } catch { }
                    _internalHandlerAttached = false;
                }

                if (IsOpen) _port.Close(); // Zavřeme samotný port
            }
            catch { /* log/ignore */ }
        }

        // Připojí externího posluchače přímo na DataReceived (nahrazuje interní)
        public void AttachExclusiveReceiver(SerialDataReceivedEventHandler handler)
        {
            DetachReceiver();  // Nejdřív se odpojí všechno předchozí

            if (handler != null) // A pokud máme nový handler, připojíme ho
            {
                _port.DataReceived += handler;
                _attachedHandler = handler;
            }
        }

        // Odpojí jakéhokoliv dříve připojeného externího posluchače
        public void DetachReceiver() 
        {
            if (_attachedHandler != null)
            {
                try { _port.DataReceived -= _attachedHandler; } catch { }
                _attachedHandler = null; // Vyčistíme referenci
            }
        }

        // Pošle řádek textu (automaticky přidá konec řádku)
        public void WriteLine(string line)
        {
            if (!IsOpen) throw new InvalidOperationException("Port není otevřen.");

            if (_isSimulated)
            {
                Task.Run(async () =>
                {
                    await Task.Delay(15);
                    string response = VirtualDeviceSimulator.Instance.ProcessCommand(line);
                    if (!string.IsNullOrEmpty(response))
                    {
                        LinesReceived?.Invoke(this, new LinesEventArgs(new[] { response }));
                    }
                });
                return;
            }

            lock (_ioLock) _port.WriteLine(line);  // Zámek pro bezpečné paralelní použití
        }

        
        // Pošle text beze změny (bez přidání konce řádku)
        public void Write(string text)
        {
            if (!IsOpen) throw new InvalidOperationException("Port není otevřen.");
            lock (_ioLock) _port.Write(text); // Zámek pro bezpečné paralelní použití
        }

        // Vyčistí vstupní a výstupní buffery portu
        public void DiscardInOut()
        {
            try
            {
                if (!_port.IsOpen) return;
                _port.DiscardInBuffer(); // Zruší nevyčtená data
                _port.DiscardOutBuffer(); // Zruší neodeslaná data
            }
            catch { /* ignore */ }
        }

        // Interní metoda, která se volá, když přijdou nová data z portu
        private void InternalDataReceived(object sender, SerialDataReceivedEventArgs e)
        {
            try
            {
                string data = _port.ReadExisting(); // Přečte všechno, co je právě dostupné
                if (string.IsNullOrEmpty(data)) return;

                List<string> completeLines = null; // Seznam celých řádků, které se detekovali


                // Uzamkne se buffer, protože může být přístupný i z jiného vlákna
                lock (_bufLock)
                {
                    _lineBuffer.Append(data); // Přidané nově přečtená data k předchozímu zbytku
                    var buf = _lineBuffer.ToString();
                    // Najde se poslední znak '\n' (znamená konec řádku)
                    int lastNewline = buf.LastIndexOf('\n');
                    if (lastNewline >= 0)
                    {
                        // Všechno do posledního '\n' je kompletní text
                        string complete = buf.Substring(0, lastNewline + 1);
                        // Co zbylo po posledním '\n', zůstává v bufferu pro příště
                        string remaining = buf.Substring(lastNewline + 1);
                        _lineBuffer.Clear();
                        if (!string.IsNullOrEmpty(remaining)) _lineBuffer.Append(remaining);

                        // Vyčistí se a rozdělí se text na jednotlivé řádky
                        complete = complete.Replace("\r", "");
                        var rawLines = complete.Split(new[] { '\n' }, StringSplitOptions.RemoveEmptyEntries);
                        // Připravíme list řádků
                        completeLines = new List<string>(rawLines.Length);
                        foreach (var raw in rawLines)
                        {
                            if (string.IsNullOrWhiteSpace(raw)) continue; // prázdné přeskočíme
                            completeLines.Add(raw.Trim()); // přidáme očištěný řádek
                        }
                    }
                }

                // Pokud se poskládali nějaké celé řádky, vyšle se událost
                if (completeLines != null && completeLines.Count > 0)
                {
                    try
                    {
                        // Vyvolá se událost LinesReceived – předají se všechny celé řádky
                        LinesReceived?.Invoke(this, new LinesEventArgs(completeLines.ToArray()));
                    }
                    catch { /* subscriber exceptions should not crash serial thread */ }
                    // Když si posluchač udělá chybu, neshodí nám to celé vlákno
                }
            }
            catch { /* ignore read errors */ }
            // Pokud dojde k chybě při čtení, prostě ji ignorujeme (port zůstává živý)
        }
    }

    // Pomocná třída pro předávání pole řádků jako argument události
    public class LinesEventArgs : EventArgs
    {
        public string[] Lines { get; } // Pole všech kompletních řádků, co dorazily
        public LinesEventArgs(string[] lines) { Lines = lines ?? Array.Empty<string>(); } // Uložíme řádky, pokud null, použijeme prázdné pole
    }
}
