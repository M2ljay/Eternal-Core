import { SP_REACT as React, DFL } from "../decky";
import { CORE, coreAlpha } from "../corePalette";
import { CoreStars } from "../CoreStars";
import { clubCopy, daysLeftText, fill, type ClubCopy } from "./clubLocale";
import { formatAgo, formatClubDate, timeLeft, updatedAgo } from "./clubTime";
import { matchPick, type LibraryEntry, type Match } from "./clubMatch";
import { openGamePage, playApp, readLibrary, steamArtFor } from "./clubLibrary";
import { clubState, loadClub, subscribeClub, type ClubMember, type ClubNomination, type ClubPick, type ClubRow } from "./clubStore";
import { CLUB_COMPLETION_COMMAND } from "./clubAssemble";

// Eternal Core: the Club tab on Core's Home: Play Retro Games Club's current picks (matched to the Steam library,
// A plays, X opens the game page), the user's standing beside the top 5, nominations, and how to log a win.
// Core palette: black with the Starfield, gunmetal cards (10 px radius), crimson square focus, steel text.

const { Focusable, DialogButton, Navigation } = DFL as any;

// The club's box art is not offered for embedding (no statement either way, 2026-11-27). Until the organisers say
// yes, tiles use Steam's own artwork for a matched game, or the platform badge.
export const CLUB_SITE_ART_ALLOWED = false;

const css = `
.ph-club{position:relative;isolation:isolate;min-height:100%;padding:24px 36px 48px;color:${CORE.text};background:${CORE.black};box-sizing:border-box}
.ph-club h2{margin:0;font-size:28px;font-weight:700;color:${CORE.white}}
.ph-club h3{margin:0 0 12px;font-size:16px;font-weight:700;letter-spacing:.06em;text-transform:uppercase;color:${CORE.steel}}
.ph-club-head{display:flex;align-items:baseline;gap:18px;margin-bottom:20px;flex-wrap:wrap}
.ph-club-updated{font-size:14px;color:${CORE.hint}}
.ph-club-updated.stale{color:${CORE.warning}}
.ph-club-focus{outline:none;border-radius:0}
.ph-club-focus.gpfocus,.ph-club-focus:focus-visible{box-shadow:0 0 0 3px ${CORE.crimson};border-radius:0}
.ph-club-picks{display:grid;grid-template-columns:repeat(auto-fill,minmax(210px,1fr));gap:22px;margin-bottom:30px}
.ph-club-tile{padding:4px}
.ph-club-card{background:${CORE.gunmetal};border-radius:10px;overflow:hidden;height:100%;display:flex;flex-direction:column}
.ph-club-art{position:relative;aspect-ratio:2/3;background:${CORE.gunmetalRaised} center/cover no-repeat;display:flex;align-items:center;justify-content:center}
.ph-club-badge{font-size:44px;font-weight:800;letter-spacing:.04em;color:${CORE.steel};border-bottom:4px solid ${CORE.crimson};padding-bottom:6px}
.ph-club-event{position:absolute;left:0;top:0;padding:6px 10px;font-size:12px;font-weight:700;text-transform:uppercase;letter-spacing:.06em;background:${coreAlpha(CORE.black, 0.78)};color:${CORE.white}}
.ph-club-body{padding:12px 14px 14px;display:flex;flex-direction:column;gap:4px}
.ph-club-title{font-size:18px;font-weight:700;color:${CORE.white};line-height:1.25}
.ph-club-meta{font-size:14px;color:${CORE.steel}}
.ph-club-left{font-size:15px;font-weight:700;color:${CORE.white}}
.ph-club-left.soon{color:${CORE.crimsonText}}
.ph-club-lib{font-size:13px;color:${CORE.hint}}
.ph-club-row{display:grid;grid-template-columns:minmax(0,1fr) minmax(0,1fr);gap:22px;margin-bottom:22px}
.ph-club-panel{background:${CORE.gunmetal};border-radius:10px;padding:18px 20px}
.ph-club-stats{display:flex;gap:28px;flex-wrap:wrap}
.ph-club-stat strong{display:block;font-size:34px;color:${CORE.white};line-height:1.1}
.ph-club-stat span{font-size:13px;color:${CORE.steel}}
.ph-club-crowns{margin-top:12px;display:flex;gap:8px;flex-wrap:wrap}
.ph-club-crown{font-size:12px;padding:4px 8px;border-radius:10px;background:${CORE.gunmetalRaised};color:${CORE.text}}
.ph-club-list{list-style:none;margin:0;padding:0;display:flex;flex-direction:column;gap:6px}
.ph-club-list li{display:flex;justify-content:space-between;gap:12px;font-size:15px}
.ph-club-list li.me{color:${CORE.white};font-weight:700;box-shadow:inset 3px 0 0 ${CORE.crimson};padding-left:8px}
.ph-club-list .muted{color:${CORE.hint}}
.ph-club-note{font-size:14px;color:${CORE.steel};line-height:1.5;margin:6px 0 0}
.ph-club-command{display:inline-block;margin:6px 0;padding:6px 10px;border-radius:10px;background:${CORE.black};color:${CORE.white};font-family:ui-monospace,Consolas,monospace;font-size:18px}
.ph-club-empty{padding:40px 0;color:${CORE.steel};font-size:16px}
@media(max-width:900px){.ph-club-row{grid-template-columns:1fr}.ph-club{padding:18px}}
@media(prefers-reduced-motion:reduce){.ph-club *{transition:none!important}}
`;

const browserLocale = () => { try { return navigator.language || "en"; } catch { return "en"; } };

function useClub() {
  return React.useSyncExternalStore(subscribeClub, clubState, clubState);
}

function PickTile({ pick, match, copy, locale, now }: { pick: ClubPick; match: Match | null; copy: ClubCopy; locale: string; now: number }) {
  const art = match ? steamArtFor(match.appId) : "";
  const image = art || (CLUB_SITE_ART_ALLOWED ? pick.image : "");
  const left = timeLeft(pick.ends_at, now);
  const leftText = left.state === "days" ? daysLeftText(locale, left.days) : left.state === "last" ? copy.lastDay
    : left.state === "ended" ? copy.ended : copy.noDate;
  const soon = left.state === "last" || (left.state === "days" && left.days <= 2);
  const platform = pick.platform ? (pick.romhack ? `${pick.platform} romhack` : pick.platform) : "";
  const play = () => { if (match) playApp(match.appId); };
  const details = () => { if (match) openGamePage(match.appId, (path: string) => Navigation?.Navigate?.(path)); };
  return <Focusable className="ph-club-tile ph-club-focus" focusable={true} role="button"
    aria-label={`${pick.label}: ${pick.title}. ${leftText}. ${match ? copy.play : copy.notInLibrary}`}
    onActivate={match ? play : undefined} onClick={match ? play : undefined}
    onOKActionDescription={match ? copy.play : undefined}
    onSecondaryButton={match ? (event: any) => { event?.preventDefault?.(); event?.stopPropagation?.(); details(); } : undefined}
    onSecondaryActionDescription={match ? copy.details : undefined}>
    <div className="ph-club-card">
      <div className="ph-club-art" style={image ? { backgroundImage: `url("${image.replace(/"/g, "%22")}")` } : undefined}>
        {!image && <span className="ph-club-badge">{pick.platform || "?"}</span>}
        <span className="ph-club-event">{pick.label}</span>
      </div>
      <div className="ph-club-body">
        <div className="ph-club-title">{pick.title}</div>
        {platform && <div className="ph-club-meta">{platform}</div>}
        <div className={`ph-club-left${soon ? " soon" : ""}`}>{leftText}</div>
        <div className="ph-club-lib">{match ? `A ${copy.play} · X ${copy.details}` : copy.notInLibrary}</div>
      </div>
    </div>
  </Focusable>;
}

function Standing({ member, name, copy }: { member: ClubMember | null; name: string; copy: ClubCopy }) {
  if (!name) return <p className="ph-club-note">{copy.setName}</p>;
  if (!member) return <p className="ph-club-note">{fill(copy.notFound, { name })}</p>;
  const streak = member.streak && member.streak.count > 0 ? member.streak : null;
  return <>
    <div className="ph-club-stats">
      <div className="ph-club-stat"><strong>{member.beaten ?? "—"}</strong><span>{copy.beaten}</span></div>
      <div className="ph-club-stat"><strong>{streak ? streak.count : "—"}</strong><span>{copy.streak}{streak?.event ? ` · ${streak.event}` : ""}</span></div>
      <div className="ph-club-stat"><strong>{member.rank ? `#${member.rank}` : "—"}</strong>
        <span>{member.rank && member.of ? fill(copy.rankOf, { rank: member.rank, of: member.of }) : copy.rank}</span></div>
    </div>
    {member.crowns.length > 0 && <div className="ph-club-crowns" aria-label={copy.crowns}>
      {member.crowns.slice(0, 6).map((crown, index) => <span className="ph-club-crown" key={index}>{crown.crown}{crown.tier ? ` (${crown.tier})` : ""} · {crown.period}</span>)}
    </div>}
  </>;
}

function TopFive({ rows, me }: { rows: ClubRow[]; me: string }) {
  return <ol className="ph-club-list">
    {rows.slice(0, 5).map(row => <li key={row.slug} className={row.slug === me ? "me" : undefined}>
      <span><span className="muted">{row.rank}.</span> {row.name}</span><span>{row.beaten}</span>
    </li>)}
  </ol>;
}

function Nominations({ groups, copy, locale }: { groups: ClubNomination[]; copy: ClubCopy; locale: string }) {
  return <>{groups.slice(0, 3).map((group, index) => <div key={index} style={{ marginBottom: 14 }}>
    <div className="ph-club-meta" style={{ color: CORE.white, fontWeight: 700 }}>{group.label}{group.count ? ` · ${group.count}` : ""}</div>
    {group.opens_at ? <div className="ph-club-lib">{fill(copy.voteOpens, { date: formatClubDate(locale, group.opens_at) })}</div> : null}
    {group.items.length > 0 && <ul className="ph-club-list" style={{ marginTop: 6 }}>
      {group.items.slice(0, 5).map((item, row) => <li key={row}><span>{item.title}</span>
        <span className="muted">{item.platform}{item.romhack ? " romhack" : ""}</span></li>)}
    </ul>}
  </div>)}</>;
}

export function ClubTab({ locale, getLocale }: { locale?: string; getLocale?: () => string }) {
  const resolved = locale ?? getLocale?.() ?? browserLocale();
  const copy = clubCopy(resolved);
  const state = useClub();
  const [now, setNow] = React.useState(() => Date.now());
  const [library, setLibrary] = React.useState<LibraryEntry[]>(() => readLibrary());
  React.useEffect(() => {
    void loadClub(false);
    // The backend serves its cache between refreshes, so asking every 30 minutes costs no request to the club.
    const poll = window.setInterval(() => void loadClub(false), 30 * 60_000);
    const clock = window.setInterval(() => setNow(Date.now()), 60_000);
    // Steam fills its library stores shortly after the UI appears: read them again a few times.
    let reads = 0;
    const libraryTimer = window.setInterval(() => { setLibrary(readLibrary()); if (++reads >= 5) window.clearInterval(libraryTimer); }, 2000);
    return () => { window.clearInterval(poll); window.clearInterval(clock); window.clearInterval(libraryTimer); };
  }, []);
  const feed = state.feed, snapshot = feed?.snapshot ?? null;
  const matches = React.useMemo(() => new Map((snapshot?.picks ?? []).map(pick => [pick, matchPick({ title: pick.title, platform: pick.platform }, library)])),
    [snapshot, library]);
  const ago = updatedAgo(feed?.updated ?? snapshot?.fetched_at, now);
  const canRefresh = !state.loading && now / 1000 >= (feed?.refresh_available_at ?? 0);
  const name = feed?.display_name ?? state.settings?.display_name ?? "";
  return <Focusable className="ph-club" flow-children="column">
    <style>{css}</style>
    <CoreStars />
    <div className="ph-club-head">
      <h2>{copy.title}</h2>
      {ago && <span className={`ph-club-updated${feed?.stale ? " stale" : ""}`} role="status">
        {fill(copy.updated, { ago: formatAgo(resolved, ago) })}{feed?.stale ? ` · ${copy.saved}` : ""}</span>}
      <DialogButton style={{ width: "auto", minWidth: 0, padding: "6px 16px", marginLeft: "auto" }} disabled={!canRefresh}
        onClick={() => void loadClub(true)}>{copy.refresh}</DialogButton>
    </div>
    {!snapshot ? <div className="ph-club-empty" role="status">{state.loading || !feed ? copy.loading : copy.noData}</div> : <>
      <h3>{copy.playing}</h3>
      <Focusable className="ph-club-picks" flow-children="row">
        {snapshot.picks.map(pick => <PickTile key={`${pick.kind}-${pick.slug}`} pick={pick} match={matches.get(pick) ?? null} copy={copy} locale={resolved} now={now} />)}
      </Focusable>
      <div className="ph-club-row">
        <Focusable className="ph-club-panel ph-club-focus" focusable={true} role="group" aria-label={copy.standing}>
          <h3>{copy.standing}{snapshot.member?.name ? ` · ${snapshot.member.name}` : ""}</h3>
          <Standing member={snapshot.member} name={name} copy={copy} />
        </Focusable>
        <Focusable className="ph-club-panel ph-club-focus" focusable={true} role="group" aria-label={copy.top}>
          <h3>{copy.top}</h3>
          <TopFive rows={snapshot.top} me={snapshot.member?.slug ?? ""} />
        </Focusable>
      </div>
      <div className="ph-club-row">
        {snapshot.nominations.length > 0 && <Focusable className="ph-club-panel ph-club-focus" focusable={true} role="group" aria-label={copy.nominations}>
          <h3>{copy.nominations}</h3>
          <Nominations groups={snapshot.nominations} copy={copy} locale={resolved} />
        </Focusable>}
        <Focusable className="ph-club-panel ph-club-focus" focusable={true} role="group" aria-label={copy.logTitle}>
          <h3>{copy.logTitle}</h3>
          <p className="ph-club-note">{copy.logDiscord}</p>
          <code className="ph-club-command">{CLUB_COMPLETION_COMMAND}</code>
          <p className="ph-club-note">{copy.logOptions}</p>
          <p className="ph-club-note">{copy.logForum}</p>
          <p className="ph-club-note">{copy.logDeadline}</p>
        </Focusable>
      </div>
    </>}
  </Focusable>;
}
