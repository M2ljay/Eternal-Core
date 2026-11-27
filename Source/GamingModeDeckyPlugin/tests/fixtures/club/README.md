# Play Retro Games Club page fixtures (captured 2026-11-27 ~22:00 UTC)

Captured by Jarvis in the user's desktop browser from https://www.playretrogamesclub.com (/, /nominations/,
/leaderboards/). The live pages are larger; these are trimmed but keep the real markup, class names and
attributes verbatim (rows cut to a few per table, scripts/styles/svg/header/footer removed, whitespace collapsed).
Site generator puts data in attributes: rows carry data-name, data-sys, data-s-order, data-s-hours, data-s-name;
leaderboard rows data-s-total/-hardcore/-completed/-mastered/-firsts/-firsthundred/-hours; times are <time datetime>.
Event classes: w=Game of the Week, m=Game of the Month, r=Romhack of the Month, s=Game of the Season, x=Specials.
Nominations live in <section class="run m|r|s" id="gotm|rhotm|gots" data-tab-panel>, heading <h2> (e.g. "December 2026"),
"Vote opens <time>", count in .stat b.big + "nominated", rows in table#nomt-<event> tbody.
Home: four <a class="room w|m|r|s"> cards (tag, h3 title, .plat "NES · about 2.8 hours", .facts beaten/at 100%/rating,
.first lines, .ends <time> + .countdown); "Coming up" ol.cal-strip; "Nominations" .noms ul.list li (.tag + .num);
"Top players" ol.list.

In this repository: home.html, nominations.html and leaderboards.html are those captures, unchanged. member.html is
fictional (no member page was captured): it tests the text-based member parser only.
