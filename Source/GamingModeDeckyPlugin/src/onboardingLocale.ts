import { normalizeStoreLocale, type StoreLocale } from "./pluginStoreLocale";

const copy: Record<StoreLocale, readonly [string, string, string, string, string, string, string, string]> = {
  en: ["Discover the new Core experience", "Open Quick Access to discover Core.", "Everything is here: your Core, in Quick Access.", "Decky is available here. You can restore its Quick Access tab in Core settings.", "Decky integration is optional. You can enable it in Core settings and restore its Quick Access tab at any time.", "Continue", "Pause guide", "Finish"],
  it: ["Scopri la nuova esperienza Core", "Apri l'accesso rapido per scoprire Core.", "Trovi tutto qui: il tuo Core, nell'accesso rapido.", "Decky è accessibile qui. Puoi ripristinare la sua scheda nell'accesso rapido dalle impostazioni di Core.", "L'integrazione di Decky è facoltativa. Puoi attivarla nelle impostazioni di Core e ripristinare la sua scheda nell'accesso rapido in qualsiasi momento.", "Continua", "Sospendi la guida", "Concludi"],
  es: ["Descubre la nueva experiencia Core", "Abre el acceso rápido para descubrir Core.", "Todo está aquí: tu Core, en el acceso rápido.", "Decky está disponible aquí. Puedes restaurar su pestaña de acceso rápido en los ajustes de Core.", "La integración de Decky es opcional. Puedes activarla en los ajustes de Core y restaurar su pestaña de acceso rápido cuando quieras.", "Continuar", "Pausar la guía", "Finalizar"],
  fr: ["Découvrez la nouvelle expérience Core", "Ouvrez l’accès rapide pour découvrir Core.", "Tout est ici : votre Core, dans l’accès rapide.", "Decky est accessible ici. Vous pouvez rétablir son onglet d’accès rapide dans les paramètres de Core.", "L’intégration de Decky est facultative. Vous pouvez l’activer dans les paramètres de Core et rétablir son onglet d’accès rapide à tout moment.", "Continuer", "Suspendre le guide", "Terminer"],
  de: ["Entdecke das neue Core-Erlebnis", "Öffne den Schnellzugriff, um Core zu entdecken.", "Alles ist hier: dein Core im Schnellzugriff.", "Decky ist hier verfügbar. Seinen Schnellzugriff-Tab kannst du in den Core-Einstellungen wiederherstellen.", "Die Decky-Integration ist optional. Du kannst sie in den Core-Einstellungen aktivieren und den Schnellzugriff-Tab jederzeit wiederherstellen.", "Weiter", "Anleitung pausieren", "Abschließen"],
  pt: ["Descubra a nova experiência Core", "Abra o acesso rápido para descobrir o Core.", "Está tudo aqui: seu Core, no acesso rápido.", "O Decky está disponível aqui. Você pode restaurar sua aba de acesso rápido nas configurações do Core.", "A integração do Decky é opcional. Você pode ativá-la nas configurações do Core e restaurar sua aba de acesso rápido a qualquer momento.", "Continuar", "Pausar guia", "Concluir"],
  uk: ["Відкрийте нові можливості Core", "Відкрийте швидкий доступ, щоб познайомитися з Core.", "Усе тут: ваш Core у швидкому доступі.", "Decky доступний тут. Його вкладку швидкого доступу можна відновити в налаштуваннях Core.", "Інтеграція Decky необов’язкова. Її можна ввімкнути в налаштуваннях Core і будь-коли відновити вкладку швидкого доступу.", "Продовжити", "Призупинити посібник", "Завершити"],
  zh: ["探索全新 Core 体验", "打开快捷访问，探索 Core。", "一切尽在此处：快捷访问中的 Core。", "你可以在此使用 Decky，也可以在 Core 设置中恢复它的快捷访问标签页。", "Decky 集成是可选的。你可以在 Core 设置中启用它，并随时恢复它的快捷访问标签页。", "继续", "暂停引导", "完成"],
  ja: ["新しい Core を体験しよう", "クイックアクセスを開いて Core を見てみましょう。", "すべてがここに。クイックアクセスから Core を使えます。", "Decky はここから使えます。Core の設定でクイックアクセスタブを元に戻せます。", "Decky の統合は任意です。Core の設定で有効にでき、いつでもクイックアクセスタブを元に戻せます。", "続ける", "ガイドを一時停止", "完了"],
  ko: ["새로운 Core를 만나보세요", "빠른 액세스를 열어 Core를 살펴보세요.", "모든 기능이 여기에 있습니다. 빠른 액세스에서 Core를 이용하세요.", "여기에서 Decky를 이용할 수 있습니다. Core 설정에서 빠른 액세스 탭을 복원할 수 있습니다.", "Decky 통합은 선택 사항입니다. Core 설정에서 활성화하고 언제든지 빠른 액세스 탭을 복원할 수 있습니다.", "계속", "안내 일시 중지", "완료"],
  hi: ["Core का नया अनुभव जानें", "Core देखने के लिए त्वरित ऐक्सेस खोलें।", "सब कुछ यहाँ है: त्वरित ऐक्सेस में आपका Core।", "Decky यहाँ उपलब्ध है। Core की सेटिंग में उसका त्वरित ऐक्सेस टैब वापस ला सकते हैं।", "Decky का एकीकरण वैकल्पिक है। इसे Core की सेटिंग में चालू कर सकते हैं और कभी भी त्वरित ऐक्सेस टैब वापस ला सकते हैं।", "जारी रखें", "गाइड रोकें", "पूरा करें"],
  ru: ["Откройте новый Core", "Откройте быстрый доступ, чтобы познакомиться с Core.", "Всё здесь: ваш Core в быстром доступе.", "Decky доступен здесь. Его вкладку быстрого доступа можно восстановить в настройках Core.", "Интеграция Decky необязательна. Её можно включить в настройках Core и в любой момент восстановить вкладку быстрого доступа.", "Продолжить", "Приостановить руководство", "Завершить"],
};
export function getOnboardingCopy(locale: unknown) {
  const [, open, , decky, , next, pause, finish] = copy[normalizeStoreLocale(locale)];
  const deckyOff = nativeDeckyCopy[normalizeStoreLocale(locale)];
  const [title, playhub] = welcome[normalizeStoreLocale(locale)];
  const text = presentation[normalizeStoreLocale(locale)];
  return { title, open: text[1], playhub: text[3], decky: text[5], deckyOff: text[5], next, pause, finish,
    playhubTitle: text[2], deckyTitle: text[4], storeTitle: text[6], customizeTitle: text[8],
    outroTitle: text[10], outro: text[11] };
}

const nativeDeckyCopy: Record<StoreLocale, string> = {
  en: "Manage Decky's Core tab in the tab editor. The separate setting only hides or restores Decky's tab in Steam Quick Access.",
  it: "Gestisci la scheda Decky di Core nell'editor delle schede. L'impostazione separata nasconde o ripristina soltanto la scheda Decky nell'accesso rapido di Steam.",
  es: "Gestiona la pestaña Decky de Core en el editor de pestañas. El ajuste separado solo oculta o restaura la pestaña Decky en el acceso rápido de Steam.",
  fr: "Gérez l’onglet Decky de Core dans l’éditeur d’onglets. Le réglage séparé masque ou rétablit uniquement l’onglet Decky dans l’accès rapide de Steam.",
  de: "Verwalte den Decky-Tab von Core im Tab-Editor. Die separate Einstellung blendet nur den Decky-Tab im Steam-Schnellzugriff aus oder wieder ein.",
  pt: "Gerencie a aba Decky do Core no editor de abas. A configuração separada apenas oculta ou restaura a aba Decky no acesso rápido do Steam.",
  uk: "Керуйте вкладкою Decky у Core через редактор вкладок. Окреме налаштування лише приховує або відновлює вкладку Decky у швидкому доступі Steam.",
  zh: "在标签页编辑器中管理 Core 的 Decky 标签页。独立设置仅隐藏或恢复 Steam 快捷访问中的 Decky 标签页。",
  ja: "Core の Decky タブはタブ編集で管理できます。別の設定では Steam のクイックアクセスにある Decky タブだけを非表示にしたり戻したりできます。",
  ko: "탭 편집기에서 Core의 Decky 탭을 관리하세요. 별도 설정은 Steam 빠른 액세스의 Decky 탭만 숨기거나 복원합니다.",
  hi: "टैब संपादक में Core के Decky टैब को व्यवस्थित करें। अलग सेटिंग केवल Steam के त्वरित ऐक्सेस में Decky टैब को छिपाती या वापस लाती है।",
  ru: "Управляйте вкладкой Decky в Core через редактор вкладок. Отдельная настройка только скрывает или восстанавливает вкладку Decky в быстром доступе Steam.",
};

const welcome: Record<StoreLocale, readonly [string, string]> = {
  en: ["Welcome to Core", "Open your Dashboard, adjust controls and discover plugins from one place."],
  it: ["Benvenuto in Core", "Apri la Dashboard, regola i controlli e scopri i plugin da un unico posto."],
  es: ["Te damos la bienvenida a Core", "Abre tu Dashboard, ajusta los controles y descubre plugins en un solo lugar."],
  fr: ["Bienvenue dans Core", "Ouvrez le Dashboard, réglez les commandes et découvrez des plugins au même endroit."],
  de: ["Willkommen bei Core", "Öffne dein Dashboard, passe die Steuerung an und entdecke Plugins an einem Ort."],
  pt: ["Boas-vindas ao Core", "Abra o Dashboard, ajuste os controles e descubra plugins em um só lugar."],
  uk: ["Ласкаво просимо до Core", "Відкривайте Dashboard, налаштовуйте керування та знаходьте плагіни в одному місці."],
  zh: ["欢迎使用 Core", "在同一处打开 Dashboard、调整控制设置并发现插件。"],
  ja: ["Core へようこそ", "Dashboard を開き、操作を調整し、プラグインを探す。すべてここから。"],
  ko: ["Core에 오신 것을 환영합니다", "한곳에서 Dashboard를 열고, 조작을 설정하고, 플러그인을 찾아보세요."],
  hi: ["Core में आपका स्वागत है", "एक ही जगह से Dashboard खोलें, नियंत्रण बदलें और प्लगइन खोजें।"],
  ru: ["Добро пожаловать в Core", "Открывайте Dashboard, настраивайте управление и находите плагины в одном месте."],
};

const extras: Record<StoreLocale, readonly [string, string, string]> = {
  en: ["Explore the Plugin Store to find plugins for Steam and Core.", "Choose which tabs appear and arrange them in the order you prefer.", "Resume Core guide"],
  it: ["Esplora il Plugin Store per trovare plugin per Steam e Core.", "Scegli quali schede mostrare e disponile nell'ordine che preferisci.", "Riprendi la guida di Core"],
  es: ["Explora Plugin Store para encontrar plugins para Steam y Core.", "Elige qué pestañas mostrar y organízalas en el orden que prefieras.", "Reanudar la guía de Core"],
  fr: ["Explorez le Plugin Store pour trouver des plugins pour Steam et Core.", "Choisissez les onglets à afficher et disposez-les dans l’ordre souhaité.", "Reprendre le guide Core"],
  de: ["Entdecke im Plugin Store Plugins für Steam und Core.", "Wähle die sichtbaren Tabs und ordne sie nach deinen Wünschen an.", "Core-Anleitung fortsetzen"],
  pt: ["Explore o Plugin Store para encontrar plugins para Steam e Core.", "Escolha quais abas mostrar e organize-as na ordem que preferir.", "Retomar guia do Core"],
  uk: ["Перегляньте Plugin Store, щоб знайти плагіни для Steam і Core.", "Виберіть видимі вкладки та розташуйте їх у зручному порядку.", "Продовжити посібник Core"],
  zh: ["浏览 Plugin Store，查找适用于 Steam 和 Core 的插件。", "选择要显示的标签页，并按喜好排列顺序。", "继续 Core 引导"],
  ja: ["Plugin Store で Steam や Core のプラグインを探しましょう。", "表示するタブを選び、好きな順序に並べられます。", "Core ガイドを再開"],
  ko: ["Plugin Store에서 Steam과 Core용 플러그인을 찾아보세요.", "표시할 탭을 선택하고 원하는 순서로 배치하세요.", "Core 안내 재개"],
  hi: ["Steam और Core के प्लगइन खोजने के लिए Plugin Store देखें।", "दिखने वाले टैब चुनें और उन्हें अपने पसंदीदा क्रम में रखें।", "Core गाइड जारी रखें"],
  ru: ["Откройте Plugin Store, чтобы найти плагины для Steam и Core.", "Выберите видимые вкладки и расположите их в удобном порядке.", "Продолжить руководство Core"],
};
export function getOnboardingExtraCopy(locale: unknown) {
  const [store, customize, resume] = extras[normalizeStoreLocale(locale)];
  const text = presentation[normalizeStoreLocale(locale)];
  return { store: text[7], customize: text[9], resume, replay: replayLabels[normalizeStoreLocale(locale)] };
}
const presentation: Record<StoreLocale, readonly [string, string, string, string, string, string, string, string, string, string, string, string]> = {
  it: ["Il controllo, a portata di gioco", "Dashboard, plugin e impostazioni: scopri cosa trovi nell'accesso rapido di Core.",
    "Il tuo viaggio inizia qui", "Apri la Core Dashboard per tenere tutto sotto controllo, e da questo pannello scegli se avviare in Gaming Mode o Desktop Mode.",
    "I tuoi plugin, tutti qui", "Ora il menu di Decky è parte di Core e puoi accedervi in qualsiasi momento per vedere la lista dei tuoi plugin installati. Puoi anche ripristinare la tab Decky in qualsiasi momento dalle impostazioni di Core.",
    "Più plugin, un solo Store", "Scopri plugin Playhub, Decky Store e progetti indipendenti su GitHub, riuniti in un solo catalogo.",
    "Le tab, nel tuo ordine", "Riordina le tab e nascondi quelle che non usi. Puoi mostrarle di nuovo quando vuoi.",
    "È il momento di giocare come mai prima d'ora", "Scopri, prova, personalizza, gioca e divertiti.\nQuesto è lo spirito di Core."],
  en: ["Control within reach", "Dashboard, plugins and settings: see what Core brings to Quick Access.",
    "Your journey starts here", "Open the Core Dashboard to keep everything under control, and choose from this panel whether to start in Gaming Mode or Desktop Mode.",
    "Your plugins, together", "The Decky menu is now part of Core, always available to browse your installed plugins. You can restore the Decky tab at any time from Core settings.",
    "More plugins, one Store", "Discover Playhub plugins, Decky Store and independent GitHub projects in one catalog.",
    "Your tabs, your order", "Reorder tabs and hide the ones you don't use. Bring them back whenever you like.",
    "It's time to enjoy gaming like never before", "Discover, try, customize, play and have fun.\nThis is the spirit of Core."],
  es: ["El control, a tu alcance", "Dashboard, plugins y ajustes: descubre lo que ofrece Core en el acceso rápido.",
    "Tu viaje empieza aquí", "Abre Core Dashboard para tenerlo todo bajo control y elige en este panel si quieres iniciar en Gaming Mode o Desktop Mode.",
    "Tus plugins, juntos", "El menú de Decky ahora forma parte de Core y está siempre disponible para ver tus plugins instalados. También puedes restaurar la tab Decky en cualquier momento desde los ajustes de Core.",
    "Más plugins, una sola tienda", "Descubre plugins de Playhub, Decky Store y proyectos independientes de GitHub en un catálogo.",
    "Tus pestañas, a tu manera", "Reordena las pestañas y oculta las que no uses. Puedes volver a mostrarlas cuando quieras.",
    "Ha llegado el momento de disfrutar de los videojuegos como nunca", "Descubre, prueba, personaliza, juega y diviértete.\nEse es el espíritu de Core."],
  fr: ["Le contrôle à portée de main", "Dashboard, plugins et réglages : découvrez Core dans l’accès rapide.",
    "Votre voyage commence ici", "Ouvrez le Core Dashboard pour garder le contrôle, et choisissez dans ce panneau de démarrer en Gaming Mode ou en Desktop Mode.",
    "Vos plugins, réunis", "Le menu Decky fait désormais partie de Core et reste accessible à tout moment pour consulter vos plugins installés. Vous pouvez aussi rétablir l’onglet Decky à tout moment dans les paramètres de Core.",
    "Plus de plugins, un seul Store", "Découvrez les plugins Playhub, Decky Store et les projets indépendants GitHub dans un même catalogue.",
    "Vos onglets, dans votre ordre", "Réorganisez les onglets et masquez ceux que vous n’utilisez pas. Vous pourrez les réafficher à tout moment.",
    "Il est temps de jouer comme jamais auparavant", "Découvre, essaie, personnalise, joue et amuse-toi.\nC'est l'esprit de Core."],
  de: ["Alles schnell im Griff", "Dashboard, Plugins und Einstellungen: entdecke Core im Schnellzugriff.",
    "Deine Reise beginnt hier", "Öffne das Core Dashboard, um alles im Griff zu behalten, und wähle in diesem Bereich, ob du im Gaming Mode oder im Desktop Mode starten willst.",
    "Deine Plugins an einem Ort", "Das Decky-Menü ist jetzt Teil von Core und jederzeit verfügbar, um deine installierten Plugins anzusehen. Du kannst den Decky-Tab jederzeit in den Core-Einstellungen wiederherstellen.",
    "Mehr Plugins, ein Store", "Entdecke Playhub-Plugins, Decky Store und unabhängige GitHub-Projekte in einem Katalog.",
    "Deine Tabs in deiner Reihenfolge", "Ordne Tabs neu an und blende ungenutzte aus. Du kannst sie jederzeit wieder einblenden.",
    "Zeit, Gaming wie nie zuvor zu genießen", "Entdecke, probiere aus, passe Core an, spiele und hab Spaß.\nGenau das macht Core aus."],
  pt: ["O controle ao seu alcance", "Dashboard, plugins e configurações: conheça o Core no acesso rápido.",
    "Sua jornada começa aqui", "Abra o Core Dashboard para manter tudo sob controle e escolha neste painel se quer iniciar em Gaming Mode ou Desktop Mode.",
    "Seus plugins, reunidos", "O menu do Decky agora faz parte do Core e está sempre disponível para ver seus plugins instalados. Você também pode restaurar a aba Decky a qualquer momento nas configurações do Core.",
    "Mais plugins, uma só loja", "Descubra plugins do Playhub, Decky Store e projetos independentes do GitHub em um catálogo.",
    "Suas abas, na sua ordem", "Reorganize as abas e oculte as que não usa. Você pode exibi-las novamente quando quiser.",
    "É hora de curtir os games como nunca antes", "Descubra, experimente, personalize, jogue e divirta-se.\nEsse é o espírito do Core."],
  uk: ["Керування завжди поруч", "Dashboard, плагіни й налаштування: відкрийте Core у швидкому доступі.",
    "Ваша подорож починається тут", "Відкрийте Core Dashboard, щоб тримати все під контролем, і виберіть на цій панелі, запускатися в Gaming Mode чи Desktop Mode.",
    "Ваші плагіни разом", "Меню Decky тепер є частиною Core і завжди доступне для перегляду встановлених плагінів. Ви також можете будь-коли відновити вкладку Decky в налаштуваннях Core.",
    "Більше плагінів, один магазин", "Знаходьте плагіни Playhub, Decky Store та незалежні проєкти GitHub в одному каталозі.",
    "Вкладки у вашому порядку", "Змінюйте порядок вкладок і приховуйте непотрібні. Їх можна знову показати будь-коли.",
    "Час насолоджуватися іграми як ніколи", "Відкривай, пробуй, налаштовуй, грай і розважайся.\nЦе і є дух Core."],
  zh: ["控制，触手可及", "在快捷访问中探索 Core 的 Dashboard、插件和设置。",
    "你的旅程从这里开始", "打开 Core Dashboard，让一切尽在掌握，并在此面板中选择以 Gaming Mode 还是 Desktop Mode 启动。",
    "你的插件，汇聚一处", "Decky 菜单现已成为 Core 的一部分，你可以随时打开它，查看已安装的插件列表。你也可以随时在 Core 设置中恢复 Decky 标签页。",
    "更多插件，一个商店", "在一个目录中探索 Playhub 插件、Decky Store 和 GitHub 独立项目。",
    "标签顺序，由你安排", "重新排列标签页，隐藏不用的标签页。随时都能重新显示。",
    "是时候以前所未有的方式享受游戏了", "探索、尝试、自定义、畅玩并享受乐趣。\n这就是 Core 的精神。"],
  ja: ["操作を、すぐ手元に", "Dashboard、プラグイン、設定。クイックアクセスで Core を見てみましょう。",
    "あなたの旅はここから", "Core Dashboard を開いてすべてを管理し、このパネルで Gaming Mode と Desktop Mode のどちらで起動するかを選べます。",
    "いつものプラグインを一か所に", "Decky メニューが Core の一部になり、インストール済みプラグインの一覧をいつでも確認できます。Core の設定から Decky タブをいつでも元に戻せます。",
    "プラグイン探しは、このストアで", "Playhub のプラグイン、Decky Store、GitHub の独立プロジェクトを、ひとつのカタログで探せます。",
    "タブを、自分の順番に", "タブを並べ替え、使わないものは非表示に。いつでも表示に戻せます。",
    "これまでにないゲーム体験を", "見つけて、試して、カスタマイズして、遊んで、楽しもう。\nこれが Core の精神です。"],
  ko: ["필요한 조작을 가까이", "Dashboard, 플러그인, 설정까지. 빠른 액세스에서 Core를 살펴보세요.",
    "여기서 시작하는 나의 여정", "Core Dashboard를 열어 모든 것을 관리하고, 이 패널에서 Gaming Mode와 Desktop Mode 중 무엇으로 시작할지 선택하세요.",
    "내 플러그인을 한곳에", "이제 Decky 메뉴가 Core의 일부가 되어 언제든 설치된 플러그인 목록을 확인할 수 있습니다. Core 설정에서 Decky 탭을 언제든 복원할 수도 있습니다.",
    "더 많은 플러그인, 하나의 스토어", "Playhub 플러그인, Decky Store, GitHub 독립 프로젝트를 하나의 카탈로그에서 살펴보세요.",
    "탭 순서는 내 방식대로", "탭 순서를 바꾸고 쓰지 않는 탭은 숨기세요. 언제든 다시 표시할 수 있습니다.",
    "이제 전에 없던 방식으로 게임을 즐길 시간입니다", "발견하고, 체험하고, 맞춤 설정하고, 플레이하며 즐기세요.\n이것이 Core의 정신입니다."],
  hi: ["नियंत्रण, आपके पास", "Dashboard, प्लगइन और सेटिंग: त्वरित ऐक्सेस में Core को जानें।",
    "आपकी यात्रा यहाँ से शुरू होती है", "सब कुछ नियंत्रण में रखने के लिए Core Dashboard खोलें, और इस पैनल से चुनें कि Gaming Mode में शुरू करना है या Desktop Mode में।",
    "आपके प्लगइन, एक साथ", "Decky मेनू अब Core का हिस्सा है और आप अपने इंस्टॉल किए गए प्लगइन की सूची देखने के लिए इसे कभी भी खोल सकते हैं। Core की सेटिंग से Decky टैब को भी कभी भी वापस ला सकते हैं।",
    "ज़्यादा प्लगइन, एक स्टोर", "Playhub प्लगइन, Decky Store और GitHub के स्वतंत्र प्रोजेक्ट एक ही कैटलॉग में खोजें।",
    "आपके टैब, आपके क्रम में", "टैब का क्रम बदलें और जिनका उपयोग नहीं करते उन्हें छिपाएँ। उन्हें कभी भी वापस दिखा सकते हैं।",
    "अब गेमिंग का ऐसा आनंद लें जैसा पहले कभी नहीं", "खोजें, आज़माएँ, कस्टमाइज़ करें, खेलें और आनंद लें।\nयही Core की भावना है।"],
  ru: ["Управление под рукой", "Dashboard, плагины и настройки: познакомьтесь с Core в быстром доступе.",
    "Ваше путешествие начинается здесь", "Откройте Core Dashboard, чтобы держать всё под контролем, и выберите на этой панели, запускаться в Gaming Mode или Desktop Mode.",
    "Ваши плагины вместе", "Меню Decky теперь является частью Core и всегда доступно для просмотра установленных плагинов. Вы также можете в любой момент восстановить вкладку Decky в настройках Core.",
    "Больше плагинов, один магазин", "Находите плагины Playhub, Decky Store и независимые проекты GitHub в одном каталоге.",
    "Вкладки в вашем порядке", "Меняйте порядок вкладок и скрывайте ненужные. Их можно снова показать в любой момент.",
    "Пора наслаждаться играми как никогда раньше", "Открывай, пробуй, настраивай, играй и получай удовольствие.\nВ этом и есть дух Core."],
};
const replayLabels: Record<StoreLocale, string> = {
  en: "Replay introduction", it: "Ripeti introduzione", es: "Repetir presentación",
  fr: "Revoir la présentation", de: "Einführung wiederholen", pt: "Repetir apresentação",
  uk: "Повторити презентацію", zh: "重新观看介绍", ja: "紹介をもう一度見る",
  ko: "소개 다시 보기", hi: "परिचय फिर से देखें", ru: "Повторить презентацию",
};

const actions: Record<StoreLocale, readonly [string, string, string]> = {
  en: ["Press", "to continue", "to finish"], it: ["Premi", "per continuare", "per concludere"],
  es: ["Pulsa", "para continuar", "para finalizar"], fr: ["Appuyez sur", "pour continuer", "pour terminer"],
  de: ["Drücke", "zum Fortfahren", "zum Abschließen"], pt: ["Pressione", "para continuar", "para concluir"],
  uk: ["Натисніть", "щоб продовжити", "щоб завершити"], zh: ["按", "继续", "完成"],
  ja: ["", "を押して続ける", "を押して完了"], ko: ["", "버튼을 눌러 계속", "버튼을 눌러 완료"],
  hi: ["", "दबाकर जारी रखें", "दबाकर पूरा करें"], ru: ["Нажмите", "чтобы продолжить", "чтобы завершить"],
};
export function getOnboardingActionCopy(locale: unknown, finish: boolean) {
  const [before, next, last] = actions[normalizeStoreLocale(locale)];
  return { before, after: finish ? last : next };
}
