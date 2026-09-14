# EduBox HUB App

**EduBox HUB App** je desktopová větev ekosystému
[EduBox HUB](https://github.com/sgtkingo/EduBox-HUB). Umožňuje ve Windows
připojovat senzory a aktuátory, zobrazovat jejich data, ovládat výstupy a
pracovat s virtuálními zařízeními přes protokol
[EduBox HUB VSCP](https://github.com/sgtkingo/EduBox-HUB-VSCP).

## Zařazení v ekosystému

```text
EduBox HUB
├── Board
├── App  ← tento repozitář
├── Panel
│   └── Firmupdater
└── VSCP
```

App spolupracuje s
[Boardem](https://github.com/sgtkingo/EduBox-HUB-Board) a dalšími zařízeními
implementujícími [VSCP](https://github.com/sgtkingo/EduBox-HUB-VSCP).
Alternativní uživatelské rozhraní poskytuje
[EduBox HUB Panel](https://github.com/sgtkingo/EduBox-HUB-Panel).

## Funkce

- sériové připojení k EduBox HUB;
- výběr a konfigurace senzorů a aktuátorů;
- vizualizace naměřených hodnot;
- ovládání aktuátorů;
- simulace virtuálních zařízení;
- integrovaná dokumentace komponent.

## Technologie a spuštění

Aplikace je vytvořena v C# jako Windows Forms projekt pro **.NET Framework
4.7.2**.

1. Otevřete `NewGUI.sln` ve Visual Studiu.
2. Obnovte NuGet balíčky.
3. Sestavte a spusťte projekt `NewGUI`.

Uživatelská a komponentová dokumentace je dostupná v PDF souborech v kořeni
repozitáře a ve složce `NewGUI/Manualy/`.
