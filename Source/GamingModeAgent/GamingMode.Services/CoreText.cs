using System;
using System.Collections.Generic;
using System.Globalization;

namespace GamingMode.Services;

// Eternal Core Mark III: text the agent shows by itself (notices over Big Picture and the
// Core Repair window), in the 12 languages Core ships. The language is the app's
// (config.json "language"); "auto" or an unknown value follows Windows, then English.
// MarkIII.Tests checks every entry has all 12 translations with matching placeholders.
internal static class CoreText
{
	internal static readonly string[] Languages = { "it", "en", "es", "fr", "de", "pt", "uk", "zh", "ja", "ko", "hi", "ru" };

	internal static string Get(string? language, string key)
	{
		if (!Table.TryGetValue(key, out string[]? values)) return key;
		int index = Array.IndexOf(Languages, Normalize(language));
		return values[index < 0 ? 1 : index];
	}

	internal static string Format(string? language, string key, params object[] arguments) =>
		string.Format(CultureInfo.InvariantCulture, Get(language, key), arguments);

	internal static string Normalize(string? language)
	{
		string value = string.IsNullOrWhiteSpace(language) || language.Equals("auto", StringComparison.OrdinalIgnoreCase)
			? CultureInfo.CurrentUICulture.TwoLetterISOLanguageName : language;
		value = value.Trim().ToLowerInvariant().Split('-', '_')[0];
		if (value == "ua") value = "uk";
		return Array.IndexOf(Languages, value) >= 0 ? value : "en";
	}

	internal static IReadOnlyDictionary<string, string[]> Entries => Table;

	// Order: it, en, es, fr, de, pt, uk, zh, ja, ko, hi, ru.
	private static readonly Dictionary<string, string[]> Table = new(StringComparer.Ordinal)
	{
		["notice.plugins"] = new[]
		{
			"I plugin si prendono una breve pausa. Tutto il resto funziona normalmente. Per sistemarli apri Core Repair nella tua libreria.",
			"Plugins are taking a short break. Everything else works normally. To fix them, open Core Repair in your library.",
			"Los plugins se toman un breve descanso. Todo lo demás funciona con normalidad. Para arreglarlos, abre Core Repair en tu biblioteca.",
			"Les plugins font une courte pause. Tout le reste fonctionne normalement. Pour les réparer, ouvre Core Repair dans ta bibliothèque.",
			"Die Plugins machen eine kurze Pause. Alles andere funktioniert normal. Zum Reparieren öffne Core Repair in deiner Bibliothek.",
			"Os plugins estão fazendo uma pausa. Todo o resto funciona normalmente. Para corrigir, abra o Core Repair na sua biblioteca.",
			"Плагіни взяли коротку перерву. Усе інше працює як завжди. Щоб виправити, відкрийте Core Repair у бібліотеці.",
			"插件暂时休息一下。其他功能一切正常。要修复，请在库中打开 Core Repair。",
			"プラグインは少し休憩中です。ほかはすべて通常どおり動作します。直すにはライブラリで Core Repair を開いてください。",
			"플러그인이 잠시 쉬고 있습니다. 나머지는 모두 정상입니다. 고치려면 라이브러리에서 Core Repair를 여세요.",
			"प्लगइन थोड़ी देर के लिए रुके हैं। बाकी सब सामान्य रूप से चल रहा है। ठीक करने के लिए अपनी लाइब्रेरी में Core Repair खोलें।",
			"Плагины взяли короткую паузу. Всё остальное работает как обычно. Чтобы исправить, откройте Core Repair в библиотеке.",
		},
		["notice.pluginsAfterUpdate"] = new[]
		{
			"Dopo un aggiornamento di Steam i plugin si prendono una breve pausa. Tutto il resto funziona normalmente. Per sistemarli apri Core Repair nella tua libreria.",
			"Plugins are taking a short break after a Steam update. Everything else works normally. To fix them, open Core Repair in your library.",
			"Tras una actualización de Steam, los plugins se toman un breve descanso. Todo lo demás funciona con normalidad. Para arreglarlos, abre Core Repair en tu biblioteca.",
			"Après une mise à jour de Steam, les plugins font une courte pause. Tout le reste fonctionne normalement. Pour les réparer, ouvre Core Repair dans ta bibliothèque.",
			"Nach einem Steam-Update machen die Plugins eine kurze Pause. Alles andere funktioniert normal. Zum Reparieren öffne Core Repair in deiner Bibliothek.",
			"Depois de uma atualização do Steam, os plugins estão fazendo uma pausa. Todo o resto funciona normalmente. Para corrigir, abra o Core Repair na sua biblioteca.",
			"Після оновлення Steam плагіни взяли коротку перерву. Усе інше працює як завжди. Щоб виправити, відкрийте Core Repair у бібліотеці.",
			"Steam 更新后插件暂时休息一下。其他功能一切正常。要修复，请在库中打开 Core Repair。",
			"Steam の更新後、プラグインは少し休憩中です。ほかはすべて通常どおり動作します。直すにはライブラリで Core Repair を開いてください。",
			"Steam 업데이트 후 플러그인이 잠시 쉬고 있습니다. 나머지는 모두 정상입니다. 고치려면 라이브러리에서 Core Repair를 여세요.",
			"Steam अपडेट के बाद प्लगइन थोड़ी देर के लिए रुके हैं। बाकी सब सामान्य रूप से चल रहा है। ठीक करने के लिए अपनी लाइब्रेरी में Core Repair खोलें।",
			"После обновления Steam плагины взяли короткую паузу. Всё остальное работает как обычно. Чтобы исправить, откройте Core Repair в библиотеке.",
		},
		["notice.steamHoldExpired"] = new[]
		{
			"Gli aggiornamenti di Steam sono in attesa da 30 giorni. Core aggiornerà Steam la prossima volta che non stai giocando.",
			"Steam updates have been on hold for 30 days. Core will update Steam the next time you're not playing.",
			"Las actualizaciones de Steam llevan 30 días en espera. Core actualizará Steam la próxima vez que no estés jugando.",
			"Les mises à jour de Steam sont en attente depuis 30 jours. Core mettra Steam à jour la prochaine fois que tu ne joues pas.",
			"Steam-Updates werden seit 30 Tagen zurückgehalten. Core aktualisiert Steam, sobald du das nächste Mal nicht spielst.",
			"As atualizações do Steam estão em espera há 30 dias. O Core vai atualizar o Steam na próxima vez que você não estiver jogando.",
			"Оновлення Steam відкладено вже 30 днів. Core оновить Steam наступного разу, коли ви не граєте.",
			"Steam 更新已暂缓 30 天。Core 会在你下次不玩游戏时更新 Steam。",
			"Steam の更新は 30 日間保留されています。次にプレイしていないときに Core が Steam を更新します。",
			"Steam 업데이트가 30일 동안 보류되었습니다. 다음에 게임을 하지 않을 때 Core가 Steam을 업데이트합니다.",
			"Steam अपडेट 30 दिनों से रुके हुए हैं। अगली बार जब आप नहीं खेल रहे होंगे, Core Steam को अपडेट करेगा।",
			"Обновления Steam отложены уже 30 дней. Core обновит Steam в следующий раз, когда вы не играете.",
		},
		["notice.steamUpdating"] = new[]
		{
			"Aggiornamento di Steam in corso. Core torna tra un momento.",
			"Updating Steam. Core will be back in a moment.",
			"Actualizando Steam. Core volverá en un momento.",
			"Mise à jour de Steam en cours. Core revient dans un instant.",
			"Steam wird aktualisiert. Core ist gleich wieder da.",
			"Atualizando o Steam. O Core volta em instantes.",
			"Оновлення Steam. Core повернеться за мить.",
			"正在更新 Steam。Core 马上回来。",
			"Steam を更新しています。Core はすぐに戻ります。",
			"Steam을 업데이트하는 중입니다. Core가 곧 돌아옵니다.",
			"Steam अपडेट हो रहा है। Core थोड़ी देर में वापस आएगा।",
			"Обновление Steam. Core вернётся через мгновение.",
		},
		["notice.steamUpdatedPluginsBroken"] = new[]
		{
			"Steam è stato aggiornato, ma i plugin non sono tornati. Apri Core Repair nella tua libreria per riportare Steam alla versione precedente.",
			"Steam was updated, but plugins didn't come back. Open Core Repair in your library to roll Steam back.",
			"Steam se actualizó, pero los plugins no volvieron. Abre Core Repair en tu biblioteca para volver a la versión anterior de Steam.",
			"Steam a été mis à jour, mais les plugins ne sont pas revenus. Ouvre Core Repair dans ta bibliothèque pour revenir à la version précédente de Steam.",
			"Steam wurde aktualisiert, aber die Plugins sind nicht zurückgekommen. Öffne Core Repair in deiner Bibliothek, um Steam zurückzusetzen.",
			"O Steam foi atualizado, mas os plugins não voltaram. Abra o Core Repair na sua biblioteca para voltar à versão anterior do Steam.",
			"Steam оновлено, але плагіни не повернулися. Відкрийте Core Repair у бібліотеці, щоб повернути попередню версію Steam.",
			"Steam 已更新，但插件没有恢复。请在库中打开 Core Repair，将 Steam 回退到之前的版本。",
			"Steam は更新されましたが、プラグインが戻りませんでした。ライブラリで Core Repair を開いて Steam を元に戻してください。",
			"Steam이 업데이트되었지만 플러그인이 돌아오지 않았습니다. 라이브러리에서 Core Repair를 열어 Steam을 이전 버전으로 되돌리세요.",
			"Steam अपडेट हो गया, लेकिन प्लगइन वापस नहीं आए। Steam को पिछले संस्करण पर लौटाने के लिए अपनी लाइब्रेरी में Core Repair खोलें।",
			"Steam обновлён, но плагины не вернулись. Откройте Core Repair в библиотеке, чтобы вернуть прежнюю версию Steam.",
		},
		["repair.title"] = new[] { "Core Repair", "Core Repair", "Core Repair", "Core Repair", "Core Repair", "Core Repair", "Core Repair", "Core Repair", "Core Repair", "Core Repair", "Core Repair", "Core Repair" },
		["repair.subtitle"] = new[]
		{
			"I plugin non compaiono? Prova questi passaggi in ordine. Steam e i tuoi giochi funzionano comunque.",
			"Plugins not showing up? Try these steps in order. Steam and your games keep working either way.",
			"¿No aparecen los plugins? Prueba estos pasos en orden. Steam y tus juegos siguen funcionando de todos modos.",
			"Les plugins n'apparaissent pas ? Essaie ces étapes dans l'ordre. Steam et tes jeux fonctionnent quoi qu'il arrive.",
			"Plugins werden nicht angezeigt? Probiere diese Schritte der Reihe nach. Steam und deine Spiele funktionieren so oder so.",
			"Os plugins não aparecem? Tente estes passos na ordem. O Steam e seus jogos continuam funcionando de qualquer forma.",
			"Плагіни не з'являються? Спробуйте ці кроки по черзі. Steam та ігри працюють у будь-якому разі.",
			"插件没有出现？请按顺序尝试以下步骤。无论如何，Steam 和你的游戏都能正常运行。",
			"プラグインが表示されませんか？ 順番に試してください。Steam とゲームはどちらにしても動作します。",
			"플러그인이 보이지 않나요? 아래 단계를 순서대로 해 보세요. Steam과 게임은 어느 쪽이든 계속 작동합니다.",
			"प्लगइन नहीं दिख रहे? ये चरण क्रम से आज़माएँ। Steam और आपके गेम हर हाल में चलते रहेंगे।",
			"Плагины не появляются? Попробуйте эти шаги по порядку. Steam и игры работают в любом случае.",
		},
		["repair.restartPlugins"] = new[] { "Riavvia i plugin", "Restart plugins", "Reiniciar los plugins", "Redémarrer les plugins", "Plugins neu starten", "Reiniciar os plugins", "Перезапустити плагіни", "重启插件", "プラグインを再起動", "플러그인 다시 시작", "प्लगइन फिर से शुरू करें", "Перезапустить плагины" },
		["repair.reenableAccess"] = new[] { "Riattiva l'accesso dei plugin a Steam", "Turn Steam's plugin access back on", "Reactivar el acceso de los plugins a Steam", "Réactiver l'accès des plugins à Steam", "Plugin-Zugriff auf Steam wieder einschalten", "Reativar o acesso dos plugins ao Steam", "Знову ввімкнути доступ плагінів до Steam", "重新开启插件对 Steam 的访问", "Steam へのプラグインのアクセスを再び有効にする", "Steam 플러그인 접근 다시 켜기", "Steam में प्लगइन की पहुँच फिर से चालू करें", "Снова включить доступ плагинов к Steam" },
		["repair.updateDecky"] = new[] { "Aggiorna Decky", "Update Decky", "Actualizar Decky", "Mettre à jour Decky", "Decky aktualisieren", "Atualizar o Decky", "Оновити Decky", "更新 Decky", "Decky を更新", "Decky 업데이트", "Decky अपडेट करें", "Обновить Decky" },
		["repair.rollbackSteam"] = new[] { "Riporta Steam alla versione precedente", "Roll back Steam", "Volver a la versión anterior de Steam", "Revenir à la version précédente de Steam", "Steam zurücksetzen", "Voltar à versão anterior do Steam", "Повернути попередню версію Steam", "回退 Steam", "Steam を元に戻す", "Steam 이전 버전으로 되돌리기", "Steam को पिछले संस्करण पर लौटाएँ", "Вернуть прежнюю версию Steam" },
		["repair.updateSteam"] = new[] { "Aggiorna Steam ora", "Update Steam now", "Actualizar Steam ahora", "Mettre à jour Steam maintenant", "Steam jetzt aktualisieren", "Atualizar o Steam agora", "Оновити Steam зараз", "立即更新 Steam", "今すぐ Steam を更新", "지금 Steam 업데이트", "Steam अभी अपडेट करें", "Обновить Steam сейчас" },
		["repair.continue"] = new[] { "Continua senza plugin", "Continue without plugins", "Continuar sin plugins", "Continuer sans plugins", "Ohne Plugins weitermachen", "Continuar sem plugins", "Продовжити без плагінів", "不使用插件继续", "プラグインなしで続ける", "플러그인 없이 계속", "प्लगइन के बिना जारी रखें", "Продолжить без плагинов" },
		["repair.close"] = new[] { "Chiudi", "Close", "Cerrar", "Fermer", "Schließen", "Fechar", "Закрити", "关闭", "閉じる", "닫기", "बंद करें", "Закрыть" },
		["repair.hint"] = new[]
		{
			"A o Invio: scegli · B o Esc: chiudi",
			"A or Enter: choose · B or Esc: close",
			"A o Intro: elegir · B o Esc: cerrar",
			"A ou Entrée : choisir · B ou Échap : fermer",
			"A oder Eingabe: auswählen · B oder Esc: schließen",
			"A ou Enter: escolher · B ou Esc: fechar",
			"A або Enter: вибрати · B або Esc: закрити",
			"A 或 Enter：选择 · B 或 Esc：关闭",
			"A または Enter：選択 · B または Esc：閉じる",
			"A 또는 Enter: 선택 · B 또는 Esc: 닫기",
			"A या Enter: चुनें · B या Esc: बंद करें",
			"A или Enter: выбрать · B или Esc: закрыть",
		},
		["repair.working"] = new[] { "Ci sto lavorando…", "Working on it…", "Trabajando en ello…", "Je m'en occupe…", "Wird erledigt…", "Trabalhando nisso…", "Працюю над цим…", "正在处理…", "処理しています…", "처리하는 중…", "काम चल रहा है…", "Работаю над этим…" },
		["repair.fixed"] = new[] { "Risolto. I plugin sono tornati.", "Fixed. Plugins are back.", "Arreglado. Los plugins han vuelto.", "Réparé. Les plugins sont revenus.", "Behoben. Die Plugins sind zurück.", "Resolvido. Os plugins voltaram.", "Виправлено. Плагіни повернулися.", "已修复。插件已恢复。", "直りました。プラグインが戻りました。", "해결되었습니다. 플러그인이 돌아왔습니다.", "ठीक हो गया। प्लगइन वापस आ गए।", "Исправлено. Плагины вернулись." },
		["repair.stillBroken"] = new[] { "Non funziona ancora. Prova il passaggio successivo.", "Still not working. Try the next step.", "Todavía no funciona. Prueba el siguiente paso.", "Ça ne fonctionne toujours pas. Essaie l'étape suivante.", "Funktioniert noch nicht. Probiere den nächsten Schritt.", "Ainda não funciona. Tente o próximo passo.", "Досі не працює. Спробуйте наступний крок.", "仍然无法使用。请尝试下一步。", "まだ動作しません。次の手順を試してください。", "아직 작동하지 않습니다. 다음 단계를 해 보세요.", "अभी भी काम नहीं कर रहा। अगला चरण आज़माएँ।", "Всё ещё не работает. Попробуйте следующий шаг." },
		["repair.done"] = new[] { "Fatto.", "Done.", "Hecho.", "C'est fait.", "Erledigt.", "Pronto.", "Готово.", "完成。", "完了しました。", "완료되었습니다.", "हो गया।", "Готово." },
		["repair.failed"] = new[] { "Non è riuscito: {0}", "That didn't work: {0}", "No ha funcionado: {0}", "Ça n'a pas marché : {0}", "Das hat nicht geklappt: {0}", "Não funcionou: {0}", "Не вдалося: {0}", "操作未成功：{0}", "うまくいきませんでした：{0}", "실패했습니다: {0}", "यह काम नहीं किया: {0}", "Не получилось: {0}" },
		["repair.noBackup"] = new[] { "Non c'è ancora un backup di Steam a cui tornare.", "There is no Steam backup to roll back to yet.", "Todavía no hay una copia de Steam a la que volver.", "Il n'y a pas encore de sauvegarde de Steam à restaurer.", "Es gibt noch keine Steam-Sicherung zum Zurücksetzen.", "Ainda não há um backup do Steam para voltar.", "Ще немає резервної копії Steam для повернення.", "还没有可回退的 Steam 备份。", "戻せる Steam のバックアップがまだありません。", "되돌릴 Steam 백업이 아직 없습니다.", "लौटने के लिए अभी कोई Steam बैकअप नहीं है।", "Пока нет резервной копии Steam для возврата." },
		["repair.notWhilePlaying"] = new[] { "Prima chiudi il gioco.", "Close your game first.", "Cierra primero el juego.", "Ferme d'abord ton jeu.", "Schließe zuerst dein Spiel.", "Feche o jogo primeiro.", "Спершу закрийте гру.", "请先关闭游戏。", "先にゲームを閉じてください。", "먼저 게임을 닫으세요.", "पहले अपना गेम बंद करें।", "Сначала закройте игру." },
		["repair.agentDown"] = new[] { "Il servizio Gaming Mode di Core non è attivo. Riavvia il PC.", "Core's Gaming Mode service is not running. Restart the PC.", "El servicio Gaming Mode de Core no está en ejecución. Reinicia el PC.", "Le service Gaming Mode de Core ne fonctionne pas. Redémarre le PC.", "Der Gaming-Mode-Dienst von Core läuft nicht. Starte den PC neu.", "O serviço Gaming Mode do Core não está em execução. Reinicie o PC.", "Служба Gaming Mode у Core не працює. Перезавантажте ПК.", "Core 的 Gaming Mode 服务未运行。请重启电脑。", "Core の Gaming Mode サービスが動作していません。PC を再起動してください。", "Core의 Gaming Mode 서비스가 실행되고 있지 않습니다. PC를 다시 시작하세요.", "Core की Gaming Mode सेवा नहीं चल रही। PC फिर से शुरू करें।", "Служба Gaming Mode в Core не запущена. Перезагрузите ПК." },
		["repair.rollbackSuggested"] = new[]
		{
			"L'ultimo aggiornamento di Steam ha bloccato i plugin. Tornare alla versione precedente è la soluzione più rapida.",
			"The last Steam update broke plugins. Rolling Steam back is the quickest fix.",
			"La última actualización de Steam rompió los plugins. Volver a la versión anterior es la solución más rápida.",
			"La dernière mise à jour de Steam a cassé les plugins. Revenir à la version précédente est la solution la plus rapide.",
			"Das letzte Steam-Update hat die Plugins kaputt gemacht. Steam zurückzusetzen ist die schnellste Lösung.",
			"A última atualização do Steam quebrou os plugins. Voltar à versão anterior é a solução mais rápida.",
			"Останнє оновлення Steam зламало плагіни. Повернення попередньої версії — найшвидше рішення.",
			"上次 Steam 更新导致插件失效。回退 Steam 是最快的解决办法。",
			"前回の Steam 更新でプラグインが動かなくなりました。Steam を元に戻すのが最も早い解決策です。",
			"지난 Steam 업데이트로 플러그인이 작동하지 않습니다. Steam을 되돌리는 것이 가장 빠른 해결책입니다.",
			"पिछले Steam अपडेट से प्लगइन बिगड़ गए। Steam को पिछले संस्करण पर लौटाना सबसे तेज़ उपाय है।",
			"Последнее обновление Steam сломало плагины. Вернуть прежнюю версию — самое быстрое решение.",
		},
		["repair.continued"] = new[]
		{
			"I plugin restano spenti fino al prossimo aggiornamento di Steam. Core continua a funzionare.",
			"Plugins stay off until the next Steam update. Core keeps working.",
			"Los plugins seguirán desactivados hasta la próxima actualización de Steam. Core sigue funcionando.",
			"Les plugins restent désactivés jusqu'à la prochaine mise à jour de Steam. Core continue de fonctionner.",
			"Die Plugins bleiben bis zum nächsten Steam-Update aus. Core funktioniert weiter.",
			"Os plugins ficam desligados até a próxima atualização do Steam. O Core continua funcionando.",
			"Плагіни залишаться вимкненими до наступного оновлення Steam. Core працює далі.",
			"插件将保持关闭，直到下次 Steam 更新。Core 会继续正常运行。",
			"プラグインは次の Steam 更新までオフのままです。Core はそのまま動作します。",
			"플러그인은 다음 Steam 업데이트까지 꺼져 있습니다. Core는 계속 작동합니다.",
			"अगले Steam अपडेट तक प्लगइन बंद रहेंगे। Core चलता रहेगा।",
			"Плагины останутся выключенными до следующего обновления Steam. Core продолжает работать.",
		},
		["repair.steamUpdated"] = new[] { "Steam è aggiornato e i plugin funzionano.", "Steam is up to date and plugins work.", "Steam está actualizado y los plugins funcionan.", "Steam est à jour et les plugins fonctionnent.", "Steam ist aktuell und die Plugins funktionieren.", "O Steam está atualizado e os plugins funcionam.", "Steam оновлено, плагіни працюють.", "Steam 已是最新版本，插件运行正常。", "Steam は最新で、プラグインも動作しています。", "Steam이 최신 상태이며 플러그인이 작동합니다.", "Steam अपडेट है और प्लगइन काम कर रहे हैं।", "Steam обновлён, плагины работают." },
		// Core Health notices (MarkIIIHost.Health.cs).
		["health.backupFailing"] = new[]
		{
			"Il backup del server non funziona da almeno due notti. I dettagli sono in Accesso rapido > Core.",
			"The server backup has not worked for two nights or more. Details are in Quick Access > Core.",
			"La copia de seguridad del servidor lleva al menos dos noches sin funcionar. Los detalles están en Acceso rápido > Core.",
			"La sauvegarde du serveur ne fonctionne plus depuis au moins deux nuits. Les détails sont dans Accès rapide > Core.",
			"Das Server-Backup funktioniert seit mindestens zwei Nächten nicht. Details unter Schnellzugriff > Core.",
			"O backup do servidor não funciona há pelo menos duas noites. Os detalhes estão em Acesso rápido > Core.",
			"Резервне копіювання сервера не працює щонайменше дві ночі. Подробиці в Швидкому доступі > Core.",
			"服务器备份已连续两晚或更久未成功。详情见 快速访问 > Core。",
			"サーバーのバックアップが 2 晩以上続けて失敗しています。詳細はクイックアクセス > Core にあります。",
			"서버 백업이 이틀 밤 이상 실패했습니다. 자세한 내용은 빠른 실행 > Core에 있습니다.",
			"सर्वर का बैकअप दो या ज़्यादा रातों से नहीं चला है। जानकारी क्विक एक्सेस > Core में है।",
			"Резервное копирование сервера не работает уже две ночи или больше. Подробности в Быстром доступе > Core.",
		},
		["health.driveWarning"] = new[]
		{
			"Il disco dei giochi del server segnala un problema di salute. I dettagli sono in Accesso rapido > Core.",
			"The server's game drive reports a health problem. Details are in Quick Access > Core.",
			"El disco de juegos del servidor informa de un problema de estado. Los detalles están en Acceso rápido > Core.",
			"Le disque de jeux du serveur signale un problème de santé. Les détails sont dans Accès rapide > Core.",
			"Das Spiele-Laufwerk des Servers meldet ein Problem. Details unter Schnellzugriff > Core.",
			"O disco de jogos do servidor relata um problema de saúde. Os detalhes estão em Acesso rápido > Core.",
			"Диск з іграми на сервері повідомляє про проблему. Подробиці в Швидкому доступі > Core.",
			"服务器的游戏硬盘报告了健康问题。详情见 快速访问 > Core。",
			"サーバーのゲーム用ドライブが問題を報告しています。詳細はクイックアクセス > Core にあります。",
			"서버의 게임 드라이브에 상태 문제가 보고되었습니다. 자세한 내용은 빠른 실행 > Core에 있습니다.",
			"सर्वर की गेम ड्राइव में समस्या की सूचना है। जानकारी क्विक एक्सेस > Core में है।",
			"Диск с играми на сервере сообщает о проблеме. Подробности в Быстром доступе > Core.",
		},
		["health.statusStale"] = new[]
		{
			"Il server non dà notizie da più di un giorno e mezzo. I dettagli sono in Accesso rapido > Core.",
			"Core has not heard from the server for more than a day and a half. Details are in Quick Access > Core.",
			"Core no tiene noticias del servidor desde hace más de un día y medio. Los detalles están en Acceso rápido > Core.",
			"Core n'a pas de nouvelles du serveur depuis plus d'un jour et demi. Les détails sont dans Accès rapide > Core.",
			"Core hat seit über anderthalb Tagen nichts vom Server gehört. Details unter Schnellzugriff > Core.",
			"O Core não recebe notícias do servidor há mais de um dia e meio. Os detalhes estão em Acesso rápido > Core.",
			"Core не отримує вістей від сервера понад півтори доби. Подробиці в Швидкому доступі > Core.",
			"Core 已超过一天半没有收到服务器的消息。详情见 快速访问 > Core。",
			"Core は 1 日半以上サーバーから応答を受け取っていません。詳細はクイックアクセス > Core にあります。",
			"Core가 하루 반 이상 서버 소식을 받지 못했습니다. 자세한 내용은 빠른 실행 > Core에 있습니다.",
			"Core को डेढ़ दिन से ज़्यादा समय से सर्वर से कोई खबर नहीं मिली। जानकारी क्विक एक्सेस > Core में है।",
			"Core не получает вестей от сервера больше полутора суток. Подробности в Быстром доступе > Core.",
		},
		["health.several"] = new[]
		{
			"Alcune cose sul server richiedono attenzione. I dettagli sono in Accesso rapido > Core.",
			"A few things on the server need attention. Details are in Quick Access > Core.",
			"Algunas cosas del servidor necesitan atención. Los detalles están en Acceso rápido > Core.",
			"Quelques éléments du serveur demandent votre attention. Les détails sont dans Accès rapide > Core.",
			"Einige Dinge auf dem Server brauchen Aufmerksamkeit. Details unter Schnellzugriff > Core.",
			"Algumas coisas no servidor precisam de atenção. Os detalhes estão em Acesso rápido > Core.",
			"Кілька речей на сервері потребують уваги. Подробиці в Швидкому доступі > Core.",
			"服务器上有几项需要注意。详情见 快速访问 > Core。",
			"サーバーでいくつか確認が必要な点があります。詳細はクイックアクセス > Core にあります。",
			"서버에서 확인이 필요한 항목이 몇 가지 있습니다. 자세한 내용은 빠른 실행 > Core에 있습니다.",
			"सर्वर पर कुछ चीज़ों पर ध्यान देने की ज़रूरत है। जानकारी क्विक एक्सेस > Core में है।",
			"Несколько вещей на сервере требуют внимания. Подробности в Быстром доступе > Core.",
		},
	};
}
