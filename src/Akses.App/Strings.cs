using System.Globalization;
using System.Runtime.InteropServices;
using Akses.Core;
using Akses.Core.Engines;

namespace Akses.App;

/// <summary>
/// Every user-visible string in the app, in the five languages the M-Tre brand guide requires
/// (it, en, es, fr, de), picked from the Windows display language (on Linux from LANGUAGE / LANG);
/// any other language falls back to English. Shared with the Linux app (src/Akses.Linux), which
/// compiles this same file: the few texts that name Windows have a Linux variant (<see cref="Linux"/>).
///
/// Deliberately not .resx/.resw (same as WinPlay Mahogany): Release builds run with
/// InvariantGlobalization, where satellite-assembly lookup can't pick a language. The display
/// language comes straight from kernel32 instead. Keeping all five translations in one call also
/// makes a missing translation a compile error rather than a silent fallback.
///
/// Set AKSES_LANG (e.g. "de", "ja") to force a language when testing.
/// </summary>
internal static class Strings
{
    public static readonly string Language = DetectLanguage();

    private static string DetectLanguage()
    {
        string? forced = Environment.GetEnvironmentVariable("AKSES_LANG");
        if (!string.IsNullOrWhiteSpace(forced))
            return Normalize(forced.Trim().ToLowerInvariant());

        if (!OperatingSystem.IsWindows())
        {
            // "it_IT.UTF-8", or LANGUAGE's "it:en" list: the first real one wins.
            foreach (string name in new[] { "LANGUAGE", "LC_ALL", "LC_MESSAGES", "LANG" })
            {
                string value = (Environment.GetEnvironmentVariable(name) ?? "").Split(':')[0].Trim().ToLowerInvariant();
                if (value.Length >= 2 && value is not ("c" or "posix") && !value.StartsWith("c.", StringComparison.Ordinal))
                    return Normalize(value);
            }
            return "en";
        }

        try
        {
            // Primary language ID = low 10 bits of the LANGID.
            return (GetUserDefaultUILanguage() & 0x3FF) switch
            {
                0x10 => "it",
                0x0A => "es",
                0x0C => "fr",
                0x07 => "de",
                _ => "en",
            };
        }
        catch { return "en"; }

        static string Normalize(string code) => code.Length >= 2 && code[..2] is "it" or "en" or "es" or "fr" or "de" ? code[..2] : "en";
    }

    [DllImport("kernel32.dll")]
    private static extern ushort GetUserDefaultUILanguage();

    private static string T(string it, string en, string es, string fr, string de) => Language switch
    {
        "it" => it,
        "es" => es,
        "fr" => fr,
        "de" => de,
        _ => en,
    };

    /// <summary>The Linux app (src/Akses.Linux): no Explorer, no registry, other folders.</summary>
    public static bool Linux => OperatingSystem.IsLinux();

    private static string F(string format, params object[] args) => string.Format(CultureInfo.InvariantCulture, format, args);

    // ---- brand -----------------------------------------------------------------------------

    public const string AppName = "Akses";
    public const string CompanyName = "M-Tre Consulting";
    public const string CompanySite = "https://mtre-consulting.it";
    public const string CompanyEmail = "info@mtre-consulting.it";
    public const string PrivacyPageUrl = "https://mtre-consulting.it/privacy/akses";
    public const string ProductPageUrl = "https://mtre-consulting.it/progetti/akses";
    public static readonly string[] CompanyOwners =
    [
        "Simone Rolando – P.IVA 01866720095",
        "Nicolò Perri – P.IVA 01949510091",
        "Emad Alaa Soliman Mohamed Soliman – P.IVA 01949520090",
    ];

    /// <summary>Current year, computed at runtime and formatted verbatim (no digit grouping).</summary>
    public static string Year => DateTime.Now.Year.ToString(CultureInfo.InvariantCulture);
    public static string CopyrightYear => "© " + Year;

    public static string AppVersion
    {
        get
        {
            Version? v = typeof(Strings).Assembly.GetName().Version;
            return v is null ? "1.0.0" : $"{v.Major}.{v.Minor}.{v.Build}";
        }
    }

    // ---- common ----------------------------------------------------------------------------

    public static string Ok => "OK";
    public static string Cancel => T("Annulla", "Cancel", "Cancelar", "Annuler", "Abbrechen");
    public static string Download => T("Scarica", "Download", "Descargar", "Télécharger", "Herunterladen");

    // ---- pandoc, downloaded on the first document conversion (ToolDownloads) ---------------

    public static string PandocTitle => T("Componente per i documenti", "Document component", "Componente para documentos", "Composant pour les documents", "Komponente für Dokumente");
    public static string PandocBody(int mb) => F(T(
        "Per convertire documenti (Word, Markdown, ePub…) Akses usa pandoc, che non è incluso per tenere il programma leggero. Lo scarico adesso da GitHub? Sono circa {0} MB, una volta sola.",
        "To convert documents (Word, Markdown, ePub…) Akses uses pandoc, which isn't included to keep the program light. Download it now from GitHub? About {0} MB, only once.",
        "Para convertir documentos (Word, Markdown, ePub…) Akses usa pandoc, que no está incluido para que el programa sea ligero. ¿Lo descargo ahora de GitHub? Son unos {0} MB, una sola vez.",
        "Pour convertir des documents (Word, Markdown, ePub…), Akses utilise pandoc, qui n'est pas inclus pour garder le programme léger. Le télécharger maintenant depuis GitHub ? Environ {0} Mo, une seule fois.",
        "Zum Konvertieren von Dokumenten (Word, Markdown, ePub…) verwendet Akses pandoc, das nicht enthalten ist, damit das Programm klein bleibt. Jetzt von GitHub herunterladen? Etwa {0} MB, nur einmal."), mb);
    public static string PandocDownloading => T("Download e controllo in corso…", "Downloading and checking…", "Descargando y comprobando…", "Téléchargement et vérification…", "Wird heruntergeladen und geprüft…");
    public static string PandocFailed => T(
        "Il download non è riuscito. Controlla la connessione a internet e riprova.",
        "The download failed. Check your internet connection and try again.",
        "La descarga no se ha completado. Comprueba la conexión a internet e inténtalo de nuevo.",
        "Le téléchargement a échoué. Vérifiez votre connexion Internet et réessayez.",
        "Der Download ist fehlgeschlagen. Prüfe die Internetverbindung und versuche es erneut.");
    public static string Close => T("Chiudi", "Close", "Cerrar", "Fermer", "Schließen");
    public static string On => T("Attivo", "On", "Activado", "Activé", "Ein");
    public static string Off => T("Disattivato", "Off", "Desactivado", "Désactivé", "Aus");
    public static string Open => T("Apri", "Open", "Abrir", "Ouvrir", "Öffnen");
    public static string ShowInFolder => T("Mostra nella cartella", "Show in folder", "Mostrar en la carpeta", "Afficher dans le dossier", "Im Ordner anzeigen");
    public static string Remove => T("Rimuovi dall'elenco", "Remove from list", "Quitar de la lista", "Retirer de la liste", "Aus der Liste entfernen");
    public static string ChooseFiles => T("Scegli i file…", "Choose files…", "Elegir archivos…", "Choisir des fichiers…", "Dateien auswählen…");
    public static string ChooseFile => T("Scegli un file…", "Choose a file…", "Elegir un archivo…", "Choisir un fichier…", "Datei auswählen…");
    public static string ChangeFile => T("Cambia file", "Change file", "Cambiar archivo", "Changer de fichier", "Andere Datei");
    public static string AddFiles => T("Aggiungi file", "Add files", "Añadir archivos", "Ajouter des fichiers", "Dateien hinzufügen");
    public static string ClearList => T("Svuota elenco", "Clear list", "Vaciar lista", "Vider la liste", "Liste leeren");
    public static string ChooseFolder => T("Scegli una cartella…", "Choose a folder…", "Elegir una carpeta…", "Choisir un dossier…", "Ordner auswählen…");
    public static string Loading => T("Caricamento…", "Loading…", "Cargando…", "Chargement…", "Wird geladen…");
    public static string Waiting => T("In coda…", "Waiting…", "En cola…", "En attente…", "Wartet…");
    public static string Working => T("Al lavoro…", "Working…", "Trabajando…", "En cours…", "Wird bearbeitet…");
    public static string Saving => T("Salvataggio…", "Saving…", "Guardando…", "Enregistrement…", "Wird gespeichert…");
    public static string Cancelled => T("Annullato", "Cancelled", "Cancelado", "Annulé", "Abgebrochen");
    public static string DropCaption => T("Aggiungi ad Akses", "Add to Akses", "Añadir a Akses", "Ajouter à Akses", "Zu Akses hinzufügen");
    public static string DropTitle => T("Trascina qui i file", "Drop your files here", "Arrastra aquí los archivos", "Déposez vos fichiers ici", "Dateien hier ablegen");
    public static string DoneSaved(string name) => F(T("Fatto: {0}", "Done: {0}", "Listo: {0}", "Terminé : {0}", "Fertig: {0}"), name);
    public static string DoneCount(int n) => F(T("Fatto: {0} file", "Done: {0} files", "Listo: {0} archivos", "Terminé : {0} fichiers", "Fertig: {0} Dateien"), n);
    public static string UnsupportedFormat => T("Akses non sa ancora aprire questo tipo di file.", "Akses can't open this type of file yet.", "Akses aún no sabe abrir este tipo de archivo.", "Akses ne sait pas encore ouvrir ce type de fichier.", "Akses kann diesen Dateityp noch nicht öffnen.");

    public static string KindLabel(FileKind kind) => kind switch
    {
        FileKind.Image => T("Immagine", "Image", "Imagen", "Image", "Bild"),
        FileKind.Video => T("Video", "Video", "Vídeo", "Vidéo", "Video"),
        FileKind.Audio => T("Audio", "Audio", "Audio", "Audio", "Audio"),
        FileKind.Pdf => "PDF",
        FileKind.Document => T("Documento", "Document", "Documento", "Document", "Dokument"),
        FileKind.Data => T("Dati", "Data", "Datos", "Données", "Daten"),
        FileKind.Subtitle => T("Sottotitoli", "Subtitles", "Subtítulos", "Sous-titres", "Untertitel"),
        _ => T("File", "File", "Archivo", "Fichier", "Datei"),
    };

    public static string FormatSize(long bytes) => bytes switch
    {
        >= 1L << 30 => (bytes / (double)(1L << 30)).ToString("0.0", CultureInfo.InvariantCulture) + " GB",
        >= 1L << 20 => (bytes / (double)(1L << 20)).ToString("0.0", CultureInfo.InvariantCulture) + " MB",
        >= 1L << 10 => (bytes / (double)(1L << 10)).ToString("0", CultureInfo.InvariantCulture) + " KB",
        _ => bytes.ToString(CultureInfo.InvariantCulture) + " B",
    };

    // ---- errors ----------------------------------------------------------------------------

    public static string ToolMissing => T(
        "Mancano dei componenti di Akses: reinstallalo da mtre-consulting.it.",
        "Some Akses components are missing: reinstall it from mtre-consulting.it.",
        "Faltan componentes de Akses: vuelve a instalarlo desde mtre-consulting.it.",
        "Des composants d'Akses sont manquants : réinstallez-le depuis mtre-consulting.it.",
        "Einige Akses-Komponenten fehlen: Installiere es neu von mtre-consulting.it.");
    public static string NoAudioTrack => T("Questo file non ha una traccia audio.", "This file has no audio track.", "Este archivo no tiene pista de audio.", "Ce fichier n'a pas de piste audio.", "Diese Datei hat keine Tonspur.");
    public static string NoVideoTrack => T("Questo file non ha immagini video.", "This file has no video.", "Este archivo no tiene vídeo.", "Ce fichier ne contient pas de vidéo.", "Diese Datei enthält kein Video.");
    public static string AccessDenied => Linux ? T(
        "Non hai il permesso di scrivere in quella cartella: scegline un'altra in Impostazioni.",
        "You don't have permission to write to that folder: choose another one in Settings.",
        "No tienes permiso para escribir en esa carpeta: elige otra en Configuración.",
        "Vous n'avez pas l'autorisation d'écrire dans ce dossier : choisissez-en un autre dans Paramètres.",
        "Du darfst in diesen Ordner nicht schreiben: Wähle in den Einstellungen einen anderen.") : T(
        "Windows non permette di scrivere in quella cartella: scegline un'altra in Impostazioni.",
        "Windows doesn't allow writing to that folder: choose another one in Settings.",
        "Windows no permite escribir en esa carpeta: elige otra en Configuración.",
        "Windows n'autorise pas l'écriture dans ce dossier : choisissez-en un autre dans Paramètres.",
        "Windows erlaubt das Schreiben in diesen Ordner nicht: Wähle in den Einstellungen einen anderen.");
    public static string FileInUse => T("Il file è aperto in un altro programma: chiudilo e riprova.", "The file is open in another program: close it and try again.", "El archivo está abierto en otro programa: ciérralo e inténtalo de nuevo.", "Le fichier est ouvert dans un autre programme : fermez-le et réessayez.", "Die Datei ist in einem anderen Programm geöffnet: Schließe es und versuche es erneut.");
    public static string CannotRead => T("Il file sembra danneggiato o in un formato non valido.", "The file looks damaged or isn't valid.", "El archivo parece dañado o no es válido.", "Le fichier semble endommagé ou invalide.", "Die Datei scheint beschädigt oder ungültig zu sein.");
    public static string PdfProtected => T("Questo PDF è protetto da password o danneggiato.", "This PDF is password-protected or damaged.", "Este PDF está protegido con contraseña o dañado.", "Ce PDF est protégé par un mot de passe ou endommagé.", "Dieses PDF ist passwortgeschützt oder beschädigt.");
    public static string LibreOfficeMissing => T(
        "Per trasformare i documenti in PDF serve LibreOffice: installalo da Gestione applicazioni e riprova.",
        "Turning documents into PDF needs LibreOffice: install it from the Software Manager and try again.",
        "Para convertir documentos a PDF hace falta LibreOffice: instálalo desde el gestor de aplicaciones e inténtalo de nuevo.",
        "Pour transformer des documents en PDF, il faut LibreOffice : installez-le depuis le gestionnaire de logiciels et réessayez.",
        "Um Dokumente in PDF umzuwandeln, braucht es LibreOffice: Installiere es über die Anwendungsverwaltung und versuche es erneut.");
    public static string Failed(string detail) => F(T("Non riuscito: {0}", "Failed: {0}", "No se ha podido: {0}", "Échec : {0}", "Fehlgeschlagen: {0}"), detail);

    // ---- convert ---------------------------------------------------------------------------

    public static string ConvertDropSubtitle(int formats) => F(Linux ? T(
        "Oltre {0} formati: WEBP, HEIC, PNG, JPG, MP4, MKV, MOV, MP3, WAV, FLAC, PDF, DOCX, Markdown, JSON e tanti altri. Oppure, nel gestore dei file, clic destro su un file → «Apri con Akses».",
        "Over {0} formats: WEBP, HEIC, PNG, JPG, MP4, MKV, MOV, MP3, WAV, FLAC, PDF, DOCX, Markdown, JSON and many more. Or, in the file manager, right-click a file → \"Open with Akses\".",
        "Más de {0} formatos: WEBP, HEIC, PNG, JPG, MP4, MKV, MOV, MP3, WAV, FLAC, PDF, DOCX, Markdown, JSON y muchos más. O, en el gestor de archivos, clic derecho en un archivo → «Abrir con Akses».",
        "Plus de {0} formats : WEBP, HEIC, PNG, JPG, MP4, MKV, MOV, MP3, WAV, FLAC, PDF, DOCX, Markdown, JSON et bien d'autres. Ou, dans le gestionnaire de fichiers, clic droit sur un fichier → « Ouvrir avec Akses ».",
        "Über {0} Formate: WEBP, HEIC, PNG, JPG, MP4, MKV, MOV, MP3, WAV, FLAC, PDF, DOCX, Markdown, JSON und viele mehr. Oder im Dateimanager: Rechtsklick auf eine Datei → „Mit Akses öffnen“.") : T(
        "Oltre {0} formati: WEBP, HEIC, PNG, JPG, MP4, MKV, MOV, MP3, WAV, FLAC, PDF, DOCX, Markdown, JSON e tanti altri. Oppure, in Esplora file, clic destro su un file → «Apri con Akses».",
        "Over {0} formats: WEBP, HEIC, PNG, JPG, MP4, MKV, MOV, MP3, WAV, FLAC, PDF, DOCX, Markdown, JSON and many more. Or, in File Explorer, right-click a file → \"Open with Akses\".",
        "Más de {0} formatos: WEBP, HEIC, PNG, JPG, MP4, MKV, MOV, MP3, WAV, FLAC, PDF, DOCX, Markdown, JSON y muchos más. O, en el Explorador, clic derecho en un archivo → «Abrir con Akses».",
        "Plus de {0} formats : WEBP, HEIC, PNG, JPG, MP4, MKV, MOV, MP3, WAV, FLAC, PDF, DOCX, Markdown, JSON et bien d'autres. Ou, dans l'Explorateur, clic droit sur un fichier → « Ouvrir avec Akses ».",
        "Über {0} Formate: WEBP, HEIC, PNG, JPG, MP4, MKV, MOV, MP3, WAV, FLAC, PDF, DOCX, Markdown, JSON und viele mehr. Oder im Explorer: Rechtsklick auf eine Datei → „Mit Akses öffnen“."), formats);
    public static string QualityLabel => T("Qualità", "Quality", "Calidad", "Qualité", "Qualität");
    public static string QualityHigh => T("Massima", "Best", "Máxima", "Maximale", "Beste");
    public static string QualityBalanced => T("Bilanciata", "Balanced", "Equilibrada", "Équilibrée", "Ausgewogen");
    public static string QualitySmall => T("File più leggero", "Smallest file", "Archivo más ligero", "Fichier plus léger", "Kleinste Datei");
    public static string SizeLabel => T("Dimensioni di immagini e video", "Image and video size", "Tamaño de imágenes y vídeos", "Taille des images et vidéos", "Größe von Bildern und Videos");
    public static string OriginalSize => T("Come l'originale", "Same as original", "Como el original", "Comme l'original", "Wie das Original");
    public static string MaxSide(int px) => F(T("Al massimo {0} px", "At most {0} px", "Como máximo {0} px", "Au maximum {0} px", "Höchstens {0} px"), px);
    public static string SaveToLabel => T("Salva in", "Save to", "Guardar en", "Enregistrer dans", "Speichern in");
    public static string SaveNextToOriginal => T("Accanto all'originale", "Next to the original", "Junto al original", "À côté de l'original", "Neben dem Original");
    public static string ConvertingTo(string format) => F(T("Conversione in {0}…", "Converting to {0}…", "Convirtiendo a {0}…", "Conversion en {0}…", "Wird in {0} konvertiert…"), format);
    /// <summary>Label of a format button: "MP3 (solo audio)" for a video, "Immagini PNG" for a PDF.</summary>
    public static string TargetLabel(FileKind kind, TargetFormat t) => (kind, t.Id) switch
    {
        (FileKind.Video, "mp3" or "m4a" or "wav" or "flac" or "ogg" or "opus") => F(T("{0} (solo audio)", "{0} (audio only)", "{0} (solo audio)", "{0} (audio seul)", "{0} (nur Ton)"), t.Label),
        (FileKind.Video, "gif") => T("GIF animata", "Animated GIF", "GIF animado", "GIF animé", "Animiertes GIF"),
        (FileKind.Pdf, "png-pages") => T("Immagini PNG", "PNG images", "Imágenes PNG", "Images PNG", "PNG-Bilder"),
        (FileKind.Pdf, "jpg-pages") => T("Immagini JPG", "JPG images", "Imágenes JPG", "Images JPG", "JPG-Bilder"),
        (FileKind.Pdf, "txt") => T("Testo (TXT)", "Text (TXT)", "Texto (TXT)", "Texte (TXT)", "Text (TXT)"),
        (FileKind.Document, "txt") => T("Testo (TXT)", "Text (TXT)", "Texto (TXT)", "Texte (TXT)", "Text (TXT)"),
        _ => t.Label,
    };

    // ---- home + the file's page ------------------------------------------------------------

    public static string HomeTitle => "Akses";
    public static string HomeSubtitle => T(
        "Dagli un file: ti mostra tutto quello che puoi farci. Convertirlo, tagliarlo, ritagliarlo, ridimensionarlo, alleggerirlo.",
        "Give it a file: it shows you everything you can do with it. Convert it, trim it, crop it, resize it, make it lighter.",
        "Dale un archivo: te muestra todo lo que puedes hacer con él. Convertirlo, recortarlo, redimensionarlo, aligerarlo.",
        "Donnez-lui un fichier : il vous montre tout ce que vous pouvez en faire. Le convertir, le couper, le rogner, le redimensionner, l'alléger.",
        "Gib ihm eine Datei: Es zeigt dir alles, was du damit machen kannst. Konvertieren, schneiden, zuschneiden, skalieren, verkleinern.");
    public static string Back => T("Indietro", "Back", "Atrás", "Retour", "Zurück");
    public static string OtherFiles => T("Altri file", "Other files", "Otros archivos", "Autres fichiers", "Andere Dateien");
    public static string OpenOriginal => T("Apri il file", "Open the file", "Abrir el archivo", "Ouvrir le fichier", "Datei öffnen");
    public static string ConvertToSection => T("Converti in", "Convert to", "Convertir a", "Convertir en", "Konvertieren in");
    public static string EditSection => T("Modifica", "Edit", "Editar", "Modifier", "Bearbeiten");
    public static string ResultsSection => T("Risultati", "Results", "Resultados", "Résultats", "Ergebnisse");
    public static string ApplyToAll(int n) => F(T("Applica a tutti i {0} file di questo tipo", "Apply to all {0} files of this type", "Aplicar a los {0} archivos de este tipo", "Appliquer aux {0} fichiers de ce type", "Auf alle {0} Dateien dieses Typs anwenden"), n);
    public static string ShellOpenWith => T("Apri con Akses", "Open with Akses", "Abrir con Akses", "Ouvrir avec Akses", "Mit Akses öffnen");

    public static string InfoImage(int w, int h, string ext, string size) => F("{0} × {1} px · {2} · {3}", w, h, ext, size);
    public static string InfoVideo(string length, int w, int h, string ext, string size) => F(T("Video · {0} · {1}×{2} · {3} · {4}", "Video · {0} · {1}×{2} · {3} · {4}", "Vídeo · {0} · {1}×{2} · {3} · {4}", "Vidéo · {0} · {1}×{2} · {3} · {4}", "Video · {0} · {1}×{2} · {3} · {4}"), length, w, h, ext, size);
    public static string InfoAudio(string length, string ext, string size) => F("Audio · {0} · {1} · {2}", length, ext, size);
    public static string InfoPdf(int pages, string size) => PageCount(pages) + " · " + size;
    public static string InfoOther(string kind, string ext, string size) => F("{0} · {1} · {2}", kind, ext, size);

    public static string ActCrop => T("Ritaglia", "Crop", "Recortar", "Rogner", "Zuschneiden");
    public static string ActCropText => T("Togli i bordi, anche in 1:1, 16:9, 4:5…", "Cut the edges away, also 1:1, 16:9, 4:5…", "Quita los bordes, también en 1:1, 16:9, 4:5…", "Enlevez les bords, aussi en 1:1, 16:9, 4:5…", "Ränder entfernen, auch 1:1, 16:9, 4:5…");
    public static string ActResize => T("Ridimensiona", "Resize", "Redimensionar", "Redimensionner", "Skalieren");
    public static string ActResizeText => T("Cambia larghezza e altezza in pixel", "Change width and height in pixels", "Cambia el ancho y el alto en píxeles", "Changez la largeur et la hauteur en pixels", "Breite und Höhe in Pixeln ändern");
    public static string ActRotate => T("Ruota o rifletti", "Rotate or flip", "Girar o voltear", "Pivoter ou retourner", "Drehen oder spiegeln");
    public static string ActRotateText => T("Raddrizza una foto girata", "Straighten a sideways photo", "Endereza una foto girada", "Redressez une photo tournée", "Ein gedrehtes Foto geraderücken");
    public static string ActCompress => T("Alleggerisci", "Make it lighter", "Aligerar", "Alléger", "Verkleinern");
    public static string ActCompressImageText => T("Stessa foto, file molto più piccolo", "Same photo, much smaller file", "La misma foto, archivo mucho más pequeño", "Même photo, fichier bien plus petit", "Gleiches Foto, viel kleinere Datei");
    public static string ActCompressVideoText => T("MP4 più leggero da mandare, fino a 1280 px", "Lighter MP4 to send, up to 1280 px", "MP4 más ligero para enviar, hasta 1280 px", "MP4 plus léger à envoyer, jusqu'à 1280 px", "Leichteres MP4 zum Verschicken, bis 1280 px");
    public static string ActCompressAudioText => T("MP3 leggero, ottimo per la voce", "Light MP3, great for voice", "MP3 ligero, ideal para voz", "MP3 léger, idéal pour la voix", "Leichtes MP3, ideal für Sprache");
    public static string ActStrip => T("Togli posizione e dati", "Remove location and data", "Quitar ubicación y datos", "Retirer position et données", "Standort und Daten entfernen");
    public static string ActStripText => T("Via GPS, fotocamera e altri dati EXIF prima di condividerla", "Strip GPS, camera and other EXIF data before sharing", "Quita GPS, cámara y otros datos EXIF antes de compartirla", "Supprimez GPS, appareil et autres données EXIF avant de partager", "GPS, Kamera und andere EXIF-Daten vor dem Teilen entfernen");
    public static string ActMakePdf => T("Crea un PDF", "Make a PDF", "Crear un PDF", "Créer un PDF", "PDF erstellen");
    public static string ActMakePdfText => T("Questa e altre immagini in un unico PDF", "This and other images in a single PDF", "Esta y otras imágenes en un único PDF", "Cette image et d'autres dans un seul PDF", "Dieses und weitere Bilder in einem PDF");
    public static string ActTrim => T("Taglia", "Trim", "Recortar", "Couper", "Schneiden");
    public static string ActTrimText => T("Tieni solo un pezzo, o togline uno", "Keep just a part, or remove one", "Conserva solo una parte, o quita una", "Gardez seulement un passage, ou retirez-en un", "Nur einen Teil behalten oder einen entfernen");
    public static string ActExtractAudio => T("Estrai l'audio", "Extract the audio", "Extraer el audio", "Extraire l'audio", "Ton extrahieren");
    public static string ActExtractAudioText => T("La traccia audio in un MP3", "The soundtrack as an MP3", "La pista de audio en un MP3", "La piste audio en MP3", "Die Tonspur als MP3");
    public static string ActGif => T("Crea una GIF", "Make a GIF", "Crear un GIF", "Créer un GIF", "GIF erstellen");
    public static string ActGifText => T("Una GIF animata da condividere", "An animated GIF to share", "Un GIF animado para compartir", "Un GIF animé à partager", "Ein animiertes GIF zum Teilen");
    public static string ActRemoveAudio => T("Togli l'audio", "Remove the audio", "Quitar el audio", "Retirer le son", "Ton entfernen");
    public static string ActRemoveAudioText => T("Lo stesso video, muto, in un secondo", "The same video, silent, in a second", "El mismo vídeo, sin sonido, en un segundo", "La même vidéo, muette, en une seconde", "Dasselbe Video, stumm, in einer Sekunde");
    public static string ActSplit => T("Dividi in pagine", "Split into pages", "Dividir en páginas", "Diviser en pages", "In Seiten teilen");
    public static string ActSplitText => T("Un PDF per ogni pagina", "One PDF per page", "Un PDF por cada página", "Un PDF par page", "Ein PDF pro Seite");
    public static string ActExtractPages => T("Estrai pagine", "Extract pages", "Extraer páginas", "Extraire des pages", "Seiten extrahieren");
    public static string ActExtractPagesText => T("Un PDF con solo le pagine che scegli", "A PDF with only the pages you pick", "Un PDF solo con las páginas que elijas", "Un PDF avec seulement les pages choisies", "Ein PDF nur mit den gewählten Seiten");
    public static string ActRotatePages => T("Ruota pagine", "Rotate pages", "Girar páginas", "Pivoter des pages", "Seiten drehen");
    public static string ActRotatePagesText => T("Raddrizza le pagine storte", "Straighten sideways pages", "Endereza las páginas giradas", "Redressez les pages tournées", "Gedrehte Seiten geraderücken");
    public static string ActMerge => T("Unisci con altri file", "Merge with other files", "Unir con otros archivos", "Fusionner avec d'autres fichiers", "Mit anderen Dateien zusammenfügen");
    public static string ActMergeText => T("PDF e immagini in un unico PDF", "PDFs and images in a single PDF", "PDF e imágenes en un único PDF", "PDF et images dans un seul PDF", "PDFs und Bilder in einem PDF");
    public static string ActJoin => T("Unisci con altri", "Join with others", "Unir con otros", "Assembler avec d'autres", "Mit anderen verbinden");
    public static string ActJoinVideoText => T("Più video in uno, uno dopo l'altro", "Several videos in one, one after the other", "Varios vídeos en uno, uno tras otro", "Plusieurs vidéos en une, l'une après l'autre", "Mehrere Videos in einem, nacheinander");
    public static string ActJoinAudioText => T("Più audio in uno, uno dopo l'altro", "Several audio files in one, one after the other", "Varios audios en uno, uno tras otro", "Plusieurs fichiers audio en un, l'un après l'autre", "Mehrere Audiodateien in einer, nacheinander");
    public static string ActPdfImages =>T("Pagine in immagini", "Pages to images", "Páginas a imágenes", "Pages en images", "Seiten als Bilder");
    public static string ActPdfImagesText => T("Ogni pagina diventa un PNG", "Every page becomes a PNG", "Cada página se convierte en un PNG", "Chaque page devient un PNG", "Jede Seite wird ein PNG");
    public static string ActPdfText => T("Estrai il testo", "Extract the text", "Extraer el texto", "Extraire le texte", "Text extrahieren");
    public static string ActPdfTextText => T("Tutto il testo in un file TXT", "All the text in a TXT file", "Todo el texto en un archivo TXT", "Tout le texte dans un fichier TXT", "Der ganze Text in einer TXT-Datei");
    public static string CompressedSuffix => T(" (leggero)", " (light)", " (ligero)", " (léger)", " (klein)");
    public static string CleanSuffix => T(" (senza dati)", " (no data)", " (sin datos)", " (sans données)", " (ohne Daten)");
    public static string NoAudioSuffix => T(" (senza audio)", " (no audio)", " (sin audio)", " (sans son)", " (ohne Ton)");

    // ---- trim ------------------------------------------------------------------------------

    public static string TrimTitle => T("Taglia audio e video", "Trim audio and video", "Recortar audio y vídeo", "Couper l'audio et la vidéo", "Audio und Video schneiden");
    public static string TrimSubtitle => T(
        "Scegli inizio e fine trascinando le maniglie: il taglio è pronto in un secondo, senza ricodificare.",
        "Pick the start and end by dragging the handles: the cut is ready in a second, without re-encoding.",
        "Elige el inicio y el final arrastrando los controles: el corte está listo en un segundo, sin recodificar.",
        "Choisissez le début et la fin en faisant glisser les poignées : la coupe est prête en une seconde, sans réencodage.",
        "Wähle Anfang und Ende mit den Griffen: Der Schnitt ist in einer Sekunde fertig, ohne Neukodierung.");
    public static string TrimDropTitle => T("Trascina qui un file audio o video", "Drop an audio or video file here", "Arrastra aquí un archivo de audio o vídeo", "Déposez un fichier audio ou vidéo ici", "Audio- oder Videodatei hier ablegen");
    public static string TrimDropSubtitle => T("MP4, MOV, MKV, AVI, WEBM, MP3, WAV, FLAC, M4A, OGG e tutti gli altri.", "MP4, MOV, MKV, AVI, WEBM, MP3, WAV, FLAC, M4A, OGG and all the others.", "MP4, MOV, MKV, AVI, WEBM, MP3, WAV, FLAC, M4A, OGG y todos los demás.", "MP4, MOV, MKV, AVI, WEBM, MP3, WAV, FLAC, M4A, OGG et tous les autres.", "MP4, MOV, MKV, AVI, WEBM, MP3, WAV, FLAC, M4A, OGG und alle anderen.");
    public static string Play => T("Riproduci (spazio)", "Play (space)", "Reproducir (espacio)", "Lire (espace)", "Abspielen (Leertaste)");
    public static string Pause => T("Pausa (spazio)", "Pause (space)", "Pausa (espacio)", "Pause (espace)", "Pause (Leertaste)");
    public static string PlaySelection => T("Ascolta la selezione", "Play selection", "Reproducir la selección", "Lire la sélection", "Auswahl abspielen");
    public static string StartLabel => T("Inizio", "Start", "Inicio", "Début", "Anfang");
    public static string EndLabel => T("Fine", "End", "Fin", "Fin", "Ende");
    public static string SetStartHere => T("Inizio qui", "Start here", "Inicio aquí", "Début ici", "Anfang hier");
    public static string SetEndHere => T("Fine qui", "End here", "Fin aquí", "Fin ici", "Ende hier");
    public static string ResultLength(string length) => F(T("Durata finale: {0}", "Final length: {0}", "Duración final: {0}", "Durée finale : {0}", "Endlänge: {0}"), length);
    public static string KeepSelection => T("Tieni la parte selezionata", "Keep the selected part", "Conservar la parte seleccionada", "Garder la partie sélectionnée", "Ausgewählten Teil behalten");
    public static string RemoveSelection => T("Togli la parte selezionata", "Remove the selected part", "Quitar la parte seleccionada", "Supprimer la partie sélectionnée", "Ausgewählten Teil entfernen");
    public static string PreciseCut => T(
        "Taglio preciso al fotogramma (ricodifica il video: più lento)",
        "Frame-exact cut (re-encodes the video: slower)",
        "Corte exacto al fotograma (recodifica el vídeo: más lento)",
        "Coupe précise à l'image près (réencode la vidéo : plus lent)",
        "Bildgenauer Schnitt (kodiert das Video neu: langsamer)");
    public static string TrimButton => T("Taglia", "Trim", "Recortar", "Couper", "Schneiden");
    public static string Cutting => T("Taglio in corso…", "Cutting…", "Recortando…", "Coupe en cours…", "Wird geschnitten…");
    public static string NothingToCut => T("Sposta prima le maniglie: ora la selezione è tutto il file.", "Move the handles first: right now the selection is the whole file.", "Mueve primero los controles: ahora la selección es todo el archivo.", "Déplacez d'abord les poignées : la sélection couvre tout le fichier.", "Verschiebe zuerst die Griffe: Im Moment ist die ganze Datei ausgewählt.");
    public static string TrimSuffix => T(" (tagliato)", " (trimmed)", " (recortado)", " (coupé)", " (geschnitten)");
    public static string NoAudioPreview => Linux ? T(
        "Questo formato non si può ascoltare qui, ma tagliarlo funziona lo stesso.",
        "This format can't be played here, but trimming it still works.",
        "Este formato no se puede reproducir aquí, pero recortarlo funciona igual.",
        "Ce format ne peut pas être lu ici, mais la coupe fonctionne quand même.",
        "Dieses Format kann hier nicht abgespielt werden, das Schneiden funktioniert trotzdem.") : T(
        "Windows non sa riprodurre questo formato, ma tagliarlo funziona lo stesso.",
        "Windows can't play this format, but trimming it still works.",
        "Windows no puede reproducir este formato, pero recortarlo funciona igual.",
        "Windows ne sait pas lire ce format, mais la coupe fonctionne quand même.",
        "Windows kann dieses Format nicht abspielen, das Schneiden funktioniert trotzdem.");
    public static string NotMediaTitle => T("Non è un file audio o video", "Not an audio or video file", "No es un archivo de audio o vídeo", "Ce n'est pas un fichier audio ou vidéo", "Keine Audio- oder Videodatei");
    public static string NotMediaBody(string name) => F(T("«{0}» non si può tagliare: scegli un file audio o video.", "\"{0}\" can't be trimmed: choose an audio or video file.", "«{0}» no se puede recortar: elige un archivo de audio o vídeo.", "« {0} » ne peut pas être coupé : choisissez un fichier audio ou vidéo.", "„{0}“ kann nicht geschnitten werden: Wähle eine Audio- oder Videodatei."), name);
    public static string MediaInfoLine(MediaInfo info)
    {
        string length = Ui.FormatTime(info.Duration, withMillis: false);
        return info.HasVideo
            ? F(T("Video · {0} · {1}×{2}", "Video · {0} · {1}×{2}", "Vídeo · {0} · {1}×{2}", "Vidéo · {0} · {1}×{2}", "Video · {0} · {1}×{2}"), length, info.Width, info.Height)
            : F(T("Audio · {0}", "Audio · {0}", "Audio · {0}", "Audio · {0}", "Audio · {0}"), length);
    }

    // ---- image -----------------------------------------------------------------------------

    public static string ImageTitle => T("Ritaglia e ridimensiona", "Crop and resize", "Recortar y redimensionar", "Rogner et redimensionner", "Zuschneiden und skalieren");
    public static string ImageSubtitle => T(
        "Ritaglia, cambia i pixel, ruota e rifletti, poi salva una copia nel formato che vuoi. L'originale non si tocca.",
        "Crop, change the pixel size, rotate and flip, then save a copy in any format. The original stays untouched.",
        "Recorta, cambia los píxeles, gira y voltea, y guarda una copia en el formato que quieras. El original no se toca.",
        "Rognez, changez la taille en pixels, faites pivoter et retournez, puis enregistrez une copie dans le format voulu. L'original reste intact.",
        "Zuschneiden, Pixelgröße ändern, drehen und spiegeln, dann eine Kopie im gewünschten Format speichern. Das Original bleibt unverändert.");
    public static string ImageDropTitle => T("Trascina qui un'immagine", "Drop an image here", "Arrastra aquí una imagen", "Déposez une image ici", "Bild hier ablegen");
    public static string ImageDropSubtitle => T("JPG, PNG, WEBP, HEIC, AVIF, GIF, TIFF, RAW, PSD, SVG e molti altri.", "JPG, PNG, WEBP, HEIC, AVIF, GIF, TIFF, RAW, PSD, SVG and many more.", "JPG, PNG, WEBP, HEIC, AVIF, GIF, TIFF, RAW, PSD, SVG y muchos más.", "JPG, PNG, WEBP, HEIC, AVIF, GIF, TIFF, RAW, PSD, SVG et bien d'autres.", "JPG, PNG, WEBP, HEIC, AVIF, GIF, TIFF, RAW, PSD, SVG und viele mehr.");
    public static string ChangeImage => T("Cambia immagine", "Change image", "Cambiar imagen", "Changer d'image", "Anderes Bild");
    public static string NotImageTitle => T("Non è un'immagine", "Not an image", "No es una imagen", "Ce n'est pas une image", "Kein Bild");
    public static string NotImageBody(string name) => F(T("«{0}» non è un'immagine che Akses sa aprire.", "\"{0}\" isn't an image Akses can open.", "«{0}» no es una imagen que Akses pueda abrir.", "« {0} » n'est pas une image qu'Akses sait ouvrir.", "„{0}“ ist kein Bild, das Akses öffnen kann."), name);
    public static string ImageInfo(int w, int h, string ext, bool animated) => F("{0} × {1} px · {2}", w, h, ext) +
        (animated ? T(" · animata", " · animated", " · animada", " · animée", " · animiert") : "");
    public static string RotateFlip => T("Ruota e rifletti", "Rotate and flip", "Girar y voltear", "Pivoter et retourner", "Drehen und spiegeln");
    public static string RotateLeft => T("Ruota a sinistra", "Rotate left", "Girar a la izquierda", "Pivoter à gauche", "Nach links drehen");
    public static string RotateRight => T("Ruota a destra", "Rotate right", "Girar a la derecha", "Pivoter à droite", "Nach rechts drehen");
    public static string FlipHorizontal => T("Rifletti in orizzontale", "Flip horizontally", "Voltear horizontalmente", "Retourner horizontalement", "Horizontal spiegeln");
    public static string FlipVertical => T("Rifletti in verticale", "Flip vertically", "Voltear verticalmente", "Retourner verticalement", "Vertikal spiegeln");
    public static string CropSection => T("Ritaglia", "Crop", "Recortar", "Rogner", "Zuschneiden");
    public static string RatioLabel => T("Proporzioni", "Aspect ratio", "Proporción", "Proportions", "Seitenverhältnis");
    public static string RatioFree => T("Libere", "Free", "Libre", "Libres", "Frei");
    public static string CropSize(int w, int h) => F(T("Area ritagliata: {0} × {1} px", "Crop area: {0} × {1} px", "Área recortada: {0} × {1} px", "Zone rognée : {0} × {1} px", "Zuschnitt: {0} × {1} px"), w, h);
    public static string SizeSection => T("Dimensioni", "Size", "Tamaño", "Taille", "Größe");
    public static string WidthPx => T("Larghezza (px)", "Width (px)", "Ancho (px)", "Largeur (px)", "Breite (px)");
    public static string HeightPx => T("Altezza (px)", "Height (px)", "Alto (px)", "Hauteur (px)", "Höhe (px)");
    public static string KeepProportions => T("Mantieni le proporzioni", "Keep proportions", "Mantener proporciones", "Conserver les proportions", "Seitenverhältnis beibehalten");
    public static string SaveAs => T("Salva come", "Save as", "Guardar como", "Enregistrer sous", "Speichern als");
    public static string FormatLabel => T("Formato", "Format", "Formato", "Format", "Format");
    public static string SameFormat(string label) => F(T("Come l'originale ({0})", "Same as original ({0})", "Como el original ({0})", "Comme l'original ({0})", "Wie das Original ({0})"), label);
    public static string SaveCopy => T("Salva una copia", "Save a copy", "Guardar una copia", "Enregistrer une copie", "Kopie speichern");
    public static string EditSuffix => T(" (modificata)", " (edited)", " (editada)", " (modifiée)", " (bearbeitet)");

    // ---- PDF -------------------------------------------------------------------------------

    public static string PdfTitle => T("Strumenti PDF", "PDF tools", "Herramientas PDF", "Outils PDF", "PDF-Werkzeuge");
    public static string PdfSubtitle => T(
        "Unisci PDF e immagini, dividi le pagine, estraile, ruotale o trasformale in immagini. Tutto sul tuo PC.",
        "Merge PDFs and images, split pages, extract them, rotate them or turn them into images. All on your PC.",
        "Une PDF e imágenes, divide las páginas, extráelas, gíralas o conviértelas en imágenes. Todo en tu PC.",
        "Fusionnez des PDF et des images, divisez les pages, extrayez-les, faites-les pivoter ou transformez-les en images. Le tout sur votre PC.",
        "PDFs und Bilder zusammenfügen, Seiten teilen, extrahieren, drehen oder in Bilder umwandeln. Alles auf deinem PC.");
    public static string PdfMerge => T("Unisci", "Merge", "Unir", "Fusionner", "Zusammenfügen");
    public static string PdfSplit => T("Dividi", "Split", "Dividir", "Diviser", "Teilen");
    public static string PdfExtract => T("Estrai pagine", "Extract pages", "Extraer páginas", "Extraire des pages", "Seiten extrahieren");
    public static string PdfRotate => T("Ruota", "Rotate", "Girar", "Pivoter", "Drehen");
    public static string PdfToImages => T("In immagini", "To images", "A imágenes", "En images", "In Bilder");
    public static string PdfDropTitle => T("Trascina qui PDF e immagini", "Drop PDFs and images here", "Arrastra aquí PDF e imágenes", "Déposez des PDF et des images ici", "PDFs und Bilder hier ablegen");
    public static string PdfDropSubtitle => T(
        "Per unire puoi mescolare PDF e foto: ogni immagine diventa una pagina.",
        "To merge you can mix PDFs and photos: every image becomes a page.",
        "Para unir puedes mezclar PDF y fotos: cada imagen se convierte en una página.",
        "Pour fusionner, vous pouvez mélanger PDF et photos : chaque image devient une page.",
        "Zum Zusammenfügen kannst du PDFs und Fotos mischen: Jedes Bild wird zu einer Seite.");
    public static string PdfMergeHint => T("Un unico PDF con tutti i file, nell'ordine dell'elenco: usa le frecce per cambiarlo.", "A single PDF with every file, in the order of the list: use the arrows to change it.", "Un único PDF con todos los archivos, en el orden de la lista: usa las flechas para cambiarlo.", "Un seul PDF avec tous les fichiers, dans l'ordre de la liste : utilisez les flèches pour le changer.", "Ein einziges PDF mit allen Dateien in der Reihenfolge der Liste: Ändere sie mit den Pfeilen.");
    public static string PdfSplitHint => T("Ogni pagina diventa un PDF a sé, in una cartella accanto all'originale.", "Every page becomes its own PDF, in a folder next to the original.", "Cada página se convierte en un PDF propio, en una carpeta junto al original.", "Chaque page devient un PDF distinct, dans un dossier à côté de l'original.", "Jede Seite wird ein eigenes PDF, in einem Ordner neben dem Original.");
    public static string PdfExtractHint => T("Un nuovo PDF con solo le pagine che scrivi, nell'ordine in cui le scrivi.", "A new PDF with only the pages you type, in the order you type them.", "Un PDF nuevo solo con las páginas que escribas, en el orden en que las escribas.", "Un nouveau PDF avec seulement les pages indiquées, dans l'ordre indiqué.", "Ein neues PDF nur mit den eingegebenen Seiten, in dieser Reihenfolge.");
    public static string PdfRotateHint => T("Ruota tutte le pagine, o solo quelle che scrivi.", "Rotates every page, or only the ones you type.", "Gira todas las páginas, o solo las que escribas.", "Fait pivoter toutes les pages, ou seulement celles indiquées.", "Dreht alle Seiten oder nur die eingegebenen.");
    public static string PdfImagesHint => T("Ogni pagina diventa un'immagine, in una cartella accanto all'originale.", "Every page becomes an image, in a folder next to the original.", "Cada página se convierte en una imagen, en una carpeta junto al original.", "Chaque page devient une image, dans un dossier à côté de l'original.", "Jede Seite wird ein Bild, in einem Ordner neben dem Original.");
    public static string PdfMergeButton => T("Unisci in un PDF", "Merge into one PDF", "Unir en un PDF", "Fusionner en un PDF", "Zu einem PDF zusammenfügen");
    public static string PdfSplitButton => T("Dividi le pagine", "Split pages", "Dividir páginas", "Diviser les pages", "Seiten teilen");
    public static string PdfExtractButton => T("Estrai", "Extract", "Extraer", "Extraire", "Extrahieren");
    public static string PdfRotateButton => T("Ruota", "Rotate", "Girar", "Pivoter", "Drehen");
    public static string PdfImagesButton => T("Crea le immagini", "Create images", "Crear imágenes", "Créer les images", "Bilder erstellen");
    public static string PagesLabel => T("Pagine", "Pages", "Páginas", "Pages", "Seiten");
    public static string PagesPlaceholder => T("es. 1-3, 5, 8-", "e.g. 1-3, 5, 8-", "p. ej. 1-3, 5, 8-", "ex. 1-3, 5, 8-", "z. B. 1-3, 5, 8-");
    public static string PagesAllPlaceholder => T("vuoto = tutte", "empty = all", "vacío = todas", "vide = toutes", "leer = alle");
    public static string RotationLabel => T("Rotazione", "Rotation", "Rotación", "Rotation", "Drehung");
    public static string Rotate90Right => T("90° a destra", "90° right", "90° a la derecha", "90° à droite", "90° nach rechts");
    public static string Rotate180 => "180°";
    public static string Rotate90Left => T("90° a sinistra", "90° left", "90° a la izquierda", "90° à gauche", "90° nach links");
    public static string ResolutionLabel => T("Risoluzione", "Resolution", "Resolución", "Résolution", "Auflösung");
    public static string PageCount(int n) => n == 1
        ? T("PDF · 1 pagina", "PDF · 1 page", "PDF · 1 página", "PDF · 1 page", "PDF · 1 Seite")
        : F(T("PDF · {0} pagine", "PDF · {0} pages", "PDF · {0} páginas", "PDF · {0} pages", "PDF · {0} Seiten"), n);
    public static string InvalidPages(int count) => F(T(
        "Pagine non valide: scrivi numeri da 1 a {0}, per esempio 1-3, 5.",
        "Invalid pages: type numbers from 1 to {0}, for example 1-3, 5.",
        "Páginas no válidas: escribe números del 1 al {0}, por ejemplo 1-3, 5.",
        "Pages invalides : indiquez des numéros de 1 à {0}, par exemple 1-3, 5.",
        "Ungültige Seiten: Gib Zahlen von 1 bis {0} ein, zum Beispiel 1-3, 5."), count);
    public static string MoveUp => T("Sposta su", "Move up", "Subir", "Monter", "Nach oben");
    public static string MoveDown => T("Sposta giù", "Move down", "Bajar", "Descendre", "Nach unten");
    public static string MergeNeedsTwo => T("Per unire servono almeno due file.", "Merging needs at least two files.", "Para unir se necesitan al menos dos archivos.", "Il faut au moins deux fichiers pour fusionner.", "Zum Zusammenfügen braucht es mindestens zwei Dateien.");
    public static string NeedPdf => T("Aggiungi almeno un PDF.", "Add at least one PDF.", "Añade al menos un PDF.", "Ajoutez au moins un PDF.", "Füge mindestens ein PDF hinzu.");
    public static string MergedSuffix => T(" (unito)", " (merged)", " (unido)", " (fusionné)", " (zusammengefügt)");
    public static string ExtractSuffix => T(" (estratto)", " (extracted)", " (extraído)", " (extrait)", " (extrahiert)");
    public static string RotatedSuffix => T(" (ruotato)", " (rotated)", " (girado)", " (pivoté)", " (gedreht)");
    public static string PagesSuffix => T(" (pagine)", " (pages)", " (páginas)", " (pages)", " (Seiten)");
    public static string ImagesSuffix => T(" (immagini)", " (images)", " (imágenes)", " (images)", " (Bilder)");

    // ---- updates ---------------------------------------------------------------------------

    public static string UpdatesSection => T("Aggiornamenti", "Updates", "Actualizaciones", "Mises à jour", "Updates");
    public static string UpdatesHint => Linux
        ? T("Akses controlla da solo se c'è una versione nuova, all'avvio e ogni 6 ore, e la scarica. Per installarla premi «Installa» in basso: il computer ti chiede la password.",
            "Akses checks for a new version by itself, at start and every 6 hours, and downloads it. To install it, press \"Install\" at the bottom: the computer asks for your password.",
            "Akses comprueba por sí sola si hay una versión nueva, al iniciar y cada 6 horas, y la descarga. Para instalarla pulsa «Instalar» abajo: el ordenador te pide la contraseña.",
            "Akses vérifie toute seule s'il existe une nouvelle version, au démarrage et toutes les 6 heures, et la télécharge. Pour l'installer, appuyez sur « Installer » en bas : l'ordinateur vous demande votre mot de passe.",
            "Akses prüft selbst, ob es eine neue Version gibt, beim Start und alle 6 Stunden, und lädt sie herunter. Zum Installieren unten auf „Installieren“ drücken: Der Computer fragt nach deinem Passwort.")
        : T("Akses controlla da solo se c'è una versione nuova, all'avvio e ogni 6 ore, la scarica e la installa quando chiudi l'app (o subito, con «Aggiorna e riavvia» in basso).",
            "Akses checks for a new version by itself, at start and every 6 hours, downloads it and installs it when you close the app (or right away, with \"Update and restart\" at the bottom).",
            "Akses comprueba por sí sola si hay una versión nueva, al iniciar y cada 6 horas, la descarga y la instala cuando cierras la app (o enseguida, con «Actualizar y reiniciar» abajo).",
            "Akses vérifie toute seule s'il existe une nouvelle version, au démarrage et toutes les 6 heures, la télécharge et l'installe quand vous fermez l'app (ou tout de suite, avec « Mettre à jour et redémarrer » en bas).",
            "Akses prüft selbst, ob es eine neue Version gibt, beim Start und alle 6 Stunden, lädt sie herunter und installiert sie, wenn du die App schließt (oder sofort, mit „Aktualisieren und neu starten“ unten).");
    public static string CheckNow => T("Controlla ora", "Check now", "Comprobar ahora", "Vérifier maintenant", "Jetzt prüfen");
    public static string UpdateChecking => T("Controllo in corso…", "Checking…", "Comprobando…", "Vérification…", "Wird geprüft…");
    public static string UpToDate => T("Hai l'ultima versione.", "You have the latest version.", "Tienes la última versión.", "Vous avez la dernière version.", "Du hast die neueste Version.");
    public static string UpdateCheckFailed => T("Controllo non riuscito: verifica la connessione e riprova.", "The check failed: check your connection and try again.", "La comprobación ha fallado: revisa la conexión y vuelve a intentarlo.", "La vérification a échoué : vérifiez la connexion et réessayez.", "Prüfung fehlgeschlagen: Verbindung prüfen und erneut versuchen.");
    public static string UpdateReady(string version) => F(T("Akses {0} è pronto", "Akses {0} is ready", "Akses {0} está lista", "Akses {0} est prête", "Akses {0} ist bereit"), version);
    public static string UpdateInstall => Linux
        ? T("Installa", "Install", "Instalar", "Installer", "Installieren")
        : T("Aggiorna e riavvia", "Update and restart", "Actualizar y reiniciar", "Mettre à jour et redémarrer", "Aktualisieren und neu starten");
    public static string UpdateInstallFailed => T(
        "Installazione non riuscita: ti apro il pacchetto, installalo da lì.",
        "Installation failed: opening the package, install it from there.",
        "La instalación ha fallado: te abro el paquete, instálalo desde ahí.",
        "L'installation a échoué : j'ouvre le paquet, installez-le depuis là.",
        "Installation fehlgeschlagen: Das Paket wird geöffnet, installiere es dort.");

    // ---- join video / audio ----------------------------------------------------------------

    public static string JoinTitle => T("Unisci video e audio", "Join videos and audio", "Unir vídeos y audio", "Assembler vidéos et audio", "Videos und Audio verbinden");
    public static string JoinSubtitle => T(
        "Più file uno dopo l'altro, in un file solo. Tutto sul tuo PC.",
        "Several files one after the other, in a single file. All on your PC.",
        "Varios archivos uno tras otro, en un solo archivo. Todo en tu PC.",
        "Plusieurs fichiers l'un après l'autre, dans un seul fichier. Le tout sur votre PC.",
        "Mehrere Dateien nacheinander in einer einzigen Datei. Alles auf deinem PC.");
    public static string JoinDropTitle => T("Trascina qui video o file audio", "Drop videos or audio files here", "Arrastra aquí vídeos o archivos de audio", "Déposez des vidéos ou des fichiers audio ici", "Videos oder Audiodateien hier ablegen");
    public static string JoinDropSubtitle => T(
        "Si uniscono nell'ordine dell'elenco: video con video, audio con audio.",
        "They are joined in the order of the list: videos with videos, audio with audio.",
        "Se unen en el orden de la lista: vídeos con vídeos, audio con audio.",
        "Ils sont assemblés dans l'ordre de la liste : vidéos avec vidéos, audio avec audio.",
        "Sie werden in der Reihenfolge der Liste verbunden: Videos mit Videos, Audio mit Audio.");
    public static string JoinHint => T(
        "Un unico file con tutti, nell'ordine dell'elenco: usa le frecce per cambiarlo. Se i file sono uguali (stesso telefono, stesso formato) ci vogliono pochi secondi; se no vengono adattati al primo, e ci vuole di più.",
        "A single file with all of them, in the order of the list: use the arrows to change it. Files that are alike (same phone, same format) take a few seconds; otherwise they are fitted to the first one, which takes longer.",
        "Un único archivo con todos, en el orden de la lista: usa las flechas para cambiarlo. Si los archivos son iguales (mismo teléfono, mismo formato) tarda unos segundos; si no, se adaptan al primero y tarda más.",
        "Un seul fichier avec tous, dans l'ordre de la liste : utilisez les flèches pour le changer. Des fichiers semblables (même téléphone, même format) prennent quelques secondes ; sinon ils sont adaptés au premier, ce qui prend plus de temps.",
        "Eine einzige Datei mit allen, in der Reihenfolge der Liste: Ändere sie mit den Pfeilen. Gleichartige Dateien (gleiches Handy, gleiches Format) dauern ein paar Sekunden; sonst werden sie an die erste angepasst, das dauert länger.");
    public static string JoinButton => T("Unisci", "Join", "Unir", "Assembler", "Verbinden");

    // ---- settings --------------------------------------------------------------------------

    public static string SettingsTitle => T("Impostazioni", "Settings", "Configuración", "Paramètres", "Einstellungen");
    public static string SettingsSubtitle => Linux
        ? T("Dove finiscono i file e come trovare Akses nel gestore dei file.", "Where your files go and how to reach Akses from the file manager.", "Dónde van los archivos y cómo usar Akses desde el gestor de archivos.", "Où vont les fichiers et comment utiliser Akses depuis le gestionnaire de fichiers.", "Wohin die Dateien kommen und wie du Akses im Dateimanager findest.")
        : T("Dove finiscono i file e come trovare Akses in Esplora file.", "Where your files go and how to reach Akses from File Explorer.", "Dónde van los archivos y cómo usar Akses desde el Explorador.", "Où vont les fichiers et comment utiliser Akses depuis l'Explorateur.", "Wohin die Dateien kommen und wie du Akses im Explorer findest.");
    public static string WhereToSave => T("Dove salvare i file", "Where to save files", "Dónde guardar los archivos", "Où enregistrer les fichiers", "Speicherort");
    public static string SaveInFolder => T("Sempre nella stessa cartella", "Always in the same folder", "Siempre en la misma carpeta", "Toujours dans le même dossier", "Immer im selben Ordner");
    public static string NextToOriginalHint => T("Il nuovo file compare accanto all'originale, che non viene mai toccato.", "The new file appears next to the original, which is never touched.", "El archivo nuevo aparece junto al original, que nunca se toca.", "Le nouveau fichier apparaît à côté de l'original, qui n'est jamais modifié.", "Die neue Datei erscheint neben dem Original, das nie verändert wird.");
    public static string ExplorerMenu => T("Menu del tasto destro", "Right-click menu", "Menú del clic derecho", "Menu du clic droit", "Kontextmenü");
    public static string ExplorerMenuHint => Linux ? T(
        "Fai clic destro su un file nel gestore dei file (Nemo, su Linux Mint) e scegli «Apri con Akses»: si apre la sua pagina con tutto quello che puoi farci. Negli altri gestori di file Akses è in «Apri con».",
        "Right-click a file in the file manager (Nemo, on Linux Mint) and choose \"Open with Akses\": its page opens with everything you can do with it. In other file managers Akses is under \"Open With\".",
        "Haz clic derecho en un archivo en el gestor de archivos (Nemo, en Linux Mint) y elige «Abrir con Akses»: se abre su página con todo lo que puedes hacer con él. En otros gestores Akses está en «Abrir con».",
        "Faites un clic droit sur un fichier dans le gestionnaire de fichiers (Nemo, sous Linux Mint) et choisissez « Ouvrir avec Akses » : sa page s'ouvre avec tout ce que vous pouvez en faire. Dans les autres gestionnaires, Akses est dans « Ouvrir avec ».",
        "Klicke im Dateimanager (Nemo, unter Linux Mint) mit der rechten Maustaste auf eine Datei und wähle „Mit Akses öffnen“: Ihre Seite öffnet sich mit allem, was du damit machen kannst. In anderen Dateimanagern findest du Akses unter „Öffnen mit“.") : T(
        "Fai clic destro su un file in Esplora file e scegli «Apri con Akses»: si apre la sua pagina con tutto quello che puoi farci. Su Windows 11 la voce è in «Mostra altre opzioni».",
        "Right-click a file in File Explorer and choose \"Open with Akses\": its page opens with everything you can do with it. On Windows 11 it's under \"Show more options\".",
        "Haz clic derecho en un archivo en el Explorador y elige «Abrir con Akses»: se abre su página con todo lo que puedes hacer con él. En Windows 11 está en «Mostrar más opciones».",
        "Faites un clic droit sur un fichier dans l'Explorateur et choisissez « Ouvrir avec Akses » : sa page s'ouvre avec tout ce que vous pouvez en faire. Sous Windows 11, c'est dans « Afficher d'autres options ».",
        "Klicke im Explorer mit der rechten Maustaste auf eine Datei und wähle „Mit Akses öffnen“: Ihre Seite öffnet sich mit allem, was du damit machen kannst. Unter Windows 11 findest du es unter „Weitere Optionen anzeigen“.");
    public static string About => T("Informazioni", "About", "Información", "À propos", "Info");
    public static string AboutText => T(
        "Akses è gratis e software libero: niente account, niente pubblicità, e i tuoi file non lasciano mai il PC.",
        "Akses is free, open-source software: no accounts, no ads, and your files never leave your PC.",
        "Akses es gratuito y software libre: sin cuentas, sin publicidad, y tus archivos nunca salen del PC.",
        "Akses est gratuit et libre : pas de compte, pas de publicité, et vos fichiers ne quittent jamais le PC.",
        "Akses ist kostenlos und freie Software: keine Konten, keine Werbung, und deine Dateien verlassen nie den PC.");

    // ---- signature + "Privacy, licenza e contatti" -----------------------------------------

    public static string LegalTitle => T("Privacy, licenza e contatti", "Privacy, license and contacts", "Privacidad, licencia y contacto", "Confidentialité, licence et contacts", "Datenschutz, Lizenz und Kontakt");
    public static string VersionLabel => F(T("Versione {0}", "Version {0}", "Versión {0}", "Version {0}", "Version {0}"), AppVersion);
    public static string CopyrightLine => F(T(
        "© {0} M-Tre Consulting. Distribuito sotto licenza GNU GPL v3.",
        "© {0} M-Tre Consulting. Distributed under the GNU GPL v3.",
        "© {0} M-Tre Consulting. Distribuido bajo la licencia GNU GPL v3.",
        "© {0} M-Tre Consulting. Distribué sous licence GNU GPL v3.",
        "© {0} M-Tre Consulting. Veröffentlicht unter der GNU GPL v3."), Year);

    public static string PrivacyHeading => T("Informativa sulla privacy", "Privacy policy", "Política de privacidad", "Politique de confidentialité", "Datenschutzerklärung");
    public static string[] PrivacyParagraphs => Linux ? LinuxPrivacyParagraphs :
    [
        PrivacyNoData,
        T("L'app legge solo i file che scegli, trascini o apri dal menu di Esplora file, e scrive i risultati accanto all'originale o nella cartella che indichi. Si collega a internet solo in tre casi. Per gli aggiornamenti (si possono spegnere in Impostazioni), all'avvio e ogni 6 ore chiede al nostro sito mtre-consulting.it qual è l'ultima versione e, se ce n'è una nuova, la scarica da GitHub: il sito e GitHub vedono l'indirizzo IP del tuo PC e la versione di Akses, nient'altro. La prima volta che converti un documento (Word, Markdown, ePub…), dopo avertelo chiesto, scarica da GitHub il componente pandoc: GitHub riceve la richiesta del tuo PC, come quando scarichi un file dal browser. E se un documento che converti contiene immagini prese da un indirizzo web, per inserirle nel PDF o nell'HTML queste vengono scaricate da quell'indirizzo, come farebbe un browser.",
          "The app only reads the files you choose, drop or open from the File Explorer menu, and writes the results next to the original or to the folder you set. It connects to the internet in three cases only. For updates (they can be turned off in Settings), at start and every 6 hours it asks our website mtre-consulting.it which version is the latest and, if there is a new one, downloads it from GitHub: the website and GitHub see your PC's IP address and the version of Akses, nothing else. The first time you convert a document (Word, Markdown, ePub…), after asking you, it downloads the pandoc component from GitHub: GitHub receives your PC's request, as when you download a file in the browser. And if a document you convert contains images from a web address, they are downloaded from that address to put them in the PDF or HTML, as a browser would.",
          "La app solo lee los archivos que eliges, arrastras o abres desde el menú del Explorador, y escribe los resultados junto al original o en la carpeta que indiques. Solo se conecta a internet en tres casos. Para las actualizaciones (se pueden desactivar en Configuración), al iniciar y cada 6 horas pregunta a nuestro sitio mtre-consulting.it cuál es la última versión y, si hay una nueva, la descarga de GitHub: el sitio y GitHub ven la dirección IP de tu PC y la versión de Akses, nada más. La primera vez que conviertes un documento (Word, Markdown, ePub…), después de preguntarte, descarga de GitHub el componente pandoc: GitHub recibe la solicitud de tu PC, como cuando descargas un archivo en el navegador. Y si un documento que conviertes contiene imágenes de una dirección web, se descargan de esa dirección para ponerlas en el PDF o el HTML, como haría un navegador.",
          "L'app lit uniquement les fichiers que vous choisissez, déposez ou ouvrez depuis le menu de l'Explorateur, et écrit les résultats à côté de l'original ou dans le dossier indiqué. Elle ne se connecte à Internet que dans trois cas. Pour les mises à jour (désactivables dans les Paramètres), au démarrage et toutes les 6 heures, elle demande à notre site mtre-consulting.it quelle est la dernière version et, s'il y en a une nouvelle, la télécharge depuis GitHub : le site et GitHub voient l'adresse IP de votre PC et la version d'Akses, rien d'autre. La première fois que vous convertissez un document (Word, Markdown, ePub…), après vous l'avoir demandé, elle télécharge depuis GitHub le composant pandoc : GitHub reçoit la requête de votre PC, comme lorsque vous téléchargez un fichier dans le navigateur. Et si un document converti contient des images provenant d'une adresse web, elles sont téléchargées depuis cette adresse pour les insérer dans le PDF ou le HTML, comme le ferait un navigateur.",
          "Die App liest nur die Dateien, die du auswählst, hineinziehst oder über das Explorer-Menü öffnest, und schreibt die Ergebnisse neben das Original oder in den gewählten Ordner. Sie verbindet sich nur in drei Fällen mit dem Internet. Für Updates (in den Einstellungen abschaltbar) fragt sie beim Start und alle 6 Stunden unsere Website mtre-consulting.it nach der neuesten Version und lädt eine neue von GitHub herunter: Die Website und GitHub sehen die IP-Adresse deines PCs und die Akses-Version, sonst nichts. Wenn du zum ersten Mal ein Dokument konvertierst (Word, Markdown, ePub…), lädt sie nach Rückfrage die Komponente pandoc von GitHub herunter: GitHub erhält die Anfrage deines PCs, wie beim Herunterladen einer Datei im Browser. Und enthält ein Dokument, das du konvertierst, Bilder von einer Webadresse, werden sie von dort heruntergeladen, um sie ins PDF oder HTML einzufügen, wie es ein Browser täte."),
        T("Sul PC l'app conserva solo: le impostazioni (qualità, dimensioni, cartella di destinazione) e un registro tecnico degli errori, che contiene i nomi e i percorsi dei file che non è riuscita a convertire, in %LOCALAPPDATA%\\Akses insieme ai dati del motore che stampa i documenti in PDF (WebView2 di Windows) e, se l'hai scaricato, al componente pandoc; anteprime temporanee e l'aggiornamento scaricato in %TEMP%\\Akses, cancellati a ogni avvio; e, se attivo, il menu «Apri con Akses» nel registro di sistema dell'utente.",
          "On the PC the app keeps only: the settings (quality, size, output folder) and a technical error log, which contains the names and paths of files it couldn't convert, in %LOCALAPPDATA%\\Akses together with the data of the engine that prints documents to PDF (Windows' WebView2) and, if you downloaded it, the pandoc component; temporary previews and the downloaded update in %TEMP%\\Akses, deleted at every start; and, if turned on, the \"Open with Akses\" menu in the user's registry.",
          "En el PC la app guarda solo: los ajustes (calidad, tamaño, carpeta de destino) y un registro técnico de errores, que contiene los nombres y rutas de los archivos que no ha podido convertir, en %LOCALAPPDATA%\\Akses junto con los datos del motor que imprime los documentos en PDF (WebView2 de Windows) y, si lo has descargado, el componente pandoc; vistas previas temporales y la actualización descargada en %TEMP%\\Akses, borradas en cada inicio; y, si está activado, el menú «Abrir con Akses» en el registro del usuario.",
          "Sur le PC, l'app conserve uniquement : les réglages (qualité, taille, dossier de destination) et un journal technique des erreurs, qui contient les noms et chemins des fichiers qu'elle n'a pas pu convertir, dans %LOCALAPPDATA%\\Akses avec les données du moteur qui imprime les documents en PDF (WebView2 de Windows) et, si vous l'avez téléchargé, le composant pandoc ; des aperçus temporaires et la mise à jour téléchargée dans %TEMP%\\Akses, effacés à chaque démarrage ; et, s'il est activé, le menu « Ouvrir avec Akses » dans le registre de l'utilisateur.",
          "Auf dem PC speichert die App nur: die Einstellungen (Qualität, Größe, Zielordner) und ein technisches Fehlerprotokoll mit Namen und Pfaden der Dateien, die sie nicht konvertieren konnte, in %LOCALAPPDATA%\\Akses zusammen mit den Daten der Komponente, die Dokumente als PDF druckt (WebView2 von Windows), und, falls heruntergeladen, die Komponente pandoc; temporäre Vorschauen und das heruntergeladene Update in %TEMP%\\Akses, die bei jedem Start gelöscht werden; und, falls eingeschaltet, das Menü „Mit Akses öffnen“ in der Registrierung des Benutzers."),
        F(T("Puoi chiederci informazioni ed esercitare i diritti previsti dal GDPR (Regolamento UE 2016/679) scrivendo a {0}. Poiché non riceviamo i tuoi dati, per cancellare tutto basta disinstallare l'app: la disinstallazione rimuove anche i dati elencati sopra. I file che hai convertito restano tuoi, dove li hai salvati.",
            "You can ask us for information and exercise your rights under the GDPR (EU Regulation 2016/679) by writing to {0}. Since we never receive your data, uninstalling the app deletes everything: the uninstaller also removes the data listed above. The files you converted remain yours, where you saved them.",
            "Puedes pedirnos información y ejercer los derechos previstos por el RGPD (Reglamento UE 2016/679) escribiendo a {0}. Como no recibimos tus datos, para borrarlo todo basta con desinstalar la app: la desinstalación elimina también los datos indicados arriba. Los archivos que has convertido siguen siendo tuyos, donde los guardaste.",
            "Vous pouvez nous demander des informations et exercer vos droits prévus par le RGPD (règlement UE 2016/679) en écrivant à {0}. Comme nous ne recevons pas vos données, il suffit de désinstaller l'app pour tout effacer : la désinstallation supprime aussi les données listées ci-dessus. Les fichiers convertis restent à vous, là où vous les avez enregistrés.",
            "Du kannst Auskunft verlangen und deine Rechte nach der DSGVO (EU-Verordnung 2016/679) ausüben, indem du an {0} schreibst. Da wir deine Daten nie erhalten, genügt es, die App zu deinstallieren: Die Deinstallation entfernt auch die oben genannten Daten. Die konvertierten Dateien bleiben deine, dort, wo du sie gespeichert hast."), CompanyEmail),
    ];

    private static string PrivacyNoData =>
        T("Akses non raccoglie dati personali e non ne invia a noi né ad altri: niente account, pubblicità, statistiche o tracciamento. Tutte le conversioni avvengono sul tuo PC e i tuoi file non vengono mai caricati da nessuna parte.",
          "Akses collects no personal data and sends none to us or to anyone else: no accounts, ads, analytics or tracking. Every conversion happens on your PC and your files are never uploaded anywhere.",
          "Akses no recoge datos personales ni los envía a nosotros ni a terceros: sin cuentas, publicidad, estadísticas ni seguimiento. Todas las conversiones se hacen en tu PC y tus archivos nunca se suben a ningún sitio.",
          "Akses ne collecte aucune donnée personnelle et n'en envoie ni à nous ni à des tiers : pas de compte, de publicité, de statistiques ni de suivi. Toutes les conversions ont lieu sur votre PC et vos fichiers ne sont jamais envoyés nulle part.",
          "Akses erhebt keine personenbezogenen Daten und sendet keine an uns oder Dritte: keine Konten, Werbung, Statistiken oder Tracking. Alle Konvertierungen laufen auf deinem PC, und deine Dateien werden nirgendwohin hochgeladen.");

    private static string[] LinuxPrivacyParagraphs =>
    [
        PrivacyNoData,
        T("L'app legge solo i file che scegli, trascini o apri dal menu del gestore dei file, e scrive i risultati accanto all'originale o nella cartella che indichi. Si collega a internet solo in tre casi. Per gli aggiornamenti (si possono spegnere in Impostazioni), all'avvio e ogni 6 ore chiede al nostro sito mtre-consulting.it qual è l'ultima versione e, se ce n'è una nuova, la scarica da GitHub: il sito e GitHub vedono l'indirizzo IP del tuo PC e la versione di Akses, nient'altro. La prima volta che converti un documento (Word, Markdown, ePub…), dopo avertelo chiesto, scarica da GitHub il componente pandoc: GitHub riceve la richiesta del tuo PC, come quando scarichi un file dal browser. E se un documento che converti contiene immagini prese da un indirizzo web, per inserirle nel PDF o nell'HTML queste vengono scaricate da quell'indirizzo, come farebbe un browser.",
          "The app only reads the files you choose, drop or open from the file manager's menu, and writes the results next to the original or to the folder you set. It connects to the internet in three cases only. For updates (they can be turned off in Settings), at start and every 6 hours it asks our website mtre-consulting.it which version is the latest and, if there is a new one, downloads it from GitHub: the website and GitHub see your PC's IP address and the version of Akses, nothing else. The first time you convert a document (Word, Markdown, ePub…), after asking you, it downloads the pandoc component from GitHub: GitHub receives your PC's request, as when you download a file in the browser. And if a document you convert contains images from a web address, they are downloaded from that address to put them in the PDF or HTML, as a browser would.",
          "La app solo lee los archivos que eliges, arrastras o abres desde el menú del gestor de archivos, y escribe los resultados junto al original o en la carpeta que indiques. Solo se conecta a internet en tres casos. Para las actualizaciones (se pueden desactivar en Configuración), al iniciar y cada 6 horas pregunta a nuestro sitio mtre-consulting.it cuál es la última versión y, si hay una nueva, la descarga de GitHub: el sitio y GitHub ven la dirección IP de tu PC y la versión de Akses, nada más. La primera vez que conviertes un documento (Word, Markdown, ePub…), después de preguntarte, descarga de GitHub el componente pandoc: GitHub recibe la solicitud de tu PC, como cuando descargas un archivo en el navegador. Y si un documento que conviertes contiene imágenes de una dirección web, se descargan de esa dirección para ponerlas en el PDF o el HTML, como haría un navegador.",
          "L'app lit uniquement les fichiers que vous choisissez, déposez ou ouvrez depuis le menu du gestionnaire de fichiers, et écrit les résultats à côté de l'original ou dans le dossier indiqué. Elle ne se connecte à Internet que dans trois cas. Pour les mises à jour (désactivables dans les Paramètres), au démarrage et toutes les 6 heures, elle demande à notre site mtre-consulting.it quelle est la dernière version et, s'il y en a une nouvelle, la télécharge depuis GitHub : le site et GitHub voient l'adresse IP de votre PC et la version d'Akses, rien d'autre. La première fois que vous convertissez un document (Word, Markdown, ePub…), après vous l'avoir demandé, elle télécharge depuis GitHub le composant pandoc : GitHub reçoit la requête de votre PC, comme lorsque vous téléchargez un fichier dans le navigateur. Et si un document converti contient des images provenant d'une adresse web, elles sont téléchargées depuis cette adresse pour les insérer dans le PDF ou le HTML, comme le ferait un navigateur.",
          "Die App liest nur die Dateien, die du auswählst, hineinziehst oder über das Menü des Dateimanagers öffnest, und schreibt die Ergebnisse neben das Original oder in den gewählten Ordner. Sie verbindet sich nur in drei Fällen mit dem Internet. Für Updates (in den Einstellungen abschaltbar) fragt sie beim Start und alle 6 Stunden unsere Website mtre-consulting.it nach der neuesten Version und lädt eine neue von GitHub herunter: Die Website und GitHub sehen die IP-Adresse deines PCs und die Akses-Version, sonst nichts. Wenn du zum ersten Mal ein Dokument konvertierst (Word, Markdown, ePub…), lädt sie nach Rückfrage die Komponente pandoc von GitHub herunter: GitHub erhält die Anfrage deines PCs, wie beim Herunterladen einer Datei im Browser. Und enthält ein Dokument, das du konvertierst, Bilder von einer Webadresse, werden sie von dort heruntergeladen, um sie ins PDF oder HTML einzufügen, wie es ein Browser täte."),
        T("Sul PC l'app conserva solo: le impostazioni (qualità, dimensioni, cartella di destinazione) e un registro tecnico degli errori, che contiene i nomi e i percorsi dei file che non è riuscita a convertire, in ~/.local/share/Akses insieme al profilo di LibreOffice usato per stampare i documenti in PDF e, se l'hai scaricato, al componente pandoc; anteprime temporanee e l'aggiornamento scaricato in /tmp/Akses, cancellati a ogni avvio; e, se attiva, la voce «Apri con Akses» del gestore dei file in ~/.local/share/nemo/actions.",
          "On the PC the app keeps only: the settings (quality, size, output folder) and a technical error log, which contains the names and paths of files it couldn't convert, in ~/.local/share/Akses together with the LibreOffice profile used to print documents to PDF and, if you downloaded it, the pandoc component; temporary previews and the downloaded update in /tmp/Akses, deleted at every start; and, if turned on, the file manager's \"Open with Akses\" entry in ~/.local/share/nemo/actions.",
          "En el PC la app guarda solo: los ajustes (calidad, tamaño, carpeta de destino) y un registro técnico de errores, que contiene los nombres y rutas de los archivos que no ha podido convertir, en ~/.local/share/Akses junto con el perfil de LibreOffice usado para imprimir los documentos en PDF y, si lo has descargado, el componente pandoc; vistas previas temporales y la actualización descargada en /tmp/Akses, borradas en cada inicio; y, si está activada, la entrada «Abrir con Akses» del gestor de archivos en ~/.local/share/nemo/actions.",
          "Sur le PC, l'app conserve uniquement : les réglages (qualité, taille, dossier de destination) et un journal technique des erreurs, qui contient les noms et chemins des fichiers qu'elle n'a pas pu convertir, dans ~/.local/share/Akses avec le profil LibreOffice utilisé pour imprimer les documents en PDF et, si vous l'avez téléchargé, le composant pandoc ; des aperçus temporaires et la mise à jour téléchargée dans /tmp/Akses, effacés à chaque démarrage ; et, si elle est activée, l'entrée « Ouvrir avec Akses » du gestionnaire de fichiers dans ~/.local/share/nemo/actions.",
          "Auf dem PC speichert die App nur: die Einstellungen (Qualität, Größe, Zielordner) und ein technisches Fehlerprotokoll mit Namen und Pfaden der Dateien, die sie nicht konvertieren konnte, in ~/.local/share/Akses zusammen mit dem LibreOffice-Profil, mit dem Dokumente als PDF gedruckt werden, und, falls heruntergeladen, der Komponente pandoc; temporäre Vorschauen und das heruntergeladene Update in /tmp/Akses, die bei jedem Start gelöscht werden; und, falls eingeschaltet, den Eintrag „Mit Akses öffnen“ des Dateimanagers in ~/.local/share/nemo/actions."),
        F(T("Puoi chiederci informazioni ed esercitare i diritti previsti dal GDPR (Regolamento UE 2016/679) scrivendo a {0}. Poiché non riceviamo i tuoi dati, per cancellare tutto basta disinstallare l'app ed eliminare la cartella ~/.local/share/Akses e, se c'è, il file akses.nemo_action in ~/.local/share/nemo/actions. I file che hai convertito restano tuoi, dove li hai salvati.",
            "You can ask us for information and exercise your rights under the GDPR (EU Regulation 2016/679) by writing to {0}. Since we never receive your data, to delete everything just uninstall the app and remove the ~/.local/share/Akses folder and, if present, the akses.nemo_action file in ~/.local/share/nemo/actions. The files you converted remain yours, where you saved them.",
            "Puedes pedirnos información y ejercer los derechos previstos por el RGPD (Reglamento UE 2016/679) escribiendo a {0}. Como no recibimos tus datos, para borrarlo todo basta con desinstalar la app y eliminar la carpeta ~/.local/share/Akses y, si existe, el archivo akses.nemo_action de ~/.local/share/nemo/actions. Los archivos que has convertido siguen siendo tuyos, donde los guardaste.",
            "Vous pouvez nous demander des informations et exercer vos droits prévus par le RGPD (règlement UE 2016/679) en écrivant à {0}. Comme nous ne recevons pas vos données, pour tout effacer il suffit de désinstaller l'app et de supprimer le dossier ~/.local/share/Akses et, s'il existe, le fichier akses.nemo_action dans ~/.local/share/nemo/actions. Les fichiers convertis restent à vous, là où vous les avez enregistrés.",
            "Du kannst Auskunft verlangen und deine Rechte nach der DSGVO (EU-Verordnung 2016/679) ausüben, indem du an {0} schreibst. Da wir deine Daten nie erhalten, genügt es, die App zu deinstallieren und den Ordner ~/.local/share/Akses sowie, falls vorhanden, die Datei akses.nemo_action in ~/.local/share/nemo/actions zu löschen. Die konvertierten Dateien bleiben deine, dort, wo du sie gespeichert hast."), CompanyEmail),
    ];

    public static string PrivacyOnSite => T(
        "Questa informativa è pubblicata anche sul nostro sito:",
        "This privacy policy is also published on our website:",
        "Esta política de privacidad también está publicada en nuestro sitio web:",
        "Cette politique de confidentialité est également publiée sur notre site :",
        "Diese Datenschutzerklärung ist auch auf unserer Website veröffentlicht:");

    public static string OwnerHeading => T("Titolare", "Owner", "Titular", "Titulaire", "Inhaber");
    public static string OwnerLine => F(T("{0}, Italia", "{0}, Italy", "{0}, Italia", "{0}, Italie", "{0}, Italien"), CompanyName);

    public static string LicenseHeading => T("Licenza", "License", "Licencia", "Licence", "Lizenz");
    public static string[] LicenseParagraphs => Linux ? LinuxLicenseParagraphs :
    [
        LicenseIntro,
        T("Akses usa: FFmpeg (GPL) compilato per Akses con x264, libvpx, dav1d, LAME, Opus e Vorbis, incluso nel programma; pandoc (GPL) di John MacFarlane, scaricato alla prima conversione di un documento; ImageMagick tramite Magick.NET (Apache 2.0); PDFsharp (MIT); PdfPig (Apache 2.0); YamlDotNet (MIT); Windows App SDK e WebView2 di Microsoft. Le loro licenze sono nelle cartelle tools (del programma e, per pandoc, in %LOCALAPPDATA%\\Akses), i dettagli nel file NOTICE.md.",
          "Akses uses: FFmpeg (GPL) built for Akses with x264, libvpx, dav1d, LAME, Opus and Vorbis, included in the program; pandoc (GPL) by John MacFarlane, downloaded on the first document conversion; ImageMagick through Magick.NET (Apache 2.0); PDFsharp (MIT); PdfPig (Apache 2.0); YamlDotNet (MIT); Microsoft's Windows App SDK and WebView2. Their licenses are in the tools folders (the program's and, for pandoc, %LOCALAPPDATA%\\Akses), details in the NOTICE.md file.",
          "Akses usa: FFmpeg (GPL) compilado para Akses con x264, libvpx, dav1d, LAME, Opus y Vorbis, incluido en el programa; pandoc (GPL) de John MacFarlane, descargado en la primera conversión de un documento; ImageMagick a través de Magick.NET (Apache 2.0); PDFsharp (MIT); PdfPig (Apache 2.0); YamlDotNet (MIT); Windows App SDK y WebView2 de Microsoft. Sus licencias están en las carpetas tools (del programa y, para pandoc, %LOCALAPPDATA%\\Akses), los detalles en el archivo NOTICE.md.",
          "Akses utilise : FFmpeg (GPL) compilé pour Akses avec x264, libvpx, dav1d, LAME, Opus et Vorbis, inclus dans le programme ; pandoc (GPL) de John MacFarlane, téléchargé à la première conversion d'un document ; ImageMagick via Magick.NET (Apache 2.0) ; PDFsharp (MIT) ; PdfPig (Apache 2.0) ; YamlDotNet (MIT) ; Windows App SDK et WebView2 de Microsoft. Leurs licences se trouvent dans les dossiers tools (du programme et, pour pandoc, %LOCALAPPDATA%\\Akses), les détails dans le fichier NOTICE.md.",
          "Akses verwendet: FFmpeg (GPL), für Akses kompiliert mit x264, libvpx, dav1d, LAME, Opus und Vorbis, im Programm enthalten; pandoc (GPL) von John MacFarlane, bei der ersten Dokumentkonvertierung heruntergeladen; ImageMagick über Magick.NET (Apache 2.0); PDFsharp (MIT); PdfPig (Apache 2.0); YamlDotNet (MIT); Windows App SDK und WebView2 von Microsoft. Ihre Lizenzen liegen in den tools-Ordnern (des Programms und, für pandoc, %LOCALAPPDATA%\\Akses), Details in der Datei NOTICE.md."),
        T("Windows ed Esplora file sono marchi di Microsoft Corporation; i nomi dei formati appartengono ai rispettivi titolari. Akses non è affiliato a Microsoft né approvato da Microsoft.",
          "Windows and File Explorer are trademarks of Microsoft Corporation; format names belong to their respective owners. Akses is not affiliated with or endorsed by Microsoft.",
          "Windows y el Explorador de archivos son marcas de Microsoft Corporation; los nombres de los formatos pertenecen a sus respectivos titulares. Akses no está afiliado a Microsoft ni cuenta con su aprobación.",
          "Windows et l'Explorateur de fichiers sont des marques de Microsoft Corporation ; les noms des formats appartiennent à leurs titulaires respectifs. Akses n'est ni affilié à Microsoft ni approuvé par Microsoft.",
          "Windows und der Datei-Explorer sind Marken der Microsoft Corporation; Formatnamen gehören ihren jeweiligen Inhabern. Akses ist nicht mit Microsoft verbunden und wird nicht von Microsoft unterstützt."),
    ];

    private static string LicenseIntro =>
        F(T("Akses è software libero di M-Tre Consulting, distribuito sotto licenza GNU GPL v3: puoi usarlo, studiarlo, modificarlo e ridistribuirlo alle condizioni della licenza, il cui testo è nel file LICENSE installato con il programma. È fornito senza alcuna garanzia. Il codice sorgente di ogni versione è pubblico su {1}; puoi anche richiederlo scrivendo a {0}.",
            "Akses is free software by M-Tre Consulting, distributed under the GNU GPL v3: you may use, study, modify and redistribute it under the terms of the license, whose text is in the LICENSE file installed with the program. It comes with no warranty. The source code of every version is public on {1}; you can also request it by writing to {0}.",
            "Akses es software libre de M-Tre Consulting, distribuido bajo la licencia GNU GPL v3: puedes usarlo, estudiarlo, modificarlo y redistribuirlo según las condiciones de la licencia, cuyo texto está en el archivo LICENSE instalado con el programa. Se ofrece sin ninguna garantía. El código fuente de cada versión es público en {1}; también puedes pedirlo escribiendo a {0}.",
            "Akses est un logiciel libre de M-Tre Consulting, distribué sous licence GNU GPL v3 : vous pouvez l'utiliser, l'étudier, le modifier et le redistribuer selon les conditions de la licence, dont le texte se trouve dans le fichier LICENSE installé avec le programme. Il est fourni sans aucune garantie. Le code source de chaque version est public sur {1} ; vous pouvez aussi le demander en écrivant à {0}.",
            "Akses ist freie Software von M-Tre Consulting, veröffentlicht unter der GNU GPL v3: Du darfst sie gemäß den Lizenzbedingungen nutzen, untersuchen, ändern und weitergeben; der Lizenztext liegt in der Datei LICENSE, die mit dem Programm installiert wird. Sie wird ohne jede Gewährleistung bereitgestellt. Der Quellcode jeder Version ist öffentlich auf {1}; du kannst ihn auch per E-Mail an {0} anfordern."), CompanyEmail, "github.com/M-Tre-Consulting/akses");

    private static string[] LinuxLicenseParagraphs =>
    [
        LicenseIntro,
        T("Akses usa: FFmpeg (GPL), poppler (GPL), VLC (LGPL) e LibreOffice (MPL 2.0), installati con i pacchetti del sistema; pandoc (GPL) di John MacFarlane, scaricato alla prima conversione di un documento; ImageMagick tramite Magick.NET (Apache 2.0); PDFsharp (MIT); PdfPig (Apache 2.0); YamlDotNet (MIT); Avalonia (MIT); LibVLCSharp (LGPL). Le licenze dei programmi del sistema sono in /usr/share/doc, i dettagli nel file NOTICE.md.",
          "Akses uses: FFmpeg (GPL), poppler (GPL), VLC (LGPL) and LibreOffice (MPL 2.0), installed with the system packages; pandoc (GPL) by John MacFarlane, downloaded on the first document conversion; ImageMagick through Magick.NET (Apache 2.0); PDFsharp (MIT); PdfPig (Apache 2.0); YamlDotNet (MIT); Avalonia (MIT); LibVLCSharp (LGPL). The licenses of the system programs are in /usr/share/doc, details in the NOTICE.md file.",
          "Akses usa: FFmpeg (GPL), poppler (GPL), VLC (LGPL) y LibreOffice (MPL 2.0), instalados con los paquetes del sistema; pandoc (GPL) de John MacFarlane, descargado en la primera conversión de un documento; ImageMagick a través de Magick.NET (Apache 2.0); PDFsharp (MIT); PdfPig (Apache 2.0); YamlDotNet (MIT); Avalonia (MIT); LibVLCSharp (LGPL). Las licencias de los programas del sistema están en /usr/share/doc, los detalles en el archivo NOTICE.md.",
          "Akses utilise : FFmpeg (GPL), poppler (GPL), VLC (LGPL) et LibreOffice (MPL 2.0), installés avec les paquets du système ; pandoc (GPL) de John MacFarlane, téléchargé à la première conversion d'un document ; ImageMagick via Magick.NET (Apache 2.0) ; PDFsharp (MIT) ; PdfPig (Apache 2.0) ; YamlDotNet (MIT) ; Avalonia (MIT) ; LibVLCSharp (LGPL). Les licences des programmes du système se trouvent dans /usr/share/doc, les détails dans le fichier NOTICE.md.",
          "Akses verwendet: FFmpeg (GPL), poppler (GPL), VLC (LGPL) und LibreOffice (MPL 2.0), mit den Systempaketen installiert; pandoc (GPL) von John MacFarlane, bei der ersten Dokumentkonvertierung heruntergeladen; ImageMagick über Magick.NET (Apache 2.0); PDFsharp (MIT); PdfPig (Apache 2.0); YamlDotNet (MIT); Avalonia (MIT); LibVLCSharp (LGPL). Die Lizenzen der Systemprogramme liegen in /usr/share/doc, Details in der Datei NOTICE.md."),
        T("Linux è un marchio registrato di Linus Torvalds; i nomi dei formati appartengono ai rispettivi titolari.",
          "Linux is a registered trademark of Linus Torvalds; format names belong to their respective owners.",
          "Linux es una marca registrada de Linus Torvalds; los nombres de los formatos pertenecen a sus respectivos titulares.",
          "Linux est une marque déposée de Linus Torvalds ; les noms des formats appartiennent à leurs titulaires respectifs.",
          "Linux ist eine eingetragene Marke von Linus Torvalds; Formatnamen gehören ihren jeweiligen Inhabern."),
    ];

    public static string ContactsHeading => T("Contatti", "Contacts", "Contacto", "Contacts", "Kontakt");
    public static string LastUpdated => T("Aggiornata il 26 ottobre 2026.", "Last updated on 26 October 2026.", "Actualizada el 26 de octubre de 2026.", "Mise à jour le 26 octobre 2026.", "Aktualisiert am 26. Oktober 2026.");
}
