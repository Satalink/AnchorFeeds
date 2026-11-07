using System.Globalization;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;

namespace AnchorFeeds.Services;

public static class LocalizationService
{
    private static readonly string[] Languages =
    [
        "en", "cs", "de", "es", "fr", "it", "ja", "ko", "pl", "pt-BR", "ru", "tr", "zh-Hans", "zh-Hant"
    ];
    private static readonly string[] LanguageNames =
    [
        "English", "Čeština", "Deutsch", "Español", "Français", "Italiano", "日本語", "한국어", "Polski",
        "Português (Brasil)", "Русский", "Türkçe", "简体中文", "繁體中文"
    ];

    private static readonly string[] EnglishText =
    [
        "File", "Exit", "View", "Unplayed only", "Show article text", "Settings",
        "Configure all settings...", "Rebuild Library...", "Playback", "Playback speed",
        "Auto Play Next", "Mark as played", "When started", "When ended or skipped",
        "Retrieve Latest Feeds", "Mark all read", "Audio library",
        "Delete article audio and saved details", "Toggle article star",
        "Drag to resize the article list and player", "Select an article to listen",
        "Star article", "Article text", "Open article in browser", "Play", "Pause",
        "Next / Skip", "Cancel", "Save", "Feeds", "RSS or Atom feeds (one URL per line)",
        "Maximum new articles per feed", "Paths", "Media output directory",
        "Article audio filename template", "Audio", "Audio retention", "MP3 bitrate",
        "Show article text in player view", "Enable Unplayed only when the app starts",
        "Anchor(s) (select multiple voices)", "Sample", "Loading...", "Playing",
        "Stop sample", "Settings help", "Invalid settings", "RSS or Atom feeds",
        "Playback speed", "When an article starts", "When an article ends or is skipped"
    ];

    private static readonly Dictionary<string, string[]> Translations = new(StringComparer.OrdinalIgnoreCase)
    {
        ["cs"] =
        [
            "Soubor", "Konec", "Zobrazení", "Pouze nepřehrané", "Zobrazit text článku", "Nastavení",
            "Konfigurovat všechna nastavení...", "Znovu sestavit knihovnu...", "Přehrávání", "Rychlost přehrávání",
            "Automaticky přehrát další", "Označit jako přečtené", "Při spuštění", "Po dokončení nebo přeskočení",
            "Načíst nejnovější kanály", "Označit vše jako přečtené", "Knihovna zvuku",
            "Smazat zvuk článku a uložené údaje", "Přepnout hvězdičku článku",
            "Tažením změňte velikost seznamu článků a přehrávače", "Vyberte článek k poslechu",
            "Označit článek hvězdičkou", "Text článku", "Otevřít článek v prohlížeči", "Přehrát", "Pozastavit",
            "Další / přeskočit", "Zrušit", "Uložit", "Kanály", "RSS nebo Atom kanály (jedna adresa na řádek)",
            "Maximum nových článků na kanál", "Cesty", "Složka pro výstup médií",
            "Šablona názvu zvukového souboru", "Zvuk", "Uchovávání zvuku", "Datový tok MP3",
            "Zobrazit text článku v přehrávači", "Při spuštění zapnout filtr nepřehraných",
            "Moderátor(ři) (vyberte více hlasů)", "Ukázka", "Načítání...", "Přehrávání",
            "Zastavit ukázku", "Nápověda k nastavení", "Neplatné nastavení", "RSS kanály",
            "Rychlost přehrávání", "Při spuštění článku", "Po skončení nebo přeskočení článku"
        ],
        ["de"] =
        [
            "Datei", "Beenden", "Ansicht", "Nur ungelesene", "Artikeltext anzeigen", "Einstellungen",
            "Alle Einstellungen konfigurieren...", "Bibliothek neu aufbauen...", "Wiedergabe", "Wiedergabegeschwindigkeit",
            "Nächsten automatisch abspielen", "Als gelesen markieren", "Beim Start", "Nach Ende oder Überspringen",
            "Neueste Feeds abrufen", "Alle als gelesen markieren", "Audiobibliothek",
            "Artikelaudio und gespeicherte Details löschen", "Artikel-Stern umschalten",
            "Ziehen, um Artikelliste und Player anzupassen", "Artikel zum Anhören auswählen",
            "Artikel markieren", "Artikeltext", "Artikel im Browser öffnen", "Wiedergabe", "Pause",
            "Weiter / Überspringen", "Abbrechen", "Speichern", "Feeds", "RSS- oder Atom-Feeds (eine URL pro Zeile)",
            "Maximale neue Artikel pro Feed", "Pfade", "Medienausgabeordner",
            "Dateinamenvorlage für Artikelaudio", "Audio", "Audiospeicherdauer", "MP3-Bitrate",
            "Artikeltext im Player anzeigen", "Beim Start „Nur ungelesene“ aktivieren",
            "Sprecher (mehrere Stimmen auswählen)", "Probe", "Wird geladen...", "Wiedergabe",
            "Probe stoppen", "Einstellungen – Hilfe", "Ungültige Einstellungen", "RSS-Feeds",
            "Wiedergabegeschwindigkeit", "Beim Start des Artikels", "Nach Ende oder Überspringen des Artikels"
        ],
        ["es"] =
        [
            "Archivo", "Salir", "Ver", "Solo no reproducidos", "Mostrar texto del artículo", "Configuración",
            "Configurar todos los ajustes...", "Reconstruir biblioteca...", "Reproducción", "Velocidad de reproducción",
            "Reproducir siguiente automáticamente", "Marcar como leído", "Al iniciar", "Al terminar o saltar",
            "Obtener las noticias más recientes", "Marcar todo como leído", "Biblioteca de audio",
            "Eliminar audio y datos guardados del artículo", "Cambiar estrella del artículo",
            "Arrastre para cambiar el tamaño de la lista y el reproductor", "Seleccione un artículo para escuchar",
            "Marcar artículo", "Texto del artículo", "Abrir artículo en el navegador", "Reproducir", "Pausar",
            "Siguiente / Saltar", "Cancelar", "Guardar", "Fuentes", "Fuentes RSS o Atom (una URL por línea)",
            "Máximo de artículos nuevos por fuente", "Rutas", "Carpeta de salida de medios",
            "Plantilla del nombre de archivo de audio", "Audio", "Retención de audio", "Tasa de bits MP3",
            "Mostrar texto del artículo en el reproductor", "Activar «Solo no reproducidos» al iniciar",
            "Voz/ces (seleccione varias)", "Muestra", "Cargando...", "Reproduciendo",
            "Detener muestra", "Ayuda de configuración", "Configuración no válida", "Fuentes RSS",
            "Velocidad de reproducción", "Al comenzar el artículo", "Al terminar o saltar el artículo"
        ],
        ["fr"] =
        [
            "Fichier", "Quitter", "Affichage", "Non lus uniquement", "Afficher le texte de l’article", "Paramètres",
            "Configurer tous les paramètres...", "Reconstruire la bibliothèque...", "Lecture", "Vitesse de lecture",
            "Lire le suivant automatiquement", "Marquer comme lu", "Au démarrage", "À la fin ou après avoir passé",
            "Récupérer les derniers flux", "Tout marquer comme lu", "Bibliothèque audio",
            "Supprimer l’audio et les détails enregistrés de l’article", "Activer/désactiver l’étoile",
            "Faites glisser pour redimensionner la liste et le lecteur", "Sélectionnez un article à écouter",
            "Ajouter une étoile", "Texte de l’article", "Ouvrir l’article dans le navigateur", "Lire", "Pause",
            "Suivant / Passer", "Annuler", "Enregistrer", "Flux", "Flux RSS ou Atom (une URL par ligne)",
            "Nombre maximal de nouveaux articles par flux", "Chemins", "Dossier de sortie des médias",
            "Modèle du nom de fichier audio", "Audio", "Durée de conservation audio", "Débit MP3",
            "Afficher le texte de l’article dans le lecteur", "Activer « Non lus uniquement » au démarrage",
            "Voix (sélection multiple)", "Écouter", "Chargement...", "Lecture",
            "Arrêter l’échantillon", "Aide des paramètres", "Paramètres non valides", "Flux RSS",
            "Vitesse de lecture", "Au début de l’article", "À la fin ou après avoir passé l’article"
        ],
        ["it"] =
        [
            "File", "Esci", "Visualizza", "Solo non riprodotti", "Mostra testo dell’articolo", "Impostazioni",
            "Configura tutte le impostazioni...", "Ricostruisci libreria...", "Riproduzione", "Velocità di riproduzione",
            "Riproduci successivo automaticamente", "Segna come letto", "All’avvio", "Al termine o dopo aver saltato",
            "Recupera i feed più recenti", "Segna tutto come letto", "Libreria audio",
            "Elimina audio e dettagli salvati dell’articolo", "Attiva/disattiva stella articolo",
            "Trascina per ridimensionare elenco e lettore", "Seleziona un articolo da ascoltare",
            "Aggiungi stella", "Testo dell’articolo", "Apri articolo nel browser", "Riproduci", "Pausa",
            "Avanti / Salta", "Annulla", "Salva", "Feed", "Feed RSS o Atom (un URL per riga)",
            "Massimo di nuovi articoli per feed", "Percorsi", "Cartella di output dei media",
            "Modello nome file audio", "Audio", "Conservazione audio", "Bitrate MP3",
            "Mostra testo articolo nel lettore", "Attiva «Solo non riprodotti» all’avvio",
            "Voci (seleziona più voci)", "Ascolta", "Caricamento...", "In riproduzione",
            "Interrompi campione", "Guida impostazioni", "Impostazioni non valide", "Feed RSS",
            "Velocità di riproduzione", "All’avvio dell’articolo", "Al termine o quando si salta l’articolo"
        ],
        ["ja"] =
        [
            "ファイル", "終了", "表示", "未再生のみ", "記事のテキストを表示", "設定",
            "すべての設定を構成...", "ライブラリを再構築...", "再生", "再生速度",
            "次の記事を自動再生", "既読にする", "再生開始時", "再生終了またはスキップ時",
            "最新フィードを取得", "すべて既読にする", "オーディオライブラリ",
            "記事の音声と保存情報を削除", "記事のスターを切り替え",
            "ドラッグして記事一覧とプレーヤーの幅を変更", "再生する記事を選択",
            "記事にスターを付ける", "記事テキスト", "ブラウザーで記事を開く", "再生", "一時停止",
            "次へ / スキップ", "キャンセル", "保存", "フィード",             "RSS または Atom フィード（1行に1つのURL）",
            "フィードごとの新規記事の最大数", "パス", "メディア出力フォルダー",
            "記事音声ファイル名テンプレート", "オーディオ", "音声の保存期間", "MP3ビットレート",
            "プレーヤーに記事テキストを表示", "起動時に「未再生のみ」を有効にする",
            "アンカー（複数の音声を選択）", "サンプル", "読み込み中...", "再生中",
            "サンプルを停止", "設定ヘルプ", "設定が無効です", "RSSフィード",
            "再生速度", "記事の再生開始時", "記事の終了またはスキップ時"
        ],
        ["ko"] =
        [
            "파일", "끝내기", "보기", "재생 안 함만", "기사 텍스트 표시", "설정",
            "모든 설정 구성...", "라이브러리 다시 빌드...", "재생", "재생 속도",
            "다음 항목 자동 재생", "읽음으로 표시", "시작할 때", "끝나거나 건너뛸 때",
            "최신 피드 가져오기", "모두 읽음으로 표시", "오디오 라이브러리",
            "기사 오디오 및 저장된 정보 삭제", "기사 별표 전환",
            "끌어서 기사 목록과 플레이어 크기 조정", "들을 기사를 선택하세요",
            "기사 별표", "기사 텍스트", "브라우저에서 기사 열기", "재생", "일시 정지",
            "다음 / 건너뛰기", "취소", "저장", "피드", "RSS 또는 Atom 피드 (한 줄에 URL 하나)",
            "피드당 새 기사 최대 개수", "경로", "미디어 출력 폴더",
            "기사 오디오 파일 이름 템플릿", "오디오", "오디오 보관 기간", "MP3 비트 전송률",
            "플레이어에 기사 텍스트 표시", "시작할 때 ‘재생 안 함만’ 사용",
            "앵커 (여러 음성 선택)", "샘플", "불러오는 중...", "재생 중",
            "샘플 중지", "설정 도움말", "잘못된 설정", "RSS 피드",
            "재생 속도", "기사 재생 시작 시", "기사 종료 또는 건너뛸 때"
        ],
        ["pl"] =
        [
            "Plik", "Zakończ", "Widok", "Tylko nieodtworzone", "Pokaż tekst artykułu", "Ustawienia",
            "Konfiguruj wszystkie ustawienia...", "Odbuduj bibliotekę...", "Odtwarzanie", "Szybkość odtwarzania",
            "Automatycznie odtwórz następny", "Oznacz jako przeczytane", "Po rozpoczęciu", "Po zakończeniu lub pominięciu",
            "Pobierz najnowsze kanały", "Oznacz wszystko jako przeczytane", "Biblioteka audio",
            "Usuń dźwięk artykułu i zapisane szczegóły", "Przełącz gwiazdkę artykułu",
            "Przeciągnij, aby zmienić szerokość listy i odtwarzacza", "Wybierz artykuł do odsłuchania",
            "Dodaj gwiazdkę", "Tekst artykułu", "Otwórz artykuł w przeglądarce", "Odtwórz", "Wstrzymaj",
            "Dalej / Pomiń", "Anuluj", "Zapisz", "Kanały", "Kanały RSS lub Atom (jeden adres URL w wierszu)",
            "Maksymalna liczba nowych artykułów na kanał", "Ścieżki", "Folder wyjściowy mediów",
            "Szablon nazwy pliku audio", "Audio", "Czas przechowywania audio", "Przepływność MP3",
            "Pokaż tekst artykułu w odtwarzaczu", "Włącz filtr nieodtworzonych przy uruchomieniu",
            "Głosy (wybierz wiele)", "Próbka", "Ładowanie...", "Odtwarzanie",
            "Zatrzymaj próbkę", "Pomoc ustawień", "Nieprawidłowe ustawienia", "Kanały RSS",
            "Szybkość odtwarzania", "Po rozpoczęciu artykułu", "Po zakończeniu lub pominięciu artykułu"
        ],
        ["pt-BR"] =
        [
            "Arquivo", "Sair", "Exibir", "Somente não reproduzidos", "Mostrar texto do artigo", "Configurações",
            "Configurar todas as opções...", "Reconstruir biblioteca...", "Reprodução", "Velocidade de reprodução",
            "Reproduzir próximo automaticamente", "Marcar como lido", "Ao iniciar", "Ao terminar ou pular",
            "Buscar feeds mais recentes", "Marcar tudo como lido", "Biblioteca de áudio",
            "Excluir áudio e detalhes salvos do artigo", "Alternar estrela do artigo",
            "Arraste para redimensionar a lista e o player", "Selecione um artigo para ouvir",
            "Marcar artigo", "Texto do artigo", "Abrir artigo no navegador", "Reproduzir", "Pausar",
            "Próximo / Pular", "Cancelar", "Salvar", "Feeds", "Feeds RSS ou Atom (uma URL por linha)",
            "Máximo de novos artigos por feed", "Caminhos", "Pasta de saída de mídia",
            "Modelo do nome do arquivo de áudio", "Áudio", "Retenção de áudio", "Taxa de bits MP3",
            "Mostrar texto do artigo no player", "Ativar «Somente não reproduzidos» ao iniciar",
            "Vozes (selecione várias)", "Amostra", "Carregando...", "Reproduzindo",
            "Parar amostra", "Ajuda das configurações", "Configurações inválidas", "Feeds RSS",
            "Velocidade de reprodução", "Ao iniciar o artigo", "Ao terminar ou pular o artigo"
        ],
        ["ru"] =
        [
            "Файл", "Выход", "Вид", "Только непрослушанные", "Показывать текст статьи", "Настройки",
            "Настроить все параметры...", "Перестроить библиотеку...", "Воспроизведение", "Скорость воспроизведения",
            "Автоматически включать следующую", "Отметить как прочитанное", "При запуске", "После окончания или пропуска",
            "Получить последние ленты", "Отметить всё как прочитанное", "Аудиотека",
            "Удалить аудио статьи и сохранённые сведения", "Переключить звезду статьи",
            "Перетащите, чтобы изменить размер списка и проигрывателя", "Выберите статью для прослушивания",
            "Добавить звезду", "Текст статьи", "Открыть статью в браузере", "Воспроизвести", "Пауза",
            "Далее / Пропустить", "Отмена", "Сохранить", "Ленты", "RSS- или Atom-ленты (по одному URL в строке)",
            "Максимум новых статей на ленту", "Пути", "Папка вывода медиа",
            "Шаблон имени аудиофайла", "Аудио", "Срок хранения аудио", "Битрейт MP3",
            "Показывать текст статьи в проигрывателе", "Включать фильтр непрослушанных при запуске",
            "Дикторы (можно выбрать несколько)", "Образец", "Загрузка...", "Воспроизведение",
            "Остановить образец", "Справка по настройкам", "Неверные настройки", "RSS-ленты",
            "Скорость воспроизведения", "При начале статьи", "После окончания или пропуска статьи"
        ],
        ["tr"] =
        [
            "Dosya", "Çıkış", "Görünüm", "Yalnızca oynatılmayanlar", "Makale metnini göster", "Ayarlar",
            "Tüm ayarları yapılandır...", "Kitaplığı yeniden oluştur...", "Oynatma", "Oynatma hızı",
            "Sonrakini otomatik oynat", "Okundu olarak işaretle", "Başlarken", "Bitince veya atlanınca",
            "En son akışları al", "Tümünü okundu olarak işaretle", "Ses kitaplığı",
            "Makale sesini ve kayıtlı ayrıntıları sil", "Makale yıldızını aç/kapat",
            "Listeyi ve oynatıcıyı yeniden boyutlandırmak için sürükleyin", "Dinlemek için bir makale seçin",
            "Makaleyi yıldızla", "Makale metni", "Makaleyi tarayıcıda aç", "Oynat", "Duraklat",
            "Sonraki / Atla", "İptal", "Kaydet", "Akışlar", "RSS veya Atom akışları (her satıra bir URL)",
            "Akış başına en fazla yeni makale", "Yollar", "Medya çıktı klasörü",
            "Makale ses dosyası adı şablonu", "Ses", "Ses saklama süresi", "MP3 bit hızı",
            "Oynatıcıda makale metnini göster", "Başlangıçta yalnızca oynatılmayanları etkinleştir",
            "Sunucular (birden fazla ses seçin)", "Örnek", "Yükleniyor...", "Oynatılıyor",
            "Örneği durdur", "Ayar yardımı", "Geçersiz ayarlar", "RSS akışları",
            "Oynatma hızı", "Makale başlayınca", "Makale bitince veya atlanınca"
        ],
        ["zh-Hans"] =
        [
            "文件", "退出", "视图", "仅未播放", "显示文章文本", "设置",
            "配置所有设置...", "重建媒体库...", "播放", "播放速度",
            "自动播放下一篇", "标记为已读", "开始播放时", "结束或跳过时",
            "获取最新订阅源", "全部标记为已读", "音频库",
            "删除文章音频和已保存的详细信息", "切换文章星标",
            "拖动以调整文章列表和播放器大小", "选择要收听的文章",
            "收藏文章", "文章文本", "在浏览器中打开文章", "播放", "暂停",
            "下一篇 / 跳过", "取消", "保存", "订阅源", "RSS 或 Atom 订阅源（每行一个 URL）",
            "每个订阅源的新增文章上限", "路径", "媒体输出文件夹",
            "文章音频文件名模板", "音频", "音频保留时间", "MP3 比特率",
            "在播放器中显示文章文本", "启动时启用“仅未播放”",
            "主播（可选择多个语音）", "试听", "正在加载...", "正在播放",
            "停止试听", "设置帮助", "设置无效", "RSS 订阅源",
            "播放速度", "文章开始播放时", "文章结束或跳过时"
        ],
        ["zh-Hant"] =
        [
            "檔案", "結束", "檢視", "僅未播放", "顯示文章文字", "設定",
            "設定所有選項...", "重建媒體庫...", "播放", "播放速度",
            "自動播放下一篇", "標示為已讀", "開始播放時", "結束或略過時",
            "取得最新動態消息", "全部標示為已讀", "音訊庫",
            "刪除文章音訊和已儲存的詳細資料", "切換文章星號",
            "拖曳以調整文章清單和播放器大小", "選取要聆聽的文章",
            "為文章加星號", "文章文字", "在瀏覽器中開啟文章", "播放", "暫停",
            "下一篇 / 略過", "取消", "儲存", "動態消息", "RSS 或 Atom 動態消息（每行一個 URL）",
            "每個動態消息的新增文章上限", "路徑", "媒體輸出資料夾",
            "文章音訊檔名範本", "音訊", "音訊保留時間", "MP3 位元率",
            "在播放器中顯示文章文字", "啟動時啟用「僅未播放」",
            "主播（可選取多個語音）", "試聽", "載入中...", "播放中",
            "停止試聽", "設定說明", "設定無效", "RSS 動態消息",
            "播放速度", "文章開始播放時", "文章結束或略過時"
        ]
    };

    private static readonly CultureInfo SystemCulture = CultureInfo.CurrentUICulture;
    private static CultureInfo _culture = SelectCulture(SystemCulture, "system");
    private static IReadOnlyDictionary<string, string> _textByCulture =
        new Dictionary<string, string>(StringComparer.Ordinal);

    static LocalizationService()
    {
        Configure("system");
    }

    public static CultureInfo Culture => _culture;
    public static IReadOnlyList<(string Code, string Name)> SupportedLanguages =>
        Languages.Select((code, index) => (code, LanguageNames[index])).ToArray();
    public static string GetLanguageName(string code)
    {
        var index = Array.FindIndex(Languages, language => language.Equals(code, StringComparison.OrdinalIgnoreCase));
        return index >= 0 ? LanguageNames[index] : throw new InvalidDataException($"Unsupported language code: {code}");
    }

    public static string Get(string englishText) =>
        _textByCulture.TryGetValue(englishText, out var translated) ? translated : englishText;

    public static void Configure(string language)
    {
        _culture = SelectCulture(SystemCulture, language);
        _textByCulture = BuildTranslations();
        CultureInfo.DefaultThreadCurrentUICulture = _culture;
        Thread.CurrentThread.CurrentUICulture = _culture;
    }

    public static void Apply(Window window)
    {
        if (!Culture.Name.StartsWith("en", StringComparison.OrdinalIgnoreCase))
        {
            LocalizeElement(window);
        }
    }

    private static void LocalizeElement(DependencyObject element)
    {
        if (element is Window window)
        {
            window.Title = Get(window.Title);
        }

        if (element is TextBlock textBlock &&
            !BindingOperations.IsDataBound(textBlock, TextBlock.TextProperty))
        {
            textBlock.Text = Get(textBlock.Text);
        }
        else if (element is AccessText accessText &&
                 !BindingOperations.IsDataBound(accessText, AccessText.TextProperty))
        {
            accessText.Text = Get(accessText.Text);
        }

        if (element is HeaderedContentControl headered &&
            !BindingOperations.IsDataBound(headered, HeaderedContentControl.HeaderProperty) &&
            headered.Header is string header)
        {
            headered.Header = Get(header);
        }
        else if (element is HeaderedItemsControl headeredItems &&
                 !BindingOperations.IsDataBound(headeredItems, HeaderedItemsControl.HeaderProperty) &&
                 headeredItems.Header is string itemsHeader)
        {
            headeredItems.Header = Get(itemsHeader);
        }

        if (element is ContentControl contentControl &&
            !BindingOperations.IsDataBound(contentControl, ContentControl.ContentProperty) &&
            contentControl.Content is string content)
        {
            contentControl.Content = Get(content);
        }

        if (element is FrameworkElement frameworkElement &&
            !BindingOperations.IsDataBound(frameworkElement, FrameworkElement.ToolTipProperty) &&
            frameworkElement.ToolTip is string toolTip)
        {
            frameworkElement.ToolTip = Get(toolTip);
        }

        foreach (var child in LogicalTreeHelper.GetChildren(element).OfType<DependencyObject>())
        {
            LocalizeElement(child);
        }
    }

    private static IReadOnlyDictionary<string, string> BuildTranslations()
    {
        var language = GetLanguageCode(_culture);
        if (!Translations.TryGetValue(language, out var translatedText))
        {
            return new Dictionary<string, string>(StringComparer.Ordinal);
        }

        if (translatedText.Length != EnglishText.Length)
        {
            throw new InvalidOperationException($"The {language} translation has an invalid number of entries.");
        }

        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        for (var index = 0; index < EnglishText.Length; index++)
        {
            result[EnglishText[index]] = translatedText[index];
        }

        var extras = ExtraTranslations[language];
        result["General"] = extras[0];
        result["Application Language / Locale"] = extras[1];
        result["System default"] = extras[2];
        result["Application language changes take effect the next time you start the application."] = extras[3];
        result["Settings saved. Restart the app to apply the selected language."] = extras[4];
        result["Mark article as played"] = extras[5];
        result["Ready"] = extras[6];
        result["AnchorFeeds Settings"] = extras[7];
        result["Retrieve latest feeds on application launch"] = LaunchFeedTranslations[language];
        result["Default"] = DefaultBitrateTranslations[language];
        result["Update Feeds"] = ToolbarTranslations[language][0];
        result["Filter"] = ToolbarTranslations[language][1];
        result["Unread only"] = ToolbarTranslations[language][2];
        result["Sort"] = SortMenuTranslations[language][0];
        result["Sort by"] = SortMenuTranslations[language][1];
        result["Alphabetically (A–Z) ↑"] = SortMenuTranslations[language][2];
        result["Alphabetically (Z–A) ↓"] = SortMenuTranslations[language][3];
        result["Newest First ↓"] = SortMenuTranslations[language][4];
        result["Oldest First ↑"] = SortMenuTranslations[language][5];
        result["Group by Feed"] = SortMenuTranslations[language][6];
        result["Starred only"] = SortMenuTranslations[language][7];
        result["Favorites"] = FavoriteCategoryFilterTranslations[language];
        return result;
    }

    private static CultureInfo SelectCulture(CultureInfo systemCulture, string language)
    {
        if (language.Equals("system", StringComparison.OrdinalIgnoreCase))
        {
            var systemLanguage = GetLanguageCode(systemCulture);
            return Translations.ContainsKey(systemLanguage) ? systemCulture : CultureInfo.GetCultureInfo("en");
        }

        if (language.Equals("en", StringComparison.OrdinalIgnoreCase))
        {
            return CultureInfo.GetCultureInfo("en");
        }

        if (Translations.ContainsKey(language))
        {
            return CultureInfo.GetCultureInfo(language);
        }

        throw new InvalidDataException($"Unsupported application language: {language}");
    }

    private static readonly IReadOnlyDictionary<string, string[]> ExtraTranslations =
        new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
        {
            ["cs"] = ["Obecné", "Jazyk / oblast aplikace", "Výchozí podle systému", "Změna jazyka aplikace se projeví při příštím spuštění.", "Nastavení uloženo. Pro použití vybraného jazyka aplikaci restartujte.", "Označit článek jako přečtený", "Připraveno", "AnchorFeeds – nastavení"],
            ["de"] = ["Allgemein", "Anwendungssprache / Gebietsschema", "Systemstandard", "Die Änderung der Anwendungssprache wird beim nächsten Start übernommen.", "Einstellungen gespeichert. Starten Sie die App neu, um die ausgewählte Sprache zu verwenden.", "Artikel als gelesen markieren", "Bereit", "AnchorFeeds – Einstellungen"],
            ["es"] = ["General", "Idioma / configuración regional de la aplicación", "Predeterminado del sistema", "El cambio de idioma de la aplicación se aplicará la próxima vez que la inicie.", "Configuración guardada. Reinicie la aplicación para usar el idioma seleccionado.", "Marcar artículo como leído", "Listo", "Configuración de AnchorFeeds"],
            ["fr"] = ["Général", "Langue / paramètres régionaux de l’application", "Valeur système", "La langue de l’application sera modifiée au prochain démarrage.", "Paramètres enregistrés. Redémarrez l’application pour appliquer la langue choisie.", "Marquer l’article comme lu", "Prêt", "Paramètres d’AnchorFeeds"],
            ["it"] = ["Generale", "Lingua / impostazioni locali dell’applicazione", "Predefinita di sistema", "La lingua dell’applicazione verrà modificata al prossimo avvio.", "Impostazioni salvate. Riavvia l’app per applicare la lingua selezionata.", "Segna articolo come letto", "Pronto", "Impostazioni di AnchorFeeds"],
            ["ja"] = ["全般", "アプリケーションの言語 / 地域", "システムの既定", "アプリケーションの言語変更は次回起動時に適用されます。", "設定を保存しました。選択した言語を適用するにはアプリを再起動してください。", "記事を既読にする", "準備完了", "AnchorFeeds の設定"],
            ["ko"] = ["일반", "애플리케이션 언어 / 로캘", "시스템 기본값", "애플리케이션 언어 변경은 다음에 시작할 때 적용됩니다.", "설정이 저장되었습니다. 선택한 언어를 적용하려면 앱을 다시 시작하세요.", "기사를 읽음으로 표시", "준비됨", "AnchorFeeds 설정"],
            ["pl"] = ["Ogólne", "Język / ustawienia regionalne aplikacji", "Domyślny systemowy", "Zmiana języka aplikacji zostanie zastosowana przy następnym uruchomieniu.", "Ustawienia zapisano. Uruchom ponownie aplikację, aby zastosować wybrany język.", "Oznacz artykuł jako przeczytany", "Gotowe", "Ustawienia AnchorFeeds"],
            ["pt-BR"] = ["Geral", "Idioma / localidade do aplicativo", "Padrão do sistema", "A alteração do idioma do aplicativo será aplicada na próxima inicialização.", "Configurações salvas. Reinicie o aplicativo para aplicar o idioma selecionado.", "Marcar artigo como lido", "Pronto", "Configurações do AnchorFeeds"],
            ["ru"] = ["Общие", "Язык / регион приложения", "Системный язык", "Изменение языка приложения вступит в силу при следующем запуске.", "Настройки сохранены. Перезапустите приложение, чтобы применить выбранный язык.", "Отметить статью как прочитанную", "Готово", "Настройки AnchorFeeds"],
            ["tr"] = ["Genel", "Uygulama dili / yerel ayar", "Sistem varsayılanı", "Uygulama dili değişikliği bir sonraki başlatmada uygulanır.", "Ayarlar kaydedildi. Seçilen dili uygulamak için uygulamayı yeniden başlatın.", "Makaleyi okundu olarak işaretle", "Hazır", "AnchorFeeds ayarları"],
            ["zh-Hans"] = ["常规", "应用程序语言 / 区域", "系统默认", "应用程序语言将在下次启动时更改。", "设置已保存。请重新启动应用程序以应用所选语言。", "将文章标记为已读", "就绪", "AnchorFeeds 设置"],
            ["zh-Hant"] = ["一般", "應用程式語言 / 地區", "系統預設", "應用程式語言將於下次啟動時變更。", "設定已儲存。請重新啟動應用程式以套用所選語言。", "將文章標示為已讀", "就緒", "AnchorFeeds 設定"]
        };

    private static readonly IReadOnlyDictionary<string, string> LaunchFeedTranslations =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["cs"] = "Načíst nejnovější kanály při spuštění aplikace",
            ["de"] = "Neueste Feeds beim Anwendungsstart abrufen",
            ["es"] = "Obtener las noticias más recientes al iniciar la aplicación",
            ["fr"] = "Récupérer les derniers flux au démarrage de l’application",
            ["it"] = "Recupera i feed più recenti all’avvio dell’applicazione",
            ["ja"] = "アプリケーション起動時に最新フィードを取得",
            ["ko"] = "애플리케이션 시작 시 최신 피드 가져오기",
            ["pl"] = "Pobierz najnowsze kanały po uruchomieniu aplikacji",
            ["pt-BR"] = "Buscar os feeds mais recentes ao iniciar o aplicativo",
            ["ru"] = "Получать последние ленты при запуске приложения",
            ["tr"] = "Uygulama başlatıldığında en son akışları al",
            ["zh-Hans"] = "应用启动时获取最新订阅源",
            ["zh-Hant"] = "應用程式啟動時取得最新動態"
        };

    private static readonly IReadOnlyDictionary<string, string> DefaultBitrateTranslations =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["cs"] = "Výchozí",
            ["de"] = "Standard",
            ["es"] = "Predeterminado",
            ["fr"] = "Par défaut",
            ["it"] = "Predefinito",
            ["ja"] = "既定",
            ["ko"] = "기본값",
            ["pl"] = "Domyślna",
            ["pt-BR"] = "Padrão",
            ["ru"] = "По умолчанию",
            ["tr"] = "Varsayılan",
            ["zh-Hans"] = "默认",
            ["zh-Hant"] = "預設"
        };

    private static readonly IReadOnlyDictionary<string, string[]> ToolbarTranslations =
        new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
        {
            ["cs"] = ["Aktualizovat kanály", "Filtr", "Pouze nepřečtené"],
            ["de"] = ["Feeds aktualisieren", "Filter", "Nur ungelesene"],
            ["es"] = ["Actualizar fuentes", "Filtro", "Solo no leídos"],
            ["fr"] = ["Mettre à jour les flux", "Filtrer", "Non lus uniquement"],
            ["it"] = ["Aggiorna feed", "Filtro", "Solo non letti"],
            ["ja"] = ["フィードを更新", "フィルター", "未読のみ"],
            ["ko"] = ["피드 업데이트", "필터", "읽지 않음만"],
            ["pl"] = ["Zaktualizuj kanały", "Filtr", "Tylko nieprzeczytane"],
            ["pt-BR"] = ["Atualizar feeds", "Filtro", "Somente não lidos"],
            ["ru"] = ["Обновить ленты", "Фильтр", "Только непрочитанные"],
            ["tr"] = ["Akışları güncelle", "Filtre", "Yalnızca okunmamış"],
            ["zh-Hans"] = ["更新订阅源", "筛选", "仅未读"],
            ["zh-Hant"] = ["更新動態", "篩選", "僅未讀"]
        };

    private static readonly IReadOnlyDictionary<string, string[]> SortMenuTranslations =
        new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
        {
            ["cs"] = ["Řadit", "Řadit podle", "Abecedně (A–Z) ↑", "Abecedně (Z–A) ↓", "Nejnovější první ↓", "Nejstarší první ↑", "Seskupit podle kanálu", "Pouze s hvězdičkou"],
            ["de"] = ["Sortieren", "Sortieren nach", "Alphabetisch (A–Z) ↑", "Alphabetisch (Z–A) ↓", "Neueste zuerst ↓", "Älteste zuerst ↑", "Nach Feed gruppieren", "Nur markierte"],
            ["es"] = ["Ordenar", "Ordenar por", "Alfabéticamente (A–Z) ↑", "Alfabéticamente (Z–A) ↓", "Más recientes primero ↓", "Más antiguos primero ↑", "Agrupar por fuente", "Solo favoritos"],
            ["fr"] = ["Trier", "Trier par", "Alphabétique (A–Z) ↑", "Alphabétique (Z–A) ↓", "Plus récents d’abord ↓", "Plus anciens d’abord ↑", "Grouper par flux", "Favoris uniquement"],
            ["it"] = ["Ordina", "Ordina per", "Alfabetico (A–Z) ↑", "Alfabetico (Z–A) ↓", "Più recenti prima ↓", "Meno recenti prima ↑", "Raggruppa per feed", "Solo preferiti"],
            ["ja"] = ["並べ替え", "並べ替え基準", "アルファベット順 (A–Z) ↑", "アルファベット逆順 (Z–A) ↓", "新しい順 ↓", "古い順 ↑", "フィードごとにグループ化", "スター付きのみ"],
            ["ko"] = ["정렬", "정렬 기준", "알파벳순 (A–Z) ↑", "알파벳 역순 (Z–A) ↓", "최신순 ↓", "오래된순 ↑", "피드별 그룹화", "별표 항목만"],
            ["pl"] = ["Sortuj", "Sortuj według", "Alfabetycznie (A–Z) ↑", "Alfabetycznie (Z–A) ↓", "Najnowsze najpierw ↓", "Najstarsze najpierw ↑", "Grupuj według kanału", "Tylko oznaczone gwiazdką"],
            ["pt-BR"] = ["Ordenar", "Ordenar por", "Alfabética (A–Z) ↑", "Alfabética (Z–A) ↓", "Mais recentes primeiro ↓", "Mais antigas primeiro ↑", "Agrupar por feed", "Somente favoritas"],
            ["ru"] = ["Сортировка", "Сортировать по", "По алфавиту (А–Я) ↑", "По алфавиту (Я–А) ↓", "Сначала новые ↓", "Сначала старые ↑", "Группировать по ленте", "Только избранное"],
            ["tr"] = ["Sırala", "Sıralama ölçütü", "Alfabetik (A–Z) ↑", "Ters alfabetik (Z–A) ↓", "En yeni önce ↓", "En eski önce ↑", "Akışa göre grupla", "Yalnızca yıldızlılar"],
            ["zh-Hans"] = ["排序", "排序方式", "按字母排序 (A–Z) ↑", "按字母逆序 (Z–A) ↓", "最新优先 ↓", "最旧优先 ↑", "按订阅源分组", "仅收藏"],
            ["zh-Hant"] = ["排序", "排序方式", "按字母排序 (A–Z) ↑", "按字母逆序 (Z–A) ↓", "最新優先 ↓", "最舊優先 ↑", "按動態分組", "僅顯示星號項目"]
        };

    private static readonly IReadOnlyDictionary<string, string> FavoriteCategoryFilterTranslations =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["cs"] = "Oblíbené kategorie",
            ["de"] = "Favoriten",
            ["es"] = "Favoritos",
            ["fr"] = "Catégories favorites",
            ["it"] = "Categorie preferite",
            ["ja"] = "お気に入りカテゴリ",
            ["ko"] = "즐겨찾는 카테고리",
            ["pl"] = "Ulubione kategorie",
            ["pt-BR"] = "Categorias favoritas",
            ["ru"] = "Избранные категории",
            ["tr"] = "Favori kategoriler",
            ["zh-Hans"] = "收藏类别",
            ["zh-Hant"] = "收藏類別"
        };

    private static string GetLanguageCode(CultureInfo culture)
    {
        if (culture.Name.StartsWith("zh", StringComparison.OrdinalIgnoreCase))
        {
            return culture.Name.Contains("Hant", StringComparison.OrdinalIgnoreCase) ||
                   culture.Name.EndsWith("-TW", StringComparison.OrdinalIgnoreCase) ||
                   culture.Name.EndsWith("-HK", StringComparison.OrdinalIgnoreCase) ||
                   culture.Name.EndsWith("-MO", StringComparison.OrdinalIgnoreCase)
                ? "zh-Hant"
                : "zh-Hans";
        }

        if (culture.TwoLetterISOLanguageName.Equals("pt", StringComparison.OrdinalIgnoreCase))
        {
            return "pt-BR";
        }

        return culture.TwoLetterISOLanguageName;
    }
}
