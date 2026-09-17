using System;
using System.Collections.Generic;

namespace NewGUI
{
    // Tøída SerialController obaluje SerialManager (pro fyzickou komunikaci)
    // a SerialParser (pro zpracování textových dat). Slouží jako prostøedník.
    public class SerialController : IDisposable // obal kolem SerialManager a SerialParser
    {
        private readonly SerialParser _parser = new SerialParser(); // Vytvoøený parser, který bude rozpoznávat pøijaté øádky
        private bool _attached = false; // Oznaèuje, jestli už jsme pøipojili handler k události LinesReceived v SerialManageru


        // Události, které jsou vystavené smìrem ven (pro uživatele této tøídy)
        public event EventHandler<InitEventArgs> InitReceived; // Když parser zjistí init zprávu
        public event EventHandler<DataFrameEventArgs> DataFrameReceived; // Když pøijde datový rámec
        public event EventHandler<RawLineEventArgs> RawLineReceived; // Když pøijde surový øádek

        public SerialController() 
        {
            _parser.InitReceived += OnParserInitReceived;  // Když parser zjistí init
            _parser.DataFrameReceived += OnParserDataFrameReceived;  // Když parser zjistí datový rámec
            _parser.RawLineReceived += OnParserRawLineReceived;  // Když parser zahlásí surový øádek
        }

        public bool IsOpen => SerialManager.Instance.IsOpen; // Vlastnost, která øekne, jestli je port otevøený
        
        // Nastavení sériového portu (název portu, rychlost, bity, parita atd.)
        public void ConfigurePort(
            string portName,
            int baudRate = 115200,
            System.IO.Ports.Parity parity = System.IO.Ports.Parity.None,
            int dataBits = 8,
            System.IO.Ports.StopBits stopBits = System.IO.Ports.StopBits.One,
            System.IO.Ports.Handshake handshake = System.IO.Ports.Handshake.None,
            string newLine = "\n")
        {
            // Delegujeme nastavení na SerialManager (singleton instance)
            SerialManager.Instance.ConfigurePort(portName, baudRate, parity, dataBits, stopBits, handshake, newLine);
        }

        // Otevøení portu a pøipojení handleru pro pøíjem dat
        public void Open()
        {
            SerialManager.Instance.Open(); // Otevøe COM port
            AttachIfNeeded(); // Pøihlásíme se na událost LinesReceived, pokud ještì nejsme
        }

        public void Close()
        {
            DetachIfNeeded(); // Odpojíme handler z LinesReceived
            try { SerialManager.Instance.DetachReceiver(); } catch { } // Pokus o odpojení v SerialManageru (bez pádu pøi chybì)
            try { SerialManager.Instance.Close(); } catch { } // Zavøeme port (chyby ignorujeme)
        }

        public void WriteLine(string line) // Odeslání textového øádku na sériový port
        {
            SerialManager.Instance.WriteLine(line);
        }

        public System.Threading.Tasks.Task<bool> PingAsync(int timeoutMs = 500)
        {
            return SerialManager.Instance.PingAsync(timeoutMs);
        }

        private void AttachIfNeeded() // Pøipojí handler na SerialManager.Instance.LinesReceived, pokud ještì není
        {
            if (_attached) return; // Pokud už je pøipojeno, nic nedìláme
            try
            {
                // Pøihlásíme se na událost, která doruèuje nové øádky
                SerialManager.Instance.LinesReceived += OnLinesReceived;
                _attached = true; // Oznaèíme, že už jsme pøipojeni
            }
            catch { } // Chyby ignorujeme (napø. SerialManager ještì není inicializovaný)
        }

        private void DetachIfNeeded() // Odpojí handler z LinesReceived, pokud byl pøipojen
        {
            if (!_attached) return; // Pokud není pøipojeno, není co dìlat
            try
            {
                SerialManager.Instance.LinesReceived -= OnLinesReceived; // Odhlásíme se z události
            }
            catch { } // Ignorujeme pøípadné chyby
            _attached = false; // Oznaèíme, že už nejsme pøipojeni
        } 

        private void OnLinesReceived(object sender, LinesEventArgs e) // Když SerialManager nahlásí, že pøišly nové øádky
        {
            if (e?.Lines == null || e.Lines.Length == 0) return; // Ochrana proti null nebo prázdnému poli
            foreach (var l in e.Lines) // Každý øádek pošleme parseru, který se pokusí ho rozpoznat
            {
                try { _parser.ProcessLine(l); } catch { } // Parser rozhodne, jestli je to init, data frame nebo raw
            }
            // Kdyby parser spadl, ignorujeme chybu a pokraèujeme
        }

        // Tyto tøi metody pøeposílají události z parseru dál ven
        private void OnParserInitReceived(object sender, InitEventArgs e) => InitReceived?.Invoke(this, e); // Vyvoláme vlastní InitReceived událost
        private void OnParserDataFrameReceived(object sender, DataFrameEventArgs e) => DataFrameReceived?.Invoke(this, e); // Vyvoláme vlastní DataFrameReceived událost
        private void OnParserRawLineReceived(object sender, RawLineEventArgs e) => RawLineReceived?.Invoke(this, e); // Vyvoláme vlastní RawLineReceived událost

        // Implementace IDisposable – úklid pøi znièení objektu
        public void Dispose()
        {
            DetachIfNeeded(); // Odpojíme se od SerialManageru (bezpeènost)

            // Odregistrujeme všechny event handlery z parseru
            try { _parser.InitReceived -= OnParserInitReceived; } catch { } 
            try { _parser.DataFrameReceived -= OnParserDataFrameReceived; } catch { } 
            try { _parser.RawLineReceived -= OnParserRawLineReceived; } catch { } 
        }
    }
}
