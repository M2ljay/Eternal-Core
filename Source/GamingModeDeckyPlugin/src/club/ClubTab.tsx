import { SP_REACT as React, DFL } from "../decky";
import { CORE, coreAlpha } from "../corePalette";
import { CoreStars } from "../CoreStars";
import { clubCopy, daysLeftText, fill, type ClubCopy } from "./clubLocale";
import { formatClubDate, timeLeft } from "./clubTime";
import { matchPick, type LibraryEntry, type Match } from "./clubMatch";
import { openGamePage, playApp, readLibrary, steamArtFor } from "./clubLibrary";
import { clubState, loadClub, subscribeClub, type ClubMember, type ClubNomination, type ClubPick, type ClubRow, type ClubScheduleItem } from "./clubStore";
import { CORE_FAMILY_TONES } from "../coreLibraryIcons";
import { CLUB_COMPLETION_COMMAND, assembleBridge, assembleEventFor, openClubCompletion, subscribeAssemble } from "./clubAssemble";

// Eternal Core: the Club tab on Core's Home: Play Retro Games Club's current picks (matched to the Steam library, A plays),
// Log completion (X: with Eternal-Assemble loaded, the pick's Discord thread opens with /completed ready) and Open in
// Library (Y), the user's standing with the top three, what is coming up, and nominations.
// Core palette: black with the Starfield, gunmetal cards (10 px radius), focus lifts, steel text.

const { Focusable, DialogButton, Navigation } = DFL as any;

// 3.3.8 (the user's call, TV pass): a pick not in the library shows the club's own box art (the home page's
// /img/<id>.png, read by the parser). Steam's artwork still wins for a matched game; the platform badge is the fallback
// when there is no image or it fails to load.
export const CLUB_SITE_ART_ALLOWED = true;
const CLUB_ART = /^https:\/\/www\.playretrogamesclub\.com\/img\/\d{1,9}\.(?:png|jpe?g|webp)$/;

// The club's image only from the club's own /img/ path (never another host).
export function clubArtUrl(pick: { image?: string; cover?: string }): string {
  if (typeof pick.image === "string" && CLUB_ART.test(pick.image)) return pick.image;
  const path = typeof pick.cover === "string" ? `https://www.playretrogamesclub.com${pick.cover}` : "";
  return CLUB_ART.test(path) ? path : "";
}

// 3.3.10 (the approved board, HomeClub): four tall full-width pick cards (box art filling each card, the details over a
// bottom fade: the event tag in the pick's colour, title, platform and length, days left and beaten count, the library
// chip), then Log completion (X) and Open in Library (Y) acting on the focused pick, then three equal boxes: Your
// standing, Coming up, Nominations. No refresh button and no "saved data" line: it refreshes itself hourly. Focus starts
// on the first pick; left and right move across the picks and across the boxes; down goes picks, buttons, boxes. Every
// box is focusable. Focus lifts; no outline.
const PICK_TONE: Record<string, string> = { gotw: CORE_FAMILY_TONES.retro, gotm: CORE_FAMILY_TONES.nintendo, rhotm: CORE_FAMILY_TONES.sega, gots: CORE_FAMILY_TONES.playstation };
export const pickTone = (pick: { kind?: string; event?: string }) => PICK_TONE[assembleEventFor(pick) ?? ""] ?? CORE.crimsonText;
// Board sizes (1920 px) scaled to the view: cards 560 px tall (about 393 at the TV's 758 px), boxes 250 (about 176).
export const CLUB_LAYOUT = { cardVh: 51.9, boxVh: 23.1, gapVw: 1.04 } as const;

const css = `
.ph-club{position:relative;isolation:isolate;min-height:100%;padding:54px 2px 40px;color:${CORE.text};box-sizing:border-box}
.ph-club h3{margin:0 0 10px;font-size:11px;font-weight:600;letter-spacing:.14em;text-transform:uppercase;color:${CORE.steel}}
.ph-club-lift{outline:none;transition:translate .15s ease-out,box-shadow .15s ease-out}
.ph-club-lift.gpfocus,.ph-club-lift:focus-visible{translate:0 -7px;outline:none}
.ph-club-picks{display:grid;grid-template-columns:repeat(4,minmax(0,1fr));gap:${CLUB_LAYOUT.gapVw}vw;margin:6px 0 22px}
.ph-club-card{position:relative;height:${CLUB_LAYOUT.cardVh}vh;min-height:260px;border-radius:10px;overflow:hidden;background:${CORE.gunmetalRaised};box-shadow:0 10px 24px ${coreAlpha(CORE.black, 0.6)}}
.ph-club-card.gpfocus,.ph-club-card:focus-visible{box-shadow:0 14px 30px ${coreAlpha(CORE.black, 0.75)}}
.ph-club-card-art{position:absolute;inset:0;width:100%;height:100%;object-fit:cover}
.ph-club-badge{position:absolute;inset:0;display:flex;align-items:center;justify-content:center;font-size:40px;font-weight:800;letter-spacing:.04em;color:${CORE.steel}}
.ph-club-tone{position:absolute;left:0;top:0;width:100%;height:4px}
.ph-club-fade{position:absolute;inset:0;background:linear-gradient(180deg,${coreAlpha(CORE.black, 0)} 35%,${coreAlpha(CORE.black, 0.92)} 78%)}
.ph-club-details{position:absolute;left:18px;right:18px;bottom:18px}
.ph-club-tag{font-size:10px;font-weight:600;letter-spacing:.14em;text-transform:uppercase}
.ph-club-title{font-size:21px;font-weight:600;color:${CORE.white};margin-top:6px;line-height:1.15}
.ph-club-meta{font-size:13px;color:${CORE.steel};margin-top:6px}
.ph-club-left{font-size:13px;color:${CORE.hint};margin-top:4px}
.ph-club-left.soon{color:${CORE.crimsonText}}
.ph-club-chip{display:inline-flex;align-items:center;height:31px;padding:0 13px;margin-top:13px;font-size:13px;font-weight:600;border-radius:10px;background:${CORE.gunmetal};color:${CORE.steel}}
.ph-club-chip.owned{background:${CORE.ok};color:${CORE.onStatus}}
.ph-club-actions{display:flex;gap:10px;align-items:center;margin:0 0 22px}
.ph-club-action{display:inline-flex;align-items:center;gap:8px;height:37px;padding:0 14px;background:${CORE.gunmetal};color:${CORE.steel};font-size:13px;font-weight:600;border-radius:0}
.ph-club-action.gpfocus,.ph-club-action:focus-visible{background:${CORE.gunmetalRaised};color:${CORE.white};box-shadow:inset 3px 0 0 ${CORE.crimson}}
.ph-club-action[aria-disabled="true"]{color:${CORE.hint}}
.ph-club-key{display:inline-flex;align-items:center;justify-content:center;width:21px;height:21px;border:1.5px solid ${CORE.steel};border-radius:50%;font-size:11px;font-weight:700}
.ph-club-status{font-size:12px;color:${CORE.hint}}
.ph-club-status.error{color:${CORE.warning}}
.ph-club-boxes{display:grid;grid-template-columns:586fr 640fr 526fr;gap:${CLUB_LAYOUT.gapVw}vw}
.ph-club-box{height:${CLUB_LAYOUT.boxVh}vh;min-height:150px;box-sizing:border-box;padding:17px 20px;background:${CORE.gunmetal};border-radius:10px;overflow:hidden}
.ph-club-box.gpfocus,.ph-club-box:focus-visible{background:${CORE.gunmetalRaised}}
.ph-club-big{font-size:28px;font-weight:600;color:${CORE.white};margin-top:4px}
.ph-club-note{font-size:12px;color:${CORE.hint};margin:6px 0 0}
.ph-club-line{font-size:12px;color:${CORE.steel};margin-top:11px}
.ph-club-dates{display:grid;grid-template-columns:84px 1fr;row-gap:10px;font-size:13px}
.ph-club-dates b{color:${CORE.crimsonText};font-weight:600}
.ph-club-dates span{color:${CORE.steel}}
.ph-club-noms{display:flex;flex-direction:column;gap:8px;font-size:13px;color:${CORE.steel}}
.ph-club-noms div{display:flex;justify-content:space-between;gap:12px}
.ph-club-noms b{color:${CORE.white}}
.ph-club-empty{padding:8px 2px;color:${CORE.hint};font-size:15px;text-align:left;outline:none}
@media(prefers-reduced-motion:reduce){.ph-club *{transition:none!important}}
`;

const browserLocale = () => { try { return navigator.language || "en"; } catch { return "en"; } };

function useClub() {
  return React.useSyncExternalStore(subscribeClub, clubState, clubState);
}

function leftTextFor(pick: ClubPick, copy: ClubCopy, locale: string, now: number): { text: string; soon: boolean } {
  const left = timeLeft(pick.ends_at, now);
  const text = left.state === "days" ? daysLeftText(locale, left.days) : left.state === "last" ? copy.lastDay : left.state === "ended" ? copy.ended : copy.noDate;
  return { text, soon: left.state === "last" || (left.state === "days" && left.days <= 2) };
}

function hoursText(hours: number | null | undefined): string {
  return typeof hours === "number" && hours > 0 ? `${Math.round(hours * 10) / 10} h` : "";
}

function PickCard({ pick, match, copy, locale, now, first, onFocus, onLog, onOpen }: { pick: ClubPick; match: Match | null; copy: ClubCopy; locale: string; now: number;
  first: boolean; onFocus: () => void; onLog: () => void; onOpen: () => void }) {
  const art = match ? steamArtFor(match.appId) : "";
  const candidate = art || (CLUB_SITE_ART_ALLOWED ? clubArtUrl(pick) : "");
  const [failed, setFailed] = React.useState("");
  const image = candidate && failed !== candidate ? candidate : "";
  const left = leftTextFor(pick, copy, locale, now);
  const tone = pickTone(pick);
  const platform = pick.platform ? (pick.romhack ? `${pick.platform} romhack` : pick.platform) : "";
  const meta = [platform, hoursText(pick.hours)].filter(Boolean).join(" · ");
  const beaten = typeof pick.beaten === "number" ? fill(copy.beatenCount, { n: pick.beaten }) : "";
  const play = () => { if (match) playApp(match.appId); };
  return <Focusable className="ph-club-card ph-club-lift" focusable={true} role="button" autoFocus={first}
    aria-label={`${pick.label}: ${pick.title}. ${left.text}. ${match ? copy.inLibrary : copy.notInLibrary}`}
    onGamepadFocus={onFocus} onFocus={onFocus}
    onActivate={match ? play : undefined} onClick={match ? play : undefined} onOKActionDescription={match ? copy.play : undefined}
    onSecondaryButton={(event: any) => { event?.preventDefault?.(); event?.stopPropagation?.(); onLog(); }} onSecondaryActionDescription={copy.logButton}
    onOptionsButton={match ? (event: any) => { event?.preventDefault?.(); event?.stopPropagation?.(); onOpen(); } : undefined}
    onOptionsActionDescription={match ? copy.openInLibrary : undefined}>
    {image ? <img className="ph-club-card-art" src={image} alt="" loading="lazy" referrerPolicy="no-referrer" onError={() => setFailed(image)} />
      : <span className="ph-club-badge">{pick.platform || "?"}</span>}
    <span className="ph-club-tone" style={{ background: tone }} />
    <span className="ph-club-fade" />
    <div className="ph-club-details">
      <div className="ph-club-tag" style={{ color: tone }}>{pick.label}</div>
      <div className="ph-club-title">{pick.title}</div>
      {meta && <div className="ph-club-meta">{meta}</div>}
      <div className={`ph-club-left${left.soon ? " soon" : ""}`}>{[left.text, beaten].filter(Boolean).join(" · ")}</div>
      <span className={`ph-club-chip${match ? " owned" : ""}`}>{match ? `✓ ${copy.inLibrary}` : copy.notInLibrary}</span>
    </div>
  </Focusable>;
}

function Standing({ member, name, top, copy }: { member: ClubMember | null; name: string; top: ClubRow[]; copy: ClubCopy }) {
  const leaders = top.slice(0, 3).map(row => `${row.name} ${row.beaten}`).join(" · ");
  return <>
    {!name ? <p className="ph-club-note">{copy.setName}</p>
      : !member ? <p className="ph-club-note">{fill(copy.notFound, { name })}</p>
        : <>
          <div className="ph-club-big">{[member.rank ? `#${member.rank}` : "", member.beaten !== null ? fill(copy.beatenCount, { n: member.beaten }) : ""].filter(Boolean).join(" · ") || "—"}</div>
          {member.rank && member.of ? <p className="ph-club-note">{fill(copy.rankOf, { rank: member.rank, of: member.of })}</p> : null}
        </>}
    {leaders && <div className="ph-club-line">{copy.topLine}: {leaders}</div>}
  </>;
}

function ComingUp({ items, locale }: { items: ClubScheduleItem[]; locale: string }) {
  return <div className="ph-club-dates">{items.slice(0, 3).map((item, index) => <React.Fragment key={index}>
    <b>{formatClubDate(locale, item.at)}</b><span>{item.title || item.text}</span>
  </React.Fragment>)}</div>;
}

function Nominations({ groups }: { groups: ClubNomination[] }) {
  return <div className="ph-club-noms">{groups.slice(0, 3).map((group, index) => <div key={index}>
    <span>{[group.period, group.label].filter(Boolean).join(" ") || group.label}</span><b>{group.count ?? group.items.length}</b>
  </div>)}</div>;
}

// Log completion (X) on the focused pick: Assemble opens the game's thread on the club's Discord with /completed ready;
// the user sends it. Without Assemble, or for a pick that is over, the button says how instead.
function useLog(copy: ClubCopy, now: number) {
  const [, setReady] = React.useState(0);
  const [busy, setBusy] = React.useState(false);
  const [status, setStatus] = React.useState<{ error: boolean; text: string } | null>(null);
  // Assemble fires its ready event when it (re)loads; the bridge is read again on every render.
  React.useEffect(() => subscribeAssemble(() => setReady(tick => tick + 1)), []);
  const bridge = assembleBridge();
  const can = (pick: ClubPick | undefined) => !!pick && !!bridge && !!assembleEventFor(pick) && timeLeft(pick.ends_at, now).state !== "ended";
  const log = async (pick: ClubPick | undefined) => {
    if (!pick || busy) return;
    const event = assembleEventFor(pick);
    if (!bridge || !event) { setStatus({ error: false, text: `${copy.logUnavailable}: ${CLUB_COMPLETION_COMMAND}` }); return; }
    setBusy(true);
    setStatus({ error: false, text: copy.logOpening });
    // Assemble only opens the thread with /completed ready; the user chooses the options and sends it.
    const result = await openClubCompletion({ event, title: pick.title });
    setBusy(false);
    setStatus(result.success ? { error: false, text: copy.logOpened } : { error: true, text: result.message || copy.logFailed });
  };
  return { can, log, status, busy };
}

export function ClubTab({ locale, getLocale }: { locale?: string; getLocale?: () => string }) {
  const resolved = locale ?? getLocale?.() ?? browserLocale();
  const copy = clubCopy(resolved);
  const state = useClub();
  const [now, setNow] = React.useState(() => Date.now());
  const [library, setLibrary] = React.useState<LibraryEntry[]>(() => readLibrary());
  const [focused, setFocused] = React.useState(0);
  React.useEffect(() => {
    // Opening the tab: the backend refreshes when its copy is older than an hour (its politeness rules decide), and
    // the background schedule (clubStore.ts) keeps it fresh while the tab is closed.
    void loadClub(false);
    const poll = window.setInterval(() => void loadClub(false), 10 * 60_000);
    const clock = window.setInterval(() => setNow(Date.now()), 60_000);
    // Steam fills its library stores shortly after the UI appears: read them again a few times.
    let reads = 0;
    const libraryTimer = window.setInterval(() => { setLibrary(readLibrary()); if (++reads >= 5) window.clearInterval(libraryTimer); }, 2000);
    return () => { window.clearInterval(poll); window.clearInterval(clock); window.clearInterval(libraryTimer); };
  }, []);
  const feed = state.feed, snapshot = feed?.snapshot ?? null;
  const picks = (snapshot?.picks ?? []).slice(0, 4);
  const matches = React.useMemo(() => new Map(picks.map(pick => [pick, matchPick({ title: pick.title, platform: pick.platform }, library)])), [snapshot, library]);
  const { can, log, status, busy } = useLog(copy, now);
  const name = feed?.display_name ?? state.settings?.display_name ?? "";
  const pick = picks[Math.min(focused, Math.max(0, picks.length - 1))];
  const match = pick ? matches.get(pick) ?? null : null;
  const open = (target: Match | null) => { if (target) openGamePage(target.appId, (path: string) => Navigation?.Navigate?.(path)); };
  return <Focusable className="ph-club" flow-children="column">
    <style>{css}</style>
    <CoreStars />
    {!snapshot ? <Focusable className="ph-club-empty" focusable={true} role="status">{state.loading && !feed ? copy.loading : copy.unreachable}</Focusable> : <>
      <Focusable className="ph-club-picks" flow-children="row">
        {picks.map((item, index) => <PickCard key={`${item.kind}-${item.slug}`} pick={item} match={matches.get(item) ?? null} copy={copy} locale={resolved} now={now}
          first={index === 0} onFocus={() => setFocused(index)} onLog={() => void log(item)} onOpen={() => open(matches.get(item) ?? null)} />)}
      </Focusable>
      <Focusable className="ph-club-actions" flow-children="row">
        <Focusable className="ph-club-action" focusable={true} role="button" aria-disabled={!can(pick) || busy ? "true" : "false"}
          onActivate={() => void log(pick)} onClick={() => void log(pick)}>
          <span className="ph-club-key" aria-hidden="true">X</span>{copy.logButton}
        </Focusable>
        <Focusable className="ph-club-action" focusable={true} role="button" aria-disabled={match ? "false" : "true"}
          onActivate={() => open(match)} onClick={() => open(match)}>
          <span className="ph-club-key" aria-hidden="true">Y</span>{copy.openInLibrary}
        </Focusable>
        {status && <span className={`ph-club-status${status.error ? " error" : ""}`} role="status">{status.text}</span>}
      </Focusable>
      <Focusable className="ph-club-boxes" flow-children="row">
        <Focusable className="ph-club-box ph-club-lift" focusable={true} role="group" aria-label={copy.standing} onActivate={() => {}}>
          <h3>{copy.standing}</h3>
          <Standing member={snapshot.member} name={name} top={snapshot.top} copy={copy} />
        </Focusable>
        <Focusable className="ph-club-box ph-club-lift" focusable={true} role="group" aria-label={copy.comingUp} onActivate={() => {}}>
          <h3>{copy.comingUp}</h3>
          <ComingUp items={snapshot.schedule} locale={resolved} />
        </Focusable>
        <Focusable className="ph-club-box ph-club-lift" focusable={true} role="group" aria-label={copy.nominations} onActivate={() => {}}>
          <h3>{copy.nominations}</h3>
          <Nominations groups={snapshot.nominations} />
        </Focusable>
      </Focusable>
    </>}
  </Focusable>;
}
