// Eternal Core: Core Health in Quick Access. Turns the agent's GET /health answer
// (GamingMode.Services/CoreHealth.cs) into the lines HealthSection.tsx shows, in Core's 12
// languages, including the console guards' rows and buttons (CONSOLE-GUARDS.md). Self-contained on purpose:
// tests/coreHealthSection.test.mjs and tests/consoleGuards.test.mjs load this file alone.

export type HealthLevel = "ok" | "attention" | "info" | "unknown";
export type HealthRowData = { id: string; level: HealthLevel; state: string; data?: Record<string, unknown> };
export type HealthAlert = { id: string; rowId: string; dismissed: boolean };
export type HealthAnswer = { attention?: number; alertsEnabled?: boolean; rows?: HealthRowData[]; alerts?: HealthAlert[] };
// tone "crimson": shown in Core crimson (a muted microphone; a partly muted one also counts as attention).
// actions: the console guards' buttons (POST /health/action). A fix shows under the list while the row needs it;
// the others (save as normal, mark as expected, check now) only while the row is open.
export type HealthAction = { id: string; label: string; fix: boolean };
export type HealthLine = { id: string; level: HealthLevel; title: string; value: string; details: string[]; alertId?: string; tone?: "crimson"; actions?: HealthAction[] };

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
  r_steam: "Steam update", stPostponed: "Postponed: {reason}", stUnsupported: "the new Steam doesn't work with the installed Decky", stUnknown: "Core can't tell yet if the new Steam works with Decky", stCantCheck: "Core can't check compatibility with Decky", stFailed: "the last try failed its check and was undone", stPending: "Waiting version: {version}",
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
    r_steam: "Aggiornamento di Steam", stPostponed: "Rimandato: {reason}", stUnsupported: "il nuovo Steam non funziona con il Decky installato", stUnknown: "Core non sa ancora se il nuovo Steam funziona con Decky", stCantCheck: "Core non riesce a verificare la compatibilità con Decky", stFailed: "l'ultimo tentativo non ha superato il controllo ed è stato annullato", stPending: "Versione in attesa: {version}",
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
    r_steam: "Actualización de Steam", stPostponed: "Aplazada: {reason}", stUnsupported: "el nuevo Steam no funciona con el Decky instalado", stUnknown: "Core aún no sabe si el nuevo Steam funciona con Decky", stCantCheck: "Core no puede comprobar la compatibilidad con Decky", stFailed: "el último intento no pasó la comprobación y se deshizo", stPending: "Versión en espera: {version}",
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
    r_steam: "Mise à jour de Steam", stPostponed: "Reportée : {reason}", stUnsupported: "le nouveau Steam ne fonctionne pas avec le Decky installé", stUnknown: "Core ne sait pas encore si le nouveau Steam fonctionne avec Decky", stCantCheck: "Core ne parvient pas à vérifier la compatibilité avec Decky", stFailed: "la dernière tentative n'a pas passé la vérification et a été annulée", stPending: "Version en attente : {version}",
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
    r_steam: "Steam-Update", stPostponed: "Verschoben: {reason}", stUnsupported: "das neue Steam funktioniert nicht mit dem installierten Decky", stUnknown: "Core weiß noch nicht, ob das neue Steam mit Decky funktioniert", stCantCheck: "Core kann die Kompatibilität mit Decky nicht prüfen", stFailed: "der letzte Versuch hat die Prüfung nicht bestanden und wurde rückgängig gemacht", stPending: "Wartende Version: {version}",
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
    r_steam: "Atualização do Steam", stPostponed: "Adiada: {reason}", stUnsupported: "o novo Steam não funciona com o Decky instalado", stUnknown: "o Core ainda não sabe se o novo Steam funciona com o Decky", stCantCheck: "o Core não consegue verificar a compatibilidade com o Decky", stFailed: "a última tentativa não passou na verificação e foi desfeita", stPending: "Versão em espera: {version}",
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
    r_steam: "Оновлення Steam", stPostponed: "Відкладено: {reason}", stUnsupported: "новий Steam не працює з установленим Decky", stUnknown: "Core ще не знає, чи працює новий Steam з Decky", stCantCheck: "Core не може перевірити сумісність із Decky", stFailed: "остання спроба не пройшла перевірку й була скасована", stPending: "Версія в очікуванні: {version}",
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
    r_steam: "Обновление Steam", stPostponed: "Отложено: {reason}", stUnsupported: "новый Steam не работает с установленным Decky", stUnknown: "Core пока не знает, работает ли новый Steam с Decky", stCantCheck: "Core не может проверить совместимость с Decky", stFailed: "последняя попытка не прошла проверку и была отменена", stPending: "Ожидающая версия: {version}",
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
    r_steam: "Steam 更新", stPostponed: "已推迟：{reason}", stUnsupported: "新版 Steam 无法与已安装的 Decky 配合使用", stUnknown: "Core 还无法判断新版 Steam 是否能与 Decky 配合使用", stCantCheck: "Core 无法检查与 Decky 的兼容性", stFailed: "上次尝试未通过检查，已被撤销", stPending: "等待中的版本：{version}",
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
    r_steam: "Steam の更新", stPostponed: "延期：{reason}", stUnsupported: "新しい Steam はインストール済みの Decky では動作しません", stUnknown: "新しい Steam が Decky で動くか Core にはまだわかりません", stCantCheck: "Core は Decky との互換性を確認できません", stFailed: "前回の試行はチェックに通らず、元に戻されました", stPending: "待機中のバージョン：{version}",
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
    r_steam: "Steam 업데이트", stPostponed: "연기됨: {reason}", stUnsupported: "새 Steam은 설치된 Decky와 작동하지 않습니다", stUnknown: "Core는 새 Steam이 Decky와 작동하는지 아직 알 수 없습니다", stCantCheck: "Core가 Decky와의 호환성을 확인할 수 없습니다", stFailed: "마지막 시도가 확인을 통과하지 못해 취소되었습니다", stPending: "대기 중인 버전: {version}",
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
    r_steam: "Steam अपडेट", stPostponed: "टाला गया: {reason}", stUnsupported: "नया Steam इंस्टॉल किए गए Decky के साथ काम नहीं करता", stUnknown: "Core अभी नहीं बता सकता कि नया Steam Decky के साथ काम करता है या नहीं", stCantCheck: "Core Decky के साथ संगतता नहीं जाँच पा रहा", stFailed: "पिछली कोशिश जाँच में पास नहीं हुई और वापस ले ली गई", stPending: "इंतज़ार में संस्करण: {version}",
  },
};

// Console guards (CONSOLE-GUARDS.md): the rows that keep the PC a console, merged into each language below.
const consoleCopy: Record<string, Copy> = {
  en: {
    r_display: "Display", r_sound: "Sound output", r_controllers: "Controllers", r_steamSign: "Steam sign-in", r_autoSign: "Automatic sign-in", r_bitlocker: "BitLocker",
    r_prompts: "Windows setup screens", r_updPolicy: "Update policy", r_launchUpd: "Launcher updates", r_launchFocus: "Launcher pop-ups", r_wake: "After waking",
    dpNormal: "Normal · {mode}", dpChanged: "Changed: {what}", dpNotSaved: "Not saved yet", dpMissing: "TV not connected", dpSaved: "Normal: {mode}", dpNow: "Now: {mode}",
    dpResolution: "resolution", dpRefresh: "refresh rate", dpHdr: "HDR", dpPrimary: "main display", dpVrr: "variable refresh",
    dpVrrNote: "G-Sync and FreeSync can't be read from Windows; only Windows' variable refresh switch is compared.",
    soNormal: "{name}", soChanged: "Now: {name}", soNotSaved: "Not saved yet", soMissing: "{name} not connected", soStream: "Left to Stream mode", soSaved: "Normal: {name}",
    coSeen: "{n} connected", coNone: "None connected", coMissing: "Expected, not seen: {n}",
    siSigned: "Signed in · {name}", siOffline: "Offline mode · {name}", siOut: "Not signed in", siOutDetail: "A sign-in or Steam Guard screen is probably on the TV: sign in there with the controller.",
    siNoRemember: "Signed in; the next start will ask for the password", siNoRememberDetail: "Sign out and back in with Remember me ticked.", siNotRunning: "Steam not running",
    asOn: "On · {user}", asOff: "Off: Windows will ask for the password", asExpires: "Ends after {n} more sign-ins", asHello: "Blocked by Windows Hello-only sign-in",
    asGuide: "Set it up again on the desktop: Core > Gaming Mode > Stays a console.", asPlain: "Windows keeps this password in plain text; Sysinternals Autologon keeps it encrypted.",
    blOff: "Off", blOn: "On · recovery key protector", blEncrypting: "Encrypting · recovery key protector", blUnchecked: "On · check it on the desktop", blNoKey: "On · no recovery key",
    blFw: "Firmware update waiting", blFwNoGuard: "Firmware update waiting: may ask for the key", blSuspended: "Suspended", blSuspendedUpd: "Suspended for one restart", blWaiting: "Waiting for activation",
    blConfirm: "Confirm the key backup at {url}", blBackup: "Windows logged a backup {when}", blCheckDesktop: "Check it on the desktop: Core > Gaming Mode > Stays a console.",
    blGuardHint: "Turn on BitLocker protection on the desktop so Core can suspend it for one restart.", blNoKeyDetail: "Add a recovery key on the desktop, then back it up to your Microsoft account.",
    blSuspendedDetail: "Resume it on the desktop: Control Panel > BitLocker > Resume protection.",
    prOff: "Off", prOn: "On", prDetail: "Undo it any time from Core > Gaming Mode > Quiet Windows.",
    upApplied: "Drivers held · feature updates stay on {version}", upHome: "Drivers held · Home can't hold feature updates", upNot: "Not applied",
    upNotDetail: "Apply it on the desktop: Core > Gaming Mode > Quiet Windows.", upHomeDetail: "Windows Home ignores the feature-update pin.",
    luNone: "Nothing waiting", luPending: "Waiting: {list}", luDetail: "These may ask for administrator rights: let them finish on the desktop.",
    lfOn: "Watching · {n} today", lfOff: "Off", lfLast: "Last pushed back: {name}",
    wkNone: "No wake checked yet", wkOk: "OK · {when}", wkFixed: "Fixed by Core · {when}", wkProblem: "Needs a look · {when}", wkNet: "Network cable unplugged", wkOff: "Off",
    wkFocus: "Big Picture was brought back to the front.", wkInput: "The TV input switch was sent again.", wkNoBp: "Big Picture was not visible.", wkTvNo: "The TV was not on Core's input.",
    wkTemps: "Temperatures: not shown (Windows has no driver-free reading).", wkAdapter: "Adapter: {name}",
    aDisplayRestore: "Restore display settings", aDisplaySave: "Save current display as normal", aSoundRestore: "Switch sound back", aSoundSave: "Save current sound output as normal",
    aControllersExpect: "Mark these controllers as expected", aPromptsOff: "Turn off setup screens", aBitlockerSuspend: "Suspend BitLocker for one restart", aWakeNow: "Run the wake check now",
    aDone: "Done", aFailed: "That didn't work", aNotWhilePlaying: "Not while a game runs",
  },
  it: {
    r_display: "Schermo", r_sound: "Uscita audio", r_controllers: "Controller", r_steamSign: "Accesso a Steam", r_autoSign: "Accesso automatico", r_bitlocker: "BitLocker",
    r_prompts: "Schermate di configurazione", r_updPolicy: "Criteri di aggiornamento", r_launchUpd: "Aggiornamenti dei launcher", r_launchFocus: "Finestre dei launcher", r_wake: "Dopo il risveglio",
    dpNormal: "Normale · {mode}", dpChanged: "Cambiato: {what}", dpNotSaved: "Non ancora salvato", dpMissing: "TV non collegata", dpSaved: "Normale: {mode}", dpNow: "Ora: {mode}",
    dpResolution: "risoluzione", dpRefresh: "frequenza", dpHdr: "HDR", dpPrimary: "schermo principale", dpVrr: "frequenza variabile",
    dpVrrNote: "G-Sync e FreeSync non si leggono da Windows; si confronta solo l'interruttore della frequenza variabile di Windows.",
    soNormal: "{name}", soChanged: "Ora: {name}", soNotSaved: "Non ancora salvata", soMissing: "{name} non collegato", soStream: "Lasciata alla modalità streaming", soSaved: "Normale: {name}",
    coSeen: "{n} collegati", coNone: "Nessuno collegato", coMissing: "Attesi, non visti: {n}",
    siSigned: "Connesso · {name}", siOffline: "Modalità offline · {name}", siOut: "Non connesso", siOutDetail: "Sulla TV c'è probabilmente l'accesso o Steam Guard: accedi lì con il controller.",
    siNoRemember: "Connesso; al prossimo avvio chiederà la password", siNoRememberDetail: "Esci e rientra con Ricordami selezionato.", siNotRunning: "Steam non è avviato",
    asOn: "Attivo · {user}", asOff: "Spento: Windows chiederà la password", asExpires: "Finisce tra {n} accessi", asHello: "Bloccato dall'accesso solo con Windows Hello",
    asGuide: "Configuralo di nuovo dal desktop: Core > Gaming Mode > Resta una console.", asPlain: "Windows conserva questa password in chiaro; Autologon di Sysinternals la conserva cifrata.",
    blOff: "Spento", blOn: "Attivo · chiave di ripristino presente", blEncrypting: "Crittografia in corso · chiave di ripristino presente", blUnchecked: "Attivo · controllalo dal desktop", blNoKey: "Attivo · nessuna chiave di ripristino",
    blFw: "Aggiornamento del firmware in attesa", blFwNoGuard: "Firmware in attesa: potrebbe chiedere la chiave", blSuspended: "Sospeso", blSuspendedUpd: "Sospeso per un riavvio", blWaiting: "In attesa di attivazione",
    blConfirm: "Conferma il backup della chiave su {url}", blBackup: "Windows ha registrato un backup {when}", blCheckDesktop: "Controllalo dal desktop: Core > Gaming Mode > Resta una console.",
    blGuardHint: "Attiva la protezione BitLocker dal desktop così Core può sospenderlo per un riavvio.", blNoKeyDetail: "Aggiungi una chiave di ripristino dal desktop e salvala nel tuo account Microsoft.",
    blSuspendedDetail: "Riattivalo dal desktop: Pannello di controllo > BitLocker > Riprendi la protezione.",
    prOff: "Spente", prOn: "Attive", prDetail: "Puoi annullare quando vuoi da Core > Gaming Mode > Windows tranquillo.",
    upApplied: "Driver bloccati · funzionalità ferme alla {version}", upHome: "Driver bloccati · Home non può fermare le funzionalità", upNot: "Non applicati",
    upNotDetail: "Applicali dal desktop: Core > Gaming Mode > Windows tranquillo.", upHomeDetail: "Windows Home ignora il blocco della versione.",
    luNone: "Niente in attesa", luPending: "In attesa: {list}", luDetail: "Potrebbero chiedere i permessi di amministratore: lasciali finire sul desktop.",
    lfOn: "Attivo · {n} oggi", lfOff: "Spento", lfLast: "Ultima rimandata dietro: {name}",
    wkNone: "Nessun risveglio controllato", wkOk: "OK · {when}", wkFixed: "Sistemato da Core · {when}", wkProblem: "Da controllare · {when}", wkNet: "Cavo di rete scollegato", wkOff: "Spento",
    wkFocus: "La Big Picture è stata riportata in primo piano.", wkInput: "Il cambio di ingresso della TV è stato ripetuto.", wkNoBp: "La Big Picture non era visibile.", wkTvNo: "La TV non era sull'ingresso di Core.",
    wkTemps: "Temperature: non mostrate (Windows non le legge senza driver).", wkAdapter: "Scheda: {name}",
    aDisplayRestore: "Ripristina le impostazioni dello schermo", aDisplaySave: "Salva lo schermo attuale come normale", aSoundRestore: "Riporta indietro l'audio", aSoundSave: "Salva l'uscita audio attuale come normale",
    aControllersExpect: "Segna questi controller come attesi", aPromptsOff: "Spegni le schermate di configurazione", aBitlockerSuspend: "Sospendi BitLocker per un riavvio", aWakeNow: "Controlla ora il risveglio",
    aDone: "Fatto", aFailed: "Non ha funzionato", aNotWhilePlaying: "Non durante un gioco",
  },
  es: {
    r_display: "Pantalla", r_sound: "Salida de sonido", r_controllers: "Mandos", r_steamSign: "Sesión de Steam", r_autoSign: "Inicio de sesión automático", r_bitlocker: "BitLocker",
    r_prompts: "Pantallas de configuración", r_updPolicy: "Directiva de actualizaciones", r_launchUpd: "Actualizaciones de launchers", r_launchFocus: "Ventanas de launchers", r_wake: "Al despertar",
    dpNormal: "Normal · {mode}", dpChanged: "Cambiado: {what}", dpNotSaved: "Aún sin guardar", dpMissing: "TV no conectada", dpSaved: "Normal: {mode}", dpNow: "Ahora: {mode}",
    dpResolution: "resolución", dpRefresh: "frecuencia", dpHdr: "HDR", dpPrimary: "pantalla principal", dpVrr: "frecuencia variable",
    dpVrrNote: "G-Sync y FreeSync no se pueden leer desde Windows; solo se compara el interruptor de frecuencia variable de Windows.",
    soNormal: "{name}", soChanged: "Ahora: {name}", soNotSaved: "Aún sin guardar", soMissing: "{name} no conectado", soStream: "En manos del modo streaming", soSaved: "Normal: {name}",
    coSeen: "{n} conectados", coNone: "Ninguno conectado", coMissing: "Esperados, no vistos: {n}",
    siSigned: "Sesión iniciada · {name}", siOffline: "Modo sin conexión · {name}", siOut: "Sin sesión", siOutDetail: "Probablemente hay una pantalla de inicio de sesión o Steam Guard en la TV: inicia sesión allí con el mando.",
    siNoRemember: "Sesión iniciada; el próximo arranque pedirá la contraseña", siNoRememberDetail: "Cierra sesión y vuelve a entrar con Recordarme marcado.", siNotRunning: "Steam no está abierto",
    asOn: "Activado · {user}", asOff: "Desactivado: Windows pedirá la contraseña", asExpires: "Termina tras {n} inicios más", asHello: "Bloqueado por el inicio solo con Windows Hello",
    asGuide: "Vuelve a configurarlo en el escritorio: Core > Gaming Mode > Sigue siendo una consola.", asPlain: "Windows guarda esta contraseña en texto plano; Autologon de Sysinternals la guarda cifrada.",
    blOff: "Desactivado", blOn: "Activado · con clave de recuperación", blEncrypting: "Cifrando · con clave de recuperación", blUnchecked: "Activado · compruébalo en el escritorio", blNoKey: "Activado · sin clave de recuperación",
    blFw: "Actualización de firmware pendiente", blFwNoGuard: "Firmware pendiente: puede pedir la clave", blSuspended: "Suspendido", blSuspendedUpd: "Suspendido durante un reinicio", blWaiting: "Esperando activación",
    blConfirm: "Confirma la copia de la clave en {url}", blBackup: "Windows registró una copia {when}", blCheckDesktop: "Compruébalo en el escritorio: Core > Gaming Mode > Sigue siendo una consola.",
    blGuardHint: "Activa la protección de BitLocker en el escritorio para que Core pueda suspenderlo durante un reinicio.", blNoKeyDetail: "Añade una clave de recuperación en el escritorio y guárdala en tu cuenta Microsoft.",
    blSuspendedDetail: "Reanúdalo en el escritorio: Panel de control > BitLocker > Reanudar protección.",
    prOff: "Desactivadas", prOn: "Activadas", prDetail: "Puedes deshacerlo cuando quieras en Core > Gaming Mode > Windows tranquilo.",
    upApplied: "Controladores retenidos · características en {version}", upHome: "Controladores retenidos · Home no puede retener características", upNot: "No aplicada",
    upNotDetail: "Aplícala en el escritorio: Core > Gaming Mode > Windows tranquilo.", upHomeDetail: "Windows Home ignora la fijación de versión.",
    luNone: "Nada pendiente", luPending: "Pendiente: {list}", luDetail: "Pueden pedir permisos de administrador: deja que terminen en el escritorio.",
    lfOn: "Vigilando · {n} hoy", lfOff: "Desactivado", lfLast: "Última enviada detrás: {name}",
    wkNone: "Ningún despertar comprobado", wkOk: "OK · {when}", wkFixed: "Arreglado por Core · {when}", wkProblem: "Revisar · {when}", wkNet: "Cable de red desconectado", wkOff: "Desactivado",
    wkFocus: "Big Picture volvió al frente.", wkInput: "Se repitió el cambio de entrada de la TV.", wkNoBp: "Big Picture no estaba visible.", wkTvNo: "La TV no estaba en la entrada de Core.",
    wkTemps: "Temperaturas: no se muestran (Windows no las lee sin controladores).", wkAdapter: "Adaptador: {name}",
    aDisplayRestore: "Restaurar la configuración de pantalla", aDisplaySave: "Guardar la pantalla actual como normal", aSoundRestore: "Volver al sonido anterior", aSoundSave: "Guardar la salida de sonido actual como normal",
    aControllersExpect: "Marcar estos mandos como esperados", aPromptsOff: "Desactivar las pantallas de configuración", aBitlockerSuspend: "Suspender BitLocker durante un reinicio", aWakeNow: "Comprobar el despertar ahora",
    aDone: "Hecho", aFailed: "No funcionó", aNotWhilePlaying: "No durante una partida",
  },
  fr: {
    r_display: "Affichage", r_sound: "Sortie audio", r_controllers: "Manettes", r_steamSign: "Connexion Steam", r_autoSign: "Connexion automatique", r_bitlocker: "BitLocker",
    r_prompts: "Écrans de configuration", r_updPolicy: "Stratégie de mise à jour", r_launchUpd: "Mises à jour des launchers", r_launchFocus: "Fenêtres des launchers", r_wake: "Au réveil",
    dpNormal: "Normal · {mode}", dpChanged: "Modifié : {what}", dpNotSaved: "Pas encore enregistré", dpMissing: "TV non connectée", dpSaved: "Normal : {mode}", dpNow: "Actuel : {mode}",
    dpResolution: "résolution", dpRefresh: "fréquence", dpHdr: "HDR", dpPrimary: "écran principal", dpVrr: "fréquence variable",
    dpVrrNote: "G-Sync et FreeSync ne sont pas lisibles depuis Windows ; seul l'interrupteur de fréquence variable de Windows est comparé.",
    soNormal: "{name}", soChanged: "Actuel : {name}", soNotSaved: "Pas encore enregistrée", soMissing: "{name} non connecté", soStream: "Laissée au mode streaming", soSaved: "Normal : {name}",
    coSeen: "{n} connectées", coNone: "Aucune connectée", coMissing: "Attendues, non vues : {n}",
    siSigned: "Connecté · {name}", siOffline: "Mode hors ligne · {name}", siOut: "Non connecté", siOutDetail: "Un écran de connexion ou Steam Guard est sans doute sur la TV : connectez-vous là avec la manette.",
    siNoRemember: "Connecté ; le prochain démarrage demandera le mot de passe", siNoRememberDetail: "Déconnectez-vous puis reconnectez-vous en cochant Se souvenir de moi.", siNotRunning: "Steam n'est pas lancé",
    asOn: "Active · {user}", asOff: "Désactivée : Windows demandera le mot de passe", asExpires: "S'arrête après {n} connexions", asHello: "Bloquée par la connexion Windows Hello uniquement",
    asGuide: "Reconfigurez-la depuis le bureau : Core > Gaming Mode > Reste une console.", asPlain: "Windows garde ce mot de passe en clair ; Autologon de Sysinternals le garde chiffré.",
    blOff: "Désactivé", blOn: "Actif · clé de récupération présente", blEncrypting: "Chiffrement en cours · clé de récupération présente", blUnchecked: "Actif · vérifiez-le depuis le bureau", blNoKey: "Actif · aucune clé de récupération",
    blFw: "Mise à jour du firmware en attente", blFwNoGuard: "Firmware en attente : la clé pourrait être demandée", blSuspended: "Suspendu", blSuspendedUpd: "Suspendu pour un redémarrage", blWaiting: "En attente d'activation",
    blConfirm: "Vérifiez la sauvegarde de la clé sur {url}", blBackup: "Windows a noté une sauvegarde {when}", blCheckDesktop: "Vérifiez-le depuis le bureau : Core > Gaming Mode > Reste une console.",
    blGuardHint: "Activez la protection BitLocker depuis le bureau pour que Core puisse le suspendre pour un redémarrage.", blNoKeyDetail: "Ajoutez une clé de récupération depuis le bureau, puis sauvegardez-la dans votre compte Microsoft.",
    blSuspendedDetail: "Reprenez-le depuis le bureau : Panneau de configuration > BitLocker > Reprendre la protection.",
    prOff: "Désactivés", prOn: "Actifs", prDetail: "Annulable à tout moment dans Core > Gaming Mode > Windows tranquille.",
    upApplied: "Pilotes bloqués · fonctionnalités en {version}", upHome: "Pilotes bloqués · Famille ne peut pas bloquer les fonctionnalités", upNot: "Non appliquée",
    upNotDetail: "Appliquez-la depuis le bureau : Core > Gaming Mode > Windows tranquille.", upHomeDetail: "Windows Famille ignore le blocage de version.",
    luNone: "Rien en attente", luPending: "En attente : {list}", luDetail: "Elles peuvent demander les droits d'administrateur : laissez-les finir sur le bureau.",
    lfOn: "Surveillance · {n} aujourd'hui", lfOff: "Désactivé", lfLast: "Dernière renvoyée derrière : {name}",
    wkNone: "Aucun réveil vérifié", wkOk: "OK · {when}", wkFixed: "Corrigé par Core · {when}", wkProblem: "À vérifier · {when}", wkNet: "Câble réseau débranché", wkOff: "Désactivé",
    wkFocus: "Big Picture a été ramené au premier plan.", wkInput: "Le changement d'entrée de la TV a été renvoyé.", wkNoBp: "Big Picture n'était pas visible.", wkTvNo: "La TV n'était pas sur l'entrée de Core.",
    wkTemps: "Températures : non affichées (Windows ne les lit pas sans pilote).", wkAdapter: "Carte : {name}",
    aDisplayRestore: "Restaurer les réglages d'affichage", aDisplaySave: "Enregistrer l'affichage actuel comme normal", aSoundRestore: "Revenir à la sortie audio", aSoundSave: "Enregistrer la sortie audio actuelle comme normale",
    aControllersExpect: "Marquer ces manettes comme attendues", aPromptsOff: "Désactiver les écrans de configuration", aBitlockerSuspend: "Suspendre BitLocker pour un redémarrage", aWakeNow: "Vérifier le réveil maintenant",
    aDone: "Fait", aFailed: "Cela n'a pas marché", aNotWhilePlaying: "Pas pendant une partie",
  },
  de: {
    r_display: "Anzeige", r_sound: "Tonausgabe", r_controllers: "Controller", r_steamSign: "Steam-Anmeldung", r_autoSign: "Automatische Anmeldung", r_bitlocker: "BitLocker",
    r_prompts: "Windows-Einrichtungsseiten", r_updPolicy: "Update-Richtlinie", r_launchUpd: "Launcher-Updates", r_launchFocus: "Launcher-Fenster", r_wake: "Nach dem Aufwachen",
    dpNormal: "Normal · {mode}", dpChanged: "Geändert: {what}", dpNotSaved: "Noch nicht gespeichert", dpMissing: "TV nicht verbunden", dpSaved: "Normal: {mode}", dpNow: "Jetzt: {mode}",
    dpResolution: "Auflösung", dpRefresh: "Bildwiederholrate", dpHdr: "HDR", dpPrimary: "Hauptanzeige", dpVrr: "variable Bildwiederholrate",
    dpVrrNote: "G-Sync und FreeSync sind unter Windows nicht lesbar; verglichen wird nur der Windows-Schalter für variable Bildwiederholrate.",
    soNormal: "{name}", soChanged: "Jetzt: {name}", soNotSaved: "Noch nicht gespeichert", soMissing: "{name} nicht verbunden", soStream: "Dem Streaming-Modus überlassen", soSaved: "Normal: {name}",
    coSeen: "{n} verbunden", coNone: "Keiner verbunden", coMissing: "Erwartet, nicht gesehen: {n}",
    siSigned: "Angemeldet · {name}", siOffline: "Offlinemodus · {name}", siOut: "Nicht angemeldet", siOutDetail: "Auf dem Fernseher ist wohl eine Anmeldung oder Steam Guard zu sehen: melde dich dort mit dem Controller an.",
    siNoRemember: "Angemeldet; der nächste Start fragt nach dem Passwort", siNoRememberDetail: "Ab- und wieder anmelden, dabei Angemeldet bleiben ankreuzen.", siNotRunning: "Steam läuft nicht",
    asOn: "An · {user}", asOff: "Aus: Windows fragt nach dem Passwort", asExpires: "Endet nach {n} weiteren Anmeldungen", asHello: "Blockiert durch Anmeldung nur mit Windows Hello",
    asGuide: "Am Desktop neu einrichten: Core > Gaming Mode > Bleibt eine Konsole.", asPlain: "Windows speichert dieses Passwort im Klartext; Sysinternals Autologon speichert es verschlüsselt.",
    blOff: "Aus", blOn: "An · Wiederherstellungsschlüssel vorhanden", blEncrypting: "Verschlüsselt gerade · Wiederherstellungsschlüssel vorhanden", blUnchecked: "An · am Desktop prüfen", blNoKey: "An · kein Wiederherstellungsschlüssel",
    blFw: "Firmware-Update wartet", blFwNoGuard: "Firmware wartet: fragt evtl. nach dem Schlüssel", blSuspended: "Ausgesetzt", blSuspendedUpd: "Für einen Neustart ausgesetzt", blWaiting: "Wartet auf Aktivierung",
    blConfirm: "Prüfe die Schlüsselsicherung unter {url}", blBackup: "Windows hat {when} eine Sicherung vermerkt", blCheckDesktop: "Am Desktop prüfen: Core > Gaming Mode > Bleibt eine Konsole.",
    blGuardHint: "Schalte am Desktop den BitLocker-Schutz ein, damit Core BitLocker für einen Neustart aussetzen kann.", blNoKeyDetail: "Füge am Desktop einen Wiederherstellungsschlüssel hinzu und sichere ihn in deinem Microsoft-Konto.",
    blSuspendedDetail: "Am Desktop fortsetzen: Systemsteuerung > BitLocker > Schutz fortsetzen.",
    prOff: "Aus", prOn: "An", prDetail: "Jederzeit rückgängig unter Core > Gaming Mode > Ruhiges Windows.",
    upApplied: "Treiber gesperrt · Funktionsupdates bleiben bei {version}", upHome: "Treiber gesperrt · Home kann Funktionsupdates nicht halten", upNot: "Nicht angewendet",
    upNotDetail: "Am Desktop anwenden: Core > Gaming Mode > Ruhiges Windows.", upHomeDetail: "Windows Home ignoriert die Versionsfixierung.",
    luNone: "Nichts wartet", luPending: "Wartet: {list}", luDetail: "Diese fragen evtl. nach Administratorrechten: lass sie am Desktop fertig werden.",
    lfOn: "Wacht · {n} heute", lfOff: "Aus", lfLast: "Zuletzt nach hinten geschoben: {name}",
    wkNone: "Noch kein Aufwachen geprüft", wkOk: "OK · {when}", wkFixed: "Von Core behoben · {when}", wkProblem: "Bitte prüfen · {when}", wkNet: "Netzwerkkabel getrennt", wkOff: "Aus",
    wkFocus: "Big Picture wurde wieder nach vorn geholt.", wkInput: "Der Eingangswechsel des Fernsehers wurde erneut gesendet.", wkNoBp: "Big Picture war nicht sichtbar.", wkTvNo: "Der Fernseher war nicht auf dem Eingang von Core.",
    wkTemps: "Temperaturen: nicht angezeigt (Windows liest sie nicht ohne Treiber).", wkAdapter: "Adapter: {name}",
    aDisplayRestore: "Anzeigeeinstellungen wiederherstellen", aDisplaySave: "Aktuelle Anzeige als normal speichern", aSoundRestore: "Ton zurückwechseln", aSoundSave: "Aktuelle Tonausgabe als normal speichern",
    aControllersExpect: "Diese Controller als erwartet markieren", aPromptsOff: "Einrichtungsseiten ausschalten", aBitlockerSuspend: "BitLocker für einen Neustart aussetzen", aWakeNow: "Aufwach-Prüfung jetzt ausführen",
    aDone: "Erledigt", aFailed: "Hat nicht geklappt", aNotWhilePlaying: "Nicht während eines Spiels",
  },
  pt: {
    r_display: "Tela", r_sound: "Saída de som", r_controllers: "Controles", r_steamSign: "Login no Steam", r_autoSign: "Login automático", r_bitlocker: "BitLocker",
    r_prompts: "Telas de configuração", r_updPolicy: "Política de atualizações", r_launchUpd: "Atualizações de launchers", r_launchFocus: "Janelas de launchers", r_wake: "Ao despertar",
    dpNormal: "Normal · {mode}", dpChanged: "Alterada: {what}", dpNotSaved: "Ainda não salva", dpMissing: "TV não conectada", dpSaved: "Normal: {mode}", dpNow: "Agora: {mode}",
    dpResolution: "resolução", dpRefresh: "taxa de atualização", dpHdr: "HDR", dpPrimary: "tela principal", dpVrr: "taxa variável",
    dpVrrNote: "G-Sync e FreeSync não podem ser lidos pelo Windows; só o botão de taxa variável do Windows é comparado.",
    soNormal: "{name}", soChanged: "Agora: {name}", soNotSaved: "Ainda não salva", soMissing: "{name} não conectado", soStream: "Deixada ao modo streaming", soSaved: "Normal: {name}",
    coSeen: "{n} conectados", coNone: "Nenhum conectado", coMissing: "Esperados, não vistos: {n}",
    siSigned: "Conectado · {name}", siOffline: "Modo offline · {name}", siOut: "Não conectado", siOutDetail: "Provavelmente há uma tela de login ou do Steam Guard na TV: entre lá com o controle.",
    siNoRemember: "Conectado; a próxima inicialização pedirá a senha", siNoRememberDetail: "Saia e entre de novo com Lembrar de mim marcado.", siNotRunning: "Steam não está aberto",
    asOn: "Ativo · {user}", asOff: "Desativado: o Windows pedirá a senha", asExpires: "Termina após mais {n} logins", asHello: "Bloqueado pela entrada somente com Windows Hello",
    asGuide: "Configure de novo na área de trabalho: Core > Gaming Mode > Continua sendo um console.", asPlain: "O Windows guarda esta senha em texto simples; o Autologon do Sysinternals a guarda criptografada.",
    blOff: "Desativado", blOn: "Ativo · com chave de recuperação", blEncrypting: "Criptografando · com chave de recuperação", blUnchecked: "Ativo · verifique na área de trabalho", blNoKey: "Ativo · sem chave de recuperação",
    blFw: "Atualização de firmware aguardando", blFwNoGuard: "Firmware aguardando: pode pedir a chave", blSuspended: "Suspenso", blSuspendedUpd: "Suspenso por um reinício", blWaiting: "Aguardando ativação",
    blConfirm: "Confirme o backup da chave em {url}", blBackup: "O Windows registrou um backup {when}", blCheckDesktop: "Verifique na área de trabalho: Core > Gaming Mode > Continua sendo um console.",
    blGuardHint: "Ative a proteção do BitLocker na área de trabalho para o Core poder suspendê-lo por um reinício.", blNoKeyDetail: "Adicione uma chave de recuperação na área de trabalho e salve-a na sua conta Microsoft.",
    blSuspendedDetail: "Retome na área de trabalho: Painel de Controle > BitLocker > Retomar proteção.",
    prOff: "Desativadas", prOn: "Ativas", prDetail: "Desfaça quando quiser em Core > Gaming Mode > Windows tranquilo.",
    upApplied: "Drivers retidos · recursos ficam na {version}", upHome: "Drivers retidos · o Home não retém recursos", upNot: "Não aplicada",
    upNotDetail: "Aplique na área de trabalho: Core > Gaming Mode > Windows tranquilo.", upHomeDetail: "O Windows Home ignora a fixação de versão.",
    luNone: "Nada aguardando", luPending: "Aguardando: {list}", luDetail: "Podem pedir permissão de administrador: deixe terminar na área de trabalho.",
    lfOn: "Vigiando · {n} hoje", lfOff: "Desativado", lfLast: "Última mandada para trás: {name}",
    wkNone: "Nenhum despertar verificado", wkOk: "OK · {when}", wkFixed: "Corrigido pelo Core · {when}", wkProblem: "Verificar · {when}", wkNet: "Cabo de rede desconectado", wkOff: "Desativado",
    wkFocus: "O Big Picture voltou para a frente.", wkInput: "A troca de entrada da TV foi enviada de novo.", wkNoBp: "O Big Picture não estava visível.", wkTvNo: "A TV não estava na entrada do Core.",
    wkTemps: "Temperaturas: não mostradas (o Windows não as lê sem driver).", wkAdapter: "Adaptador: {name}",
    aDisplayRestore: "Restaurar as configurações de tela", aDisplaySave: "Salvar a tela atual como normal", aSoundRestore: "Voltar o som", aSoundSave: "Salvar a saída de som atual como normal",
    aControllersExpect: "Marcar estes controles como esperados", aPromptsOff: "Desativar as telas de configuração", aBitlockerSuspend: "Suspender o BitLocker por um reinício", aWakeNow: "Verificar o despertar agora",
    aDone: "Feito", aFailed: "Não funcionou", aNotWhilePlaying: "Não durante um jogo",
  },
  uk: {
    r_display: "Екран", r_sound: "Виведення звуку", r_controllers: "Контролери", r_steamSign: "Вхід у Steam", r_autoSign: "Автоматичний вхід", r_bitlocker: "BitLocker",
    r_prompts: "Екрани налаштування Windows", r_updPolicy: "Політика оновлень", r_launchUpd: "Оновлення лаунчерів", r_launchFocus: "Вікна лаунчерів", r_wake: "Після пробудження",
    dpNormal: "Звичайно · {mode}", dpChanged: "Змінено: {what}", dpNotSaved: "Ще не збережено", dpMissing: "Телевізор не підключено", dpSaved: "Звичайно: {mode}", dpNow: "Зараз: {mode}",
    dpResolution: "роздільна здатність", dpRefresh: "частота оновлення", dpHdr: "HDR", dpPrimary: "основний екран", dpVrr: "змінна частота",
    dpVrrNote: "G-Sync і FreeSync не читаються з Windows; порівнюється лише перемикач змінної частоти Windows.",
    soNormal: "{name}", soChanged: "Зараз: {name}", soNotSaved: "Ще не збережено", soMissing: "{name} не підключено", soStream: "Залишено режиму трансляції", soSaved: "Звичайно: {name}",
    coSeen: "Підключено: {n}", coNone: "Немає підключених", coMissing: "Очікувані, не видно: {n}",
    siSigned: "Вхід виконано · {name}", siOffline: "Офлайн-режим · {name}", siOut: "Вхід не виконано", siOutDetail: "На телевізорі, мабуть, екран входу або Steam Guard: увійдіть там контролером.",
    siNoRemember: "Вхід виконано; наступний запуск попросить пароль", siNoRememberDetail: "Вийдіть і увійдіть знову з позначкою Запам'ятати мене.", siNotRunning: "Steam не запущено",
    asOn: "Увімкнено · {user}", asOff: "Вимкнено: Windows попросить пароль", asExpires: "Закінчиться через {n} входів", asHello: "Заблоковано входом лише через Windows Hello",
    asGuide: "Налаштуйте знову на робочому столі: Core > Gaming Mode > Лишається консоллю.", asPlain: "Windows зберігає цей пароль відкритим текстом; Autologon від Sysinternals зберігає його зашифрованим.",
    blOff: "Вимкнено", blOn: "Увімкнено · є ключ відновлення", blEncrypting: "Шифрування · є ключ відновлення", blUnchecked: "Увімкнено · перевірте на робочому столі", blNoKey: "Увімкнено · немає ключа відновлення",
    blFw: "Оновлення прошивки чекає", blFwNoGuard: "Прошивка чекає: може знадобитися ключ", blSuspended: "Призупинено", blSuspendedUpd: "Призупинено на один перезапуск", blWaiting: "Чекає активації",
    blConfirm: "Перевірте резервну копію ключа на {url}", blBackup: "Windows записала резервну копію {when}", blCheckDesktop: "Перевірте на робочому столі: Core > Gaming Mode > Лишається консоллю.",
    blGuardHint: "Увімкніть захист BitLocker на робочому столі, щоб Core міг призупинити його на один перезапуск.", blNoKeyDetail: "Додайте ключ відновлення на робочому столі й збережіть його в обліковому записі Microsoft.",
    blSuspendedDetail: "Відновіть на робочому столі: Панель керування > BitLocker > Відновити захист.",
    prOff: "Вимкнено", prOn: "Увімкнено", prDetail: "Скасувати можна будь-коли в Core > Gaming Mode > Тихий Windows.",
    upApplied: "Драйвери затримано · функції лишаються на {version}", upHome: "Драйвери затримано · Home не може затримати функції", upNot: "Не застосовано",
    upNotDetail: "Застосуйте на робочому столі: Core > Gaming Mode > Тихий Windows.", upHomeDetail: "Windows Home ігнорує фіксацію версії.",
    luNone: "Нічого не чекає", luPending: "Чекають: {list}", luDetail: "Вони можуть попросити права адміністратора: дайте їм завершитися на робочому столі.",
    lfOn: "Стежить · {n} сьогодні", lfOff: "Вимкнено", lfLast: "Останнє відсунуте: {name}",
    wkNone: "Пробудження ще не перевірялося", wkOk: "OK · {when}", wkFixed: "Виправлено Core · {when}", wkProblem: "Варто глянути · {when}", wkNet: "Мережевий кабель від'єднано", wkOff: "Вимкнено",
    wkFocus: "Big Picture повернуто наперед.", wkInput: "Перемикання входу телевізора надіслано знову.", wkNoBp: "Big Picture не було видно.", wkTvNo: "Телевізор був не на вході Core.",
    wkTemps: "Температури: не показано (Windows не читає їх без драйвера).", wkAdapter: "Адаптер: {name}",
    aDisplayRestore: "Відновити налаштування екрана", aDisplaySave: "Зберегти поточний екран як звичайний", aSoundRestore: "Повернути звук", aSoundSave: "Зберегти поточне виведення звуку як звичайне",
    aControllersExpect: "Позначити ці контролери як очікувані", aPromptsOff: "Вимкнути екрани налаштування", aBitlockerSuspend: "Призупинити BitLocker на один перезапуск", aWakeNow: "Перевірити пробудження зараз",
    aDone: "Готово", aFailed: "Не вдалося", aNotWhilePlaying: "Не під час гри",
  },
  ru: {
    r_display: "Экран", r_sound: "Вывод звука", r_controllers: "Контроллеры", r_steamSign: "Вход в Steam", r_autoSign: "Автоматический вход", r_bitlocker: "BitLocker",
    r_prompts: "Экраны настройки Windows", r_updPolicy: "Политика обновлений", r_launchUpd: "Обновления лаунчеров", r_launchFocus: "Окна лаунчеров", r_wake: "После пробуждения",
    dpNormal: "Обычно · {mode}", dpChanged: "Изменено: {what}", dpNotSaved: "Ещё не сохранено", dpMissing: "Телевизор не подключён", dpSaved: "Обычно: {mode}", dpNow: "Сейчас: {mode}",
    dpResolution: "разрешение", dpRefresh: "частота обновления", dpHdr: "HDR", dpPrimary: "основной экран", dpVrr: "переменная частота",
    dpVrrNote: "G-Sync и FreeSync не читаются из Windows; сравнивается только переключатель переменной частоты Windows.",
    soNormal: "{name}", soChanged: "Сейчас: {name}", soNotSaved: "Ещё не сохранено", soMissing: "{name} не подключено", soStream: "Оставлено режиму трансляции", soSaved: "Обычно: {name}",
    coSeen: "Подключено: {n}", coNone: "Нет подключённых", coMissing: "Ожидаемые, не видны: {n}",
    siSigned: "Вход выполнен · {name}", siOffline: "Автономный режим · {name}", siOut: "Вход не выполнен", siOutDetail: "На телевизоре, вероятно, экран входа или Steam Guard: войдите там с контроллера.",
    siNoRemember: "Вход выполнен; следующий запуск спросит пароль", siNoRememberDetail: "Выйдите и войдите снова с отметкой Запомнить меня.", siNotRunning: "Steam не запущен",
    asOn: "Включён · {user}", asOff: "Выключен: Windows спросит пароль", asExpires: "Закончится через {n} входов", asHello: "Заблокирован входом только через Windows Hello",
    asGuide: "Настройте снова на рабочем столе: Core > Gaming Mode > Остаётся консолью.", asPlain: "Windows хранит этот пароль открытым текстом; Autologon от Sysinternals хранит его зашифрованным.",
    blOff: "Выключен", blOn: "Включён · есть ключ восстановления", blEncrypting: "Шифрование · есть ключ восстановления", blUnchecked: "Включён · проверьте на рабочем столе", blNoKey: "Включён · нет ключа восстановления",
    blFw: "Обновление прошивки ждёт", blFwNoGuard: "Прошивка ждёт: может понадобиться ключ", blSuspended: "Приостановлен", blSuspendedUpd: "Приостановлен на одну перезагрузку", blWaiting: "Ждёт активации",
    blConfirm: "Проверьте резервную копию ключа на {url}", blBackup: "Windows записала резервную копию {when}", blCheckDesktop: "Проверьте на рабочем столе: Core > Gaming Mode > Остаётся консолью.",
    blGuardHint: "Включите защиту BitLocker на рабочем столе, чтобы Core мог приостановить его на одну перезагрузку.", blNoKeyDetail: "Добавьте ключ восстановления на рабочем столе и сохраните его в учётной записи Microsoft.",
    blSuspendedDetail: "Возобновите на рабочем столе: Панель управления > BitLocker > Возобновить защиту.",
    prOff: "Выключены", prOn: "Включены", prDetail: "Отменить можно в любой момент в Core > Gaming Mode > Тихий Windows.",
    upApplied: "Драйверы задержаны · компоненты остаются на {version}", upHome: "Драйверы задержаны · Home не может задержать компоненты", upNot: "Не применена",
    upNotDetail: "Примените на рабочем столе: Core > Gaming Mode > Тихий Windows.", upHomeDetail: "Windows Home игнорирует фиксацию версии.",
    luNone: "Ничего не ждёт", luPending: "Ждут: {list}", luDetail: "Они могут запросить права администратора: дайте им завершиться на рабочем столе.",
    lfOn: "Следит · {n} сегодня", lfOff: "Выключено", lfLast: "Последнее отодвинутое: {name}",
    wkNone: "Пробуждение ещё не проверялось", wkOk: "OK · {when}", wkFixed: "Исправлено Core · {when}", wkProblem: "Стоит взглянуть · {when}", wkNet: "Сетевой кабель отключён", wkOff: "Выключено",
    wkFocus: "Big Picture возвращён вперёд.", wkInput: "Переключение входа телевизора отправлено снова.", wkNoBp: "Big Picture не был виден.", wkTvNo: "Телевизор был не на входе Core.",
    wkTemps: "Температуры: не показаны (Windows не читает их без драйвера).", wkAdapter: "Адаптер: {name}",
    aDisplayRestore: "Восстановить настройки экрана", aDisplaySave: "Сохранить текущий экран как обычный", aSoundRestore: "Вернуть звук", aSoundSave: "Сохранить текущий вывод звука как обычный",
    aControllersExpect: "Отметить эти контроллеры как ожидаемые", aPromptsOff: "Выключить экраны настройки", aBitlockerSuspend: "Приостановить BitLocker на одну перезагрузку", aWakeNow: "Проверить пробуждение сейчас",
    aDone: "Готово", aFailed: "Не получилось", aNotWhilePlaying: "Не во время игры",
  },
  zh: {
    r_display: "显示", r_sound: "声音输出", r_controllers: "控制器", r_steamSign: "Steam 登录", r_autoSign: "自动登录", r_bitlocker: "BitLocker",
    r_prompts: "Windows 设置页面", r_updPolicy: "更新策略", r_launchUpd: "启动器更新", r_launchFocus: "启动器弹窗", r_wake: "唤醒后",
    dpNormal: "常用 · {mode}", dpChanged: "已更改：{what}", dpNotSaved: "尚未保存", dpMissing: "电视未连接", dpSaved: "常用：{mode}", dpNow: "当前：{mode}",
    dpResolution: "分辨率", dpRefresh: "刷新率", dpHdr: "HDR", dpPrimary: "主显示器", dpVrr: "可变刷新率",
    dpVrrNote: "Windows 无法读取 G-Sync 和 FreeSync；只比较 Windows 的可变刷新率开关。",
    soNormal: "{name}", soChanged: "当前：{name}", soNotSaved: "尚未保存", soMissing: "{name} 未连接", soStream: "交给串流模式", soSaved: "常用：{name}",
    coSeen: "已连接 {n} 个", coNone: "未连接", coMissing: "预期但未见：{n}",
    siSigned: "已登录 · {name}", siOffline: "离线模式 · {name}", siOut: "未登录", siOutDetail: "电视上可能是登录或 Steam 令牌页面：请用控制器在那里登录。",
    siNoRemember: "已登录；下次启动会要求密码", siNoRememberDetail: "退出后勾选“记住我”重新登录。", siNotRunning: "Steam 未运行",
    asOn: "已开启 · {user}", asOff: "已关闭：Windows 会要求密码", asExpires: "再登录 {n} 次后结束", asHello: "被“仅允许 Windows Hello 登录”阻止",
    asGuide: "请在桌面上重新设置：Core > Gaming Mode > 保持主机体验。", asPlain: "Windows 以明文保存此密码；Sysinternals Autologon 会加密保存。",
    blOff: "已关闭", blOn: "已开启 · 有恢复密钥", blEncrypting: "正在加密 · 有恢复密钥", blUnchecked: "已开启 · 请在桌面上检查", blNoKey: "已开启 · 没有恢复密钥",
    blFw: "固件更新等待中", blFwNoGuard: "固件等待中：可能要求输入密钥", blSuspended: "已暂停", blSuspendedUpd: "暂停一次重启", blWaiting: "等待激活",
    blConfirm: "请在 {url} 确认密钥备份", blBackup: "Windows 于 {when} 记录了备份", blCheckDesktop: "请在桌面上检查：Core > Gaming Mode > 保持主机体验。",
    blGuardHint: "请在桌面上开启 BitLocker 保护，以便 Core 暂停它一次重启。", blNoKeyDetail: "请在桌面上添加恢复密钥，并备份到你的 Microsoft 帐户。",
    blSuspendedDetail: "请在桌面上恢复：控制面板 > BitLocker > 恢复保护。",
    prOff: "已关闭", prOn: "已开启", prDetail: "可随时在 Core > Gaming Mode > 安静的 Windows 中撤销。",
    upApplied: "驱动已暂缓 · 功能更新保持在 {version}", upHome: "驱动已暂缓 · 家庭版无法暂缓功能更新", upNot: "未应用",
    upNotDetail: "请在桌面上应用：Core > Gaming Mode > 安静的 Windows。", upHomeDetail: "Windows 家庭版会忽略版本固定。",
    luNone: "没有等待中的更新", luPending: "等待中：{list}", luDetail: "它们可能会要求管理员权限：请在桌面上让它们完成。",
    lfOn: "监视中 · 今天 {n} 次", lfOff: "已关闭", lfLast: "上次移到后面：{name}",
    wkNone: "尚未检查唤醒", wkOk: "正常 · {when}", wkFixed: "已由 Core 修复 · {when}", wkProblem: "需要查看 · {when}", wkNet: "网线已断开", wkOff: "已关闭",
    wkFocus: "Big Picture 已调回前台。", wkInput: "已重新发送电视输入切换。", wkNoBp: "Big Picture 不可见。", wkTvNo: "电视不在 Core 的输入源。",
    wkTemps: "温度：不显示（Windows 无驱动时无法读取）。", wkAdapter: "网卡：{name}",
    aDisplayRestore: "恢复显示设置", aDisplaySave: "将当前显示保存为常用", aSoundRestore: "切换回声音设备", aSoundSave: "将当前声音输出保存为常用",
    aControllersExpect: "将这些控制器标为预期", aPromptsOff: "关闭设置页面", aBitlockerSuspend: "暂停 BitLocker 一次重启", aWakeNow: "立即检查唤醒",
    aDone: "完成", aFailed: "没有成功", aNotWhilePlaying: "游戏运行时不可用",
  },
  ja: {
    r_display: "画面", r_sound: "サウンド出力", r_controllers: "コントローラー", r_steamSign: "Steam のサインイン", r_autoSign: "自動サインイン", r_bitlocker: "BitLocker",
    r_prompts: "Windows の設定画面", r_updPolicy: "更新ポリシー", r_launchUpd: "ランチャーの更新", r_launchFocus: "ランチャーのポップアップ", r_wake: "スリープ解除後",
    dpNormal: "通常 · {mode}", dpChanged: "変更あり：{what}", dpNotSaved: "まだ保存していません", dpMissing: "テレビ未接続", dpSaved: "通常：{mode}", dpNow: "現在：{mode}",
    dpResolution: "解像度", dpRefresh: "リフレッシュレート", dpHdr: "HDR", dpPrimary: "メイン ディスプレイ", dpVrr: "可変リフレッシュレート",
    dpVrrNote: "G-Sync と FreeSync は Windows から読めません。Windows の可変リフレッシュレートの設定だけを比べます。",
    soNormal: "{name}", soChanged: "現在：{name}", soNotSaved: "まだ保存していません", soMissing: "{name} は未接続", soStream: "ストリーミング モードに任せています", soSaved: "通常：{name}",
    coSeen: "{n} 台接続", coNone: "接続なし", coMissing: "見当たらない予定のもの：{n}",
    siSigned: "サインイン中 · {name}", siOffline: "オフライン モード · {name}", siOut: "サインインしていません", siOutDetail: "テレビにサインインか Steam Guard の画面が出ているはずです。コントローラーでサインインしてください。",
    siNoRemember: "サインイン中。次回起動時にパスワードを求められます", siNoRememberDetail: "「ログイン情報を保存」にチェックして入り直してください。", siNotRunning: "Steam は起動していません",
    asOn: "オン · {user}", asOff: "オフ：Windows がパスワードを求めます", asExpires: "あと {n} 回のサインインで終了", asHello: "Windows Hello のみのサインインでブロック",
    asGuide: "デスクトップで設定し直してください：Core > Gaming Mode > ゲーム機のまま。", asPlain: "Windows はこのパスワードを平文で保管しています。Sysinternals Autologon なら暗号化されます。",
    blOff: "オフ", blOn: "オン · 回復キーあり", blEncrypting: "暗号化中 · 回復キーあり", blUnchecked: "オン · デスクトップで確認してください", blNoKey: "オン · 回復キーなし",
    blFw: "ファームウェア更新が待機中", blFwNoGuard: "ファームウェア待機中：キーを求められる可能性", blSuspended: "一時停止中", blSuspendedUpd: "再起動 1 回分だけ一時停止", blWaiting: "アクティブ化待ち",
    blConfirm: "キーのバックアップを {url} で確認してください", blBackup: "Windows が {when} にバックアップを記録", blCheckDesktop: "デスクトップで確認してください：Core > Gaming Mode > ゲーム機のまま。",
    blGuardHint: "デスクトップで BitLocker 保護をオンにすると、Core が再起動 1 回分だけ一時停止できます。", blNoKeyDetail: "デスクトップで回復キーを追加し、Microsoft アカウントにバックアップしてください。",
    blSuspendedDetail: "デスクトップで再開してください：コントロール パネル > BitLocker > 保護の再開。",
    prOff: "オフ", prOn: "オン", prDetail: "Core > Gaming Mode > 静かな Windows からいつでも元に戻せます。",
    upApplied: "ドライバーを保留 · 機能更新は {version} のまま", upHome: "ドライバーを保留 · Home は機能更新を保留できません", upNot: "未適用",
    upNotDetail: "デスクトップで適用してください：Core > Gaming Mode > 静かな Windows。", upHomeDetail: "Windows Home はバージョンの固定を無視します。",
    luNone: "待機中のものはありません", luPending: "待機中：{list}", luDetail: "管理者権限を求められる場合があります。デスクトップで完了させてください。",
    lfOn: "監視中 · 今日 {n} 回", lfOff: "オフ", lfLast: "最後に後ろへ回したもの：{name}",
    wkNone: "まだスリープ解除を確認していません", wkOk: "OK · {when}", wkFixed: "Core が修正 · {when}", wkProblem: "確認が必要 · {when}", wkNet: "LAN ケーブルが抜けています", wkOff: "オフ",
    wkFocus: "Big Picture を前面に戻しました。", wkInput: "テレビの入力切り替えをもう一度送りました。", wkNoBp: "Big Picture が見えていませんでした。", wkTvNo: "テレビが Core の入力になっていませんでした。",
    wkTemps: "温度：表示しません（Windows はドライバーなしで読めません）。", wkAdapter: "アダプター：{name}",
    aDisplayRestore: "画面の設定を元に戻す", aDisplaySave: "今の画面を通常として保存", aSoundRestore: "音を元に戻す", aSoundSave: "今のサウンド出力を通常として保存",
    aControllersExpect: "これらのコントローラーを予定として登録", aPromptsOff: "設定画面をオフにする", aBitlockerSuspend: "BitLocker を再起動 1 回分だけ一時停止", aWakeNow: "今すぐスリープ解除チェック",
    aDone: "完了", aFailed: "うまくいきませんでした", aNotWhilePlaying: "ゲーム中はできません",
  },
  ko: {
    r_display: "디스플레이", r_sound: "소리 출력", r_controllers: "컨트롤러", r_steamSign: "Steam 로그인", r_autoSign: "자동 로그인", r_bitlocker: "BitLocker",
    r_prompts: "Windows 설정 화면", r_updPolicy: "업데이트 정책", r_launchUpd: "런처 업데이트", r_launchFocus: "런처 팝업", r_wake: "절전 해제 후",
    dpNormal: "기본 · {mode}", dpChanged: "변경됨: {what}", dpNotSaved: "아직 저장 안 함", dpMissing: "TV 연결 안 됨", dpSaved: "기본: {mode}", dpNow: "현재: {mode}",
    dpResolution: "해상도", dpRefresh: "주사율", dpHdr: "HDR", dpPrimary: "주 디스플레이", dpVrr: "가변 주사율",
    dpVrrNote: "G-Sync와 FreeSync는 Windows에서 읽을 수 없습니다. Windows의 가변 주사율 스위치만 비교합니다.",
    soNormal: "{name}", soChanged: "현재: {name}", soNotSaved: "아직 저장 안 함", soMissing: "{name} 연결 안 됨", soStream: "스트리밍 모드에 맡김", soSaved: "기본: {name}",
    coSeen: "{n}개 연결됨", coNone: "연결된 컨트롤러 없음", coMissing: "예상했지만 안 보임: {n}",
    siSigned: "로그인됨 · {name}", siOffline: "오프라인 모드 · {name}", siOut: "로그인 안 됨", siOutDetail: "TV에 로그인 또는 Steam Guard 화면이 떠 있을 것입니다. 컨트롤러로 로그인하세요.",
    siNoRemember: "로그인됨. 다음 시작 때 암호를 묻습니다", siNoRememberDetail: "로그아웃한 뒤 '로그인 정보 기억'을 체크하고 다시 로그인하세요.", siNotRunning: "Steam 실행 안 됨",
    asOn: "켜짐 · {user}", asOff: "꺼짐: Windows가 암호를 묻습니다", asExpires: "로그인 {n}번 후 종료", asHello: "Windows Hello 전용 로그인으로 차단됨",
    asGuide: "데스크톱에서 다시 설정하세요: Core > Gaming Mode > 콘솔처럼 유지.", asPlain: "Windows가 이 암호를 일반 텍스트로 보관합니다. Sysinternals Autologon은 암호화해 보관합니다.",
    blOff: "꺼짐", blOn: "켜짐 · 복구 키 있음", blEncrypting: "암호화 중 · 복구 키 있음", blUnchecked: "켜짐 · 데스크톱에서 확인하세요", blNoKey: "켜짐 · 복구 키 없음",
    blFw: "펌웨어 업데이트 대기 중", blFwNoGuard: "펌웨어 대기 중: 키를 물을 수 있음", blSuspended: "일시 중단됨", blSuspendedUpd: "재시작 한 번 동안 일시 중단", blWaiting: "활성화 대기 중",
    blConfirm: "{url}에서 키 백업을 확인하세요", blBackup: "Windows가 {when} 백업을 기록함", blCheckDesktop: "데스크톱에서 확인하세요: Core > Gaming Mode > 콘솔처럼 유지.",
    blGuardHint: "데스크톱에서 BitLocker 보호를 켜면 Core가 재시작 한 번 동안 일시 중단할 수 있습니다.", blNoKeyDetail: "데스크톱에서 복구 키를 추가하고 Microsoft 계정에 백업하세요.",
    blSuspendedDetail: "데스크톱에서 다시 시작하세요: 제어판 > BitLocker > 보호 다시 시작.",
    prOff: "꺼짐", prOn: "켜짐", prDetail: "Core > Gaming Mode > 조용한 Windows에서 언제든 되돌릴 수 있습니다.",
    upApplied: "드라이버 보류 · 기능 업데이트는 {version} 유지", upHome: "드라이버 보류 · Home은 기능 업데이트를 보류할 수 없음", upNot: "적용 안 됨",
    upNotDetail: "데스크톱에서 적용하세요: Core > Gaming Mode > 조용한 Windows.", upHomeDetail: "Windows Home은 버전 고정을 무시합니다.",
    luNone: "대기 중인 항목 없음", luPending: "대기 중: {list}", luDetail: "관리자 권한을 요구할 수 있습니다. 데스크톱에서 마치게 하세요.",
    lfOn: "감시 중 · 오늘 {n}회", lfOff: "꺼짐", lfLast: "마지막으로 뒤로 보냄: {name}",
    wkNone: "아직 확인한 절전 해제 없음", wkOk: "정상 · {when}", wkFixed: "Core가 해결 · {when}", wkProblem: "확인 필요 · {when}", wkNet: "네트워크 케이블 빠짐", wkOff: "꺼짐",
    wkFocus: "Big Picture를 다시 앞으로 가져왔습니다.", wkInput: "TV 입력 전환을 다시 보냈습니다.", wkNoBp: "Big Picture가 보이지 않았습니다.", wkTvNo: "TV가 Core의 입력에 있지 않았습니다.",
    wkTemps: "온도: 표시 안 함(Windows는 드라이버 없이 읽을 수 없음).", wkAdapter: "어댑터: {name}",
    aDisplayRestore: "디스플레이 설정 복원", aDisplaySave: "현재 디스플레이를 기본으로 저장", aSoundRestore: "소리 되돌리기", aSoundSave: "현재 소리 출력을 기본으로 저장",
    aControllersExpect: "이 컨트롤러를 예상 목록에 추가", aPromptsOff: "설정 화면 끄기", aBitlockerSuspend: "재시작 한 번 동안 BitLocker 일시 중단", aWakeNow: "지금 절전 해제 확인",
    aDone: "완료", aFailed: "실패했습니다", aNotWhilePlaying: "게임 중에는 안 됨",
  },
  hi: {
    r_display: "डिस्प्ले", r_sound: "आवाज़ आउटपुट", r_controllers: "कंट्रोलर", r_steamSign: "Steam साइन-इन", r_autoSign: "अपने-आप साइन-इन", r_bitlocker: "BitLocker",
    r_prompts: "Windows सेटअप स्क्रीन", r_updPolicy: "अपडेट नीति", r_launchUpd: "लॉन्चर अपडेट", r_launchFocus: "लॉन्चर पॉप-अप", r_wake: "जागने के बाद",
    dpNormal: "सामान्य · {mode}", dpChanged: "बदला: {what}", dpNotSaved: "अभी सेव नहीं", dpMissing: "TV जुड़ा नहीं", dpSaved: "सामान्य: {mode}", dpNow: "अभी: {mode}",
    dpResolution: "रिज़ॉल्यूशन", dpRefresh: "रिफ़्रेश रेट", dpHdr: "HDR", dpPrimary: "मुख्य डिस्प्ले", dpVrr: "वेरिएबल रिफ़्रेश",
    dpVrrNote: "G-Sync और FreeSync को Windows से पढ़ा नहीं जा सकता; सिर्फ़ Windows का वेरिएबल रिफ़्रेश स्विच मिलाया जाता है।",
    soNormal: "{name}", soChanged: "अभी: {name}", soNotSaved: "अभी सेव नहीं", soMissing: "{name} जुड़ा नहीं", soStream: "स्ट्रीम मोड पर छोड़ा", soSaved: "सामान्य: {name}",
    coSeen: "{n} जुड़े", coNone: "कोई नहीं जुड़ा", coMissing: "अपेक्षित, नहीं दिखे: {n}",
    siSigned: "साइन-इन · {name}", siOffline: "ऑफ़लाइन मोड · {name}", siOut: "साइन-इन नहीं", siOutDetail: "TV पर शायद साइन-इन या Steam Guard स्क्रीन है: कंट्रोलर से वहीं साइन-इन करें।",
    siNoRemember: "साइन-इन है; अगली बार शुरू होने पर पासवर्ड माँगेगा", siNoRememberDetail: "साइन-आउट करके 'मुझे याद रखें' चुनकर फिर साइन-इन करें।", siNotRunning: "Steam नहीं चल रहा",
    asOn: "चालू · {user}", asOff: "बंद: Windows पासवर्ड माँगेगा", asExpires: "{n} और साइन-इन के बाद खत्म", asHello: "सिर्फ़ Windows Hello साइन-इन ने रोका",
    asGuide: "डेस्कटॉप पर फिर से सेट करें: Core > Gaming Mode > कंसोल बना रहे।", asPlain: "Windows यह पासवर्ड सादे टेक्स्ट में रखता है; Sysinternals Autologon इसे एन्क्रिप्ट करके रखता है।",
    blOff: "बंद", blOn: "चालू · रिकवरी कुंजी मौजूद", blEncrypting: "एन्क्रिप्ट हो रहा · रिकवरी कुंजी मौजूद", blUnchecked: "चालू · डेस्कटॉप पर जाँचें", blNoKey: "चालू · कोई रिकवरी कुंजी नहीं",
    blFw: "फ़र्मवेयर अपडेट इंतज़ार में", blFwNoGuard: "फ़र्मवेयर इंतज़ार में: कुंजी माँग सकता है", blSuspended: "रुका हुआ", blSuspendedUpd: "एक रीस्टार्ट के लिए रुका", blWaiting: "सक्रिय होने का इंतज़ार",
    blConfirm: "कुंजी का बैकअप {url} पर पक्का करें", blBackup: "Windows ने {when} बैकअप दर्ज किया", blCheckDesktop: "डेस्कटॉप पर जाँचें: Core > Gaming Mode > कंसोल बना रहे।",
    blGuardHint: "डेस्कटॉप पर BitLocker सुरक्षा चालू करें ताकि Core इसे एक रीस्टार्ट के लिए रोक सके।", blNoKeyDetail: "डेस्कटॉप पर रिकवरी कुंजी जोड़ें और उसे अपने Microsoft खाते में बैकअप करें।",
    blSuspendedDetail: "डेस्कटॉप पर फिर शुरू करें: कंट्रोल पैनल > BitLocker > सुरक्षा फिर शुरू करें।",
    prOff: "बंद", prOn: "चालू", prDetail: "Core > Gaming Mode > शांत Windows से कभी भी वापस लें।",
    upApplied: "ड्राइवर रोके · फ़ीचर अपडेट {version} पर", upHome: "ड्राइवर रोके · Home फ़ीचर अपडेट नहीं रोक सकता", upNot: "लागू नहीं",
    upNotDetail: "डेस्कटॉप पर लागू करें: Core > Gaming Mode > शांत Windows।", upHomeDetail: "Windows Home वर्ज़न पिन को नहीं मानता।",
    luNone: "कुछ इंतज़ार में नहीं", luPending: "इंतज़ार में: {list}", luDetail: "ये एडमिनिस्ट्रेटर अनुमति माँग सकते हैं: इन्हें डेस्कटॉप पर पूरा होने दें।",
    lfOn: "निगरानी · आज {n}", lfOff: "बंद", lfLast: "आख़िरी बार पीछे भेजा: {name}",
    wkNone: "अभी कोई जागना नहीं जाँचा", wkOk: "ठीक · {when}", wkFixed: "Core ने ठीक किया · {when}", wkProblem: "देखना चाहिए · {when}", wkNet: "नेटवर्क केबल निकला है", wkOff: "बंद",
    wkFocus: "Big Picture को फिर सामने लाया गया।", wkInput: "TV का इनपुट बदलना फिर भेजा गया।", wkNoBp: "Big Picture दिख नहीं रहा था।", wkTvNo: "TV, Core के इनपुट पर नहीं था।",
    wkTemps: "तापमान: नहीं दिखाया (Windows बिना ड्राइवर के इसे नहीं पढ़ता)।", wkAdapter: "अडैप्टर: {name}",
    aDisplayRestore: "डिस्प्ले सेटिंग्स वापस लाएँ", aDisplaySave: "मौजूदा डिस्प्ले को सामान्य के रूप में सेव करें", aSoundRestore: "आवाज़ वापस बदलें", aSoundSave: "मौजूदा आवाज़ आउटपुट को सामान्य के रूप में सेव करें",
    aControllersExpect: "इन कंट्रोलरों को अपेक्षित चिह्नित करें", aPromptsOff: "सेटअप स्क्रीन बंद करें", aBitlockerSuspend: "BitLocker को एक रीस्टार्ट के लिए रोकें", aWakeNow: "अभी जागने की जाँच करें",
    aDone: "हो गया", aFailed: "काम नहीं हुआ", aNotWhilePlaying: "गेम चलते समय नहीं",
  },
};
for (const [language, table] of Object.entries(consoleCopy)) Object.assign(copy[language] ?? {}, table);

// Batch 1 (BATCH-1.md): the save backup before update night and Decky with the plugins that report errors.
const batchCopy: Record<string, Copy> = {
  en: {
    r_saves: "Save backup", svOkNight: "Last night OK", svOkWhen: "OK {when}", svFailedNight: "Last night failed", svFailedWhen: "Failed {when}", svNone: "Ludusavi not installed", svOff: "Off", svNever: "Not run yet", svGames: "{n} games backed up", svLimit: "Took longer than 10 minutes and was stopped", svSome: "Some games' saves could not be copied", svNoAnswer: "Ludusavi gave no answer", svExit: "Ludusavi stopped with code {code}", svNotStarted: "Ludusavi could not start", svInstall: "Install it on the desktop: winget install -e --id mtkennerly.ludusavi", r_decky: "Decky", dkOk: "Plugins loaded", dkWaiting: "Waiting for plugins", dkNa: "Not in Gaming Mode", dkNotLoaded: "Plugins not loaded", dkErrOne: "{name} is reporting errors", dkErrMany: "{names} are reporting errors", dkLast: "Last error: {line}", dkCount: "{n} errors in the last 6 hours", dkFrom: "Errors from: {names}",
  },
  it: {
    r_saves: "Backup dei salvataggi", svOkNight: "Stanotte OK", svOkWhen: "OK {when}", svFailedNight: "Stanotte non riuscito", svFailedWhen: "Non riuscito {when}", svNone: "Ludusavi non installato", svOff: "Spento", svNever: "Non ancora eseguito", svGames: "Backup di {n} giochi", svLimit: "Ci ha messo più di 10 minuti ed è stato fermato", svSome: "Non è stato possibile copiare i salvataggi di alcuni giochi", svNoAnswer: "Ludusavi non ha risposto", svExit: "Ludusavi si è fermato con il codice {code}", svNotStarted: "Ludusavi non è partito", svInstall: "Installalo sul desktop: winget install -e --id mtkennerly.ludusavi", r_decky: "Decky", dkOk: "Plugin caricati", dkWaiting: "In attesa dei plugin", dkNa: "Non in Gaming Mode", dkNotLoaded: "Plugin non caricati", dkErrOne: "{name} segnala errori", dkErrMany: "{names} segnalano errori", dkLast: "Ultimo errore: {line}", dkCount: "{n} errori nelle ultime 6 ore", dkFrom: "Errori da: {names}",
  },
  es: {
    r_saves: "Copia de partidas", svOkNight: "Anoche OK", svOkWhen: "OK {when}", svFailedNight: "Anoche falló", svFailedWhen: "Falló {when}", svNone: "Ludusavi no está instalado", svOff: "Desactivado", svNever: "Aún no se ha hecho", svGames: "Copia de {n} juegos", svLimit: "Tardó más de 10 minutos y se detuvo", svSome: "No se pudieron copiar las partidas de algunos juegos", svNoAnswer: "Ludusavi no respondió", svExit: "Ludusavi se detuvo con el código {code}", svNotStarted: "Ludusavi no pudo iniciarse", svInstall: "Instálalo en el escritorio: winget install -e --id mtkennerly.ludusavi", r_decky: "Decky", dkOk: "Plugins cargados", dkWaiting: "Esperando a los plugins", dkNa: "Fuera de Gaming Mode", dkNotLoaded: "Plugins no cargados", dkErrOne: "{name} informa de errores", dkErrMany: "{names} informan de errores", dkLast: "Último error: {line}", dkCount: "{n} errores en las últimas 6 horas", dkFrom: "Errores de: {names}",
  },
  fr: {
    r_saves: "Sauvegarde des parties", svOkNight: "Cette nuit OK", svOkWhen: "OK {when}", svFailedNight: "Échec cette nuit", svFailedWhen: "Échec {when}", svNone: "Ludusavi n'est pas installé", svOff: "Désactivé", svNever: "Pas encore lancé", svGames: "{n} jeux sauvegardés", svLimit: "A duré plus de 10 minutes et a été arrêté", svSome: "Les parties de certains jeux n'ont pas pu être copiées", svNoAnswer: "Ludusavi n'a pas répondu", svExit: "Ludusavi s'est arrêté avec le code {code}", svNotStarted: "Ludusavi n'a pas pu démarrer", svInstall: "Installe-le sur le bureau : winget install -e --id mtkennerly.ludusavi", r_decky: "Decky", dkOk: "Plugins chargés", dkWaiting: "En attente des plugins", dkNa: "Hors Gaming Mode", dkNotLoaded: "Plugins non chargés", dkErrOne: "{name} signale des erreurs", dkErrMany: "{names} signalent des erreurs", dkLast: "Dernière erreur : {line}", dkCount: "{n} erreurs ces 6 dernières heures", dkFrom: "Erreurs de : {names}",
  },
  de: {
    r_saves: "Spielstand-Backup", svOkNight: "Letzte Nacht OK", svOkWhen: "OK {when}", svFailedNight: "Letzte Nacht fehlgeschlagen", svFailedWhen: "Fehlgeschlagen {when}", svNone: "Ludusavi nicht installiert", svOff: "Aus", svNever: "Noch nicht gelaufen", svGames: "{n} Spiele gesichert", svLimit: "Dauerte länger als 10 Minuten und wurde gestoppt", svSome: "Die Spielstände einiger Spiele konnten nicht kopiert werden", svNoAnswer: "Ludusavi hat nicht geantwortet", svExit: "Ludusavi wurde mit Code {code} beendet", svNotStarted: "Ludusavi konnte nicht starten", svInstall: "Auf dem Desktop installieren: winget install -e --id mtkennerly.ludusavi", r_decky: "Decky", dkOk: "Plugins geladen", dkWaiting: "Warte auf Plugins", dkNa: "Nicht im Gaming Mode", dkNotLoaded: "Plugins nicht geladen", dkErrOne: "{name} meldet Fehler", dkErrMany: "{names} melden Fehler", dkLast: "Letzter Fehler: {line}", dkCount: "{n} Fehler in den letzten 6 Stunden", dkFrom: "Fehler von: {names}",
  },
  pt: {
    r_saves: "Backup dos jogos salvos", svOkNight: "Ontem à noite OK", svOkWhen: "OK {when}", svFailedNight: "Falhou ontem à noite", svFailedWhen: "Falhou {when}", svNone: "Ludusavi não instalado", svOff: "Desligado", svNever: "Ainda não executado", svGames: "Backup de {n} jogos", svLimit: "Demorou mais de 10 minutos e foi parado", svSome: "Não foi possível copiar os jogos salvos de alguns jogos", svNoAnswer: "O Ludusavi não respondeu", svExit: "O Ludusavi parou com o código {code}", svNotStarted: "O Ludusavi não conseguiu iniciar", svInstall: "Instale-o na área de trabalho: winget install -e --id mtkennerly.ludusavi", r_decky: "Decky", dkOk: "Plugins carregados", dkWaiting: "Aguardando os plugins", dkNa: "Fora do Gaming Mode", dkNotLoaded: "Plugins não carregados", dkErrOne: "{name} está relatando erros", dkErrMany: "{names} estão relatando erros", dkLast: "Último erro: {line}", dkCount: "{n} erros nas últimas 6 horas", dkFrom: "Erros de: {names}",
  },
  uk: {
    r_saves: "Резервна копія збережень", svOkNight: "Минулої ночі OK", svOkWhen: "OK {when}", svFailedNight: "Минулої ночі не вдалося", svFailedWhen: "Не вдалося {when}", svNone: "Ludusavi не встановлено", svOff: "Вимкнено", svNever: "Ще не запускалося", svGames: "Збережено {n} ігор", svLimit: "Тривало понад 10 хвилин і було зупинено", svSome: "Збереження деяких ігор не вдалося скопіювати", svNoAnswer: "Ludusavi не відповів", svExit: "Ludusavi зупинився з кодом {code}", svNotStarted: "Ludusavi не запустився", svInstall: "Установіть його на робочому столі: winget install -e --id mtkennerly.ludusavi", r_decky: "Decky", dkOk: "Плагіни завантажено", dkWaiting: "Очікування плагінів", dkNa: "Не в Gaming Mode", dkNotLoaded: "Плагіни не завантажено", dkErrOne: "{name} повідомляє про помилки", dkErrMany: "{names} повідомляють про помилки", dkLast: "Остання помилка: {line}", dkCount: "{n} помилок за останні 6 годин", dkFrom: "Помилки від: {names}",
  },
  ru: {
    r_saves: "Резервная копия сохранений", svOkNight: "Прошлой ночью OK", svOkWhen: "OK {when}", svFailedNight: "Прошлой ночью не удалось", svFailedWhen: "Не удалось {when}", svNone: "Ludusavi не установлен", svOff: "Выключено", svNever: "Ещё не запускалось", svGames: "Сохранено игр: {n}", svLimit: "Длилось больше 10 минут и было остановлено", svSome: "Сохранения некоторых игр не удалось скопировать", svNoAnswer: "Ludusavi не ответил", svExit: "Ludusavi остановился с кодом {code}", svNotStarted: "Ludusavi не запустился", svInstall: "Установите его на рабочем столе: winget install -e --id mtkennerly.ludusavi", r_decky: "Decky", dkOk: "Плагины загружены", dkWaiting: "Ожидание плагинов", dkNa: "Не в Gaming Mode", dkNotLoaded: "Плагины не загружены", dkErrOne: "{name} сообщает об ошибках", dkErrMany: "{names} сообщают об ошибках", dkLast: "Последняя ошибка: {line}", dkCount: "Ошибок за последние 6 часов: {n}", dkFrom: "Ошибки от: {names}",
  },
  zh: {
    r_saves: "存档备份", svOkNight: "昨晚成功", svOkWhen: "成功 {when}", svFailedNight: "昨晚失败", svFailedWhen: "失败 {when}", svNone: "未安装 Ludusavi", svOff: "已关闭", svNever: "尚未运行", svGames: "已备份 {n} 个游戏", svLimit: "超过 10 分钟，已停止", svSome: "部分游戏的存档无法复制", svNoAnswer: "Ludusavi 没有响应", svExit: "Ludusavi 以代码 {code} 退出", svNotStarted: "Ludusavi 无法启动", svInstall: "请在桌面安装：winget install -e --id mtkennerly.ludusavi", r_decky: "Decky", dkOk: "插件已加载", dkWaiting: "正在等待插件", dkNa: "不在 Gaming Mode", dkNotLoaded: "插件未加载", dkErrOne: "{name} 正在报告错误", dkErrMany: "{names} 正在报告错误", dkLast: "最近的错误：{line}", dkCount: "过去 6 小时 {n} 个错误", dkFrom: "错误来自：{names}",
  },
  ja: {
    r_saves: "セーブのバックアップ", svOkNight: "昨夜 OK", svOkWhen: "OK {when}", svFailedNight: "昨夜は失敗", svFailedWhen: "失敗 {when}", svNone: "Ludusavi 未インストール", svOff: "オフ", svNever: "まだ実行していません", svGames: "{n} 本のゲームをバックアップ", svLimit: "10 分を超えたため停止しました", svSome: "一部のゲームのセーブをコピーできませんでした", svNoAnswer: "Ludusavi が応答しませんでした", svExit: "Ludusavi がコード {code} で終了しました", svNotStarted: "Ludusavi を起動できませんでした", svInstall: "デスクトップでインストール: winget install -e --id mtkennerly.ludusavi", r_decky: "Decky", dkOk: "プラグイン読み込み済み", dkWaiting: "プラグインを待っています", dkNa: "Gaming Mode ではありません", dkNotLoaded: "プラグイン未読み込み", dkErrOne: "{name} がエラーを報告しています", dkErrMany: "{names} がエラーを報告しています", dkLast: "最新のエラー: {line}", dkCount: "過去 6 時間のエラー: {n}", dkFrom: "エラーの発生元: {names}",
  },
  ko: {
    r_saves: "세이브 백업", svOkNight: "어젯밤 정상", svOkWhen: "정상 {when}", svFailedNight: "어젯밤 실패", svFailedWhen: "실패 {when}", svNone: "Ludusavi 미설치", svOff: "꺼짐", svNever: "아직 실행 안 함", svGames: "게임 {n}개 백업", svLimit: "10분이 넘게 걸려 중지됨", svSome: "일부 게임의 세이브를 복사하지 못함", svNoAnswer: "Ludusavi가 응답하지 않음", svExit: "Ludusavi가 코드 {code}로 종료됨", svNotStarted: "Ludusavi를 시작하지 못함", svInstall: "데스크톱에서 설치: winget install -e --id mtkennerly.ludusavi", r_decky: "Decky", dkOk: "플러그인 로드됨", dkWaiting: "플러그인 기다리는 중", dkNa: "Gaming Mode 아님", dkNotLoaded: "플러그인 로드 안 됨", dkErrOne: "{name}에서 오류 보고 중", dkErrMany: "{names}에서 오류 보고 중", dkLast: "마지막 오류: {line}", dkCount: "최근 6시간 오류 {n}개", dkFrom: "오류 출처: {names}",
  },
  hi: {
    r_saves: "सेव बैकअप", svOkNight: "कल रात ठीक", svOkWhen: "ठीक {when}", svFailedNight: "कल रात विफल", svFailedWhen: "विफल {when}", svNone: "Ludusavi इंस्टॉल नहीं है", svOff: "बंद", svNever: "अभी तक नहीं चला", svGames: "{n} गेम का बैकअप", svLimit: "10 मिनट से ज़्यादा लगा और रोक दिया गया", svSome: "कुछ गेम के सेव कॉपी नहीं हो सके", svNoAnswer: "Ludusavi ने जवाब नहीं दिया", svExit: "Ludusavi कोड {code} के साथ रुका", svNotStarted: "Ludusavi शुरू नहीं हो सका", svInstall: "डेस्कटॉप पर इंस्टॉल करें: winget install -e --id mtkennerly.ludusavi", r_decky: "Decky", dkOk: "प्लगइन लोड हैं", dkWaiting: "प्लगइन का इंतज़ार", dkNa: "Gaming Mode में नहीं", dkNotLoaded: "प्लगइन लोड नहीं हुए", dkErrOne: "{name} त्रुटियाँ बता रहा है", dkErrMany: "{names} त्रुटियाँ बता रहे हैं", dkLast: "आख़िरी त्रुटि: {line}", dkCount: "पिछले 6 घंटों में {n} त्रुटियाँ", dkFrom: "त्रुटियाँ इनसे: {names}",
  },
};
for (const [language, table] of Object.entries(batchCopy)) Object.assign(copy[language] ?? {}, table);

// Batch 2 (BATCH-2.md): sleep after streaming, Wake-on-LAN readiness and the Home Assistant bridge.
const batchTwoCopy: Record<string, Copy> = {
  en: {
    r_streamSleep: "Sleep after streaming", ssOff: "Off", ssReady: "On · after {n} min", ssCounting: "Sleeping at {time} unless someone plays here", ssLast: "Last slept {when}", r_wol: "Wake-on-LAN", wolReady: "Ready", wolCheck: "Check the network adapter", wolWifi: "Wi-Fi only", wolNone: "No network adapter", wolUnknown: "Unknown", wolMac: "Wake address (MAC): {mac}", wolMagic: "Turn on \"Wake on Magic Packet\" in the adapter's Advanced settings (Device Manager)", wolPower: "Let the adapter wake the PC: Device Manager > adapter > Power Management", wolArmed: "Windows does not let the adapter wake the PC (Power Management)", wolCable: "Network cable unplugged", wolFast: "Fast startup is on: waking from shutdown may fail; waking from sleep works", wolCableHint: "Use a network cable to wake the PC from a handheld", r_ha: "Home Assistant", haOff: "Off", haNotConfigured: "Set the server in Core > Gaming Mode", haConnected: "Connected · {host}", haConnecting: "Connecting…", haRefused: "Login refused by {host}", haUnreachable: "Can't reach {host}",
  },
  it: {
    r_streamSleep: "Sospensione dopo lo streaming", ssOff: "Spenta", ssReady: "Attiva · dopo {n} min", ssCounting: "Sospensione alle {time}, a meno che qualcuno giochi qui", ssLast: "Ultima sospensione {when}", r_wol: "Wake-on-LAN", wolReady: "Pronto", wolCheck: "Controlla la scheda di rete", wolWifi: "Solo Wi-Fi", wolNone: "Nessuna scheda di rete", wolUnknown: "Sconosciuto", wolMac: "Indirizzo di risveglio (MAC): {mac}", wolMagic: "Attiva \"Wake on Magic Packet\" nelle impostazioni avanzate della scheda (Gestione dispositivi)", wolPower: "Lascia che la scheda risvegli il PC: Gestione dispositivi > scheda > Risparmio energia", wolArmed: "Windows non permette alla scheda di risvegliare il PC (Risparmio energia)", wolCable: "Cavo di rete scollegato", wolFast: "Avvio rapido attivo: il risveglio da spento può non riuscire; da sospensione funziona", wolCableHint: "Usa un cavo di rete per risvegliare il PC da una console portatile", r_ha: "Home Assistant", haOff: "Spento", haNotConfigured: "Imposta il server in Core > Gaming Mode", haConnected: "Collegato · {host}", haConnecting: "Collegamento…", haRefused: "Accesso rifiutato da {host}", haUnreachable: "{host} non raggiungibile",
  },
  es: {
    r_streamSleep: "Suspensión tras la transmisión", ssOff: "Desactivada", ssReady: "Activa · tras {n} min", ssCounting: "Suspensión a las {time} salvo que alguien juegue aquí", ssLast: "Última suspensión {when}", r_wol: "Wake-on-LAN", wolReady: "Listo", wolCheck: "Revisa el adaptador de red", wolWifi: "Solo Wi-Fi", wolNone: "Sin adaptador de red", wolUnknown: "Desconocido", wolMac: "Dirección de activación (MAC): {mac}", wolMagic: "Activa \"Wake on Magic Packet\" en las opciones avanzadas del adaptador (Administrador de dispositivos)", wolPower: "Deja que el adaptador active el PC: Administrador de dispositivos > adaptador > Administración de energía", wolArmed: "Windows no deja que el adaptador active el PC (Administración de energía)", wolCable: "Cable de red desconectado", wolFast: "El inicio rápido está activado: activar desde apagado puede fallar; desde suspensión funciona", wolCableHint: "Usa un cable de red para activar el PC desde una portátil", r_ha: "Home Assistant", haOff: "Desactivado", haNotConfigured: "Configura el servidor en Core > Gaming Mode", haConnected: "Conectado · {host}", haConnecting: "Conectando…", haRefused: "{host} rechazó el inicio de sesión", haUnreachable: "No se puede contactar con {host}",
  },
  fr: {
    r_streamSleep: "Veille après le streaming", ssOff: "Désactivée", ssReady: "Active · après {n} min", ssCounting: "Veille à {time} sauf si quelqu'un joue ici", ssLast: "Dernière veille {when}", r_wol: "Wake-on-LAN", wolReady: "Prêt", wolCheck: "Vérifie la carte réseau", wolWifi: "Wi-Fi uniquement", wolNone: "Aucune carte réseau", wolUnknown: "Inconnu", wolMac: "Adresse de réveil (MAC) : {mac}", wolMagic: "Active « Wake on Magic Packet » dans les paramètres avancés de la carte (Gestionnaire de périphériques)", wolPower: "Autorise la carte à réveiller le PC : Gestionnaire de périphériques > carte > Gestion de l'alimentation", wolArmed: "Windows n'autorise pas la carte à réveiller le PC (Gestion de l'alimentation)", wolCable: "Câble réseau débranché", wolFast: "Le démarrage rapide est activé : réveiller depuis l'arrêt peut échouer ; depuis la veille, ça marche", wolCableHint: "Utilise un câble réseau pour réveiller le PC depuis une console portable", r_ha: "Home Assistant", haOff: "Désactivé", haNotConfigured: "Configure le serveur dans Core > Gaming Mode", haConnected: "Connecté · {host}", haConnecting: "Connexion…", haRefused: "Connexion refusée par {host}", haUnreachable: "{host} injoignable",
  },
  de: {
    r_streamSleep: "Standby nach dem Streamen", ssOff: "Aus", ssReady: "An · nach {n} Min.", ssCounting: "Standby um {time}, außer jemand spielt hier", ssLast: "Zuletzt im Standby {when}", r_wol: "Wake-on-LAN", wolReady: "Bereit", wolCheck: "Netzwerkadapter prüfen", wolWifi: "Nur WLAN", wolNone: "Kein Netzwerkadapter", wolUnknown: "Unbekannt", wolMac: "Weckadresse (MAC): {mac}", wolMagic: "„Wake on Magic Packet“ in den erweiterten Adaptereinstellungen einschalten (Geräte-Manager)", wolPower: "Adapter darf den PC wecken: Geräte-Manager > Adapter > Energieverwaltung", wolArmed: "Windows lässt den Adapter den PC nicht wecken (Energieverwaltung)", wolCable: "Netzwerkkabel getrennt", wolFast: "Schnellstart ist an: Wecken aus dem ausgeschalteten Zustand kann fehlschlagen; aus dem Standby klappt es", wolCableHint: "Nimm ein Netzwerkkabel, um den PC vom Handheld aus zu wecken", r_ha: "Home Assistant", haOff: "Aus", haNotConfigured: "Server in Core > Gaming Mode festlegen", haConnected: "Verbunden · {host}", haConnecting: "Verbinde…", haRefused: "Anmeldung von {host} abgelehnt", haUnreachable: "{host} nicht erreichbar",
  },
  pt: {
    r_streamSleep: "Suspensão após a transmissão", ssOff: "Desligada", ssReady: "Ligada · após {n} min", ssCounting: "Suspensão às {time}, a menos que alguém jogue aqui", ssLast: "Última suspensão {when}", r_wol: "Wake-on-LAN", wolReady: "Pronto", wolCheck: "Verifique o adaptador de rede", wolWifi: "Só Wi-Fi", wolNone: "Nenhum adaptador de rede", wolUnknown: "Desconhecido", wolMac: "Endereço de ativação (MAC): {mac}", wolMagic: "Ative \"Wake on Magic Packet\" nas configurações avançadas do adaptador (Gerenciador de Dispositivos)", wolPower: "Deixe o adaptador ativar o PC: Gerenciador de Dispositivos > adaptador > Gerenciamento de Energia", wolArmed: "O Windows não deixa o adaptador ativar o PC (Gerenciamento de Energia)", wolCable: "Cabo de rede desconectado", wolFast: "A inicialização rápida está ligada: ativar a partir de desligado pode falhar; da suspensão funciona", wolCableHint: "Use um cabo de rede para ativar o PC a partir de um portátil", r_ha: "Home Assistant", haOff: "Desligado", haNotConfigured: "Defina o servidor em Core > Gaming Mode", haConnected: "Conectado · {host}", haConnecting: "Conectando…", haRefused: "Login recusado por {host}", haUnreachable: "Não foi possível alcançar {host}",
  },
  uk: {
    r_streamSleep: "Сон після трансляції", ssOff: "Вимкнено", ssReady: "Увімкнено · через {n} хв", ssCounting: "Сон о {time}, якщо тут ніхто не грає", ssLast: "Востаннє засинав {when}", r_wol: "Wake-on-LAN", wolReady: "Готово", wolCheck: "Перевірте мережевий адаптер", wolWifi: "Лише Wi-Fi", wolNone: "Немає мережевого адаптера", wolUnknown: "Невідомо", wolMac: "Адреса пробудження (MAC): {mac}", wolMagic: "Увімкніть «Wake on Magic Packet» у додаткових параметрах адаптера (Диспетчер пристроїв)", wolPower: "Дозвольте адаптеру будити ПК: Диспетчер пристроїв > адаптер > Керування живленням", wolArmed: "Windows не дозволяє адаптеру будити ПК (Керування живленням)", wolCable: "Мережевий кабель від’єднано", wolFast: "Швидкий запуск увімкнено: пробудження з вимкненого стану може не спрацювати; зі сну працює", wolCableHint: "Щоб будити ПК з портативної консолі, використовуйте мережевий кабель", r_ha: "Home Assistant", haOff: "Вимкнено", haNotConfigured: "Вкажіть сервер у Core > Gaming Mode", haConnected: "Підключено · {host}", haConnecting: "Підключення…", haRefused: "{host} відхилив вхід", haUnreachable: "{host} недоступний",
  },
  ru: {
    r_streamSleep: "Сон после трансляции", ssOff: "Выключено", ssReady: "Включено · через {n} мин", ssCounting: "Сон в {time}, если здесь никто не играет", ssLast: "Последний сон {when}", r_wol: "Wake-on-LAN", wolReady: "Готово", wolCheck: "Проверьте сетевой адаптер", wolWifi: "Только Wi-Fi", wolNone: "Нет сетевого адаптера", wolUnknown: "Неизвестно", wolMac: "Адрес пробуждения (MAC): {mac}", wolMagic: "Включите «Wake on Magic Packet» в дополнительных параметрах адаптера (Диспетчер устройств)", wolPower: "Разрешите адаптеру будить ПК: Диспетчер устройств > адаптер > Управление электропитанием", wolArmed: "Windows не разрешает адаптеру будить ПК (Управление электропитанием)", wolCable: "Сетевой кабель отключён", wolFast: "Быстрый запуск включён: пробуждение из выключенного состояния может не сработать; из сна работает", wolCableHint: "Чтобы будить ПК с портативной консоли, используйте сетевой кабель", r_ha: "Home Assistant", haOff: "Выключено", haNotConfigured: "Укажите сервер в Core > Gaming Mode", haConnected: "Подключено · {host}", haConnecting: "Подключение…", haRefused: "{host} отклонил вход", haUnreachable: "{host} недоступен",
  },
  zh: {
    r_streamSleep: "串流后睡眠", ssOff: "已关闭", ssReady: "已开启 · {n} 分钟后", ssCounting: "将在 {time} 睡眠，除非有人在这里玩", ssLast: "上次睡眠 {when}", r_wol: "网络唤醒", wolReady: "就绪", wolCheck: "请检查网络适配器", wolWifi: "仅 Wi-Fi", wolNone: "没有网络适配器", wolUnknown: "未知", wolMac: "唤醒地址（MAC）：{mac}", wolMagic: "在适配器的高级设置中打开“Wake on Magic Packet”（设备管理器）", wolPower: "允许适配器唤醒电脑：设备管理器 > 适配器 > 电源管理", wolArmed: "Windows 不允许适配器唤醒电脑（电源管理）", wolCable: "网线已拔出", wolFast: "快速启动已开启：从关机唤醒可能失败；从睡眠唤醒没问题", wolCableHint: "要从掌机唤醒电脑，请使用网线", r_ha: "Home Assistant", haOff: "已关闭", haNotConfigured: "在 Core > Gaming Mode 中设置服务器", haConnected: "已连接 · {host}", haConnecting: "正在连接…", haRefused: "{host} 拒绝登录", haUnreachable: "无法连接 {host}",
  },
  ja: {
    r_streamSleep: "ストリーミング後にスリープ", ssOff: "オフ", ssReady: "オン · {n} 分後", ssCounting: "{time} にスリープ（ここで誰かが遊ばない限り）", ssLast: "前回のスリープ {when}", r_wol: "Wake-on-LAN", wolReady: "準備完了", wolCheck: "ネットワークアダプターを確認してください", wolWifi: "Wi-Fi のみ", wolNone: "ネットワークアダプターがありません", wolUnknown: "不明", wolMac: "復帰用アドレス（MAC）：{mac}", wolMagic: "アダプターの詳細設定で「Wake on Magic Packet」をオンにします（デバイス マネージャー）", wolPower: "アダプターで PC を復帰できるようにします：デバイス マネージャー > アダプター > 電源の管理", wolArmed: "Windows がアダプターによる復帰を許可していません（電源の管理）", wolCable: "ネットワークケーブルが抜けています", wolFast: "高速スタートアップがオン：シャットダウンからの起動は失敗することがあります。スリープからの復帰は問題ありません", wolCableHint: "携帯機から PC を起こすにはネットワークケーブルを使ってください", r_ha: "Home Assistant", haOff: "オフ", haNotConfigured: "Core > Gaming Mode でサーバーを設定してください", haConnected: "接続済み · {host}", haConnecting: "接続しています…", haRefused: "{host} にログインを拒否されました", haUnreachable: "{host} に接続できません",
  },
  ko: {
    r_streamSleep: "스트리밍 후 절전", ssOff: "꺼짐", ssReady: "켜짐 · {n}분 후", ssCounting: "여기서 누가 플레이하지 않으면 {time}에 절전", ssLast: "마지막 절전 {when}", r_wol: "Wake-on-LAN", wolReady: "준비됨", wolCheck: "네트워크 어댑터를 확인하세요", wolWifi: "Wi-Fi만 있음", wolNone: "네트워크 어댑터 없음", wolUnknown: "알 수 없음", wolMac: "깨우기 주소(MAC): {mac}", wolMagic: "어댑터의 고급 설정에서 \"Wake on Magic Packet\"을 켜세요(장치 관리자)", wolPower: "어댑터가 PC를 깨울 수 있게 하세요: 장치 관리자 > 어댑터 > 전원 관리", wolArmed: "Windows가 어댑터의 PC 깨우기를 허용하지 않음(전원 관리)", wolCable: "네트워크 케이블 분리됨", wolFast: "빠른 시작이 켜져 있음: 종료 상태에서 깨우기는 실패할 수 있고, 절전에서는 됩니다", wolCableHint: "휴대용 기기에서 PC를 깨우려면 네트워크 케이블을 쓰세요", r_ha: "Home Assistant", haOff: "꺼짐", haNotConfigured: "Core > Gaming Mode에서 서버를 설정하세요", haConnected: "연결됨 · {host}", haConnecting: "연결하는 중…", haRefused: "{host}에서 로그인 거부", haUnreachable: "{host}에 연결할 수 없음",
  },
  hi: {
    r_streamSleep: "स्ट्रीमिंग के बाद स्लीप", ssOff: "बंद", ssReady: "चालू · {n} मिनट बाद", ssCounting: "{time} पर स्लीप, जब तक कोई यहाँ न खेले", ssLast: "पिछली बार स्लीप {when}", r_wol: "Wake-on-LAN", wolReady: "तैयार", wolCheck: "नेटवर्क एडैप्टर जाँचें", wolWifi: "सिर्फ़ Wi-Fi", wolNone: "कोई नेटवर्क एडैप्टर नहीं", wolUnknown: "अज्ञात", wolMac: "जगाने का पता (MAC): {mac}", wolMagic: "एडैप्टर की Advanced सेटिंग में \"Wake on Magic Packet\" चालू करें (Device Manager)", wolPower: "एडैप्टर को PC जगाने दें: Device Manager > एडैप्टर > Power Management", wolArmed: "Windows एडैप्टर को PC जगाने नहीं देता (Power Management)", wolCable: "नेटवर्क केबल निकली हुई है", wolFast: "फ़ास्ट स्टार्टअप चालू है: बंद PC को जगाना विफल हो सकता है; स्लीप से जगाना ठीक है", wolCableHint: "हैंडहेल्ड से PC जगाने के लिए नेटवर्क केबल लगाएँ", r_ha: "Home Assistant", haOff: "बंद", haNotConfigured: "Core > Gaming Mode में सर्वर सेट करें", haConnected: "जुड़ा · {host}", haConnecting: "जुड़ रहा है…", haRefused: "{host} ने लॉगिन ठुकराया", haUnreachable: "{host} तक नहीं पहुँच पा रहे",
  },
};
for (const [language, table] of Object.entries(batchTwoCopy)) Object.assign(copy[language] ?? {}, table);

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
  "windows-restart": "r_restart", "update-night": "r_night", "save-backup": "r_saves", "steam-update": "r_steam", "decky-plugins": "r_decky", tv: "r_tv", streaming: "r_stream", "stream-sleep": "r_streamSleep", "wake-on-lan": "r_wol", "home-assistant": "r_ha", microphone: "r_mic", "pc-disk": "r_pc", "core-update": "r_core",
  // Console guards (ConsoleGuards.cs).
  display: "r_display", "sound-output": "r_sound", controllers: "r_controllers", "steam-sign-in": "r_steamSign", "auto-sign-in": "r_autoSign", bitlocker: "r_bitlocker",
  "windows-prompts": "r_prompts", "update-policy": "r_updPolicy", "launcher-updates": "r_launchUpd", "launcher-focus": "r_launchFocus", "wake-check": "r_wake",
};

// The console guards' actions the agent accepts, and their labels.
const actionLabels: Record<string, string> = {
  "display-restore": "aDisplayRestore", "display-save": "aDisplaySave", "sound-restore": "aSoundRestore", "sound-save": "aSoundSave",
  "controllers-expect": "aControllersExpect", "prompts-off": "aPromptsOff", "bitlocker-suspend": "aBitlockerSuspend", "wake-check-now": "aWakeNow",
};

// Short, single-line text from the PC (device and monitor names): no control characters, at most 64 characters.
function clean(value: unknown, max = 64): string {
  return typeof value === "string" ? value.replace(/[\u0000-\u001f\u007f]/g, "").trim().slice(0, max) : "";
}

const serverStates: Record<string, string> = { "not-configured": "notConfigured", unreachable: "unreachable", missing: "missing", unreadable: "unreadable" };

function rowValue(row: HealthRowData, text: Copy, locale: string, now: Date): { value: string; details: string[] } {
  const data = row.data ?? {};
  const details: string[] = [];
  const when = (key: string) => formatWhen(data[key], locale, now);
  const sourceState = serverStates[row.state];
  if (sourceState && ["server-backup", "restore-check", "server-drive", "romm"].includes(row.id)) {
    if (row.id === "server-backup" && typeof data.server === "string" && data.server) details.push(`${data.server} · Home Assistant`);
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
    case "steam-update": {
      // Steam updates (STEAM-UPDATES.md): a row only while an automatic update is postponed.
      const reasons: Record<string, string> = { "pending-unsupported": text.stUnsupported, "pending-unknown": text.stUnknown, "rolled-back": text.stFailed };
      const reason = reasons[String(data.reason ?? "")] ?? text.stCantCheck;
      if (typeof data.pending === "string" && /^\d{1,20}$/.test(data.pending)) details.push(fill(text.stPending, { version: data.pending }));
      if (typeof data.failedCheck === "string" && /^[a-z-]{1,20}$/.test(data.failedCheck)) details.push(data.failedCheck);
      return { value: fill(text.stPostponed, { reason }), details };
    }
    case "save-backup": {
      // Batch 1: Ludusavi before update night. One reason line, never Ludusavi's output.
      const recent = data.recent === true, games = Number(data.games) || 0;
      const reason = String(data.reason ?? "");
      const exit = /^exit-(-?\d{1,6})$/.exec(reason);
      if (row.state === "ok" && games > 0) details.push(fill(text.svGames, { n: games }));
      if (row.state === "failed") {
        const why = reason === "time-limit" ? text.svLimit : reason === "some-games-failed" ? text.svSome : reason === "no-answer" ? text.svNoAnswer
          : reason === "not-started" ? text.svNotStarted : exit ? fill(text.svExit, { code: exit[1] }) : "";
        if (why) details.push(why);
      }
      if (row.state === "not-installed") details.push(text.svInstall);
      const value = row.state === "ok" ? (recent ? text.svOkNight : fill(text.svOkWhen, { when: when("at") }))
        : row.state === "failed" ? (recent ? text.svFailedNight : fill(text.svFailedWhen, { when: when("at") }))
        : row.state === "not-installed" ? text.svNone : row.state === "off" ? text.svOff : row.state === "never" ? text.svNever : text.unknown;
      return { value, details };
    }
    case "decky-plugins": {
      // Batch 1 (B11): the heartbeat's verdict and the plugins whose logs report errors. The agent masks the one line.
      const names = (Array.isArray(data.plugins) ? data.plugins : []).map(item => clean(item, 40)).filter(Boolean).slice(0, 6);
      const line = clean(data.line, 120), errors = Number(data.errors) || 0;
      if (row.state === "errors" || row.state === "not-loaded") {
        if (row.state === "not-loaded" && names.length) details.push(fill(text.dkFrom, { names: names.join(", ") }));
        if (errors > 0 && names.length) details.push(fill(text.dkCount, { n: errors }));
        if (line && names.length) details.push(fill(text.dkLast, { line }));
      }
      if (row.state === "errors" && names.length)
        return { value: names.length === 1 ? fill(text.dkErrOne, { name: names[0] }) : fill(text.dkErrMany, { names: names.join(", ") }), details };
      const states: Record<string, string> = { healthy: text.dkOk, waiting: text.dkWaiting, "not-applicable": text.dkNa, "not-loaded": text.dkNotLoaded };
      return { value: states[row.state] ?? text.unknown, details };
    }
    case "stream-sleep": {
      // Batch 2 (B13): information only. The countdown names the time; the last sleep rides along.
      if (typeof data.last === "string") details.push(fill(text.ssLast, { when: when("last") }));
      const minutes = Number(data.minutes);
      const value = row.state === "counting" ? fill(text.ssCounting, { time: formatTime(data.at, locale, 0) })
        : row.state === "ready" ? fill(text.ssReady, { n: Number.isInteger(minutes) && minutes >= 1 && minutes <= 120 ? minutes : 10 }) : text.ssOff;
      return { value, details };
    }
    case "wake-on-lan": {
      // Batch 2 (B13): what to change on the desktop so a handheld can wake the PC. Never attention.
      const problems = Array.isArray(data.problems) ? data.problems : [];
      const hints: Record<string, string> = { cable: text.wolCable, "magic-packet": text.wolMagic, "power-management": text.wolPower, "wake-armed": text.wolArmed };
      for (const problem of problems) if (typeof problem === "string" && hints[problem]) details.push(hints[problem]);
      if (row.state === "wifi-only") details.push(text.wolCableHint);
      const mac = clean(data.mac, 17);
      if (/^[0-9A-F]{2}(:[0-9A-F]{2}){5}$/.test(mac)) details.push(fill(text.wolMac, { mac }));
      if (data.fastStartup === true) details.push(text.wolFast);
      const states: Record<string, string> = { ready: text.wolReady, check: text.wolCheck, "wifi-only": text.wolWifi, "no-adapter": text.wolNone };
      return { value: states[row.state] ?? text.wolUnknown, details };
    }
    case "home-assistant": {
      // Batch 2 (B8): the MQTT bridge. The host is the user's own setting; never a password.
      const host = clean(data.host, 64) || "serverv3";
      const states: Record<string, string> = { off: text.haOff, "not-configured": text.haNotConfigured, connecting: text.haConnecting,
        connected: fill(text.haConnected, { host }), refused: fill(text.haRefused, { host }), unreachable: fill(text.haUnreachable, { host }) };
      return { value: states[row.state] ?? text.unknown, details };
    }
    case "core-update":
      return { value: row.state === "available" ? fill(text.cAvail, { latest: String(data.latest ?? "?") })
        : row.state === "current" ? fill(text.cCurrent, { current: String(data.current ?? "?") }) : text.unknown, details };
    default:
      return consoleValue(row, text, locale, now);
  }
  return { value: text.unknown, details };
}

// Console guards (CONSOLE-GUARDS.md): what each row says, and its one-line instruction where Windows offers no
// one-button fix at the TV.
function consoleValue(row: HealthRowData, text: Copy, locale: string, now: Date): { value: string; details: string[] } {
  const data = row.data ?? {};
  const details: string[] = [];
  const when = (key: string) => formatWhen(data[key], locale, now);
  switch (row.id) {
    case "display": {
      const saved = clean(data.saved), current = clean(data.current), monitor = clean(data.monitor);
      if (monitor) details.push(monitor);
      if (row.state === "changed") {
        const names: Record<string, string> = { resolution: text.dpResolution, refresh: text.dpRefresh, hdr: text.dpHdr, primary: text.dpPrimary, vrr: text.dpVrr };
        const what = (Array.isArray(data.differences) ? data.differences : []).map(item => names[String(item)]).filter(Boolean).join(", ");
        if (saved) details.push(fill(text.dpSaved, { mode: saved }));
        if (current) details.push(fill(text.dpNow, { mode: current }));
        if (data.vrrReadable !== true) details.push(text.dpVrrNote);
        return { value: fill(text.dpChanged, { what: what || "?" }), details };
      }
      if (data.vrrReadable !== true && row.state !== "unknown") details.push(text.dpVrrNote);
      if (row.state === "normal") return { value: fill(text.dpNormal, { mode: current || saved || "?" }), details };
      if (row.state === "not-saved") { if (current) details.push(fill(text.dpNow, { mode: current })); return { value: text.dpNotSaved, details }; }
      if (row.state === "missing") { if (saved) details.push(fill(text.dpSaved, { mode: saved })); return { value: text.dpMissing, details }; }
      return { value: text.unknown, details };
    }
    case "sound-output": {
      const saved = clean(data.saved, 80) || "?", current = clean(data.current, 80) || "?";
      if (row.state === "normal") return { value: fill(text.soNormal, { name: current }), details };
      if (row.state === "changed") { details.push(fill(text.soSaved, { name: saved })); return { value: fill(text.soChanged, { name: current }), details }; }
      if (row.state === "missing") { details.push(fill(text.soChanged, { name: current })); return { value: fill(text.soMissing, { name: saved }), details }; }
      if (row.state === "stream-mode") return { value: text.soStream, details };
      if (row.state === "not-saved") return { value: text.soNotSaved, details };
      return { value: text.unknown, details };
    }
    case "controllers": {
      const list = Array.isArray(data.controllers) ? data.controllers as Array<Record<string, unknown>> : [];
      for (const controller of list.slice(0, 12)) {
        const name = clean(controller?.name), key = clean(controller?.key, 9);
        if (name) details.push(key ? `${name} (${key})` : name);
      }
      const missing = Array.isArray(data.missing) ? data.missing.length : 0;
      if (row.state === "expected-missing") return { value: fill(text.coMissing, { n: missing }), details };
      if (row.state === "seen") return { value: fill(text.coSeen, { n: list.length }), details };
      if (row.state === "none") return { value: text.coNone, details };
      return { value: text.unknown, details };
    }
    case "steam-sign-in": {
      const name = clean(data.persona) || "Steam";
      if (row.state === "signed-in") return { value: fill(text.siSigned, { name }), details };
      if (row.state === "offline") return { value: fill(text.siOffline, { name }), details };
      if (row.state === "signed-out") { details.push(text.siOutDetail); return { value: text.siOut, details }; }
      if (row.state === "no-remember") { details.push(text.siNoRememberDetail); return { value: text.siNoRemember, details }; }
      if (row.state === "not-running") return { value: text.siNotRunning, details };
      return { value: text.unknown, details };
    }
    case "auto-sign-in": {
      const user = clean(data.user) || "?";
      if (data.plainTextPassword === true) details.push(text.asPlain);
      if (row.state === "on") return { value: fill(text.asOn, { user }), details };
      details.push(text.asGuide);
      if (row.state === "off") return { value: text.asOff, details };
      if (row.state === "expires") return { value: fill(text.asExpires, { n: Number(data.count) || 0 }), details };
      if (row.state === "passwordless") return { value: text.asHello, details };
      return { value: text.unknown, details: [] };
    }
    case "bitlocker": {
      const url = clean(data.confirmAt) || "account.microsoft.com/devices/recoverykey";
      const states: Record<string, string> = { off: text.blOff, on: text.blOn, encrypting: text.blEncrypting, unchecked: text.blUnchecked, "no-recovery-key": text.blNoKey,
        "firmware-pending": text.blFw, "firmware-no-guard": text.blFwNoGuard, suspended: text.blSuspended, "suspended-for-update": text.blSuspendedUpd, waiting: text.blWaiting };
      if (row.state === "unchecked") details.push(text.blCheckDesktop);
      if (row.state === "no-recovery-key") details.push(text.blNoKeyDetail);
      if (row.state === "firmware-no-guard") details.push(text.blGuardHint);
      if (row.state === "suspended") details.push(text.blSuspendedDetail);
      if (["on", "encrypting", "unchecked", "no-recovery-key", "firmware-pending", "firmware-no-guard", "waiting"].includes(row.state)) {
        if (data.backupSeenAt) details.push(fill(text.blBackup, { when: when("backupSeenAt") }));
        details.push(fill(text.blConfirm, { url }));
      }
      return { value: states[row.state] ?? text.unknown, details };
    }
    case "windows-prompts":
      if (row.state === "on" || row.state === "off") details.push(text.prDetail);
      return { value: row.state === "off" ? text.prOff : row.state === "on" ? text.prOn : text.unknown, details };
    case "update-policy": {
      if (row.state === "applied") return { value: fill(text.upApplied, { version: clean(data.pinnedVersion, 8) || "?" }), details };
      if (row.state === "applied-home") { details.push(text.upHomeDetail); return { value: text.upHome, details }; }
      if (row.state === "not-applied") { details.push(text.upNotDetail); if (data.home === true) details.push(text.upHomeDetail); return { value: text.upNot, details }; }
      return { value: text.unknown, details };
    }
    case "launcher-updates": {
      const list = (Array.isArray(data.pending) ? data.pending : []).map(item => clean(item, 40)).filter(Boolean);
      if (row.state === "pending") { details.push(text.luDetail); return { value: fill(text.luPending, { list: list.join(", ") || "?" }), details }; }
      return { value: row.state === "none" ? text.luNone : text.unknown, details };
    }
    case "launcher-focus": {
      const last = clean(data.last, 40);
      if (last) details.push(fill(text.lfLast, { name: last }));
      return { value: row.state === "watching" ? fill(text.lfOn, { n: Number(data.pushedToday) || 0 }) : row.state === "off" ? text.lfOff : text.unknown, details };
    }
    case "wake-check": {
      if (data.focusRetaken === true) details.push(text.wkFocus);
      if (data.inputResent === true) details.push(text.wkInput);
      if (data.bigPicture === false) details.push(text.wkNoBp);
      if (data.tvOnCore === false) details.push(text.wkTvNo);
      if (clean(data.adapter)) details.push(fill(text.wkAdapter, { name: clean(data.adapter) }));
      details.push(text.wkTemps);
      const states: Record<string, string> = { ok: text.wkOk, fixed: text.wkFixed, problem: text.wkProblem };
      if (states[row.state]) return { value: fill(states[row.state], { when: when("at") }), details };
      if (row.state === "network-down") return { value: text.wkNet, details };
      return { value: row.state === "none-yet" ? text.wkNone : row.state === "off" ? text.wkOff : text.unknown, details };
    }
  }
  return { value: text.unknown, details };
}

// A row's buttons: its fix (from data.fix) first, then the others it offers (data.actions).
function rowActions(row: HealthRowData, text: Copy): HealthAction[] {
  const data = row.data ?? {};
  const actions: HealthAction[] = [];
  const fix = typeof data.fix === "string" && actionLabels[data.fix] ? data.fix : undefined;
  if (fix) actions.push({ id: fix, label: text[actionLabels[fix]], fix: true });
  for (const id of Array.isArray(data.actions) ? data.actions : [])
    if (typeof id === "string" && actionLabels[id] && id !== fix) actions.push({ id, label: text[actionLabels[id]], fix: false });
  return actions;
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
    const actions = rowActions(row, text);
    if (actions.length) line.actions = actions;
    return line;
  });
}
