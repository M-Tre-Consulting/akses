# Akses — note per chi ci lavora (persone e Claude)

App per Windows e Linux di M-Tre Consulting: dagli un file, ti mostra tutto quello che puoi farci.
Come è fatta: [README.md](README.md). Decisioni di distribuzione (licenza, repo, prezzo, firma, stato):
scheda «Akses» in `m-tre-site/brand/DISTRIBUZIONE.md`, la cartella accanto a questa. **Leggi la
scheda prima di cambiare licenza, release o download.** Brand, firma, privacy e 5 lingue seguono
`m-tre-site/brand/README.md`.

## Regole decise con Nicolò

- **Una schermata, niente menu.** Gli dai un file e vedi formati e modifiche possibili; uno strumento
  (taglio, ritaglio, PDF) si apre lì dentro con «← Indietro». Il tasto destro di Esplora file ha una
  voce sola, «Apri con Akses». Niente barre laterali o sottomenu.
- **Leggera.** Installer ~46 MB, installato ~185 MB: non reintrodurre pandoc nell'installer né la
  build generica di ffmpeg. Se serve un encoder/formato/filtro nuovo in `MediaEngine.cs`, aggiungilo
  in `installer/build-ffmpeg.sh` e ricompila (WSL Arch su questo PC).
- **Repo pubblica** (`M-Tre-Consulting/akses`), licenza GPL v3. WinPlay invece resta privata.
- **Testi**: italiano, inglese, spagnolo, francese, tedesco in `src/Akses.App/Strings.cs`. L'informativa
  dell'app e `/privacy/akses` del sito devono dire le stesse cose: se cambia una, cambia l'altra.
- **Due app, un motore** (25/10/2026): `src/Akses.App` (WinUI 3, Windows) e `src/Akses.Linux` (Avalonia,
  `.deb` per Mint/Ubuntu/Debian) usano lo stesso `Akses.Core` e lo stesso `Strings.cs`. Una funzione nuova
  va fatta in tutte e due; i testi che nominano Windows hanno la variante Linux (`Strings.Linux`). Su Linux
  ffmpeg, poppler e VLC sono dipendenze del pacchetto, non dentro.

## Fare una release

1. `<Version>` in `src/Akses.App/Akses.App.csproj` (solo lì).
2. `dotnet test tests/Akses.Core.Tests` (serve `tools/ffmpeg` ridotto e `tools/pandoc`, vedi README).
3. `powershell -File installer/build-installer.ps1` → `installer/output/Akses-Setup-<versione>.exe`, e
   `powershell -File installer/build-linux.ps1` → `installer/output/akses_<versione>_amd64.deb`.
4. **Prova la versione installata**, non solo quella di debug: il trimming di .NET rompe le cose
   solo lì (errore "Interfaccia non supportata"). Almeno una conversione per tipo di file, il taglio
   video e un documento → PDF. Il `.deb` va provato installato con apt su una Ubuntu/Mint vera (in WSL
   basta importare la rootfs di Ubuntu 24.04, la base di Mint 22): lì mancano cose che Arch ha.
5. Commit, push, `gh release create <versione> installer/output/Akses-Setup-<versione>.exe
   installer/output/akses_<versione>_amd64.deb` con le note in italiano. Tag = versione, release
   pubblicata (non bozza né pre-release), nomi dei file invariati: il sito (`/download/akses` e
   `/download/akses-linux`) prende da solo l'ultima release entro 5 minuti, senza deploy.

## Da sapere

- **Aggiornamenti automatici** (dalla 1.1.0): l'app chiede `mtre-consulting.it/aggiornamenti/akses` (Linux:
  `akses-linux`), che legge l'ultima release GitHub, quindi basta la release. Su Windows l'installer nuovo si
  installa in silenzio alla chiusura o con «Aggiorna e riavvia» (`/relaunch=1` in `Akses.iss`); su Linux con
  pkexec + apt. Per provarli prima di una release: `AKSES_UPDATE_SITE=http://127.0.0.1:<porta>/aggiornamenti/`
  e un server finto che risponde `{"version": ...}` e manda l'installer su `/scarica`.
- Il sito online lo aggiorna Nicolò a mano (`deploy/update.sh` sul server): non serve aspettarlo.
- Installer non ancora firmato: sulla pagina c'è la nota su SmartScreen. Quando arriva la firma
  (Authenticode), firma exe e installer, fai una release e togli la nota (`NOTES.windows` in
  `m-tre-site/components/apps/akses/AksesDownload.tsx`).
- Chi prova l'app sul PC di Nicolò lo fa sul suo desktop: le finestre che compaiono le vede anche lui.
