# Akses

**Dagli un file: ti mostra tutto quello che puoi farci.** App nativa per Windows (WinUI 3) e per
Linux (Avalonia, pacchetto `.deb` per Linux Mint, Ubuntu e Debian) di M-Tre Consulting, gratis e
software libero.

Trascini un file nella finestra, o fai clic destro in Esplora file → **Apri con Akses**, e si apre
la sua pagina: anteprima, informazioni e, sotto, tutte le possibilità.

- **Converti in**: un clic su un formato e il nuovo file compare accanto all'originale. Oltre 170
  formati letti: immagini (WEBP, HEIC, AVIF, PNG, JPG, RAW, PSD, SVG…), video (MP4, MKV, MOV,
  AVI, WEBM…), audio (MP3, WAV, FLAC, M4A, OGG, OPUS…), PDF, documenti (DOCX, ODT, Markdown,
  HTML, EPUB, RTF…), dati (JSON, YAML, XML, CSV) e sottotitoli (SRT, VTT, ASS).
- **Modifica**, secondo il tipo di file:
  - foto: ritaglia (con proporzioni 1:1, 16:9…), ridimensiona in pixel, ruota e rifletti,
    alleggerisci, togli posizione GPS e dati EXIF, crea un PDF;
  - video: taglia (tieni un pezzo o togline uno), estrai l'audio, crea una GIF, alleggerisci,
    togli l'audio;
  - audio: taglia, alleggerisci;
  - PDF: dividi in pagine, estrai pagine, ruota pagine, unisci con altri PDF e immagini,
    pagine in immagini, estrai il testo.
- Il taglio di audio e video **copia i flussi senza ricodificarli**: anche un film di due ore è
  pronto in un secondo. "Taglio preciso" ricodifica per un taglio esatto al fotogramma.
- Più file insieme: una scheda per file, e conversioni e azioni rapide possono valere per tutti
  i file dello stesso tipo.
- L'originale non viene mai toccato; i nomi nuovi non sovrascrivono mai niente
  (`foto (1).png`).
- Niente account, niente internet, niente statistiche: tutto succede sul PC.
- 5 lingue (italiano, inglese, spagnolo, francese, tedesco) dalla lingua di Windows; le altre
  in inglese (`AKSES_LANG=ja` per provarlo).

## Com'è fatto

| Cartella | Cosa c'è |
|---|---|
| `src/Akses.Core` | i motori, senza interfaccia: `FormatCatalog` (formati letti e in cosa si convertono), `Converter`, `Engines/` (immagini con Magick.NET, audio/video con ffmpeg, PDF con PDFsharp + `Windows.Data.Pdf` (su Linux `pdftoppm`) + PdfPig, documenti con pandoc, dati in C#). Compila sia per Windows sia per `net9.0` (Linux) |
| `src/Akses.App` | l'app WinUI 3: `MainWindow` (una schermata alla volta), `FileView` (la pagina del file), gli strumenti `TrimPage`, `ImagePage`, `PdfPage`, `SettingsPage`, i testi in 5 lingue in `Strings.cs` |
| `src/Akses.Linux` | l'app per Linux, in Avalonia: le stesse pagine con gli stessi nomi, rifatte senza WinUI. `Strings.cs`, `Settings.cs` e `AppLog.cs` sono quelli dell'app Windows (collegati, non copiati) |
| `tests/Akses.Core.Tests` | test xUnit: conversioni vere su file generati al volo |
| `installer/` | `build-ffmpeg.sh` (l'ffmpeg ridotto, da WSL), `fetch-tools.ps1` (pandoc per sviluppare), `build-installer.ps1`, `Akses.iss` (Inno Setup); per Linux `build-linux.ps1` e `linux/` (`build-deb.sh`, `akses.desktop`) |

Scelte da sapere:

- **Una sola istanza.** Esplora file lancia Akses una volta per ogni file selezionato: il primo
  processo apre la finestra, gli altri le passano i file su una named pipe ed escono
  (`Program.cs`).
- **Documenti → PDF** con WebView2 (il motore di Edge, già in Windows): pandoc scrive l'HTML,
  WebView2 lo stampa in PDF (`WebViewPdfPrinter.cs`). Niente LaTeX da distribuire.
- **Leggero: installer ~46 MB, installato ~185 MB.** Tre scelte:
  - **ffmpeg ridotto** (33 MB invece di 183): `installer/build-ffmpeg.sh` lo compila con tutti i
    decoder (si apre qualsiasi file) ma solo gli encoder, i contenitori e i filtri che Akses usa.
    Se `MediaEngine.cs` usa un encoder, un formato o un filtro nuovo, va aggiunto lì e ricompilato;
  - **pandoc su richiesta** (225 MB): non è nell'installer. Alla prima conversione di un documento
    Akses chiede se scaricarlo, ne controlla lo SHA-256 e lo tiene in `%LOCALAPPDATA%\Akses\tools`
    (`ToolDownloads.cs`). HTML e testo → PDF non ne hanno bisogno;
  - **trimming di .NET** in Release, solo sul runtime (`System.*`): il livello WinRT/WinUI resta
    intero (target `KeepWinRTInteropWhole` nel csproj), perché tagliato si rompe con "Interfaccia
    non supportata". Dopo ogni modifica che tocca API di Windows, provare la build installata.
- **Interfaccia costruita in codice** (tranne `MainWindow.xaml`), come `LegalDialog` in
  WinPlay: niente `.xbf` in più da copiare a mano in pubblicazione.
- **Menu del tasto destro** per utente, senza amministratore
  (`HKCU\Software\Classes\SystemFileAssociations\.ext\shell\Akses`); su Windows 11 è in
  "Mostra altre opzioni".

### La versione per Linux

Stesso motore e stessi testi, interfaccia rifatta in Avalonia (WinUI esiste solo su Windows). Le
differenze, tutte in `src/Akses.Linux`:

- **Niente ffmpeg ridotto né installer da 46 MB**: il `.deb` (~26 MB) dipende dai pacchetti della
  distribuzione, `ffmpeg`, `poppler-utils` (le pagine PDF in immagini, al posto di
  `Windows.Data.Pdf`) e `libvlc5` (l'anteprima del taglio, al posto del MediaPlayer di Windows: VLC
  disegna i fotogrammi in una bitmap della finestra, `VideoPlayer.cs`). apt li installa da solo.
- **Documenti → PDF con LibreOffice** (`LibreOfficePdfPrinter.cs`), già presente su Mint e Ubuntu:
  stampa l'HTML di pandoc e apre direttamente Word/OpenDocument/RTF, quindi per questi non serve
  pandoc. Usa un profilo suo, così funziona anche con LibreOffice aperto.
- **«Apri con Akses»** nel clic destro di Nemo (il gestore dei file di Mint): un file
  `~/.local/share/nemo/actions/akses.nemo_action`, scritto dall'app al primo avvio (un pacchetto
  non può scrivere nelle home) e tolto dall'interruttore in Impostazioni. Negli altri gestori di
  file Akses compare in «Apri con» grazie al `.desktop`.
- **Una sola finestra**: un lock esclusivo su `~/.local/share/Akses/instance.lock` decide chi è il
  primo, gli altri passano i file su una named pipe (un socket locale) come su Windows.
- **Icone disegnate come tracciati** (`Glyph.cs`): Segoe Fluent Icons non c'è.

## Compilare

Serve .NET 9 SDK, Visual Studio 2022 con il workload "Sviluppo di applicazioni desktop .NET"
(per i target AppxPackage, vedi il commento nel csproj) e, per l'installer, Inno Setup 6.

```powershell
wsl -d Arch -u root -- bash installer/build-ffmpeg.sh   # una volta: ffmpeg ridotto in tools\ffmpeg
powershell -File installer\fetch-tools.ps1              # una volta: pandoc in tools\pandoc (per i test)
dotnet test tests\Akses.Core.Tests                      # 40 test, conversioni vere
dotnet build src\Akses.App\Akses.App.csproj -r win-x64
src\Akses.App\bin\Debug\net9.0-windows10.0.19041.0\win-x64\Akses.exe
```

Installer: `powershell -File installer\build-installer.ps1` → `installer\output\Akses-Setup-<versione>.exe`.
La versione si cambia solo in `<Version>` di `src/Akses.App/Akses.App.csproj` (anche l'app Linux la
legge da lì).

Linux: `powershell -File installer\build-linux.ps1` → `installer\output\akses_<versione>_amd64.deb`.
Pubblica l'app per `linux-x64` da Windows (niente di nativo da compilare) e la impacchetta in WSL con
`dpkg-deb` (`pacman -S dpkg` su Arch). Per provarla al volo senza pacchetto, in WSL con WSLg e i
pacchetti `ffmpeg poppler vlc libice libsm`: `dotnet publish src\Akses.Linux -c Release -r linux-x64
--self-contained` e lancia `Akses` dalla cartella `publish`. Su Ubuntu la libreria di VLC si chiama
solo `libvlc.so.5`: `VideoPlayer.cs` la cerca con quel nome.

## Pubblicazione

Come WinPlay Mahogany, l'installer si scarica dalla pagina dell'app sul sito
(<https://mtre-consulting.it/progetti/akses>), preso dall'ultima release GitHub di questa repo: la
pagina offre l'`.exe` a chi arriva da Windows e il `.deb` a chi arriva da Linux, con l'altro a un
link di distanza. Le decisioni (licenza, prezzo, firma)
sono nella scheda di Akses in `m-tre-site/brand/DISTRIBUZIONE.md`.

## Licenza

© M-Tre Consulting — [mtre-consulting.it](https://mtre-consulting.it). Software libero sotto
**GNU GPL v3** (vedi [LICENSE](LICENSE)); librerie e programmi di terzi in [NOTICE.md](NOTICE.md).
Informativa sulla privacy: <https://mtre-consulting.it/privacy/akses>.
