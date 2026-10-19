# Attribuzioni

Akses è © M-Tre Consulting ed è rilasciato sotto **GNU GPL v3** (vedi [LICENSE](LICENSE)).
Fa il lavoro pesante con questi progetti open source, che ringraziamo.

## Programmi esterni

Sono eseguibili separati, lanciati da Akses come processi a parte. Ciascuno ha la propria licenza,
copiata accanto.

### FFmpeg — audio, video e sottotitoli (incluso nell'installer, `tools\ffmpeg\`)

<https://ffmpeg.org> — FFmpeg 9.0.2 **compilato apposta per Akses** da `installer/build-ffmpeg.sh`
(mingw-w64, ogni sorgente fissato a un tag): tutti i decoder, solo gli encoder e i contenitori che
Akses scrive, così pesa circa un quinto della build generica. Librerie incluse, ciascuna alla
versione indicata nello script e in `tools\ffmpeg\BUILD.txt`:

| Libreria | Uso | Licenza |
|---|---|---|
| [x264](https://www.videolan.org/developers/x264.html) | video H.264 (MP4, MOV, MKV) | GPL v2+ |
| [libvpx](https://chromium.googlesource.com/webm/libvpx) | video VP9/VP8 (WebM) | BSD |
| [dav1d](https://code.videolan.org/videolan/dav1d) | lettura video AV1 | BSD |
| [LAME](https://lame.sourceforge.io) | MP3 | LGPL |
| [Opus](https://opus-codec.org), [Ogg/Vorbis](https://xiph.org) | Opus, OGG | BSD |
| [zlib](https://zlib.net) | compressione | zlib |

FFmpeg con x264 è sotto **GPL v3**: è uno dei motivi per cui Akses è a sua volta sotto GPL v3, come
WinPlay Mahogany. Il sorgente di Akses è in questa repo; quello di FFmpeg e delle librerie è ai tag
indicati, oppure si chiede a info@mtre-consulting.it.

Nota: alcuni formati (H.264, HEVC, AAC…) sono coperti da brevetti in alcuni paesi. Akses li usa
tramite FFmpeg come fanno i principali programmi open source di conversione.

### pandoc — documenti (scaricato alla prima conversione di un documento)

<https://pandoc.org> — © John MacFarlane, licenza **GPL v2 o successiva**. Versione 3.12.1, release
ufficiale da GitHub. Non è nell'installer (pesa 225 MB): Akses lo scarica solo se l'utente converte
un documento e accetta, ne controlla lo SHA-256 (`src/Akses.Core/ToolDownloads.cs`) e lo tiene in
`%LOCALAPPDATA%\Akses\tools\pandoc\`, con il suo `COPYRIGHT.txt` e `COPYING.rtf`. Per sviluppare,
`installer/fetch-tools.ps1` lo mette in `tools\pandoc\`.

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
