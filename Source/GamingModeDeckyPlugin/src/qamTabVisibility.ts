import { DFL } from "./decky";
import { findOnboardingBrowserViewDocument, invalidateOnboardingBrowserViewOwners } from './onboardingAnchors';

const registryHost=globalThis as any;
const registryKey=Symbol.for('playhub.qam.documents.v1');
const documentRegistry=registryHost[registryKey]??(registryHost[registryKey]={documents:new Map(),listeners:new Set()});
const mountedDocuments:Map<Document,number> = documentRegistry.documents;
const documentListeners:Set<()=>void> = documentRegistry.listeners;
export function registerQamVisibilityDocument(doc:Document) {
  mountedDocuments.set(doc,(mountedDocuments.get(doc)??0)+1);
  documentListeners.forEach(listener=>listener());
  return ()=>{const count=(mountedDocuments.get(doc)??1)-1;if(count)mountedDocuments.set(doc,count);else mountedDocuments.delete(doc);documentListeners.forEach(listener=>listener());};
}

// Eternal Core 3.3.8 (TV: Quick Access tabs kept appearing and disappearing). Before, every managed tab got an inline
// `display: flex|none !important`, re-written on every DOM mutation (style changes included) and every 500 ms, from a
// map rebuilt per render of whichever tab array React rendered last: two arrays meant two answers, and the observer
// fought React's own writes. Now:
// - only hiding is written; a tab that should show is left exactly as Steam renders it (an earlier hide is undone);
// - Decky's tab (999) and any protected id (Core's tab, Jarvis's, the layout's plugin shortcuts) are never hidden;
// - a hide is applied only to a node whose label still matches the one recorded for that id, so a numeric id Steam
//   re-assigns to another tab is not hidden by mistake;
// - mutations are coalesced (one pass per QAM_VISIBILITY_DEBOUNCE_MS), only for added or removed nodes, never for
//   style changes, and a node is written only when its value differs.
export const QAM_VISIBILITY_DEBOUNCE_MS = 120;
export type QamHide = { label?: string };
const NEVER = new Set(['999']);

function labelOf(node:any):string{
  try{return String(node.getAttribute?.('aria-label')||node.getAttribute?.('title')||'').trim();}catch{return '';}
}

/** Hides, by stable native key and label, only the Steam tabs the QAM layout hides. */
export function createQamTabVisibility(documents?: () => Document[], options: { protectedIds?: () => Iterable<string|number>; schedule?: (run: () => void, ms: number) => any; cancel?: (handle: any) => void } = {}) {
  let desired = new Map<string, QamHide>();
  let stopped = false;
  let lastDiscovery = 0;
  let knownDocuments: Document[] = [];
  let pending: any;
  const watched = new Map<Document, MutationObserver>();
  const saved = new Map<HTMLElement, {value:string;priority:string;id:string}>();
  const labels = new Map<string,string>();
  const schedule = options.schedule ?? ((run: () => void, ms: number) => setTimeout(run, ms));
  const cancel = options.cancel ?? ((handle: any) => clearTimeout(handle));
  const protectedIds = () => { const out = new Set(NEVER); try { for (const id of options.protectedIds?.() ?? []) out.add(String(id)); } catch { /* None extra. */ } return out; };
  const discover = documents ?? (() => {
    const result:Document[]=[...mountedDocuments.keys()];
    const host=typeof window==='undefined'?undefined:window as any;
    if (!host) return result;
    if(host.document)result.push(host.document);
    const store=(DFL as any).Router?.WindowStore;
    for(const win of [store?.GamepadUIMainWindowInstance,...(store?.SteamUIWindows??[]),...(store?.OverlayWindows??[])]){
      const doc=(win?.BrowserWindow??win?.m_BrowserWindow)?.document;
      if(!doc)continue;
      if(!result.includes(doc))result.push(doc);
      // Popup BrowserViews belong to React owners, not SteamUIWindows.
      invalidateOnboardingBrowserViewOwners(doc);
      for(const id of desired.keys()){
        const popup=findOnboardingBrowserViewDocument(doc,Number(id));
        if(popup&&!result.includes(popup))result.push(popup);
        if(popup)break;
      }
    }
    // SharedJSContext owns popup BrowserViews; the main window may be only a shell.
    if(host.document){
      invalidateOnboardingBrowserViewOwners(host.document);
      for(const id of desired.keys()){
        const popup=findOnboardingBrowserViewDocument(host.document,Number(id));
        if(popup&&!result.includes(popup))result.push(popup);
        if(popup)break;
      }
    }
    return result;
  });
  const restore = (node:HTMLElement,entry:{value:string;priority:string}) => {
    try{
      if(node.style.getPropertyValue('display')==='none'&&node.style.getPropertyPriority('display')==='important'){
        if(entry.value)node.style.setProperty('display',entry.value,entry.priority);else node.style.removeProperty('display');
      }
    }catch{/* Gone with its window. */}
    saved.delete(node);
  };
  const apply = (doc:Document) => {
    const guard=protectedIds();
    for(const [node,entry] of saved)if(!node.isConnected||!desired.has(entry.id)||guard.has(entry.id)||node.id!==`quickaccess_tab_${entry.id}`)restore(node,entry);
    for(const [id,hide] of desired){
      if(!/^\d+$/.test(id)||guard.has(id))continue;
      const node=doc.getElementById(`quickaccess_tab_${id}`) as HTMLElement|null;
      if(!node?.style)continue;
      // The label this id had when it was first hidden (or the layout's own label): a re-assigned id is left alone.
      const now=labelOf(node), want=hide.label||labels.get(id)||'';
      if(want&&now&&now!==want)continue;
      if(now&&!labels.has(id))labels.set(id,now);
      if(!saved.has(node))saved.set(node,{value:node.style.getPropertyValue('display'),priority:node.style.getPropertyPriority('display'),id});
      if(node.style.getPropertyValue('display')!=='none'||node.style.getPropertyPriority('display')!=='important')node.style.setProperty('display','none','important');
    }
  };
  const run=()=>{pending=undefined;if(stopped)return;for(const doc of currentDocs())apply(doc);};
  const later=()=>{if(stopped||pending!==undefined)return;pending=schedule(run,QAM_VISIBILITY_DEBOUNCE_MS);};
  const currentDocs=()=>{
    if (!knownDocuments.length || Date.now() - lastDiscovery >= 500) {
      try { knownDocuments = discover(); lastDiscovery = Date.now(); } catch { knownDocuments=[...mountedDocuments.keys()]; }
    }
    return [...new Set([...mountedDocuments.keys(),...knownDocuments])];
  };
  const reconcile=()=>{
    if(stopped)return;
    const docs=currentDocs();
    for(const [doc,observer] of watched)if(!docs.includes(doc)){observer.disconnect();watched.delete(doc);}
    for(const doc of docs){
      apply(doc);
      const Observer=doc.defaultView?.MutationObserver;
      if(!watched.has(doc)&&Observer&&doc.documentElement){
        // Added or removed nodes only (React replacing the rail); never style or attribute changes, so the observer
        // cannot answer its own writes or React's.
        const observer=new Observer((records:any[])=>{if(!stopped&&records.some(r=>r?.addedNodes?.length||r?.removedNodes?.length))later();});
        observer.observe(doc.documentElement,{subtree:true,childList:true});watched.set(doc,observer);
      }
    }
  };
  const documentChanged=()=>{lastDiscovery=0;reconcile();};
  documentListeners.add(documentChanged);
  const timer=typeof window==='undefined'?undefined:window.setInterval(reconcile,2000);
  return {
    update(value:ReadonlyMap<string,boolean|QamHide>){
      const next=new Map<string,QamHide>();
      for(const [id,entry] of value){if(entry===false)next.set(String(id),{});else if(entry&&typeof entry==='object')next.set(String(id),entry as QamHide);}
      const same=next.size===desired.size&&[...next].every(([id,entry])=>desired.has(id)&&desired.get(id)?.label===entry.label);
      if(same)return;
      for(const id of desired.keys())if(!next.has(id))labels.delete(id);
      desired=next;reconcile();
    },
    reconcile,
    flush:run,
    stop(){stopped=true;documentListeners.delete(documentChanged);if(pending!==undefined){try{cancel(pending);}catch{}pending=undefined;}if(timer!==undefined)window.clearInterval(timer);for(const observer of watched.values())observer.disconnect();watched.clear();for(const [node,entry] of saved)restore(node,entry);}
  };
}
