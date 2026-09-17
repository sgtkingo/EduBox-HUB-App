# EduBox HUB App

![Logo EduBox HUB App](assets/logo.svg)

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

Komunikace používá **VSCP API 1.5**, včetně odpovědí na PING zařízení i před
INIT. `SerialController.PingAsync(timeoutMs)` umožňuje ověřit dostupnost
protistrany; vrátí `true` při správné odpovědi a `false` při timeoutu nebo
uzavření spojení. Pravidelný heartbeat se automaticky nespouští.

Aplikace je vytvořena v C# jako Windows Forms projekt pro **.NET Framework
4.7.2**.

1. Otevřete `NewGUI.sln` ve Visual Studiu.
2. Obnovte NuGet balíčky.
3. Sestavte a spusťte projekt `NewGUI`.

Uživatelská a komponentová dokumentace je dostupná v PDF souborech v kořeni
repozitáře a ve složce `NewGUI/Manualy/`.

## GitHub build a release

Workflow `Build App` sestavuje Release pro Windows x64 při pushi do `main`,
pull requestu do `main` a při ručním spuštění. ZIP je dostupný mezi artefakty
daného běhu GitHub Actions. Aplikace vyžaduje .NET Framework 4.7.2 nebo novější
kompatibilní verzi; celý ZIP rozbalte a spusťte `Spustit.cmd`.

Automatické vydání se řídí prefixem zprávy posledního commitu pushnutého do
`main`, stejně jako v EduBox HUB Panel:

| Prefix | Změna verze (příklad z `1.2.3.4`) |
| --- | --- |
| `(major)` | `2.0.0.0` |
| `(minor)` | `1.3.0.0` |
| `(patch)` | `1.2.4.0` |
| `(build)` | `1.2.3.5` |

Například commit `(patch) Oprava sériového připojení` spustí vydání.
Bez prefixu proběhne pouze build. `Release Auto` lze také spustit ručně
z větve `main` a vybrat část verze k navýšení.

Před vydáním upravte `RELEASE_NOTES.md`: celý obsah souboru bude popisem
GitHub Release. Výchozí verzi určuje `VERSION`; workflow synchronizuje
`AssemblyVersion` a `AssemblyFileVersion`, sestaví aplikaci a po úspěchu
zapíše commit `chore(release): ...`, tag `vX.Y.Z.B` a release s ZIP přílohou.
Verze se zveřejní až po úspěšném sestavení. Při souběžné změně větve `main`
workflow odmítne zápis; spusťte release znovu nad aktuálním stavem.

Release používá automatický `GITHUB_TOKEN`, další secret není potřeba.
Pravidla repozitáře musí povolit GitHub Actions zápis do `main` a vytvoření
tagu. Pokud je přímý zápis zakázán ochranou větve, krok publikování selže.
