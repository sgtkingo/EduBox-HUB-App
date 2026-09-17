# EduBox HUB App – poznámky k vydání

## Změny

- Aktualizace komunikace na VSCP API 1.5 v senzorech, aktuátorech a simulátoru.
- Podpora obousměrného PING i před INIT; kontrola role, sekvence a timeoutu.

- Automatické sestavení Windows x64 aplikace pomocí GitHub Actions.
- Automatické verzování a vydávání ZIP balíčku podle prefixu commitu.
- Verze aplikace se synchronizuje ze souboru `VERSION` do metadat sestavení.

## Obsah balíčku a spuštění

ZIP obsahuje aplikaci, závislosti včetně PDFium, katalogy senzorů a aktuátorů,
obrázky a PDF dokumentaci. Rozbalte celý archiv a spusťte `Spustit.cmd`.
Zachovejte strukturu složek, kterou aplikace používá pro načítání dokumentace.

Požadavky: Windows x64 a .NET Framework 4.7.2 nebo novější kompatibilní verze.
