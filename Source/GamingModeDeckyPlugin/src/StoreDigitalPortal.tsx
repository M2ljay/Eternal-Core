// SPDX-License-Identifier: MIT. Playhub, Copyright (c) 2026 LoZazaMastro.
import {SP_REACT as React,routerHook} from "./decky";
import {StoreDigitalPage,StoreDigitalStoreIcon} from "./StoreDigitalPage";
import {StoreDigitalController,storeDigitalApi} from "./StoreDigitalState";
import {registerSteamMenu} from "./StoreDigitalMenuRegistry";
import {STORE_DIGITAL_ROUTE,installStoreDigitalIntentPump} from "./StoreDigitalNavigation";
import {getSteamLocale,subscribeSteamLocale,startSteamLocaleObservation} from "./StoreDigitalSteamLocale";
import {TbMusic} from "react-icons/tb";
import {coreSteamMenuEntries,AWESOME_MIX_PLUGIN,type CoreMenuEntry} from "./coreMenuEntries";
import {corePluginGlyph} from "./corePluginLogos";
import {coreUiSetting} from "./coreUiSetting";
export function StoreDigitalMenuIcon(){return <span style={{display:"flex",width:"1em",height:"1em"}}><StoreDigitalStoreIcon/></span>;}
// Eternal Core: the Awesome Mix entry shows Core's awesome-mix mark (corePluginLogos.ts), single colour like Steam's own
// menu icons; Steam's music note only if the mark is missing.
export function AwesomeMixMenuIcon(){const glyph=corePluginGlyph(AWESOME_MIX_PLUGIN);return glyph
 ?<span aria-hidden="true" style={{display:"flex",width:"1em",height:"1em"}} dangerouslySetInnerHTML={{__html:glyph.replace("<svg ",'<svg width="100%" height="100%" ')}}/>
 :<span style={{display:"flex",width:"1em",height:"1em"}}><TbMusic style={{width:"100%",height:"100%"}}/></span>;}
// Eternal Core (Core UI phase 1, docs/eternal/CORE-UI.md): Digital stores keeps its route but no longer has a
// Steam menu entry (it opens from Quick Access > Core). The Steam menu gets Awesome Mix instead, only while
// Decky has that plugin loaded; checked at start, on Decky's plugin updates and every 5 seconds.
type MenuRegistration={entry:CoreMenuEntry;remove:()=>void};
export function syncCoreSteamMenu(current:Map<string,MenuRegistration>,wanted:CoreMenuEntry[],register:(entry:CoreMenuEntry)=>()=>void){
 const ids=new Set(wanted.map(entry=>entry.id));
 for(const[id,item]of current)if(!ids.has(id)){current.delete(id);try{item.remove();}catch{}}
 for(const entry of wanted){const prior=current.get(entry.id);if(prior&&prior.entry.label===entry.label&&prior.entry.route===entry.route)continue;current.set(entry.id,{entry,remove:register(entry)});if(prior)try{prior.remove();}catch{}}
}
export function installStoreDigitalPortal(locale:()=>string=()=>navigator.language){
 const controller=new StoreDigitalController(),stopLocale=startSteamLocaleObservation();
 const menu=new Map<string,MenuRegistration>();
 const register=(entry:CoreMenuEntry)=>registerSteamMenu({...entry,icon:entry.hide?null:<AwesomeMixMenuIcon/>});
 const refresh=()=>{let state:any,coreUi=false;try{state=(window as any).DeckyPluginLoader?.deckyState?.publicState?.();}catch{state=undefined;}try{coreUi=coreUiSetting.get();}catch{coreUi=false;}syncCoreSteamMenu(menu,coreSteamMenuEntries(state,coreUi),register);};
 refresh();
 const bus=(window as any).DeckyPluginLoader?.deckyState?.eventBus;
 try{bus?.addEventListener?.("update",refresh);}catch{}
 // 3.3.11: Friends & Chat follows the Core UI switch at once.
 let unsubscribeCoreUi=()=>{};try{unsubscribeCoreUi=coreUiSetting.subscribe(refresh);}catch{}
 const Route=()=>{const language=React.useSyncExternalStore(subscribeSteamLocale,getSteamLocale);return <StoreDigitalPage controller={controller} locale={language}/>;};
 routerHook.addRoute(STORE_DIGITAL_ROUTE,Route,{exact:true});
 const stopIntent=installStoreDigitalIntentPump(storeDigitalApi.request);
 const timer=setInterval(refresh,5000);
 return()=>{clearInterval(timer);try{unsubscribeCoreUi();}catch{}try{bus?.removeEventListener?.("update",refresh);}catch{}stopLocale();stopIntent();syncCoreSteamMenu(menu,[],register);routerHook.removeRoute(STORE_DIGITAL_ROUTE);controller.dispose();};
}
