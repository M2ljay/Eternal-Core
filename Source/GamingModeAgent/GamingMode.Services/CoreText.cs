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
		["notice.steamRolledBack.bigPicture"] = new[]
		{
			"Dopo l'aggiornamento Steam non ha aperto Big Picture, quindi Core ha rimesso la versione precedente. Gli aggiornamenti di Steam aspettano una correzione.",
			"After the update Steam didn't open Big Picture, so Core put the previous version back. Steam updates wait for a fix.",
			"Tras la actualización, Steam no abrió Big Picture, así que Core volvió a poner la versión anterior. Las actualizaciones de Steam esperan una corrección.",
			"Après la mise à jour, Steam n'a pas ouvert Big Picture : Core a remis la version précédente. Les mises à jour de Steam attendent un correctif.",
			"Nach dem Update hat Steam Big Picture nicht geöffnet, deshalb hat Core die vorherige Version zurückgeholt. Steam-Updates warten auf eine Korrektur.",
			"Depois da atualização o Steam não abriu o Big Picture, então o Core voltou à versão anterior. As atualizações do Steam esperam uma correção.",
			"Після оновлення Steam не відкрив Big Picture, тому Core повернув попередню версію. Оновлення Steam чекають на виправлення.",
			"更新后 Steam 没有打开大屏幕模式，因此 Core 已恢复之前的版本。Steam 更新将等待修复。",
			"更新後に Steam が Big Picture を開かなかったため、Core は前のバージョンに戻しました。Steam の更新は修正を待ちます。",
			"업데이트 후 Steam이 Big Picture를 열지 않아 Core가 이전 버전으로 되돌렸습니다. Steam 업데이트는 수정될 때까지 기다립니다.",
			"अपडेट के बाद Steam ने Big Picture नहीं खोला, इसलिए Core ने पिछला संस्करण वापस लगा दिया। Steam अपडेट सुधार का इंतज़ार करेंगे।",
			"После обновления Steam не открыл Big Picture, поэтому Core вернул прежнюю версию. Обновления Steam ждут исправления.",
		},
		["notice.steamRolledBack.deckyLoader"] = new[]
		{
			"Dopo l'aggiornamento di Steam il loader di Decky non rispondeva, quindi Core ha rimesso la versione precedente. Gli aggiornamenti di Steam aspettano una correzione.",
			"After the Steam update Decky's loader didn't answer, so Core put the previous version back. Steam updates wait for a fix.",
			"Tras la actualización de Steam, el cargador de Decky no respondía, así que Core volvió a poner la versión anterior. Las actualizaciones de Steam esperan una corrección.",
			"Après la mise à jour de Steam, le chargeur de Decky ne répondait pas : Core a remis la version précédente. Les mises à jour de Steam attendent un correctif.",
			"Nach dem Steam-Update hat Deckys Loader nicht geantwortet, deshalb hat Core die vorherige Version zurückgeholt. Steam-Updates warten auf eine Korrektur.",
			"Depois da atualização do Steam o carregador do Decky não respondeu, então o Core voltou à versão anterior. As atualizações do Steam esperam uma correção.",
			"Після оновлення Steam завантажувач Decky не відповідав, тому Core повернув попередню версію. Оновлення Steam чекають на виправлення.",
			"Steam 更新后 Decky 加载器没有响应，因此 Core 已恢复之前的版本。Steam 更新将等待修复。",
			"Steam の更新後に Decky のローダーが応答しなかったため、Core は前のバージョンに戻しました。Steam の更新は修正を待ちます。",
			"Steam 업데이트 후 Decky 로더가 응답하지 않아 Core가 이전 버전으로 되돌렸습니다. Steam 업데이트는 수정될 때까지 기다립니다.",
			"Steam अपडेट के बाद Decky का लोडर जवाब नहीं दे रहा था, इसलिए Core ने पिछला संस्करण वापस लगा दिया। Steam अपडेट सुधार का इंतज़ार करेंगे।",
			"После обновления Steam загрузчик Decky не отвечал, поэтому Core вернул прежнюю версию. Обновления Steam ждут исправления.",
		},
		["notice.steamRolledBack.plugins"] = new[]
		{
			"Dopo l'aggiornamento di Steam alcuni plugin non si sono caricati, quindi Core ha rimesso la versione precedente. Gli aggiornamenti di Steam aspettano una correzione.",
			"After the Steam update some plugins didn't load, so Core put the previous version back. Steam updates wait for a fix.",
			"Tras la actualización de Steam algunos plugins no se cargaron, así que Core volvió a poner la versión anterior. Las actualizaciones de Steam esperan una corrección.",
			"Après la mise à jour de Steam, certains plugins ne se sont pas chargés : Core a remis la version précédente. Les mises à jour de Steam attendent un correctif.",
			"Nach dem Steam-Update wurden einige Plugins nicht geladen, deshalb hat Core die vorherige Version zurückgeholt. Steam-Updates warten auf eine Korrektur.",
			"Depois da atualização do Steam alguns plugins não carregaram, então o Core voltou à versão anterior. As atualizações do Steam esperam uma correção.",
			"Після оновлення Steam деякі плагіни не завантажилися, тому Core повернув попередню версію. Оновлення Steam чекають на виправлення.",
			"Steam 更新后部分插件没有加载，因此 Core 已恢复之前的版本。Steam 更新将等待修复。",
			"Steam の更新後に一部のプラグインが読み込まれなかったため、Core は前のバージョンに戻しました。Steam の更新は修正を待ちます。",
			"Steam 업데이트 후 일부 플러그인이 로드되지 않아 Core가 이전 버전으로 되돌렸습니다. Steam 업데이트는 수정될 때까지 기다립니다.",
			"Steam अपडेट के बाद कुछ प्लगइन लोड नहीं हुए, इसलिए Core ने पिछला संस्करण वापस लगा दिया। Steam अपडेट सुधार का इंतज़ार करेंगे।",
			"После обновления Steam некоторые плагины не загрузились, поэтому Core вернул прежнюю версию. Обновления Steam ждут исправления.",
		},
		["notice.steamRolledBack.corePlugin"] = new[]
		{
			"Dopo l'aggiornamento di Steam il plugin di Core non rispondeva, quindi Core ha rimesso la versione precedente. Gli aggiornamenti di Steam aspettano una correzione.",
			"After the Steam update Core's plugin didn't answer, so Core put the previous version back. Steam updates wait for a fix.",
			"Tras la actualización de Steam el plugin de Core no respondía, así que Core volvió a poner la versión anterior. Las actualizaciones de Steam esperan una corrección.",
			"Après la mise à jour de Steam, le plugin de Core ne répondait pas : Core a remis la version précédente. Les mises à jour de Steam attendent un correctif.",
			"Nach dem Steam-Update hat das Core-Plugin nicht geantwortet, deshalb hat Core die vorherige Version zurückgeholt. Steam-Updates warten auf eine Korrektur.",
			"Depois da atualização do Steam o plugin do Core não respondeu, então o Core voltou à versão anterior. As atualizações do Steam esperam uma correção.",
			"Після оновлення Steam плагін Core не відповідав, тому Core повернув попередню версію. Оновлення Steam чекають на виправлення.",
			"Steam 更新后 Core 插件没有响应，因此 Core 已恢复之前的版本。Steam 更新将等待修复。",
			"Steam の更新後に Core のプラグインが応答しなかったため、Core は前のバージョンに戻しました。Steam の更新は修正を待ちます。",
			"Steam 업데이트 후 Core 플러그인이 응답하지 않아 Core가 이전 버전으로 되돌렸습니다. Steam 업데이트는 수정될 때까지 기다립니다.",
			"Steam अपडेट के बाद Core का प्लगइन जवाब नहीं दे रहा था, इसलिए Core ने पिछला संस्करण वापस लगा दिया। Steam अपडेट सुधार का इंतज़ार करेंगे।",
			"После обновления Steam плагин Core не отвечал, поэтому Core вернул прежнюю версию. Обновления Steam ждут исправления.",
		},
		["notice.steamRollbackFailed"] = new[]
		{
			"L'aggiornamento di Steam non ha superato il controllo e Core non è riuscito a rimettere la versione precedente. Apri Core Repair nella tua libreria per riportare indietro Steam.",
			"The Steam update failed its check and Core couldn't put the previous version back. Open Core Repair in your library to roll Steam back.",
			"La actualización de Steam no pasó la comprobación y Core no pudo volver a poner la versión anterior. Abre Core Repair en tu biblioteca para volver a la versión anterior de Steam.",
			"La mise à jour de Steam n'a pas passé la vérification et Core n'a pas pu remettre la version précédente. Ouvre Core Repair dans ta bibliothèque pour revenir à la version précédente de Steam.",
			"Das Steam-Update hat die Prüfung nicht bestanden, und Core konnte die vorherige Version nicht zurückholen. Öffne Core Repair in deiner Bibliothek, um Steam zurückzusetzen.",
			"A atualização do Steam não passou na verificação e o Core não conseguiu voltar à versão anterior. Abra o Core Repair na sua biblioteca para voltar à versão anterior do Steam.",
			"Оновлення Steam не пройшло перевірку, і Core не зміг повернути попередню версію. Відкрийте Core Repair у бібліотеці, щоб повернути попередню версію Steam.",
			"Steam 更新未通过检查，Core 也未能恢复之前的版本。请在库中打开 Core Repair，将 Steam 回退到之前的版本。",
			"Steam の更新はチェックに通らず、Core は前のバージョンに戻せませんでした。ライブラリで Core Repair を開いて Steam を元に戻してください。",
			"Steam 업데이트가 확인을 통과하지 못했고 Core가 이전 버전으로 되돌리지 못했습니다. 라이브러리에서 Core Repair를 열어 Steam을 이전 버전으로 되돌리세요.",
			"Steam अपडेट जाँच में पास नहीं हुआ और Core पिछला संस्करण वापस नहीं लगा सका। Steam को पिछले संस्करण पर लौटाने के लिए अपनी लाइब्रेरी में Core Repair खोलें।",
			"Обновление Steam не прошло проверку, и Core не смог вернуть прежнюю версию. Откройте Core Repair в библиотеке, чтобы вернуть прежнюю версию Steam.",
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
		["repair.confirmCantTell"] = new[]
		{
			"Core non sa se il nuovo Steam funziona con Decky. Premi di nuovo per aggiornare comunque: Core salva prima una copia e torna indietro se qualcosa non va.",
			"Core can't tell whether the new Steam works with Decky. Press again to update anyway: Core saves a copy first and rolls back if something goes wrong.",
			"Core no sabe si el nuevo Steam funciona con Decky. Pulsa de nuevo para actualizar igualmente: Core guarda antes una copia y vuelve atrás si algo falla.",
			"Core ne sait pas si le nouveau Steam fonctionne avec Decky. Appuie de nouveau pour mettre à jour quand même : Core fait d'abord une copie et revient en arrière si quelque chose ne va pas.",
			"Core weiß nicht, ob das neue Steam mit Decky funktioniert. Drücke erneut, um trotzdem zu aktualisieren: Core sichert vorher und setzt zurück, wenn etwas schiefgeht.",
			"O Core não sabe se o novo Steam funciona com o Decky. Pressione de novo para atualizar mesmo assim: o Core salva uma cópia antes e volta atrás se algo der errado.",
			"Core не знає, чи працює новий Steam з Decky. Натисніть ще раз, щоб усе одно оновити: Core спершу збереже копію й поверне попередню версію, якщо щось піде не так.",
			"Core 无法判断新版 Steam 是否能与 Decky 配合使用。再按一次即可仍然更新：Core 会先备份，出问题时自动回退。",
			"新しい Steam が Decky で動くかどうか Core にはわかりません。それでも更新するにはもう一度押してください。Core は先にコピーを保存し、問題があれば元に戻します。",
			"Core는 새 Steam이 Decky와 작동하는지 알 수 없습니다. 그래도 업데이트하려면 다시 누르세요. Core가 먼저 사본을 저장하고 문제가 생기면 되돌립니다.",
			"Core नहीं बता सकता कि नया Steam Decky के साथ काम करता है या नहीं। फिर भी अपडेट करने के लिए दोबारा दबाएँ: Core पहले एक कॉपी सहेजता है और कुछ गलत हो तो वापस लौट जाता है।",
			"Core не знает, работает ли новый Steam с Decky. Нажмите ещё раз, чтобы всё равно обновить: Core сначала сохранит копию и вернёт прежнюю версию, если что-то пойдёт не так.",
		},
		["repair.confirmIncompatible"] = new[]
		{
			"Il nuovo Steam non funziona con il Decky installato: i plugin smetteranno di caricarsi. Premi di nuovo se vuoi aggiornare lo stesso.",
			"The new Steam doesn't work with the installed Decky: plugins will stop loading. Press again if you want to update anyway.",
			"El nuevo Steam no funciona con el Decky instalado: los plugins dejarán de cargarse. Pulsa de nuevo si quieres actualizar igualmente.",
			"Le nouveau Steam ne fonctionne pas avec le Decky installé : les plugins ne se chargeront plus. Appuie de nouveau si tu veux mettre à jour quand même.",
			"Das neue Steam funktioniert nicht mit dem installierten Decky: Plugins werden nicht mehr geladen. Drücke erneut, wenn du trotzdem aktualisieren willst.",
			"O novo Steam não funciona com o Decky instalado: os plugins vão parar de carregar. Pressione de novo se quiser atualizar mesmo assim.",
			"Новий Steam не працює з установленим Decky: плагіни перестануть завантажуватися. Натисніть ще раз, якщо все одно хочете оновити.",
			"新版 Steam 无法与已安装的 Decky 配合使用：插件将无法加载。如果仍要更新，请再按一次。",
			"新しい Steam はインストール済みの Decky では動作せず、プラグインが読み込まれなくなります。それでも更新する場合はもう一度押してください。",
			"새 Steam은 설치된 Decky와 작동하지 않아 플러그인이 로드되지 않습니다. 그래도 업데이트하려면 다시 누르세요.",
			"नया Steam इंस्टॉल किए गए Decky के साथ काम नहीं करता: प्लगइन लोड होना बंद हो जाएँगे। फिर भी अपडेट करना हो तो दोबारा दबाएँ।",
			"Новый Steam не работает с установленным Decky: плагины перестанут загружаться. Нажмите ещё раз, если всё равно хотите обновить.",
		},
		["repair.confirmIncompatibleAgain"] = new[]
		{
			"Ultima conferma: premi ancora una volta per aggiornare Steam sapendo che i plugin non funzioneranno.",
			"Last confirmation: press once more to update Steam knowing that plugins won't work.",
			"Última confirmación: pulsa una vez más para actualizar Steam sabiendo que los plugins no funcionarán.",
			"Dernière confirmation : appuie encore une fois pour mettre Steam à jour en sachant que les plugins ne fonctionneront pas.",
			"Letzte Bestätigung: Drücke noch einmal, um Steam zu aktualisieren, obwohl die Plugins dann nicht funktionieren.",
			"Última confirmação: pressione mais uma vez para atualizar o Steam sabendo que os plugins não vão funcionar.",
			"Останнє підтвердження: натисніть ще раз, щоб оновити Steam, знаючи, що плагіни не працюватимуть.",
			"最后确认：再按一次即会更新 Steam，但插件将无法使用。",
			"最終確認：プラグインが動かなくなることを承知で Steam を更新するには、もう一度押してください。",
			"마지막 확인: 플러그인이 작동하지 않는다는 것을 알고도 Steam을 업데이트하려면 한 번 더 누르세요.",
			"आख़िरी पुष्टि: यह जानते हुए कि प्लगइन काम नहीं करेंगे, Steam अपडेट करने के लिए एक बार और दबाएँ।",
			"Последнее подтверждение: нажмите ещё раз, чтобы обновить Steam, зная, что плагины не будут работать.",
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
		["health.deckyErrorsOne"] = new[]
		{
			"Decky: {0} sta segnalando errori. I dettagli sono in Accesso rapido > Core.",
			"Decky: {0} is reporting errors. Details are in Quick Access > Core.",
			"Decky: {0} está informando de errores. Los detalles están en Acceso rápido > Core.",
			"Decky : {0} signale des erreurs. Les détails sont dans Accès rapide > Core.",
			"Decky: {0} meldet Fehler. Details unter Schnellzugriff > Core.",
			"Decky: {0} está relatando erros. Os detalhes estão em Acesso rápido > Core.",
			"Decky: {0} повідомляє про помилки. Подробиці в Швидкому доступі > Core.",
			"Decky：{0} 正在报告错误。详情见 快速访问 > Core。",
			"Decky: {0} がエラーを報告しています。詳細はクイックアクセス > Core にあります。",
			"Decky: {0}에서 오류를 보고하고 있습니다. 자세한 내용은 빠른 실행 > Core에 있습니다.",
			"Decky: {0} त्रुटियाँ बता रहा है। जानकारी क्विक एक्सेस > Core में है।",
			"Decky: {0} сообщает об ошибках. Подробности в Быстром доступе > Core.",
		},
		["health.deckyErrorsMany"] = new[]
		{
			"Decky: {0} stanno segnalando errori. I dettagli sono in Accesso rapido > Core.",
			"Decky: {0} are reporting errors. Details are in Quick Access > Core.",
			"Decky: {0} están informando de errores. Los detalles están en Acceso rápido > Core.",
			"Decky : {0} signalent des erreurs. Les détails sont dans Accès rapide > Core.",
			"Decky: {0} melden Fehler. Details unter Schnellzugriff > Core.",
			"Decky: {0} estão relatando erros. Os detalhes estão em Acesso rápido > Core.",
			"Decky: {0} повідомляють про помилки. Подробиці в Швидкому доступі > Core.",
			"Decky：{0} 正在报告错误。详情见 快速访问 > Core。",
			"Decky: {0} がエラーを報告しています。詳細はクイックアクセス > Core にあります。",
			"Decky: {0}에서 오류를 보고하고 있습니다. 자세한 내용은 빠른 실행 > Core에 있습니다.",
			"Decky: {0} त्रुटियाँ बता रहे हैं। जानकारी क्विक एक्सेस > Core में है।",
			"Decky: {0} сообщают об ошибках. Подробности в Быстром доступе > Core.",
		},
		["notice.pluginsErrors"] = new[]
		{
			"Errori segnalati da: {0}.",
			"Errors reported by: {0}.",
			"Errores informados por: {0}.",
			"Erreurs signalées par : {0}.",
			"Fehler gemeldet von: {0}.",
			"Erros relatados por: {0}.",
			"Помилки повідомляє: {0}.",
			"报告错误的插件：{0}。",
			"エラーを報告したプラグイン: {0}。",
			"오류를 보고한 플러그인: {0}.",
			"त्रुटियाँ बताने वाले प्लगइन: {0}।",
			"Об ошибках сообщает: {0}.",
		},
		["winddown.save"] = new[]
		{
			"Sono le {0}: buon momento per salvare.",
			"It's {0} — good moment to save.",
			"Son las {0}: buen momento para guardar.",
			"Il est {0} : bon moment pour sauvegarder.",
			"Es ist {0} – guter Moment zum Speichern.",
			"São {0}: bom momento para salvar.",
			"Уже {0} — слушний момент, щоб зберегтися.",
			"现在是 {0}，正是存档的好时机。",
			"{0} です。セーブするのにちょうどいいタイミングです。",
			"{0}입니다. 저장하기 좋은 때예요.",
			"{0} बज गए हैं — सेव करने का अच्छा समय है।",
			"Уже {0} — хороший момент, чтобы сохраниться.",
		},
		// Batch 2: sleep after streaming, one minute before the PC sleeps ({0} = minutes left). A Steam toast.
		["streamsleep.soon"] = new[]
		{
			"Nessuno sta giocando: Core va in sospensione tra {0} min. Premi un tasto per tenerlo sveglio.",
			"Nobody is playing: Core goes to sleep in {0} min. Press any button to keep it awake.",
			"Nadie está jugando: Core entrará en suspensión en {0} min. Pulsa un botón para mantenerlo activo.",
			"Personne ne joue : Core se met en veille dans {0} min. Appuie sur un bouton pour le garder éveillé.",
			"Niemand spielt: Core geht in {0} Min. in den Standby. Drück eine Taste, damit er wach bleibt.",
			"Ninguém está jogando: o Core entra em suspensão em {0} min. Aperte um botão para mantê-lo ativo.",
			"Ніхто не грає: Core засне через {0} хв. Натисніть будь-яку кнопку, щоб він не засинав.",
			"没有人在玩：Core 将在 {0} 分钟后睡眠。按任意键保持唤醒。",
			"誰もプレイしていません。Core は {0} 分後にスリープします。起こしておくにはボタンを押してください。",
			"아무도 플레이하지 않아요. Core가 {0}분 후 절전 모드로 전환됩니다. 깨어 있게 하려면 아무 버튼이나 누르세요.",
			"कोई नहीं खेल रहा: Core {0} मिनट में स्लीप में जाएगा। जगाए रखने के लिए कोई भी बटन दबाएँ।",
			"Никто не играет: Core уснёт через {0} мин. Нажмите любую кнопку, чтобы он не засыпал.",
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
		// Console guards (ConsoleGuards.cs, MarkIIIHost.Console.cs).
		["console.displayChanged"] = new[]
		{
			"Le impostazioni dello schermo non sono più quelle normali. Ripristinale da Accesso rapido > Core.",
			"The display settings are no longer the ones you saved as normal. Restore them from Quick Access > Core.",
			"La configuración de pantalla ya no es la que guardaste como normal. Restáurala desde Acceso rápido > Core.",
			"Les réglages d'affichage ne sont plus ceux enregistrés comme normaux. Restaurez-les depuis Accès rapide > Core.",
			"Die Anzeigeeinstellungen sind nicht mehr die als normal gespeicherten. Stelle sie unter Schnellzugriff > Core wieder her.",
			"As configurações de tela não são mais as salvas como normais. Restaure-as em Acesso rápido > Core.",
			"Налаштування екрана вже не ті, що збережені як звичайні. Відновіть їх у Швидкому доступі > Core.",
			"显示设置已不是你保存的常用设置。可在 快速访问 > Core 中恢复。",
			"画面の設定が、通常として保存したものと変わっています。クイックアクセス > Core から元に戻せます。",
			"디스플레이 설정이 기본으로 저장한 설정과 다릅니다. 빠른 실행 > Core에서 복원하세요.",
			"डिस्प्ले सेटिंग्स अब वे नहीं हैं जिन्हें आपने सामान्य के रूप में सेव किया था। क्विक एक्सेस > Core से उन्हें वापस लाएँ।",
			"Настройки экрана уже не те, что сохранены как обычные. Восстановите их в Быстром доступе > Core.",
		},
		["console.soundChanged"] = new[]
		{
			"Il suono non esce più dal dispositivo scelto. Riportalo indietro da Accesso rapido > Core.",
			"Sound no longer plays on the device you chose. Switch it back from Quick Access > Core.",
			"El sonido ya no sale por el dispositivo que elegiste. Vuelve a él desde Acceso rápido > Core.",
			"Le son ne sort plus sur l'appareil choisi. Revenez-y depuis Accès rapide > Core.",
			"Der Ton kommt nicht mehr über das gewählte Gerät. Wechsle unter Schnellzugriff > Core zurück.",
			"O som não sai mais pelo dispositivo escolhido. Volte para ele em Acesso rápido > Core.",
			"Звук більше не йде на вибраний пристрій. Поверніть його у Швидкому доступі > Core.",
			"声音已不再从你选择的设备输出。可在 快速访问 > Core 中切换回来。",
			"選んだデバイスから音が出なくなっています。クイックアクセス > Core から戻せます。",
			"선택한 장치로 소리가 나오지 않습니다. 빠른 실행 > Core에서 되돌리세요.",
			"आवाज़ अब आपके चुने डिवाइस पर नहीं चल रही। क्विक एक्सेस > Core से वापस बदलें।",
			"Звук больше не идёт на выбранное устройство. Верните его в Быстром доступе > Core.",
		},
		["console.autoSignIn"] = new[]
		{
			"L'accesso automatico a Windows non è più configurato: al prossimo avvio Windows chiederà la password. Rifallo dal desktop.",
			"Automatic Windows sign-in is no longer set up: Windows will ask for the password at the next start. Set it up again from the desktop.",
			"El inicio de sesión automático de Windows ya no está configurado: Windows pedirá la contraseña al próximo arranque. Vuelve a configurarlo desde el escritorio.",
			"La connexion automatique à Windows n'est plus configurée : Windows demandera le mot de passe au prochain démarrage. Reconfigurez-la depuis le bureau.",
			"Die automatische Windows-Anmeldung ist nicht mehr eingerichtet: Beim nächsten Start fragt Windows nach dem Passwort. Richte sie am Desktop neu ein.",
			"O login automático do Windows não está mais configurado: o Windows pedirá a senha na próxima inicialização. Configure de novo pela área de trabalho.",
			"Автоматичний вхід у Windows більше не налаштовано: під час наступного запуску Windows попросить пароль. Налаштуйте його знову на робочому столі.",
			"Windows 自动登录已不再设置：下次启动时 Windows 会要求输入密码。请在桌面上重新设置。",
			"Windows の自動サインインが設定されていません。次回の起動時にパスワードを求められます。デスクトップで設定し直してください。",
			"Windows 자동 로그인이 더 이상 설정되어 있지 않습니다. 다음 시작 때 암호를 묻습니다. 데스크톱에서 다시 설정하세요.",
			"Windows का अपने-आप साइन-इन अब सेट नहीं है: अगली बार शुरू होने पर Windows पासवर्ड माँगेगा। डेस्कटॉप से इसे फिर से सेट करें।",
			"Автоматический вход в Windows больше не настроен: при следующем запуске Windows спросит пароль. Настройте его снова на рабочем столе.",
		},
		["console.bitlockerFirmware"] = new[]
		{
			"Un aggiornamento del firmware aspetta il riavvio e BitLocker è attivo: al riavvio potrebbe chiedere la chiave di ripristino. I dettagli sono in Accesso rapido > Core.",
			"A firmware update is waiting for the restart and BitLocker is on: the restart may ask for the recovery key. Details are in Quick Access > Core.",
			"Una actualización de firmware espera al reinicio y BitLocker está activado: el reinicio podría pedir la clave de recuperación. Los detalles están en Acceso rápido > Core.",
			"Une mise à jour du firmware attend le redémarrage et BitLocker est actif : le redémarrage pourrait demander la clé de récupération. Les détails sont dans Accès rapide > Core.",
			"Ein Firmware-Update wartet auf den Neustart und BitLocker ist an: Der Neustart könnte nach dem Wiederherstellungsschlüssel fragen. Details unter Schnellzugriff > Core.",
			"Uma atualização de firmware aguarda o reinício e o BitLocker está ativo: o reinício pode pedir a chave de recuperação. Os detalhes estão em Acesso rápido > Core.",
			"Оновлення прошивки чекає перезапуску, а BitLocker увімкнено: під час перезапуску може знадобитися ключ відновлення. Подробиці в Швидкому доступі > Core.",
			"有固件更新在等待重启，且 BitLocker 已开启：重启时可能会要求恢复密钥。详情见 快速访问 > Core。",
			"ファームウェア更新が再起動待ちで、BitLocker がオンです。再起動時に回復キーを求められる可能性があります。詳細はクイックアクセス > Core にあります。",
			"펌웨어 업데이트가 재시작을 기다리고 있고 BitLocker가 켜져 있습니다. 재시작 시 복구 키를 물을 수 있습니다. 자세한 내용은 빠른 실행 > Core에 있습니다.",
			"एक फ़र्मवेयर अपडेट रीस्टार्ट का इंतज़ार कर रहा है और BitLocker चालू है: रीस्टार्ट पर रिकवरी कुंजी माँगी जा सकती है। जानकारी क्विक एक्सेस > Core में है।",
			"Обновление прошивки ждёт перезагрузки, а BitLocker включён: при перезагрузке может понадобиться ключ восстановления. Подробности в Быстром доступе > Core.",
		},
		["console.network"] = new[]
		{
			"Il cavo di rete del PC non risulta collegato o la rete non risponde. I dettagli sono in Accesso rapido > Core.",
			"The PC's network cable seems unplugged or the network is down. Details are in Quick Access > Core.",
			"El cable de red del PC parece desconectado o la red no funciona. Los detalles están en Acceso rápido > Core.",
			"Le câble réseau du PC semble débranché ou le réseau est coupé. Les détails sont dans Accès rapide > Core.",
			"Das Netzwerkkabel des PCs scheint getrennt oder das Netzwerk ist ausgefallen. Details unter Schnellzugriff > Core.",
			"O cabo de rede do PC parece desconectado ou a rede caiu. Os detalhes estão em Acesso rápido > Core.",
			"Мережевий кабель ПК, схоже, від'єднано, або мережа не працює. Подробиці в Швидкому доступі > Core.",
			"电脑的网线似乎未连接，或网络已断开。详情见 快速访问 > Core。",
			"PC の LAN ケーブルが抜けているか、ネットワークが落ちているようです。詳細はクイックアクセス > Core にあります。",
			"PC의 네트워크 케이블이 빠졌거나 네트워크가 끊긴 것 같습니다. 자세한 내용은 빠른 실행 > Core에 있습니다.",
			"PC का नेटवर्क केबल निकला हुआ लगता है या नेटवर्क बंद है। जानकारी क्विक एक्सेस > Core में है।",
			"Сетевой кабель ПК, похоже, отключён, или сеть не работает. Подробности в Быстром доступе > Core.",
		},
		["console.several"] = new[]
		{
			"Alcune cose richiedono attenzione. I dettagli sono in Accesso rapido > Core.",
			"A few things need attention. Details are in Quick Access > Core.",
			"Algunas cosas necesitan atención. Los detalles están en Acceso rápido > Core.",
			"Quelques éléments demandent votre attention. Les détails sont dans Accès rapide > Core.",
			"Einige Dinge brauchen Aufmerksamkeit. Details unter Schnellzugriff > Core.",
			"Algumas coisas precisam de atenção. Os detalhes estão em Acesso rápido > Core.",
			"Кілька речей потребують уваги. Подробиці в Швидкому доступі > Core.",
			"有几项需要注意。详情见 快速访问 > Core。",
			"いくつか確認が必要な点があります。詳細はクイックアクセス > Core にあります。",
			"확인이 필요한 항목이 몇 가지 있습니다. 자세한 내용은 빠른 실행 > Core에 있습니다.",
			"कुछ चीज़ों पर ध्यान देने की ज़रूरत है। जानकारी क्विक एक्सेस > Core में है।",
			"Несколько вещей требуют внимания. Подробности в Быстром доступе > Core.",
		},
		// Jarvis (JARVIS.md): the small indicator Core shows over the screen while push-to-talk records, and every time the
		// screen helper takes a screenshot.
		["jarvis.listening"] = new[]
		{
			"Jarvis ascolta", "Jarvis is listening", "Jarvis escucha", "Jarvis écoute", "Jarvis hört zu", "Jarvis está ouvindo",
			"Jarvis слухає", "Jarvis 正在聆听", "Jarvis が聞いています", "Jarvis가 듣고 있어요", "Jarvis सुन रहा है", "Jarvis слушает",
		},
		["jarvis.screenshot"] = new[]
		{
			"Screenshot per Jarvis", "Screenshot taken for Jarvis", "Captura para Jarvis", "Capture d'écran pour Jarvis", "Screenshot für Jarvis", "Captura de tela para o Jarvis",
			"Знімок екрана для Jarvis", "已为 Jarvis 截图", "Jarvis 用にスクリーンショットを撮りました", "Jarvis용 스크린샷을 찍었어요", "Jarvis के लिए स्क्रीनशॉट लिया गया", "Снимок экрана для Jarvis",
		},
	};
}
