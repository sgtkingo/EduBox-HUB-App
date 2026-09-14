using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace NewGUI
{
    public class Komponenty
    {
        public int Id { get; set; }
        public string Nazev { get; set; }
        public string Alias { get; set; }
        public string Znaceni { get; set; }
        public string Request_INIT { get; set; }
        public string Request_UPDATE { get; set; }
        public string Request_CONNECT { get; set; }
        public string Request_DISCONNECT { get; set; }
        public string Request_RESET { get; set; }
        public string Response { get; set; }
        public string PIN1 { get; set; }   // např. "Trig" nebo "Data pin"
        public string PIN2 { get; set; }   // např. "Echo" (může být null/"" pokud není druhý pin)
        public string PIN3 { get; set; }   // SPEŠL AKTUÁTOR
        public string PIN4 { get; set; }   // SPEŠL AKTUÁTOR
        public Dictionary<string, string> Keywords_values { get; set; }
        public Dictionary<string, string> Keywords_configs { get; set; }
        public string Request_CONFIG { get; set; }
        public string Request_CONTROL { get; set; }
        public string Config1 { get; set; }
        public string Config2 { get; set; }
        public string Config3 { get; set; }
    }
}
