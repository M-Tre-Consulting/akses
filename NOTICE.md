# Attribuzioni

Akses è © M-Tre Consulting ed è rilasciato sotto **GNU GPL v3** (vedi [LICENSE](LICENSE)).
Fa il lavoro pesante con questi progetti open source, che ringraziamo.

## Programmi inclusi nell'installer (cartella `tools\`)

Sono eseguibili separati, scaricati da `installer/fetch-tools.ps1` con versione e SHA-256 fissati,
e lanciati da Akses come processi a parte. Ciascuno ha la propria licenza, copiata accanto.

### FFmpeg — audio, video e sottotitoli

<https://ffmpeg.org> — build **GPL shared** di <https://github.com/BtbN/FFmpeg-Builds>
(FFmpeg 9.0, `autobuild-2026-10-19-13-06`). Licenza **GPL v3** per questa build, perché include
librerie GPL: x264, x265, libvpx, libaom, SVT-AV1, LAME, Opus, Vorbis, dav1d, libass, libwebp e
altre (elenco completo nel `LICENSE.txt` della build, in `tools\ffmpeg\`).

La GPL di questa build è uno dei motivi per cui Akses è a sua volta sotto GPL v3, come WinPlay
Mahogany: il sorgente di ogni versione si scarica dal sito accanto all'installer.

Nota: alcuni formati (H.264, HEVC, AAC…) sono coperti da brevetti in alcuni paesi. Akses li usa
tramite FFmpeg come fanno i principali programmi open source di conversione.

### pandoc — documenti

<https://pandoc.org> — © John MacFarlane, licenza **GPL v2 o successiva** (`tools\pandoc\COPYING.rtf`).
Versione 3.12.1, release ufficiale.

## Librerie .NET (dentro l'app)

| Libreria | Uso | Licenza |
|---|---|---|
| [Magick.NET](https://github.com/dlemstra/Magick.NET) (ImageMagick e le sue librerie di formato) | immagini | Apache 2.0 (ImageMagick License) |
| [PDFsharp](https://www.pdfsharp.com) | unire, dividere, ruotare PDF; immagini → PDF | MIT |
| [PdfPig](https://github.com/UglyToad/PdfPig) | PDF → testo | Apache 2.0 |
| [YamlDotNet](https://github.com/aaubry/YamlDotNet) | YAML | MIT |
| Windows App SDK / WinUI 3 | interfaccia | MIT |
| WebView2 (Microsoft Edge) | documenti → PDF | componente di Windows |

Il rendering delle pagine PDF in immagini usa `Windows.Data.Pdf`, che fa parte di Windows.
