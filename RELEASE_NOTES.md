# EduBox HUB App – poznámky k vydání

## Komunikace a VSCP

- Senzory, aktuátory a integrovaný simulátor používají **VSCP API 1.6** podle
  protokolové knihovny 2.2.2. Verze API je definována na jednom místě.
- Obousměrný **PING** ověřuje dostupnost protistrany i před INIT. Aplikace
  odpovídá na požadavky zařízení a umožňuje vlastní kontrolu spojení přes
  `PingAsync`; kontroluje roli protistrany, sekvenci a timeout.
- Požadavek PING obsahuje `type=PING`. Odpověď obsahuje pouze `side`, `seq`
  a `status=1`; neobsahuje `type`. Chybné, nevyžádané a opožděné odpovědi
  nepotvrzují čekající ping a PING rámce se nezpracovávají jako naměřená data.
- **BYE** ukončuje komunikační relaci bez čekání na odpověď. Automaticky se
  odesílá při zavírání portu. Přijaté BYE ukončí relaci a čekající ping,
  zastaví komunikaci v UI a vyvolá událost `PeerDisconnected`.
- Po přijetí BYE zůstává port otevřený a piny připojené; před dalšími běžnými
  příkazy je nutný nový INIT. Integrovaný simulátor používá stejné chování
  a odmítá INIT s nekompatibilní verzí API.
- Protokolové a integrační testy pokrývají komunikaci a chování simulátoru.

## Sestavení a vydávání

- GitHub Actions sestavuje aplikaci v konfiguraci **Release pro Windows x64**
  při pushi do `main`, pull requestu do `main` i ručním spuštění.
- Opravena konfigurace solution, která dříve při výběru Release sestavovala
  Debug. Sestavení x64 odpovídá přibalené nativní knihovně PDFium.
- Automatické vydání navyšuje verzi podle prefixů commitů `(major)`, `(minor)`,
  `(patch)` a `(build)`; typ navýšení lze vybrat také při ručním spuštění.
- Soubor `VERSION` určuje `AssemblyVersion` a `AssemblyFileVersion` aplikace.
  Commit s novou verzí a tag `vX.Y.Z.B` se zveřejňují po úspěšném sestavení.
- GitHub Release obsahuje ZIP balíček aplikace a používá celý obsah tohoto
  souboru jako popis vydání.

## Obsah balíčku a spuštění

ZIP obsahuje aplikaci, závislosti včetně PDFium, katalogy senzorů a aktuátorů,
obrázky a PDF dokumentaci. Rozbalte celý archiv a spusťte `Spustit.cmd`.
Zachovejte strukturu složek, kterou aplikace používá pro načítání dokumentace.

Požadavky: Windows x64 a .NET Framework 4.7.2 nebo novější kompatibilní verze.

## Kompatibilita

Protistrana musí podporovat VSCP API 1.6, zejména odpovědi na PING bez `type`
a ukončení relace pomocí BYE. Úspěšný PING potvrzuje dostupnost protistrany,
nikoli dokončený INIT nebo funkčnost připojených senzorů. Pravidelný heartbeat
ani automatické obnovení relace se nespouští.
