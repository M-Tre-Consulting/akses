# Akses — note per chi ci lavora (persone e Claude)

App per Windows di M-Tre Consulting: dagli un file, ti mostra tutto quello che puoi farci.
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

## Fare una release

1. `<Version>` in `src/Akses.App/Akses.App.csproj` (solo lì).
2. `dotnet test tests/Akses.Core.Tests` (serve `tools/ffmpeg` ridotto e `tools/pandoc`, vedi README).
3. `powershell -File installer/build-installer.ps1` → `installer/output/Akses-Setup-<versione>.exe`.
4. **Prova la versione installata**, non solo quella di debug: il trimming di .NET rompe le cose
   solo lì (errore "Interfaccia non supportata"). Almeno una conversione per tipo di file, il taglio
   video e un documento → PDF.
5. Commit, push, `gh release create <versione> installer/output/Akses-Setup-<versione>.exe` con le note
   in italiano. Tag = versione, release pubblicata (non bozza né pre-release), nome dell'installer
   invariato: il sito (`/download/akses`) prende da solo l'ultima release entro 5 minuti, senza deploy.

## Da sapere

- Il sito online lo aggiorna Nicolò a mano (`deploy/update.sh` sul server): non serve aspettarlo.
- Installer non ancora firmato: sulla pagina c'è la nota su SmartScreen. Quando arriva la firma
  (Authenticode), firma exe e installer, fai una release e togli la nota (`note` di `DownloadButtons`
  in `m-tre-site/app/progetti/akses/page.tsx`).
- Chi prova l'app sul PC di Nicolò lo fa sul suo desktop: le finestre che compaiono le vede anche lui.
