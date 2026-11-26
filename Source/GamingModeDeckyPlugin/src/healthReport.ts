// Eternal Core: Core Health in Quick Access. Turns the agent's GET /health answer
// (GamingMode.Services/CoreHealth.cs) into the lines HealthSection.tsx shows, in Core's 12
// languages. Self-contained on purpose: tests/coreHealthSection.test.mjs loads this file alone.

export type HealthLevel = "ok" | "attention" | "info" | "unknown";
export type HealthRowData = { id: string; level: HealthLevel; state: string; data?: Record<string, unknown> };
export type HealthAlert = { id: string; rowId: string; dismissed: boolean };
export type HealthAnswer = { attention?: number; alertsEnabled?: boolean; rows?: HealthRowData[]; alerts?: HealthAlert[] };
// tone "crimson": shown in Core crimson (a muted microphone; a partly muted one also counts as attention).
export type HealthLine = { id: string; level: HealthLevel; title: string; value: string; details: string[]; alertId?: string; tone?: "crimson" };

type Copy = Record<string, string>;

const en: Copy = {
  title: "Health", allGood: "All good", attention: "Needs attention: {n}", checking: "Checking…", agentDown: "Core's service is not answering.", dismiss: "Dismiss alert",
  r_backup: "Server backup", r_restore: "Restore check", r_drive: "Server drive", r_romm: "RomM", r_restart: "Windows restart", r_night: "Update night", r_tv: "TV", r_pc: "PC disk space", r_core: "Core update",
  notConfigured: "Set the server in Core > Gaming Mode", unreachable: "Server not reachable", missing: "No status file on the server", unreadable: "Status file unreadable", stale: "Last news {when}", unknown: "Unknown",
  bOk: "OK · {when}", bPartial: "Partly failed", bFailed: "Failed", bNever: "No backup yet", lastSuccess: "Last success {when}", failures: "{n} runs in a row not OK", skipped: "ROMs skipped: {list}",
  rOk: "Passed {when}", rFailed: "Failed", rOverdue: "Overdue (last {when})", rNever: "Not run yet",
  dHealthy: "Healthy · {free} free", dWarning: "Warning", dUnhealthy: "Failing",
  mUp: "Running", mDown: "Not answering", pending: "Pending", notNeeded: "Not needed",
  nScheduled: "Restart at {time}", nWaiting: "Updates waiting", nNothing: "Nothing waiting",
  tOff: "TV control off", tNotPaired: "Not paired", tOk: "Paired, answering", tDown: "Not answering",
  pcOk: "{free} free", pcLow: "Only {free} free", cAvail: "Core {latest} available", cCurrent: "Up to date · {current}",
  r_mic: "Microphone", micMuted: "Muted", micLive: "Live", micNone: "No microphone", micPartial: "Partly muted",
  micCalls: "Calls mic: {name}", micAway: "calls mic not connected", micAwayDetail: "Not connected. Calls use Windows' default microphone.", micActiveDetail: "Connected and used for calls.", micChangedDetail: "Connected; Windows uses another microphone for calls.", micFailedDetail: "Windows did not let Core make it the default.",
  r_stream: "Streaming", sIdle: "Idle", sTo: "Streaming to {device}", sVia: "Streaming · {source}", sKept: "TV kept out by you", tHeld: "Left alone (Stream mode)", sThrough: "Through {source}", sVd: "Apollo's virtual display is active",
};

const copy: Record<string, Copy> = {
  en,
  it: {
    title: "Salute", allGood: "Tutto a posto", attention: "Da controllare: {n}", checking: "Controllo…", agentDown: "Il servizio di Core non risponde.", dismiss: "Ignora l'avviso",
    r_backup: "Backup del server", r_restore: "Prova di ripristino", r_drive: "Disco del server", r_romm: "RomM", r_restart: "Riavvio di Windows", r_night: "Notte degli aggiornamenti", r_tv: "TV", r_pc: "Spazio sul PC", r_core: "Aggiornamento di Core",
    notConfigured: "Imposta il server in Core > Gaming Mode", unreachable: "Server non raggiungibile", missing: "Nessun file di stato sul server", unreadable: "File di stato illeggibile", stale: "Ultime notizie {when}", unknown: "Sconosciuto",
    bOk: "OK · {when}", bPartial: "In parte fallito", bFailed: "Fallito", bNever: "Ancora nessun backup", lastSuccess: "Ultimo riuscito {when}", failures: "{n} esecuzioni di fila non OK", skipped: "ROM saltate: {list}",
    rOk: "Superata {when}", rFailed: "Fallita", rOverdue: "In ritardo (l'ultima {when})", rNever: "Mai eseguita",
    dHealthy: "In salute · {free} liberi", dWarning: "Attenzione", dUnhealthy: "Si sta guastando",
    mUp: "Attivo", mDown: "Non risponde", pending: "In attesa", notNeeded: "Non serve",
    nScheduled: "Riavvio alle {time}", nWaiting: "Aggiornamenti in attesa", nNothing: "Niente in attesa",
    tOff: "Controllo TV spento", tNotPaired: "Non abbinata", tOk: "Abbinata, risponde", tDown: "Non risponde",
    pcOk: "{free} liberi", pcLow: "Solo {free} liberi", cAvail: "Core {latest} disponibile", cCurrent: "Aggiornato · {current}",
    r_mic: "Microfono", micMuted: "Silenziato", micLive: "Attivo", micNone: "Nessun microfono", micPartial: "Silenziato in parte",
    micCalls: "Microfono per le chiamate: {name}", micAway: "microfono per le chiamate non collegato", micAwayDetail: "Non collegato. Le chiamate usano il microfono predefinito di Windows.", micActiveDetail: "Collegato e usato per le chiamate.", micChangedDetail: "Collegato; Windows usa un altro microfono per le chiamate.", micFailedDetail: "Windows non ha permesso a Core di impostarlo come predefinito.",
    r_stream: "Streaming", sIdle: "Inattivo", sTo: "Streaming verso {device}", sVia: "Streaming · {source}", sKept: "TV lasciata stare da te", tHeld: "Lasciata stare (modalità streaming)", sThrough: "Tramite {source}", sVd: "Lo schermo virtuale di Apollo è attivo",
  },
  es: {
    title: "Estado", allGood: "Todo bien", attention: "Necesita atención: {n}", checking: "Comprobando…", agentDown: "El servicio de Core no responde.", dismiss: "Descartar aviso",
    r_backup: "Copia del servidor", r_restore: "Prueba de restauración", r_drive: "Disco del servidor", r_romm: "RomM", r_restart: "Reinicio de Windows", r_night: "Noche de actualizaciones", r_tv: "TV", r_pc: "Espacio en el PC", r_core: "Actualización de Core",
    notConfigured: "Configura el servidor en Core > Gaming Mode", unreachable: "Servidor inaccesible", missing: "No hay archivo de estado en el servidor", unreadable: "Archivo de estado ilegible", stale: "Últimas noticias {when}", unknown: "Desconocido",
    bOk: "OK · {when}", bPartial: "Falló en parte", bFailed: "Falló", bNever: "Aún no hay copia", lastSuccess: "Último correcto {when}", failures: "{n} ejecuciones seguidas sin OK", skipped: "ROM omitidas: {list}",
    rOk: "Superada {when}", rFailed: "Falló", rOverdue: "Atrasada (la última {when})", rNever: "Aún no se ha hecho",
    dHealthy: "Sano · {free} libres", dWarning: "Advertencia", dUnhealthy: "Fallando",
    mUp: "En marcha", mDown: "No responde", pending: "Pendiente", notNeeded: "No hace falta",
    nScheduled: "Reinicio a las {time}", nWaiting: "Actualizaciones pendientes", nNothing: "Nada pendiente",
    tOff: "Control de TV desactivado", tNotPaired: "Sin vincular", tOk: "Vinculada, responde", tDown: "No responde",
    pcOk: "{free} libres", pcLow: "Solo {free} libres", cAvail: "Core {latest} disponible", cCurrent: "Actualizado · {current}",
    r_mic: "Micrófono", micMuted: "Silenciado", micLive: "Activo", micNone: "No hay micrófono", micPartial: "Silenciado en parte",
    micCalls: "Micrófono de llamadas: {name}", micAway: "micrófono de llamadas no conectado", micAwayDetail: "No conectado. Las llamadas usan el micrófono predeterminado de Windows.", micActiveDetail: "Conectado y usado para llamadas.", micChangedDetail: "Conectado; Windows usa otro micrófono para llamadas.", micFailedDetail: "Windows no dejó que Core lo pusiera como predeterminado.",
    r_stream: "Streaming", sIdle: "Inactivo", sTo: "Transmitiendo a {device}", sVia: "Transmitiendo · {source}", sKept: "TV apartada por ti", tHeld: "Sin tocar (modo streaming)", sThrough: "Mediante {source}", sVd: "La pantalla virtual de Apollo está activa",
  },
  fr: {
    title: "Santé", allGood: "Tout va bien", attention: "À vérifier : {n}", checking: "Vérification…", agentDown: "Le service de Core ne répond pas.", dismiss: "Ignorer l'alerte",
    r_backup: "Sauvegarde du serveur", r_restore: "Test de restauration", r_drive: "Disque du serveur", r_romm: "RomM", r_restart: "Redémarrage de Windows", r_night: "Nuit des mises à jour", r_tv: "TV", r_pc: "Espace sur le PC", r_core: "Mise à jour de Core",
    notConfigured: "Indiquez le serveur dans Core > Gaming Mode", unreachable: "Serveur injoignable", missing: "Aucun fichier d'état sur le serveur", unreadable: "Fichier d'état illisible", stale: "Dernières nouvelles {when}", unknown: "Inconnu",
    bOk: "OK · {when}", bPartial: "Échec partiel", bFailed: "Échec", bNever: "Pas encore de sauvegarde", lastSuccess: "Dernière réussite {when}", failures: "{n} exécutions de suite non OK", skipped: "ROM ignorées : {list}",
    rOk: "Réussi {when}", rFailed: "Échec", rOverdue: "En retard (dernier {when})", rNever: "Jamais lancé",
    dHealthy: "Sain · {free} libres", dWarning: "Avertissement", dUnhealthy: "Défaillant",
    mUp: "En marche", mDown: "Ne répond pas", pending: "En attente", notNeeded: "Inutile",
    nScheduled: "Redémarrage à {time}", nWaiting: "Mises à jour en attente", nNothing: "Rien en attente",
    tOff: "Contrôle de la TV désactivé", tNotPaired: "Non appairée", tOk: "Appairée, répond", tDown: "Ne répond pas",
    pcOk: "{free} libres", pcLow: "Seulement {free} libres", cAvail: "Core {latest} disponible", cCurrent: "À jour · {current}",
    r_mic: "Micro", micMuted: "Coupé", micLive: "Actif", micNone: "Aucun micro", micPartial: "Coupé en partie",
    micCalls: "Micro des appels : {name}", micAway: "micro des appels non connecté", micAwayDetail: "Non connecté. Les appels utilisent le micro par défaut de Windows.", micActiveDetail: "Connecté et utilisé pour les appels.", micChangedDetail: "Connecté ; Windows utilise un autre micro pour les appels.", micFailedDetail: "Windows n'a pas laissé Core le définir par défaut.",
    r_stream: "Streaming", sIdle: "Inactif", sTo: "Streaming vers {device}", sVia: "Streaming · {source}", sKept: "TV tenue à l'écart par vous", tHeld: "Laissée tranquille (mode streaming)", sThrough: "Via {source}", sVd: "L'écran virtuel d'Apollo est actif",
  },
  de: {
    title: "Status", allGood: "Alles in Ordnung", attention: "Zu prüfen: {n}", checking: "Wird geprüft…", agentDown: "Der Core-Dienst antwortet nicht.", dismiss: "Warnung ausblenden",
    r_backup: "Server-Backup", r_restore: "Wiederherstellungstest", r_drive: "Server-Laufwerk", r_romm: "RomM", r_restart: "Windows-Neustart", r_night: "Update-Nacht", r_tv: "TV", r_pc: "Speicher am PC", r_core: "Core-Update",
    notConfigured: "Server unter Core > Gaming Mode eintragen", unreachable: "Server nicht erreichbar", missing: "Keine Statusdatei auf dem Server", unreadable: "Statusdatei unlesbar", stale: "Letzte Meldung {when}", unknown: "Unbekannt",
    bOk: "OK · {when}", bPartial: "Teilweise fehlgeschlagen", bFailed: "Fehlgeschlagen", bNever: "Noch kein Backup", lastSuccess: "Zuletzt erfolgreich {when}", failures: "{n} Läufe in Folge nicht OK", skipped: "ROMs übersprungen: {list}",
    rOk: "Bestanden {when}", rFailed: "Fehlgeschlagen", rOverdue: "Überfällig (zuletzt {when})", rNever: "Noch nie ausgeführt",
    dHealthy: "Gesund · {free} frei", dWarning: "Warnung", dUnhealthy: "Fällt aus",
    mUp: "Läuft", mDown: "Antwortet nicht", pending: "Ausstehend", notNeeded: "Nicht nötig",
    nScheduled: "Neustart um {time}", nWaiting: "Updates warten", nNothing: "Nichts ausstehend",
    tOff: "TV-Steuerung aus", tNotPaired: "Nicht gekoppelt", tOk: "Gekoppelt, antwortet", tDown: "Antwortet nicht",
    pcOk: "{free} frei", pcLow: "Nur {free} frei", cAvail: "Core {latest} verfügbar", cCurrent: "Aktuell · {current}",
    r_mic: "Mikrofon", micMuted: "Stumm", micLive: "An", micNone: "Kein Mikrofon", micPartial: "Teilweise stumm",
    micCalls: "Anruf-Mikrofon: {name}", micAway: "Anruf-Mikrofon nicht verbunden", micAwayDetail: "Nicht verbunden. Anrufe nutzen das Standardmikrofon von Windows.", micActiveDetail: "Verbunden und für Anrufe genutzt.", micChangedDetail: "Verbunden; Windows nutzt ein anderes Mikrofon für Anrufe.", micFailedDetail: "Windows hat Core nicht erlaubt, es als Standard festzulegen.",
    r_stream: "Streaming", sIdle: "Inaktiv", sTo: "Streamt an {device}", sVia: "Streamt · {source}", sKept: "Fernseher von dir ausgenommen", tHeld: "In Ruhe gelassen (Streaming-Modus)", sThrough: "Über {source}", sVd: "Der virtuelle Bildschirm von Apollo ist aktiv",
  },
  pt: {
    title: "Saúde", allGood: "Tudo certo", attention: "Precisa de atenção: {n}", checking: "Verificando…", agentDown: "O serviço do Core não responde.", dismiss: "Dispensar alerta",
    r_backup: "Backup do servidor", r_restore: "Teste de restauração", r_drive: "Disco do servidor", r_romm: "RomM", r_restart: "Reinício do Windows", r_night: "Noite de atualizações", r_tv: "TV", r_pc: "Espaço no PC", r_core: "Atualização do Core",
    notConfigured: "Defina o servidor em Core > Gaming Mode", unreachable: "Servidor inacessível", missing: "Nenhum arquivo de estado no servidor", unreadable: "Arquivo de estado ilegível", stale: "Últimas notícias {when}", unknown: "Desconhecido",
    bOk: "OK · {when}", bPartial: "Falhou em parte", bFailed: "Falhou", bNever: "Ainda sem backup", lastSuccess: "Último sucesso {when}", failures: "{n} execuções seguidas sem OK", skipped: "ROMs ignoradas: {list}",
    rOk: "Aprovado {when}", rFailed: "Falhou", rOverdue: "Atrasado (último {when})", rNever: "Ainda não feito",
    dHealthy: "Saudável · {free} livres", dWarning: "Aviso", dUnhealthy: "Falhando",
    mUp: "Funcionando", mDown: "Não responde", pending: "Pendente", notNeeded: "Não é preciso",
    nScheduled: "Reinício às {time}", nWaiting: "Atualizações pendentes", nNothing: "Nada pendente",
    tOff: "Controle da TV desligado", tNotPaired: "Não pareada", tOk: "Pareada, responde", tDown: "Não responde",
    pcOk: "{free} livres", pcLow: "Só {free} livres", cAvail: "Core {latest} disponível", cCurrent: "Atualizado · {current}",
    r_mic: "Microfone", micMuted: "Silenciado", micLive: "Ativo", micNone: "Sem microfone", micPartial: "Silenciado em parte",
    micCalls: "Microfone das chamadas: {name}", micAway: "microfone das chamadas não conectado", micAwayDetail: "Não conectado. As chamadas usam o microfone padrão do Windows.", micActiveDetail: "Conectado e usado nas chamadas.", micChangedDetail: "Conectado; o Windows usa outro microfone nas chamadas.", micFailedDetail: "O Windows não deixou o Core defini-lo como padrão.",
    r_stream: "Streaming", sIdle: "Inativo", sTo: "Transmitindo para {device}", sVia: "Transmitindo · {source}", sKept: "TV deixada de fora por você", tHeld: "Deixada em paz (modo streaming)", sThrough: "Via {source}", sVd: "A tela virtual do Apollo está ativa",
  },
  uk: {
    title: "Стан", allGood: "Усе гаразд", attention: "Потребує уваги: {n}", checking: "Перевірка…", agentDown: "Служба Core не відповідає.", dismiss: "Приховати сповіщення",
    r_backup: "Резервна копія сервера", r_restore: "Перевірка відновлення", r_drive: "Диск сервера", r_romm: "RomM", r_restart: "Перезапуск Windows", r_night: "Ніч оновлень", r_tv: "Телевізор", r_pc: "Місце на ПК", r_core: "Оновлення Core",
    notConfigured: "Укажіть сервер у Core > Gaming Mode", unreachable: "Сервер недоступний", missing: "На сервері немає файлу стану", unreadable: "Файл стану не читається", stale: "Останні вісті {when}", unknown: "Невідомо",
    bOk: "OK · {when}", bPartial: "Частково не вдалося", bFailed: "Не вдалося", bNever: "Ще немає копії", lastSuccess: "Остання вдала {when}", failures: "{n} запусків поспіль не OK", skipped: "Пропущені ROM: {list}",
    rOk: "Пройдено {when}", rFailed: "Не вдалося", rOverdue: "Прострочено (остання {when})", rNever: "Ще не запускалася",
    dHealthy: "Справний · вільно {free}", dWarning: "Попередження", dUnhealthy: "Виходить з ладу",
    mUp: "Працює", mDown: "Не відповідає", pending: "Очікує", notNeeded: "Не потрібен",
    nScheduled: "Перезапуск о {time}", nWaiting: "Оновлення очікують", nNothing: "Нічого не очікує",
    tOff: "Керування ТВ вимкнено", tNotPaired: "Не з'єднано", tOk: "З'єднано, відповідає", tDown: "Не відповідає",
    pcOk: "Вільно {free}", pcLow: "Вільно лише {free}", cAvail: "Доступний Core {latest}", cCurrent: "Актуальний · {current}",
    r_mic: "Мікрофон", micMuted: "Вимкнено", micLive: "Увімкнено", micNone: "Немає мікрофона", micPartial: "Вимкнено частково",
    micCalls: "Мікрофон для дзвінків: {name}", micAway: "мікрофон для дзвінків не підключено", micAwayDetail: "Не підключено. Дзвінки використовують мікрофон Windows за замовчуванням.", micActiveDetail: "Підключено й використовується для дзвінків.", micChangedDetail: "Підключено; Windows використовує інший мікрофон для дзвінків.", micFailedDetail: "Windows не дозволила Core зробити його мікрофоном за замовчуванням.",
    r_stream: "Трансляція", sIdle: "Немає", sTo: "Трансляція на {device}", sVia: "Трансляція · {source}", sKept: "Телевізор не чіпати (вами)", tHeld: "Не чіпаємо (режим трансляції)", sThrough: "Через {source}", sVd: "Віртуальний екран Apollo активний",
  },
  ru: {
    title: "Состояние", allGood: "Всё в порядке", attention: "Требует внимания: {n}", checking: "Проверка…", agentDown: "Служба Core не отвечает.", dismiss: "Скрыть оповещение",
    r_backup: "Резервная копия сервера", r_restore: "Проверка восстановления", r_drive: "Диск сервера", r_romm: "RomM", r_restart: "Перезагрузка Windows", r_night: "Ночь обновлений", r_tv: "Телевизор", r_pc: "Место на ПК", r_core: "Обновление Core",
    notConfigured: "Укажите сервер в Core > Gaming Mode", unreachable: "Сервер недоступен", missing: "На сервере нет файла состояния", unreadable: "Файл состояния не читается", stale: "Последние вести {when}", unknown: "Неизвестно",
    bOk: "OK · {when}", bPartial: "Частично не удалось", bFailed: "Не удалось", bNever: "Копий ещё нет", lastSuccess: "Последняя удачная {when}", failures: "{n} запусков подряд не OK", skipped: "Пропущены ROM: {list}",
    rOk: "Пройдена {when}", rFailed: "Не удалась", rOverdue: "Просрочена (последняя {when})", rNever: "Ещё не запускалась",
    dHealthy: "Исправен · свободно {free}", dWarning: "Предупреждение", dUnhealthy: "Выходит из строя",
    mUp: "Работает", mDown: "Не отвечает", pending: "Ожидает", notNeeded: "Не нужна",
    nScheduled: "Перезагрузка в {time}", nWaiting: "Обновления ждут", nNothing: "Ничего не ждёт",
    tOff: "Управление ТВ выключено", tNotPaired: "Не сопряжён", tOk: "Сопряжён, отвечает", tDown: "Не отвечает",
    pcOk: "Свободно {free}", pcLow: "Свободно всего {free}", cAvail: "Доступен Core {latest}", cCurrent: "Актуален · {current}",
    r_mic: "Микрофон", micMuted: "Выключен", micLive: "Включён", micNone: "Нет микрофона", micPartial: "Выключен частично",
    micCalls: "Микрофон для звонков: {name}", micAway: "микрофон для звонков не подключён", micAwayDetail: "Не подключён. Звонки используют микрофон Windows по умолчанию.", micActiveDetail: "Подключён и используется для звонков.", micChangedDetail: "Подключён; Windows использует другой микрофон для звонков.", micFailedDetail: "Windows не позволила Core сделать его микрофоном по умолчанию.",
    r_stream: "Трансляция", sIdle: "Нет", sTo: "Трансляция на {device}", sVia: "Трансляция · {source}", sKept: "Телевизор не трогать (вами)", tHeld: "Не трогаем (режим трансляции)", sThrough: "Через {source}", sVd: "Виртуальный экран Apollo активен",
  },
  zh: {
    title: "健康", allGood: "一切正常", attention: "需要注意：{n}", checking: "正在检查…", agentDown: "Core 服务没有响应。", dismiss: "忽略提醒",
    r_backup: "服务器备份", r_restore: "恢复测试", r_drive: "服务器硬盘", r_romm: "RomM", r_restart: "Windows 重启", r_night: "更新之夜", r_tv: "电视", r_pc: "电脑磁盘空间", r_core: "Core 更新",
    notConfigured: "请在 Core > Gaming Mode 中设置服务器", unreachable: "无法连接服务器", missing: "服务器上没有状态文件", unreadable: "状态文件无法读取", stale: "最后消息 {when}", unknown: "未知",
    bOk: "正常 · {when}", bPartial: "部分失败", bFailed: "失败", bNever: "尚无备份", lastSuccess: "上次成功 {when}", failures: "连续 {n} 次未成功", skipped: "已跳过的 ROM：{list}",
    rOk: "已通过 {when}", rFailed: "失败", rOverdue: "已逾期（上次 {when}）", rNever: "尚未运行",
    dHealthy: "健康 · 剩余 {free}", dWarning: "警告", dUnhealthy: "正在损坏",
    mUp: "运行中", mDown: "没有响应", pending: "待处理", notNeeded: "不需要",
    nScheduled: "{time} 重启", nWaiting: "有更新等待中", nNothing: "没有等待项",
    tOff: "电视控制已关闭", tNotPaired: "未配对", tOk: "已配对，有响应", tDown: "没有响应",
    pcOk: "剩余 {free}", pcLow: "仅剩 {free}", cAvail: "Core {latest} 可用", cCurrent: "已是最新 · {current}",
    r_mic: "麦克风", micMuted: "已静音", micLive: "已开启", micNone: "没有麦克风", micPartial: "部分静音",
    micCalls: "通话麦克风：{name}", micAway: "通话麦克风未连接", micAwayDetail: "未连接。通话使用 Windows 的默认麦克风。", micActiveDetail: "已连接，用于通话。", micChangedDetail: "已连接；Windows 正在用另一个麦克风通话。", micFailedDetail: "Windows 不允许 Core 将其设为默认。",
    r_stream: "串流", sIdle: "空闲", sTo: "正在串流到 {device}", sVia: "正在串流 · {source}", sKept: "你已设置不动电视", tHeld: "不动电视（串流模式）", sThrough: "通过 {source}", sVd: "Apollo 虚拟显示器已启用",
  },
  ja: {
    title: "状態", allGood: "問題なし", attention: "要確認: {n}", checking: "確認中…", agentDown: "Core のサービスが応答しません。", dismiss: "お知らせを閉じる",
    r_backup: "サーバーのバックアップ", r_restore: "復元テスト", r_drive: "サーバーのドライブ", r_romm: "RomM", r_restart: "Windows の再起動", r_night: "更新の夜", r_tv: "テレビ", r_pc: "PC の空き容量", r_core: "Core の更新",
    notConfigured: "Core > Gaming Mode でサーバーを設定してください", unreachable: "サーバーに接続できません", missing: "サーバーに状態ファイルがありません", unreadable: "状態ファイルを読めません", stale: "最後の連絡 {when}", unknown: "不明",
    bOk: "OK · {when}", bPartial: "一部失敗", bFailed: "失敗", bNever: "まだバックアップなし", lastSuccess: "最後の成功 {when}", failures: "{n} 回続けて OK でない", skipped: "スキップした ROM: {list}",
    rOk: "合格 {when}", rFailed: "失敗", rOverdue: "期限切れ（前回 {when}）", rNever: "未実行",
    dHealthy: "正常 · 空き {free}", dWarning: "警告", dUnhealthy: "故障しかけています",
    mUp: "動作中", mDown: "応答なし", pending: "保留中", notNeeded: "不要",
    nScheduled: "{time} に再起動", nWaiting: "更新が待機中", nNothing: "待機中のものなし",
    tOff: "テレビ操作はオフ", tNotPaired: "未ペアリング", tOk: "ペアリング済み・応答あり", tDown: "応答なし",
    pcOk: "空き {free}", pcLow: "空きは {free} のみ", cAvail: "Core {latest} を利用可能", cCurrent: "最新 · {current}",
    r_mic: "マイク", micMuted: "ミュート中", micLive: "オン", micNone: "マイクなし", micPartial: "一部ミュート中",
    micCalls: "通話用マイク：{name}", micAway: "通話用マイク未接続", micAwayDetail: "未接続です。通話には Windows の既定のマイクが使われます。", micActiveDetail: "接続済みで、通話に使われています。", micChangedDetail: "接続済み。Windows は通話に別のマイクを使っています。", micFailedDetail: "Windows が Core による既定の設定を許可しませんでした。",
    r_stream: "ストリーミング", sIdle: "待機中", sTo: "{device} にストリーミング中", sVia: "ストリーミング中 · {source}", sKept: "手動でテレビに触れない設定", tHeld: "触れていません（ストリーミングモード）", sThrough: "{source} 経由", sVd: "Apollo の仮想ディスプレイが有効です",
  },
  ko: {
    title: "상태", allGood: "모두 정상", attention: "확인 필요: {n}", checking: "확인 중…", agentDown: "Core 서비스가 응답하지 않습니다.", dismiss: "알림 닫기",
    r_backup: "서버 백업", r_restore: "복원 테스트", r_drive: "서버 드라이브", r_romm: "RomM", r_restart: "Windows 다시 시작", r_night: "업데이트의 밤", r_tv: "TV", r_pc: "PC 디스크 공간", r_core: "Core 업데이트",
    notConfigured: "Core > Gaming Mode에서 서버를 설정하세요", unreachable: "서버에 연결할 수 없음", missing: "서버에 상태 파일이 없음", unreadable: "상태 파일을 읽을 수 없음", stale: "마지막 소식 {when}", unknown: "알 수 없음",
    bOk: "정상 · {when}", bPartial: "일부 실패", bFailed: "실패", bNever: "아직 백업 없음", lastSuccess: "마지막 성공 {when}", failures: "{n}회 연속 정상 아님", skipped: "건너뛴 ROM: {list}",
    rOk: "통과 {when}", rFailed: "실패", rOverdue: "기한 지남(마지막 {when})", rNever: "아직 실행 안 함",
    dHealthy: "정상 · {free} 남음", dWarning: "경고", dUnhealthy: "고장 중",
    mUp: "실행 중", mDown: "응답 없음", pending: "대기 중", notNeeded: "필요 없음",
    nScheduled: "{time}에 다시 시작", nWaiting: "업데이트 대기 중", nNothing: "대기 중인 항목 없음",
    tOff: "TV 제어 꺼짐", tNotPaired: "페어링 안 됨", tOk: "페어링됨, 응답함", tDown: "응답 없음",
    pcOk: "{free} 남음", pcLow: "{free}만 남음", cAvail: "Core {latest} 사용 가능", cCurrent: "최신 · {current}",
    r_mic: "마이크", micMuted: "음소거됨", micLive: "켜짐", micNone: "마이크 없음", micPartial: "일부 음소거됨",
    micCalls: "통화용 마이크: {name}", micAway: "통화용 마이크 연결 안 됨", micAwayDetail: "연결되지 않았습니다. 통화에는 Windows 기본 마이크가 쓰입니다.", micActiveDetail: "연결되어 통화에 쓰이고 있습니다.", micChangedDetail: "연결됨. Windows가 통화에 다른 마이크를 쓰고 있습니다.", micFailedDetail: "Windows가 Core의 기본값 설정을 허용하지 않았습니다.",
    r_stream: "스트리밍", sIdle: "대기 중", sTo: "{device}(으)로 스트리밍 중", sVia: "스트리밍 중 · {source}", sKept: "직접 TV 건드리지 않기 켬", tHeld: "건드리지 않음 (스트리밍 모드)", sThrough: "{source} 통해", sVd: "Apollo 가상 디스플레이 사용 중",
  },
  hi: {
    title: "हेल्थ", allGood: "सब ठीक है", attention: "ध्यान चाहिए: {n}", checking: "जाँच हो रही है…", agentDown: "Core की सेवा जवाब नहीं दे रही।", dismiss: "अलर्ट हटाएँ",
    r_backup: "सर्वर बैकअप", r_restore: "रीस्टोर जाँच", r_drive: "सर्वर ड्राइव", r_romm: "RomM", r_restart: "Windows रीस्टार्ट", r_night: "अपडेट की रात", r_tv: "टीवी", r_pc: "पीसी डिस्क स्पेस", r_core: "Core अपडेट",
    notConfigured: "Core > Gaming Mode में सर्वर सेट करें", unreachable: "सर्वर तक नहीं पहुँच सके", missing: "सर्वर पर स्टेटस फ़ाइल नहीं है", unreadable: "स्टेटस फ़ाइल पढ़ी नहीं जा सकी", stale: "आख़िरी खबर {when}", unknown: "अज्ञात",
    bOk: "ठीक · {when}", bPartial: "आंशिक रूप से विफल", bFailed: "विफल", bNever: "अभी कोई बैकअप नहीं", lastSuccess: "आख़िरी सफलता {when}", failures: "लगातार {n} बार ठीक नहीं", skipped: "छोड़े गए ROM: {list}",
    rOk: "पास {when}", rFailed: "विफल", rOverdue: "देर हो गई (पिछली {when})", rNever: "अभी नहीं चली",
    dHealthy: "स्वस्थ · {free} खाली", dWarning: "चेतावनी", dUnhealthy: "ख़राब हो रही है",
    mUp: "चल रहा है", mDown: "जवाब नहीं", pending: "बाकी है", notNeeded: "ज़रूरत नहीं",
    nScheduled: "{time} बजे रीस्टार्ट", nWaiting: "अपडेट इंतज़ार में", nNothing: "कुछ बाकी नहीं",
    tOff: "टीवी कंट्रोल बंद", tNotPaired: "पेयर नहीं है", tOk: "पेयर है, जवाब दे रहा है", tDown: "जवाब नहीं",
    pcOk: "{free} खाली", pcLow: "सिर्फ़ {free} खाली", cAvail: "Core {latest} उपलब्ध", cCurrent: "अप टू डेट · {current}",
    r_mic: "माइक्रोफ़ोन", micMuted: "म्यूट", micLive: "चालू", micNone: "कोई माइक्रोफ़ोन नहीं", micPartial: "आंशिक रूप से म्यूट",
    micCalls: "कॉल माइक: {name}", micAway: "कॉल माइक कनेक्ट नहीं", micAwayDetail: "कनेक्ट नहीं है। कॉल Windows का डिफ़ॉल्ट माइक्रोफ़ोन इस्तेमाल करती हैं।", micActiveDetail: "कनेक्ट है और कॉल के लिए इस्तेमाल हो रहा है।", micChangedDetail: "कनेक्ट है; Windows कॉल के लिए दूसरा माइक्रोफ़ोन इस्तेमाल कर रहा है।", micFailedDetail: "Windows ने Core को इसे डिफ़ॉल्ट बनाने नहीं दिया।",
    r_stream: "स्ट्रीमिंग", sIdle: "निष्क्रिय", sTo: "{device} पर स्ट्रीमिंग", sVia: "स्ट्रीमिंग · {source}", sKept: "आपने टीवी को अलग रखा", tHeld: "नहीं छुआ गया (स्ट्रीम मोड)", sThrough: "{source} के ज़रिए", sVd: "Apollo का वर्चुअल डिस्प्ले चालू है",
  },
};

export const healthLanguages = Object.keys(copy);

export function healthCopy(locale: string): Copy {
  const language = String(locale || "en").toLowerCase().split(/[-_]/)[0];
  const table = copy[language === "ua" ? "uk" : language] ?? en;
  return new Proxy(table, { get: (target, key: string) => target[key] ?? en[key] ?? key });
}

function fill(template: string, values: Record<string, string | number>): string {
  return template.replace(/\{(\w+)\}/g, (all, name) => (name in values ? String(values[name]) : all));
}

export function formatBytes(bytes: unknown, locale = "en"): string {
  const value = Number(bytes);
  if (!Number.isFinite(value) || value < 0) return "?";
  const [amount, unit] = value >= 1e12 ? [value / 1e12, "TB"] : value >= 1e9 ? [value / 1e9, "GB"] : [value / 1e6, "MB"];
  const digits = amount >= 100 ? 0 : 1;
  try { return `${new Intl.NumberFormat(locale, { maximumFractionDigits: digits }).format(amount)} ${unit}`; }
  catch { return `${amount.toFixed(digits)} ${unit}`; }
}

// "3 hours ago", "yesterday", in the user's language.
export function formatWhen(value: unknown, locale = "en", now = new Date()): string {
  const when = typeof value === "string" ? new Date(value) : undefined;
  if (!when || Number.isNaN(when.getTime())) return "?";
  const seconds = (when.getTime() - now.getTime()) / 1000;
  const sign = seconds < 0 ? -1 : 1, size = Math.abs(seconds);
  const [amount, unit]: [number, Intl.RelativeTimeFormatUnit] = size < 3600 ? [sign * Math.round(size / 60), "minute"]
    : size < 48 * 3600 ? [sign * Math.round(size / 3600), "hour"] : [sign * Math.round(size / 86400), "day"];
  try { return new Intl.RelativeTimeFormat(locale, { numeric: "auto" }).format(amount, unit); }
  catch { return `${Math.abs(amount)} ${unit}${Math.abs(amount) === 1 ? "" : "s"}`; }
}

function formatTime(value: unknown, locale: string, hour: unknown): string {
  let when = typeof value === "string" ? new Date(value) : undefined;
  if (!when || Number.isNaN(when.getTime())) { when = new Date(); when.setHours(Number(hour) || 5, 0, 0, 0); }
  try { return when.toLocaleTimeString(locale, { hour: "numeric", minute: "2-digit" }); }
  catch { return `${when.getHours()}:${String(when.getMinutes()).padStart(2, "0")}`; }
}

const titles: Record<string, string> = {
  "server-backup": "r_backup", "restore-check": "r_restore", "server-drive": "r_drive", romm: "r_romm",
  "windows-restart": "r_restart", "update-night": "r_night", tv: "r_tv", streaming: "r_stream", microphone: "r_mic", "pc-disk": "r_pc", "core-update": "r_core",
};

const serverStates: Record<string, string> = { "not-configured": "notConfigured", unreachable: "unreachable", missing: "missing", unreadable: "unreadable" };

function rowValue(row: HealthRowData, text: Copy, locale: string, now: Date): { value: string; details: string[] } {
  const data = row.data ?? {};
  const details: string[] = [];
  const when = (key: string) => formatWhen(data[key], locale, now);
  const sourceState = serverStates[row.state];
  if (sourceState && ["server-backup", "restore-check", "server-drive", "romm"].includes(row.id)) {
    if (row.id === "server-backup" && typeof data.server === "string" && data.server) details.push(`\\\\${data.server}\\CoreStatus`);
    return { value: row.id === "server-backup" ? text[sourceState] : text.unknown, details };
  }
  if (row.state === "stale") return { value: fill(text.stale, { when: when("writtenAt") }), details };
  switch (row.id) {
    case "server-backup": {
      const failures = Number(data.consecutiveFailures) || 0;
      const skipped = Array.isArray(data.skippedPlatforms) ? data.skippedPlatforms.filter(item => typeof item === "string") : [];
      if (row.state !== "ok" && data.lastSuccessAt) details.push(fill(text.lastSuccess, { when: when("lastSuccessAt") }));
      if (failures > 0) details.push(fill(text.failures, { n: failures }));
      if (skipped.length) details.push(fill(text.skipped, { list: skipped.join(", ") }));
      if (row.state !== "ok" && typeof data.message === "string" && data.message) details.push(data.message);
      const value = row.state === "ok" ? fill(text.bOk, { when: when("lastSuccessAt") })
        : row.state === "partial" ? text.bPartial : row.state === "failed" ? text.bFailed : row.state === "never" ? text.bNever : text.unknown;
      return { value, details };
    }
    case "restore-check": {
      if (typeof data.message === "string" && data.message && row.state !== "ok") details.push(data.message);
      const value = row.state === "ok" ? fill(text.rOk, { when: when("at") }) : row.state === "failed" ? text.rFailed
        : row.state === "overdue" ? fill(text.rOverdue, { when: when("at") }) : row.state === "never" ? text.rNever : text.unknown;
      return { value, details };
    }
    case "server-drive": {
      if (typeof data.label === "string" && data.label) details.push(data.label);
      if (row.state !== "healthy" && typeof data.message === "string" && data.message) details.push(data.message);
      const value = row.state === "healthy" ? fill(text.dHealthy, { free: formatBytes(data.freeBytes, locale) })
        : row.state === "warning" ? text.dWarning : row.state === "unhealthy" ? text.dUnhealthy : text.unknown;
      return { value, details };
    }
    case "romm":
      if (row.state !== "reachable" && typeof data.message === "string" && data.message) details.push(data.message);
      return { value: row.state === "reachable" ? text.mUp : row.state === "unreachable" ? text.mDown : text.unknown, details };
    case "windows-restart":
      return { value: row.state === "pending" ? text.pending : text.notNeeded, details };
    case "update-night":
      return { value: row.state === "scheduled" ? fill(text.nScheduled, { time: formatTime(data.scheduledFor, locale, data.hour) })
        : row.state === "waiting" ? text.nWaiting : text.nNothing, details };
    case "tv":
      return { value: ({ off: text.tOff, "not-paired": text.tNotPaired, reachable: text.tOk, unreachable: text.tDown, "stream-mode": text.tHeld } as Record<string, string>)[row.state] ?? text.unknown, details };
    case "streaming": {
      // Stream mode (CORE-STREAM-MODE.md): information only; the TV is left alone while it is on.
      const sources: Record<string, string> = { steam: "Steam Remote Play", steamvr: "SteamVR", sunshine: "Sunshine", apollo: "Apollo" };
      const source = typeof data.source === "string" ? sources[data.source] ?? "" : "";
      const device = typeof data.device === "string" ? data.device.replace(/[\u0000-\u001f\u007f]/g, "").trim().slice(0, 64) : "";
      if (source) details.push(fill(text.sThrough, { source }));
      if (data.virtualDisplay === true) details.push(text.sVd);
      if (row.state === "streaming") return { value: device ? fill(text.sTo, { device }) : source ? fill(text.sVia, { source }) : text.sKept, details };
      if (row.state === "kept-out") return { value: text.sKept, details };
      return { value: row.state === "idle" ? text.sIdle : text.unknown, details };
    }
    case "microphone": {
      // The calls mic rides along as information: its name, or "not connected" (calm, never an alert;
      // calls then use Windows' default). No battery: Windows exposes none for a USB audio receiver.
      const state = ({ muted: text.micMuted, live: text.micLive, partial: text.micPartial, none: text.micNone } as Record<string, string>)[row.state] ?? text.unknown;
      const status = typeof data.callsMicStatus === "string" ? data.callsMicStatus : "none";
      const name = typeof data.callsMic === "string" && data.callsMic.trim() ? data.callsMic.trim().slice(0, 80) : "";
      if (status === "none" || !["active", "absent", "changed", "failed"].includes(status)) return { value: state, details };
      if (name) details.push(fill(text.micCalls, { name }));
      details.push(({ active: text.micActiveDetail, absent: text.micAwayDetail, changed: text.micChangedDetail, failed: text.micFailedDetail } as Record<string, string>)[status]);
      return { value: `${state} · ${status === "absent" ? text.micAway : name || text.micActiveDetail}`, details };
    }
    case "pc-disk": {
      if (typeof data.drive === "string" && data.drive) details.push(data.drive);
      const free = formatBytes(data.freeBytes, locale);
      return { value: row.state === "ok" ? fill(text.pcOk, { free }) : row.state === "low" ? fill(text.pcLow, { free }) : text.unknown, details };
    }
    case "core-update":
      return { value: row.state === "available" ? fill(text.cAvail, { latest: String(data.latest ?? "?") })
        : row.state === "current" ? fill(text.cCurrent, { current: String(data.current ?? "?") }) : text.unknown, details };
  }
  return { value: text.unknown, details };
}

export function healthSummary(answer: HealthAnswer | undefined, locale: string): { line: string; attention: number } {
  const text = healthCopy(locale);
  const attention = Math.max(0, Number(answer?.attention) || 0);
  return { line: attention === 0 ? text.allGood : fill(text.attention, { n: attention }), attention };
}

export function healthLines(answer: HealthAnswer | undefined, locale: string, now = new Date()): HealthLine[] {
  const text = healthCopy(locale);
  const alerts = Array.isArray(answer?.alerts) ? answer!.alerts : [];
  return (Array.isArray(answer?.rows) ? answer!.rows : []).filter(row => row && typeof row.id === "string" && titles[row.id]).map(row => {
    const { value, details } = rowValue(row, text, locale, now);
    const alert = alerts.find(item => item?.rowId === row.id && !item.dismissed);
    const level: HealthLevel = ["ok", "attention", "info", "unknown"].includes(row.level) ? row.level : "unknown";
    const line: HealthLine = { id: row.id, level, title: text[titles[row.id]], value, details, alertId: answer?.alertsEnabled !== false ? alert?.id : undefined };
    if (row.id === "microphone" && (row.state === "muted" || row.state === "partial")) line.tone = "crimson";
    return line;
  });
}
